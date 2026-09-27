using Godot;

namespace LastTide;

/// <summary>
/// Plan-view hull art (`assets/art/ships/&lt;hull-id&gt;.png`, bow toward +X) and its painted variants. The
/// painted outer band is marked by `&lt;hull-id&gt;-rim.png` (alpha = how much of a pixel is hull paint), so a
/// cosmetic or faction paint recolours only that band and keeps every ink line and plank of the art.
/// Variants are built once on first use and cached; null when the art is absent (procedural fallback).
/// </summary>
public static class ShipArt
{
    static readonly Dictionary<(string, Color), Texture2D?> painted = new();

    public static Texture2D? Hull(string id) => Art.Tex("ships/" + id);

    /// <summary>Width/height of the hull art (1 when absent).</summary>
    public static float Aspect(string id)
    {
        var t = Hull(id);
        return t == null ? 1f : t.GetWidth() / (float)Math.Max(1, t.GetHeight());
    }

    /// <summary>The hull with its outer band repainted in <paramref name="paint"/>; the base art for the plain umber.</summary>
    public static Texture2D? Painted(string id, Color paint)
    {
        var baseTex = Hull(id);
        if (baseTex == null || paint.IsEqualApprox(Ink.Hull)) return baseTex;
        if (painted.TryGetValue((id, paint), out var t)) return t;
        t = baseTex;
        var rimTex = Art.Tex("ships/" + id + "-rim");
        try
        {
            if (rimTex != null)
            {
                var img = Rgba(baseTex.GetImage());
                var rim = Rgba(rimTex.GetImage());
                if (img.GetWidth() == rim.GetWidth() && img.GetHeight() == rim.GetHeight())
                {
                    byte[] px = img.GetData(), rm = rim.GetData();
                    // Luminance-preserving recolour: the paint takes the art's light and shade, so planks,
                    // nails and the ink outline survive. 0.44 is the art's typical band luminance.
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        float w = rm[i + 3] / 255f;
                        if (w <= 0.004f) continue;
                        float r = px[i] / 255f, g = px[i + 1] / 255f, b = px[i + 2] / 255f;
                        float lum = r * 0.299f + g * 0.587f + b * 0.114f;
                        float k = Mathf.Pow(lum / 0.44f, 0.9f);
                        px[i] = (byte)(255 * Mathf.Clamp(Mathf.Lerp(r, paint.R * k, w), 0, 1));
                        px[i + 1] = (byte)(255 * Mathf.Clamp(Mathf.Lerp(g, paint.G * k, w), 0, 1));
                        px[i + 2] = (byte)(255 * Mathf.Clamp(Mathf.Lerp(b, paint.B * k, w), 0, 1));
                    }
                    var outImg = Image.CreateFromData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, px);
                    outImg.GenerateMipmaps();
                    t = ImageTexture.CreateFromImage(outImg);
                }
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"hull paint for {id} failed: {e.Message}");
            t = baseTex;
        }
        painted[(id, paint)] = t;
        return t;
    }

    static Image Rgba(Image img)
    {
        if (img.IsCompressed()) img.Decompress();
        if (img.HasMipmaps()) img.ClearMipmaps();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        return img;
    }

    /// <summary>The hull-band paint a ship wears: the player's cosmetic, or a faction's colours at sea.</summary>
    public static Color FactionPaint(LastTide.Sim.Faction f) => f switch
    {
        LastTide.Sim.Faction.Crown => Ink.Hull.Lerp(Ink.Crown, 0.55f),
        LastTide.Sim.Faction.Brethren => Ink.Hull.Lerp(Ink.Brethren, 0.6f).Darkened(0.15f),
        _ => Ink.Hull,
    };
}
