using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>A hull's paint: her topsides and a stripe (the wale or gun strake) along them. Two stripes on a two-decker.</summary>
public readonly record struct Livery(Color Topsides, Color Stripe, bool Double = false);

/// <summary>
/// Plan-view hull art (`assets/art/ships/&lt;hull-id&gt;.png`, bow toward +X) dressed for the sea. The painted outer
/// band is marked by `&lt;hull-id&gt;-rim.png` (alpha = how much of a pixel is hull paint); every hull wears its own
/// livery (<see cref="LiveryOf"/>): the rail is recoloured so every ink line and plank of the art survives, and her
/// topsides show as a band outside the rail (as a hull with tumblehome does from above) carrying the stripe.
/// The dressing also bakes the hull's volume (the rail catching the light, the deck shaded under the bulwarks),
/// and each hull has a soft silhouette for her shadow on the water.
/// Everything is built once on first use and cached; null when the art is absent (procedural fallback).
/// </summary>
public static class ShipArt
{
    static readonly Dictionary<(string, Livery), Texture2D?> dressed = new();
    static readonly Dictionary<string, Masks?> masks = new();

    public static Texture2D? Hull(string id) => Art.Tex("ships/" + id);

    // ---- liveries ----

    static readonly Color Tar = new(0.17f, 0.15f, 0.13f);
    static readonly Color Ochre = new(0.86f, 0.68f, 0.33f);
    static readonly Color White = new(0.93f, 0.91f, 0.85f);
    static readonly Color Cream = new(0.89f, 0.83f, 0.66f);

    /// <summary>Every hull's own paint, after the ships she is drawn from (view data, no rules).</summary>
    static readonly Dictionary<string, Livery> Liveries = new()
    {
        ["sloop"] = new(new(0.55f, 0.42f, 0.28f), Cream),                         // tarred umber, a cream sheer line
        ["cutter"] = new(new(0.20f, 0.28f, 0.42f), White),                        // revenue blue and white
        ["schooner"] = new(new(0.24f, 0.36f, 0.28f), Cream),                      // Baltimore green
        ["xebec"] = new(new(0.66f, 0.27f, 0.17f), Ochre),                         // corsair vermilion and gold
        ["brigantine"] = new(new(0.66f, 0.48f, 0.27f), new(0.26f, 0.40f, 0.30f)), // bright oak, a green strake
        ["fluyt"] = new(new(0.30f, 0.44f, 0.40f), new(0.74f, 0.38f, 0.20f)),      // Dutch sea-green and red
        ["brig"] = new(new(0.74f, 0.58f, 0.31f), Tar),                            // old navy ochre, black wale
        ["barque"] = new(new(0.19f, 0.18f, 0.17f), White),                        // merchant black, painted ports
        ["corvette"] = new(new(0.30f, 0.36f, 0.46f), Ochre),                      // slate blue, an ochre band
        ["frigate"] = new(Tar, Ochre),                                            // black and ochre
        ["indiaman"] = new(new(0.34f, 0.23f, 0.16f), new(0.88f, 0.77f, 0.55f)),   // Company brown and buff
        ["heavy_frigate"] = new(Tar, White),                                      // black with a white gun strake
        ["galleon"] = new(new(0.56f, 0.17f, 0.13f), new(0.88f, 0.70f, 0.26f)),    // crimson and gilt
        ["man_o_war"] = new(Tar, Ochre, Double: true),                            // black, two ochre gun decks
    };

    public static Livery LiveryOf(string id) => Liveries.TryGetValue(id, out var l) ? l : new Livery(Ink.Hull, Cream);

    /// <summary>
    /// The paint a ship wears: the player's hull in its own livery (a cosmetic colour repaints the topsides);
    /// at sea the Crown adds its red strake and the Brethren tar their topsides and run a sea-green stripe.
    /// </summary>
    public static Livery For(HullDef hull, bool isPlayer, Faction faction, string hullCosmetic = "")
    {
        var l = LiveryOf(hull.Id);
        if (isPlayer) return hullCosmetic.Length > 0 ? l with { Topsides = Cosmetic.Hull(hullCosmetic) } : l;
        return faction switch
        {
            Faction.Crown => l with { Stripe = Ink.Crown.Lightened(0.12f) },
            Faction.Brethren => l with { Topsides = l.Topsides.Lerp(Ink.Brethren.Darkened(0.55f), 0.6f), Stripe = Ink.Brethren.Lightened(0.3f) },
            _ => l,
        };
    }

