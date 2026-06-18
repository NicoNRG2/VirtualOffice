using UnityEngine;
using Ubiq.Messaging;
using Ubiq.Rooms;
using System.Collections;
using System.Collections.Generic;
using System;

/// <summary>
/// Whiteboard con sincronizzazione snapshot corretta.
/// </summary>
public class Whiteboard : MonoBehaviour
{
    public Texture2D texture;
    public Vector2 textureSize = new Vector2(2048, 2048);

    [Header("Mirror")]
    public RenderTexture mirrorRenderTexture;

    // -------------------------------------------------------
    // Message type discriminators used in ProcessMessage to route
    // incoming network messages to the correct handler:
    //   Type 0 (WhiteboardMessage, isClear=false): stroke pixel data
    //   Type 1 (WhiteboardMessage, isClear=true):  full whiteboard reset
    //   Type 2 (SnapshotChunkMessage):             one chunk of a snapshot
    //   Type 3 (SnapshotRequestMessage):           request for a snapshot
    // -------------------------------------------------------

    // Serializable structs used with Ubiq's SendJson / FromJson for network messages.
    // Kept as structs (value types) to minimize GC allocations.
    private struct WhiteboardMessage
    {
        public bool isClear;
        public int x, y;
        public int lastX, lastY;
        public bool hasLast;
        public int penSize;
        public float r, g, b, a;
    }

    private struct SnapshotRequestMessage
    {
        public bool isRequest;
    }

    // Snapshots are too large to send in one message, so they are split
    // into fixed-size base64 chunks and reassembled on the receiver side.
    private struct SnapshotChunkMessage
    {
        public int chunkIndex;
        public int totalChunks;
        public string base64Data;
    }

    // -------------------------------------------------------
    // Internal state
    // -------------------------------------------------------

    private NetworkContext context;
    private Renderer _renderer;
    private RoomClient _roomClient;

    // Incoming draw messages are queued and processed in Update()
    // to ensure all texture writes happen on the main thread.
    private List<WhiteboardMessage> _networkQueue = new List<WhiteboardMessage>();

    // Reusable Color[] buffer to avoid per-stroke heap allocations inside the draw loop.
    private Color[] _drawColorBuffer;
    private int     _drawColorBufferSize = 0;

    // State for reassembling a multi-chunk snapshot received from the network.
    private string[] _incomingChunks;
    private int      _incomingTotal    = 0;
    private int      _incomingReceived = 0;

    // Cooldowns prevent snapshot spam when many peers join at once.
    private float _lastSnapshotSentTime    = -999f;
    private float _lastSnapshotRequestTime = -999f;
    private const float SNAPSHOT_SEND_COOLDOWN    = 2f;
    private const float SNAPSHOT_REQUEST_COOLDOWN = 5f;

    // Maximum bytes per chunk (base64 string length); keeps individual messages under Ubiq limits.
    private const int CHUNK_BYTES = 24000;

    // -------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------

    void Start()
    {
        _renderer = GetComponent<Renderer>();

        // Create the writable Texture2D that serves as the drawing canvas.
        texture = new Texture2D(
            (int)textureSize.x,
            (int)textureSize.y,
            TextureFormat.RGBA32,
            false
        );

        FillWhite();
        _renderer.material.mainTexture = texture;
        UpdateRenderTexture();

        // Register with Ubiq's NetworkScene to start sending/receiving messages.
        context = NetworkScene.Register(this);

        // Subscribe to room events to handle snapshot sync when peers join.
        _roomClient = RoomClient.Find(this);
        if (_roomClient != null)
        {
            _roomClient.OnPeerAdded.AddListener(OnPeerAdded);
            _roomClient.OnJoinedRoom.AddListener(OnJoinedRoom);
        }
        else
        {
            Debug.LogWarning("[Whiteboard] RoomClient non trovato.");
        }
    }

    void OnDestroy()
    {
        if (_roomClient == null) return;
        _roomClient.OnPeerAdded.RemoveListener(OnPeerAdded);
        _roomClient.OnJoinedRoom.RemoveListener(OnJoinedRoom);
    }

