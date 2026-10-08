using UnityEngine;

namespace KSPVE.Post;

/// <summary>Procedural lens-dirt texture: soft smudges and specks, generated once and cached.</summary>
internal static class LensDirt
{
    private const int Size = 512;
    private static Texture2D _texture;

    public static Texture2D Texture => _texture != null ? _texture : (_texture = Generate());

    private static Texture2D Generate()
    {
        var pixels = new Color[Size * Size];
        var rng = new System.Random(20260929);
        for (int blob = 0; blob < 110; blob++)
        {
            float cx = (float)rng.NextDouble() * Size;
            float cy = (float)rng.NextDouble() * Size;
            double r = rng.NextDouble();
            float radius = 4f + (float)(r * r) * 70f;
            float strength = 0.04f + (float)rng.NextDouble() * 0.3f;
            float warm = (float)rng.NextDouble() * 0.1f;
            var tint = new Color(1f, 0.95f + warm * 0.5f, 0.9f + warm, 1f);

            int x0 = Mathf.Max(0, (int)(cx - radius)), x1 = Mathf.Min(Size - 1, (int)(cx + radius));
            int y0 = Mathf.Max(0, (int)(cy - radius)), y1 = Mathf.Min(Size - 1, (int)(cy + radius));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x - cx) / radius, dy = (y - cy) / radius;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= 1f) continue;
                    // Brighter rim than centre, like out-of-focus dust.
                    float a = strength * (1f - d2) * (0.55f + 0.45f * d2);
                    pixels[y * Size + x] += tint * a;
                }
            }
        }

        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
        {
            name = "KSPVE Lens Dirt",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels(pixels);
        texture.Apply(true, false);
        return texture;
    }
}
