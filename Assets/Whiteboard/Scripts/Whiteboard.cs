using UnityEngine;
using Ubiq.Messaging;
using Ubiq.Rooms;
using System.Collections;
using System.Collections.Generic;
using System;

public class Whiteboard : MonoBehaviour
{
    public Texture2D texture;
    public Vector2 textureSize = new Vector2(2048, 2048);

    [Header("Mirror")]
    public RenderTexture mirrorRenderTexture;

    // -------------------------------------------------------
    // Messaggi di rete
    // -------------------------------------------------------

    // Tipo 0: pennellata normale
    // Tipo 1: reset lavagna
    // Tipo 2: chunk di snapshot (sincronizzazione nuovo peer)
    // Tipo 3: richiesta snapshot da parte di un nuovo peer

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
        public bool isRequest; // sempre true, serve solo per distinguere il tipo
    }

    private struct SnapshotChunkMessage
    {
        public int chunkIndex;
        public int totalChunks;
        public string base64Data; // porzione dei byte JPG codificata in Base64
    }

    // -------------------------------------------------------
    // Stato interno
    // -------------------------------------------------------

    private NetworkContext context;
    private Renderer _renderer;
    private RoomClient _roomClient;

    // Coda pennellate remote
    private List<WhiteboardMessage> _networkQueue = new List<WhiteboardMessage>();

    // Ricostruzione snapshot in ingresso
    private string[] _incomingChunks;
    private int      _incomingTotal   = 0;
    private int      _incomingReceived = 0;

    // Throttle invio snapshot: evita di inviare più snapshot in rapida successione
    private float _lastSnapshotSentTime = -999f;
    private const float SNAPSHOT_COOLDOWN = 2f; // secondi

    // Dimensione massima sicura per chunk Base64 in un messaggio Ubiq (~32 KB)
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

        // Cerca il RoomClient per intercettare OnPeerAdded
        _roomClient = RoomClient.Find(this);
        if (_roomClient != null)
        {
            _roomClient.OnPeerAdded.AddListener(OnPeerAdded);
            _roomClient.OnJoinedRoom.AddListener(OnJoinedRoom);
        }
        else
        {
            Debug.LogWarning("[Whiteboard] RoomClient non trovato: la sincronizzazione " +
                             "snapshot non funzionerà.");
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

        foreach (var msg in _networkQueue)
        {
            if (msg.isClear) { FillWhite(); continue; }

            var col    = new Color(msg.r, msg.g, msg.b, msg.a);
            var colors = new Color[msg.penSize * msg.penSize];
            for (int i = 0; i < colors.Length; i++) colors[i] = col;

            int cx = Mathf.Clamp(msg.x, 0, (int)textureSize.x - msg.penSize);
            int cy = Mathf.Clamp(msg.y, 0, (int)textureSize.y - msg.penSize);
            texture.SetPixels(cx, cy, msg.penSize, msg.penSize, colors);

            if (msg.hasLast)
            {
                for (float f = 0.01f; f < 1.00f; f += 0.01f)
                {
                    int lerpX = Mathf.Clamp((int)Mathf.Lerp(msg.lastX, msg.x, f),
                                            0, (int)textureSize.x - msg.penSize);
                    int lerpY = Mathf.Clamp((int)Mathf.Lerp(msg.lastY, msg.y, f),
                                            0, (int)textureSize.y - msg.penSize);
                    texture.SetPixels(lerpX, lerpY, msg.penSize, msg.penSize, colors);
                }
            }
        }

        _networkQueue.Clear();
        texture.Apply();
        UpdateRenderTexture();
    }

    // -------------------------------------------------------
    // Ubiq Room events
    // -------------------------------------------------------

    /// <summary>
    /// Chiamato su tutti i peer quando un nuovo peer entra.
    /// Il peer con UUID più basso fa da "master" e invia lo snapshot.
    /// </summary>
    private void OnPeerAdded(IPeer newPeer)
    {
        if (_roomClient == null) return;

        // Determina se questo peer è il "master" (UUID lessicograficamente minore
        // tra tutti i peer presenti, incluso se stesso).
        string myUuid = _roomClient.Me.uuid;

        bool iAmMaster = true;
        foreach (var peer in _roomClient.Peers)
        {
            // Se almeno un peer remoto ha UUID minore del mio, non sono il master
            if (string.Compare(peer.uuid, myUuid, StringComparison.Ordinal) < 0)
            {
                iAmMaster = false;
                break;
            }
        }

        if (!iAmMaster) return;

        // Aspetta un frame prima di inviare, per dare tempo a Ubiq di
        // finalizzare il join del nuovo peer.
        if (gameObject.activeInHierarchy && enabled)
        {
            StartCoroutine(SendSnapshotAfterDelay(0.5f));
        }
    }

    /// <summary>
    /// Chiamato sul peer appena entrato: richiede lo snapshot agli altri.
    /// Meccanismo di backup nel caso OnPeerAdded non sia sufficiente.
    /// </summary>
    private void OnJoinedRoom(IRoom room)
    {
        if (!gameObject.activeInHierarchy || !enabled)
            return;

        StartCoroutine(RequestSnapshotAfterDelay(1.0f));
    }

    private IEnumerator SendSnapshotAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Cooldown: evita burst di snapshot
        if (Time.time - _lastSnapshotSentTime < SNAPSHOT_COOLDOWN) yield break;
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
        // Ubiq invia messaggi come JSON generico; proviamo a deserializzare
        // nel tipo corretto in base ai campi presenti.

        var raw = message.ToString();

        // Tentativo 1: SnapshotRequestMessage
        if (raw.Contains("\"isRequest\""))
        {
            var req = message.FromJson<SnapshotRequestMessage>();
            if (req.isRequest)
            {
                if (Time.time - _lastSnapshotSentTime < SNAPSHOT_COOLDOWN) return;
                _lastSnapshotSentTime = Time.time;
                if (gameObject.activeInHierarchy && enabled)
                {
                    StartCoroutine(SendSnapshotAfterDelay(0.1f));
                }
            }
            return;
        }

        // Tentativo 2: SnapshotChunkMessage
        if (raw.Contains("\"chunkIndex\""))
        {
            var chunk = message.FromJson<SnapshotChunkMessage>();
            HandleSnapshotChunk(chunk);
            return;
        }

        // Tentativo 3: WhiteboardMessage (pennellata o clear)
        var msg = message.FromJson<WhiteboardMessage>();
        _networkQueue.Add(msg);
    }

    // -------------------------------------------------------
    // Snapshot: invio
    // -------------------------------------------------------

    private void SendSnapshot()
    {
        // Codifica la texture come JPG (qualità 85 — buon compromesso dimensione/fedeltà)
        byte[] jpgBytes = texture.EncodeToJPG(85);
        string base64   = Convert.ToBase64String(jpgBytes);

        int totalChunks = Mathf.CeilToInt((float)base64.Length / CHUNK_BYTES);

        Debug.Log($"[Whiteboard] Invio snapshot: {jpgBytes.Length / 1024} KB " +
                  $"→ {totalChunks} chunk/s");

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
        // Primo chunk: inizializza il buffer
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

        // Tutti i chunk arrivati: ricostruisci e applica
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

            // LoadImage sovrascrive la texture esistente con i dati JPG decodificati
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
}