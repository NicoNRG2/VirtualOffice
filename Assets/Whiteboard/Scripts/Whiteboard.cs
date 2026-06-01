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
    // Tipi di messaggio
    // Tipo 0 (WhiteboardMessage, isClear=false): pennellata
    // Tipo 1 (WhiteboardMessage, isClear=true):  reset lavagna
    // Tipo 2 (SnapshotChunkMessage):             chunk snapshot
    // Tipo 3 (SnapshotRequestMessage):           richiesta snapshot
    // -------------------------------------------------------

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

    private struct SnapshotChunkMessage
    {
        public int chunkIndex;
        public int totalChunks;
        public string base64Data;
    }

    // -------------------------------------------------------
    // Stato interno
    // -------------------------------------------------------

    private NetworkContext context;
    private Renderer _renderer;
    private RoomClient _roomClient;

    private List<WhiteboardMessage> _networkQueue = new List<WhiteboardMessage>();

    // Pool di Color[] riusabile per evitare allocazioni continue nel loop
    private Color[] _drawColorBuffer;
    private int     _drawColorBufferSize = 0;

    // Ricostruzione snapshot in ingresso
    private string[] _incomingChunks;
    private int      _incomingTotal    = 0;
    private int      _incomingReceived = 0;

    private float _lastSnapshotSentTime    = -999f;
    private float _lastSnapshotRequestTime = -999f;
    private const float SNAPSHOT_SEND_COOLDOWN    = 2f;
    private const float SNAPSHOT_REQUEST_COOLDOWN = 5f;

    private const int CHUNK_BYTES = 24000;

    // -------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------

    void Start()
    {
        _renderer = GetComponent<Renderer>();

        texture = new Texture2D(
            (int)textureSize.x,
            (int)textureSize.y,
            TextureFormat.RGBA32,
            false
        );

        FillWhite();
        _renderer.material.mainTexture = texture;
        UpdateRenderTexture();

        context = NetworkScene.Register(this);

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

            // Riusa il buffer di colori se la dimensione è la stessa, altrimenti riallocalo
            int needed = msg.penSize * msg.penSize;
            if (_drawColorBuffer == null || _drawColorBufferSize != needed)
            {
                _drawColorBuffer     = new Color[needed];
                _drawColorBufferSize = needed;
            }
            for (int i = 0; i < needed; i++) _drawColorBuffer[i] = col;

            if (msg.hasLast)
            {
                // Lerp dal punto precedente a quello corrente (stessa logica del marker locale)
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
                // Primo punto del tratto
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
    // API pubblica per WhiteboardMarker
    // -------------------------------------------------------

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

    public void NetworkedClear()
    {
        FillWhite();
        texture.Apply();
        UpdateRenderTexture();
        context.SendJson(new WhiteboardMessage { isClear = true });
    }

    // -------------------------------------------------------
    // Ricezione messaggi Ubiq
    // -------------------------------------------------------

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var raw = message.ToString();

        // SnapshotRequestMessage
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

        // SnapshotChunkMessage
        if (raw.Contains("\"chunkIndex\""))
        {
            var chunk = message.FromJson<SnapshotChunkMessage>();
            HandleSnapshotChunk(chunk);
            return;
        }

        // WhiteboardMessage (pennellata o clear)
        var msg = message.FromJson<WhiteboardMessage>();
        _networkQueue.Add(msg);
    }

    // -------------------------------------------------------
    // Snapshot: invio
    // -------------------------------------------------------

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
    // Snapshot: ricezione e ricostruzione
    // -------------------------------------------------------

    private void HandleSnapshotChunk(SnapshotChunkMessage chunk)
    {
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

    private void FillWhite()
    {
        var colors = new Color[(int)(textureSize.x * textureSize.y)];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        texture.SetPixels(colors);
    }

    public void UpdateRenderTexture()
    {
        if (mirrorRenderTexture == null) return;
        Graphics.Blit(texture, mirrorRenderTexture);
    }

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