    // Drain the network queue every frame. All texture mutations are batched
    // here so that texture.Apply() is called at most once per frame.
    void Update()
    {
        if (_networkQueue.Count == 0) return;

        bool dirty = false;

        foreach (var msg in _networkQueue)
        {
            if (msg.isClear)
            {
                FillWhite();
                dirty = true;
                continue;
            }

            var col = new Color(msg.r, msg.g, msg.b, msg.a);

            // Resize the reusable buffer only when pen size changes.
            int needed = msg.penSize * msg.penSize;
            if (_drawColorBuffer == null || _drawColorBufferSize != needed)
            {
                _drawColorBuffer     = new Color[needed];
                _drawColorBufferSize = needed;
            }
            for (int i = 0; i < needed; i++) _drawColorBuffer[i] = col;

            if (msg.hasLast)
            {
                // Interpolate between the previous and current texture positions
                // to produce a smooth stroke, mirroring the local drawing logic.
                for (float f = 0f; f <= 1.00f; f += 0.01f)
                {
                    int lerpX = Mathf.Clamp((int)Mathf.Lerp(msg.lastX, msg.x, f),
                                            0, (int)textureSize.x - msg.penSize);
                    int lerpY = Mathf.Clamp((int)Mathf.Lerp(msg.lastY, msg.y, f),
                                            0, (int)textureSize.y - msg.penSize);
                    texture.SetPixels(lerpX, lerpY, msg.penSize, msg.penSize, _drawColorBuffer);
                }
            }
            else
            {
                // First point of a new stroke — no interpolation needed.
                int cx = Mathf.Clamp(msg.x, 0, (int)textureSize.x - msg.penSize);
                int cy = Mathf.Clamp(msg.y, 0, (int)textureSize.y - msg.penSize);
                texture.SetPixels(cx, cy, msg.penSize, msg.penSize, _drawColorBuffer);
            }

            dirty = true;
        }

        _networkQueue.Clear();

        if (dirty)
        {
            texture.Apply();
            UpdateRenderTexture();
        }
    }

    // -------------------------------------------------------
    // Ubiq Room events
    // -------------------------------------------------------

    // When a new peer joins, the client with the lexicographically highest UUID
    // acts as "master" and sends the current snapshot so the newcomer is in sync.
    private void OnPeerAdded(IPeer newPeer)
    {
        if (_roomClient == null) return;
        if (!gameObject.activeInHierarchy || !enabled) return;

        string myUuid    = _roomClient.Me.uuid;
        bool   iAmMaster = true;

        foreach (var peer in _roomClient.Peers)
        {
            if (peer.uuid == newPeer.uuid) continue;

            if (string.Compare(peer.uuid, myUuid, StringComparison.Ordinal) < 0)
            {
                iAmMaster = false;
                break;
            }
        }

        if (!iAmMaster) return;

        if (IsTextureBlank())
        {
            Debug.Log("[Whiteboard] Sono master ma la lavagna è bianca: snapshot non inviato.");
            return;
        }

        StartCoroutine(SendSnapshotAfterDelay(0.5f));
    }

    // When this client joins a room it requests a snapshot from whoever
    // already has content on the board.
    private void OnJoinedRoom(IRoom room)
    {
        if (!gameObject.activeInHierarchy || !enabled) return;

        if (Time.time - _lastSnapshotRequestTime < SNAPSHOT_REQUEST_COOLDOWN) return;
        _lastSnapshotRequestTime = Time.time;

        StartCoroutine(RequestSnapshotAfterDelay(1.0f));
    }

    private IEnumerator SendSnapshotAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (Time.time - _lastSnapshotSentTime < SNAPSHOT_SEND_COOLDOWN) yield break;
        _lastSnapshotSentTime = Time.time;

