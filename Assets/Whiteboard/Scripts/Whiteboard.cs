using UnityEngine;

public class Whiteboard : MonoBehaviour
{
    public Texture2D texture;
    public Vector2 textureSize = new Vector2(2048, 2048);

    [Header("Mirror")]
    public RenderTexture mirrorRenderTexture; // assegna dal Inspector

    private Renderer _renderer;

    void Start()
    {
        _renderer = GetComponent<Renderer>();

        texture = new Texture2D(
            (int)textureSize.x,
            (int)textureSize.y,
            TextureFormat.RGBA32,
            false
        );

        Color[] colors = new Color[(int)(textureSize.x * textureSize.y)];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = Color.white;

        texture.SetPixels(colors);
        texture.Apply();

        _renderer.material.mainTexture = texture;

        // Inizializza la RenderTexture con lo stato bianco iniziale
        UpdateRenderTexture();
    }

    /// <summary>
    /// Copia la Texture2D corrente nella RenderTexture mirror.
    /// Chiamata dal marker dopo ogni Apply().
    /// </summary>
    public void UpdateRenderTexture()
    {
        if (mirrorRenderTexture == null) return;
        Graphics.Blit(texture, mirrorRenderTexture);
    }
}