    // ---- the dressed hull ----

    /// <summary>The hull in <paramref name="livery"/> with her volume baked in; the bare art if the masks can't be read.</summary>
    public static Texture2D? Dressed(string id, Livery livery)
    {
        var baseTex = Hull(id);
        if (baseTex == null) return null;
        if (dressed.TryGetValue((id, livery), out var t)) return t;
        t = baseTex;
        try
        {
            var mk = MasksOf(id);
            if (mk != null) t = Dress(mk, livery);
        }
        catch (Exception e)
        {
            GD.PushWarning($"hull dressing for {id} failed: {e.Message}");
            t = baseTex;
        }
        dressed[(id, livery)] = t;
        return t;
    }

    /// <summary>A soft silhouette of the hull for her shadow, and how far it reaches past the hull's rect (fractions of W and H).</summary>
    public static (Texture2D? Tex, Vector2 Pad) Shadow(string id)
    {
        var mk = Hull(id) == null ? null : MasksOf(id);
        return mk == null ? (null, Vector2.Zero) : (mk.Shadow, mk.ShadowPad);
    }

    sealed class Masks
    {
        public int W, H;        // the padded canvas: the art plus a band of Pad px all round for her topsides
        public int Pad;
        public byte[] Art = Array.Empty<byte>();     // the art's RGBA placed on the padded canvas
        public float[] Rim = Array.Empty<float>();   // 0..1 rail paint weight (from the -rim art)
        public float[] In = Array.Empty<float>();    // inside the art: distance to the water, px
        public float[] Out = Array.Empty<float>();   // outside the art: distance to the hull, px
        public Texture2D? Shadow;
        public Vector2 ShadowPad;
    }

    static Masks? MasksOf(string id)
    {
        if (masks.TryGetValue(id, out var mk)) return mk;
        mk = null;
        try { mk = Build(id); }
        catch (Exception e) { GD.PushWarning($"hull masks for {id} failed: {e.Message}"); }
        masks[id] = mk;
        return mk;
    }

    static Masks? Build(string id)
    {
        var baseTex = Hull(id);
        if (baseTex == null) return null;
        var img = Rgba(baseTex.GetImage());
        int w0 = img.GetWidth(), h0 = img.GetHeight();
        var src = img.GetData();
        byte[]? rimSrc = null;
        if (Art.Tex("ships/" + id + "-rim") is { } rimTex)
        {
            var r = Rgba(rimTex.GetImage());
            if (r.GetWidth() == w0 && r.GetHeight() == h0) rimSrc = r.GetData();
        }
        // Her topsides show outside the rail from above (the tumblehome): a band ~6% of her beam each side.
        int pad = Math.Max(4, (int)MathF.Round(h0 * 0.06f));
        int w = w0 + 2 * pad, h = h0 + 2 * pad, n = w * h;
        var art = new byte[n * 4];
        var rim = new float[n];
        var inside = new bool[n];
        for (int y = 0; y < h0; y++)
            for (int x = 0; x < w0; x++)
            {
                int s = (y * w0 + x) * 4, d = ((y + pad) * w + x + pad);
                Buffer.BlockCopy(src, s, art, d * 4, 4);
                inside[d] = src[s + 3] >= 128;
                if (rimSrc != null) rim[d] = rimSrc[s + 3] / 255f;
            }
        var mk = new Masks { W = w, H = h, Pad = pad, Art = art, Rim = rim, In = Chamfer(inside, w, h, false), Out = Chamfer(inside, w, h, true) };
        BuildShadow(mk);
        return mk;
    }

