using UnityEngine;

public class Whiteboard : MonoBehaviour
{
    public Texture2D texture;
    public Vector2 textureSize = new Vector2(2048, 2048);

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

        // Riempie la texture di bianco
        Color[] colors = new Color[(int)(textureSize.x * textureSize.y)];

        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = Color.white;
        }

        texture.SetPixels(colors);
        texture.Apply();

        _renderer.material.mainTexture = texture;
    }
}

