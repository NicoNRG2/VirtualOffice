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
        public int x;
        public int y;
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

        bool didClear = false;

        foreach (var msg in _networkQueue)
        {
            if (msg.isClear)
            {
                FillWhite(); // scrive pixel, Apply() dopo
                didClear = true;
                continue;
            }

            var col = new Color(msg.r, msg.g, msg.b, msg.a);
            var colors = new Color[msg.penSize * msg.penSize];
            for (int i = 0; i < colors.Length; i++) colors[i] = col;

            int cx = Mathf.Clamp(msg.x, 0, (int)textureSize.x - msg.penSize);
            int cy = Mathf.Clamp(msg.y, 0, (int)textureSize.y - msg.penSize);
            texture.SetPixels(cx, cy, msg.penSize, msg.penSize, colors);
        }

        _networkQueue.Clear();

        texture.Apply();
        UpdateRenderTexture();
    }

    // -------------------------------------------------------
    // API pubblica per WhiteboardMarker (solo il proprietario la chiama)
    // -------------------------------------------------------

    /// <summary>
    /// Invia una pennellata agli altri peer.
    /// Il marker applica già localmente: qui mandiamo solo il messaggio.
    /// </summary>
    public void SendDraw(int x, int y, int penSize, Color color)
    {
        context.SendJson(new WhiteboardMessage
        {
            isClear = false,
            x      = x,
            y      = y,
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