    /// <summary>
    /// Chamfer distance (px): for <paramref name="outside"/>, from every water pixel to the hull; else from every
    /// hull pixel to the water. Zero on the other side.
    /// </summary>
    static float[] Chamfer(bool[] inside, int w, int h, bool outside)
    {
        const float Inf = 1e9f, D1 = 1f, D2 = 1.4142f;
        var dist = new float[w * h];
        for (int i = 0; i < dist.Length; i++) dist[i] = inside[i] != outside ? Inf : 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (dist[i] == 0) continue;
                float d = dist[i];
                if (x > 0) d = Math.Min(d, dist[i - 1] + D1);
                if (y > 0)
                {
                    d = Math.Min(d, dist[i - w] + D1);
                    if (x > 0) d = Math.Min(d, dist[i - w - 1] + D2);
                    if (x < w - 1) d = Math.Min(d, dist[i - w + 1] + D2);
                }
                dist[i] = d;
            }
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                if (dist[i] == 0) continue;
                float d = dist[i];
                if (x < w - 1) d = Math.Min(d, dist[i + 1] + D1);
                if (y < h - 1)
                {
                    d = Math.Min(d, dist[i + w] + D1);
                    if (x < w - 1) d = Math.Min(d, dist[i + w + 1] + D2);
                    if (x > 0) d = Math.Min(d, dist[i + w - 1] + D2);
                }
                dist[i] = d;
            }
        return dist;
    }

    /// <summary>1 at <paramref name="c"/>, falling to 0 at ±<paramref name="r"/> (a smooth hump).</summary>
    static float Bump(float x, float c, float r)
    {
        float u = Mathf.Clamp(1 - Mathf.Abs(x - c) / r, 0, 1);
        return u * u * (3 - 2 * u);
    }

    /// <summary>A quarter-size silhouette of the dressed hull, padded and blurred: the soft shadow she casts.</summary>
    static void BuildShadow(Masks mk)
    {
        const int S = 4;
        int sw0 = (mk.W + S - 1) / S, sh0 = (mk.H + S - 1) / S;
        int pad = Math.Max(3, (int)MathF.Ceiling(sh0 * 0.2f));
        int sw = sw0 + 2 * pad, sh = sh0 + 2 * pad;
        var a = new float[sw * sh];
        for (int y = 0; y < mk.H; y++)
            for (int x = 0; x < mk.W; x++)
            {
                int i = y * mk.W + x;
                float cover = Math.Max(mk.Art[i * 4 + 3] / 255f, Mathf.Clamp(mk.Pad - mk.Out[i], 0, 1));
                a[(y / S + pad) * sw + x / S + pad] += cover / (S * S);
            }
        int r = Math.Max(1, (int)MathF.Round(sh0 * 0.07f));
        var tmp = new float[a.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlur(a, tmp, sw, sh, r, true);
            BoxBlur(tmp, a, sw, sh, r, false);
        }
        var data = new byte[sw * sh * 4];
        for (int i = 0; i < a.Length; i++)
        {
            data[i * 4] = data[i * 4 + 1] = data[i * 4 + 2] = 255;
            data[i * 4 + 3] = (byte)(255 * Mathf.Clamp(a[i], 0, 1));
        }
        var img = Image.CreateFromData(sw, sh, false, Image.Format.Rgba8, data);
        img.GenerateMipmaps();
        mk.Shadow = ImageTexture.CreateFromImage(img);
        mk.ShadowPad = new Vector2(pad / (float)sw0, pad / (float)sh0);
    }

    static void BoxBlur(float[] src, float[] dst, int w, int h, int r, bool horizontal)
    {
        float inv = 1f / (2 * r + 1);
        int len = horizontal ? w : h, lines = horizontal ? h : w;
        int step = horizontal ? 1 : w, lineStep = horizontal ? w : 1;
        for (int l = 0; l < lines; l++)
        {
            int o = l * lineStep;
            float acc = 0;
            for (int k = -r; k <= r; k++) acc += src[o + Math.Clamp(k, 0, len - 1) * step];
            for (int k = 0; k < len; k++)
            {
                dst[o + k * step] = acc * inv;
                acc += src[o + Math.Min(k + r + 1, len - 1) * step] - src[o + Math.Max(k - r, 0) * step];
            }
        }
    }

    /// <summary>
    /// The dressed hull on the padded canvas: her topsides band outside the rail (stripe, strakes, darkening toward
    /// the waterline, an ink edge), and over it the art with its rail repainted and its volume baked in.
    /// </summary>
    static Texture2D Dress(Masks mk, Livery lv)
    {
        int n = mk.W * mk.H;
        var px = new byte[n * 4];
        float pad = mk.Pad;
        float rimPx = Math.Max(3f, MathF.Round((mk.H - 2 * mk.Pad) * 0.085f));   // the band hull.py cut the -rim mask with
        for (int i = 0; i < n; i++)
        {
            int o = i * 4;
            // The topsides band: s runs 0 at the rail → 1 at the waterline edge.
            float dOut = mk.Out[i];
            float bandA = Mathf.Clamp(pad - dOut, 0, 1);
            float br = 0, bg = 0, bb = 0;
            if (bandA > 0)
            {
                float s = Mathf.Clamp(dOut / pad, 0, 1);
                float stripe = lv.Double ? Mathf.Max(Bump(s, 0.28f, 0.17f), Bump(s, 0.66f, 0.17f)) : Bump(s, 0.44f, 0.25f);
                var c = lv.Topsides.Lerp(lv.Stripe, stripe);
                float strake = 1 - 0.07f * Bump(Mathf.PosMod(s * 5f, 1f), 0.5f, 0.18f);                 // plank seams along her
                float light = Mathf.Lerp(1.06f, 0.7f, Mathf.SmoothStep(0.1f, 1f, s)) * strake;          // she turns under toward the water
                float edge = Mathf.SmoothStep(pad - 2.4f, pad - 0.7f, dOut);                            // the ink line at her waterline
                br = Mathf.Lerp(c.R * light, Ink.Black.R, edge);
                bg = Mathf.Lerp(c.G * light, Ink.Black.G, edge);
                bb = Mathf.Lerp(c.B * light, Ink.Black.B, edge);
            }
            // The art, its rail repainted in her topsides, the volume baked in.
            float aa = mk.Art[o + 3] / 255f;
            float r = 0, g = 0, b = 0;
            if (aa > 0)
            {
                r = mk.Art[o] / 255f; g = mk.Art[o + 1] / 255f; b = mk.Art[o + 2] / 255f;
                float w = mk.Rim[i];
                if (w > 0.004f)
                {
                    // Luminance-preserving recolour: the paint takes the art's light and shade, so planks, nails,
                    // guns and the ink outline survive. 0.44 is the art's typical band luminance.
                    float lum = r * 0.299f + g * 0.587f + b * 0.114f;
                    float k = Mathf.Pow(lum / 0.44f, 0.9f);
                    r = Mathf.Lerp(r, lv.Topsides.R * k, w);
                    g = Mathf.Lerp(g, lv.Topsides.G * k, w);
                    b = Mathf.Lerp(b, lv.Topsides.B * k, w);
                }
                // The rail cap catches the light; the deck sits in the bulwarks' shade for a band's width inboard.
                float t = mk.In[i] / rimPx;
                float v = Mathf.Lerp(0.86f, 1f, Mathf.SmoothStep(0.05f, 0.5f, t)) + 0.07f * Bump(t, 0.7f, 0.2f);
                v *= t < 0.95f ? Mathf.Lerp(1f, 0.86f, Mathf.SmoothStep(0.8f, 0.95f, t)) : Mathf.Lerp(0.86f, 1f, Mathf.SmoothStep(0.95f, 2.2f, t));
                r *= v; g *= v; b *= v;
            }
            // Art over band (straight alpha out).
            float a = aa + bandA * (1 - aa);
            if (a <= 0.001f) continue;
            px[o] = (byte)(255 * Mathf.Clamp((r * aa + br * bandA * (1 - aa)) / a, 0, 1));
            px[o + 1] = (byte)(255 * Mathf.Clamp((g * aa + bg * bandA * (1 - aa)) / a, 0, 1));
            px[o + 2] = (byte)(255 * Mathf.Clamp((b * aa + bb * bandA * (1 - aa)) / a, 0, 1));
            px[o + 3] = (byte)(255 * Mathf.Clamp(a, 0, 1));
        }
        var img = Image.CreateFromData(mk.W, mk.H, false, Image.Format.Rgba8, px);
        img.FixAlphaEdges();
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    static Image Rgba(Image img)
    {
        if (img.IsCompressed()) img.Decompress();
        if (img.HasMipmaps()) img.ClearMipmaps();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        return img;
    }
}
