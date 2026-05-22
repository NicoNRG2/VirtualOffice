using UnityEngine;
using Ubiq.Messaging;
using System.Collections.Generic;

public class Whiteboard : MonoBehaviour
{
    public Texture2D texture;
    public Vector2 textureSize = new Vector2(2048, 2048);

    [Header("Mirror")]
    public RenderTexture mirrorRenderTexture;

    // -------------------------------------------------------
    // Struttura unica per tutti i messaggi di rete.
    // "isClear = true" => comando reset lavagna.
    // "isClear = false" => pennellata con coordinate e colore.
    // -------------------------------------------------------
    private struct WhiteboardMessage
    {
        public bool isClear;
        public int x, y;
        public int lastX, lastY;  // punto precedente per interpolazione remota
        public bool hasLast;      // false al primo tocco (nessun punto precedente)
        public int penSize;
        public float r, g, b, a;
    }

    private NetworkContext context;
    private Renderer _renderer;

    // Buffer: accumula le pennellate ricevute dalla rete e le applica tutte in un unico Apply() per frame.
    private List<WhiteboardMessage> _networkQueue = new List<WhiteboardMessage>();

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
    }

    void Update()
    {
        if (_networkQueue.Count == 0) return;

        foreach (var msg in _networkQueue)
        {
            if (msg.isClear)
            {
                FillWhite();
                continue;
            }

            var col = new Color(msg.r, msg.g, msg.b, msg.a);
            var colors = new Color[msg.penSize * msg.penSize];
            for (int i = 0; i < colors.Length; i++) colors[i] = col;

            // Punto corrente
            int cx = Mathf.Clamp(msg.x, 0, (int)textureSize.x - msg.penSize);
            int cy = Mathf.Clamp(msg.y, 0, (int)textureSize.y - msg.penSize);
            texture.SetPixels(cx, cy, msg.penSize, msg.penSize, colors);

            // Interpolazione remota: ricostruisce il tratto continuo tra last e current
            if (msg.hasLast)
            {
                for (float f = 0.01f; f < 1.00f; f += 0.01f)
                {
                    var lerpX = (int)Mathf.Lerp(msg.lastX, msg.x, f);
                    var lerpY = (int)Mathf.Lerp(msg.lastY, msg.y, f);
                    int clampedX = Mathf.Clamp(lerpX, 0, (int)textureSize.x - msg.penSize);
                    int clampedY = Mathf.Clamp(lerpY, 0, (int)textureSize.y - msg.penSize);
                    texture.SetPixels(clampedX, clampedY, msg.penSize, msg.penSize, colors);
                }
            }
        }

        _networkQueue.Clear();

        texture.Apply();
        UpdateRenderTexture();
    }

    // -------------------------------------------------------
    // API pubblica per WhiteboardMarker (solo il proprietario la chiama)
    // -------------------------------------------------------

    /// <summary>
    /// Invia una pennellata agli altri peer, includendo il punto precedente
    /// per permettere la ricostruzione dell'interpolazione remota.
    /// </summary>
    public void SendDraw(int x, int y, int lastX, int lastY, bool hasLast, int penSize, Color color)
    {
        context.SendJson(new WhiteboardMessage
        {
            isClear = false,
            x       = x,
            y       = y,
            lastX   = lastX,
            lastY   = lastY,
            hasLast = hasLast,
            penSize = penSize,
            r = color.r, g = color.g, b = color.b, a = color.a
        });
    }

    /// <summary>
    /// Pulisce localmente e notifica tutti i peer.
    /// </summary>
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
        var msg = message.FromJson<WhiteboardMessage>();
        _networkQueue.Add(msg);
    }

    // -------------------------------------------------------
    // Helpers
    // -------------------------------------------------------

    private void FillWhite()
    {
        var colors = new Color[(int)(textureSize.x * textureSize.y)];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        texture.SetPixels(colors);
        // NON chiamiamo Apply() qui — lo fa chi chiama FillWhite
    }

    public void UpdateRenderTexture()
    {
        if (mirrorRenderTexture == null) return;
        Graphics.Blit(texture, mirrorRenderTexture);
    }
}