        SendSnapshot();
    }

    private IEnumerator RequestSnapshotAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        context.SendJson(new SnapshotRequestMessage { isRequest = true });
        Debug.Log("[Whiteboard] Snapshot richiesto ai peer.");
    }

    // -------------------------------------------------------
    // Public API called by WhiteboardMarker
    // -------------------------------------------------------

    // Broadcasts a single stroke segment to all peers.
    // hasLast=true means the segment is interpolated from (lastX,lastY) to (x,y).
    public void SendDraw(int x, int y, int lastX, int lastY,
                         bool hasLast, int penSize, Color color)
    {
        context.SendJson(new WhiteboardMessage
        {
            isClear = false,
            x       = x,    y     = y,
            lastX   = lastX, lastY = lastY,
            hasLast = hasLast,
            penSize = penSize,
            r = color.r, g = color.g, b = color.b, a = color.a
        });
    }

    // Clears the board locally and broadcasts the clear command to all peers.
    public void NetworkedClear()
    {
        FillWhite();
        texture.Apply();
        UpdateRenderTexture();
        context.SendJson(new WhiteboardMessage { isClear = true });
    }

    // -------------------------------------------------------
    // Ubiq message reception — routes by inspecting the raw JSON string
    // -------------------------------------------------------

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var raw = message.ToString();

        // Distinguish message types by checking for a unique field name in the JSON.
        if (raw.Contains("\"isRequest\""))
        {
            var req = message.FromJson<SnapshotRequestMessage>();
            if (req.isRequest)
            {
                if (Time.time - _lastSnapshotSentTime < SNAPSHOT_SEND_COOLDOWN) return;

                if (IsTextureBlank())
                {
                    Debug.Log("[Whiteboard] Ricevuta richiesta snapshot ma lavagna bianca: ignorata.");
                    return;
                }

                _lastSnapshotSentTime = Time.time;
                if (gameObject.activeInHierarchy && enabled)
                    StartCoroutine(SendSnapshotAfterDelay(0.1f));
            }
            return;
        }

        if (raw.Contains("\"chunkIndex\""))
        {
            var chunk = message.FromJson<SnapshotChunkMessage>();
            HandleSnapshotChunk(chunk);
            return;
        }

        // Default: stroke or clear message — enqueue for processing in Update().
        var msg = message.FromJson<WhiteboardMessage>();
        _networkQueue.Add(msg);
    }

    // -------------------------------------------------------
    // Snapshot: sending
    // -------------------------------------------------------

    // Encodes the current texture as JPEG, converts to base64, and sends it
    // in fixed-size string chunks to stay within Ubiq message size limits.
    private void SendSnapshot()
    {
        byte[] jpgBytes = texture.EncodeToJPG(85);
        string base64   = Convert.ToBase64String(jpgBytes);

        int totalChunks = Mathf.CeilToInt((float)base64.Length / CHUNK_BYTES);

        Debug.Log($"[Whiteboard] Invio snapshot: {jpgBytes.Length / 1024} KB → {totalChunks} chunk/s");

        for (int i = 0; i < totalChunks; i++)
        {
            int start  = i * CHUNK_BYTES;
            int length = Mathf.Min(CHUNK_BYTES, base64.Length - start);

            context.SendJson(new SnapshotChunkMessage
            {
                chunkIndex  = i,
                totalChunks = totalChunks,
                base64Data  = base64.Substring(start, length)
            });
        }
    }

    // -------------------------------------------------------
    // Snapshot: receiving and reassembly
    // -------------------------------------------------------

    // Accumulates incoming chunks in order. When all chunks are received,
    // concatenates the base64 strings and applies the full snapshot.
    private void HandleSnapshotChunk(SnapshotChunkMessage chunk)
    {
        // Reset the receive buffer if this is the start of a new snapshot transfer.
        if (chunk.chunkIndex == 0 || _incomingChunks == null ||
            _incomingTotal != chunk.totalChunks)
        {
            _incomingChunks   = new string[chunk.totalChunks];
            _incomingTotal    = chunk.totalChunks;
            _incomingReceived = 0;
        }

        if (_incomingChunks[chunk.chunkIndex] == null)
        {
            _incomingChunks[chunk.chunkIndex] = chunk.base64Data;
            _incomingReceived++;
        }

        Debug.Log($"[Whiteboard] Chunk {chunk.chunkIndex + 1}/{chunk.totalChunks} ricevuto");

        if (_incomingReceived == _incomingTotal)
        {
            ApplySnapshot(string.Concat(_incomingChunks));
            _incomingChunks   = null;
            _incomingReceived = 0;
            _incomingTotal    = 0;
        }
    }

    // Decodes the reassembled base64 JPEG and loads it directly into the texture.
    private void ApplySnapshot(string base64)
    {
        try
        {
            byte[] jpgBytes = Convert.FromBase64String(base64);
            texture.LoadImage(jpgBytes);
            texture.Apply();
            UpdateRenderTexture();
            Debug.Log("[Whiteboard] Snapshot applicato correttamente.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Whiteboard] Errore applicando snapshot: {e.Message}");
        }
    }

    // -------------------------------------------------------
    // Helpers
    // -------------------------------------------------------

    // Fills the entire texture with white pixels (board reset).
    private void FillWhite()
    {
        var colors = new Color[(int)(textureSize.x * textureSize.y)];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        texture.SetPixels(colors);
    }

    // Blits the CPU-side Texture2D onto the RenderTexture used by mirror displays.
    public void UpdateRenderTexture()
    {
        if (mirrorRenderTexture == null) return;
        Graphics.Blit(texture, mirrorRenderTexture);
    }

    // Samples a sparse 16x16 grid of pixels to quickly decide whether the board
    // is still blank without scanning every pixel.
    private bool IsTextureBlank()
    {
        int w = (int)textureSize.x;
        int h = (int)textureSize.y;

        int step = w / 16;
        for (int x = 0; x < w; x += step)
        {
            for (int y = 0; y < h; y += step)
            {
                Color c = texture.GetPixel(x, y);
                if (c.r < 0.99f || c.g < 0.99f || c.b < 0.99f)
                    return false;
            }
        }
        return true;
    }
}