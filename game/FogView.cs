using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The blank parchment over everything the chart has not inked yet (GDD §5, §15: "the chart draws itself").
/// Newly charted cells are not switched on: each one blooms in over ~0.6–1.6 s (a per-cell rate), so the spyglass
/// cone, the dawn and the edge of sight ink themselves in like wet ink spreading. The mask texture carries mipmaps so
/// the shader can read a blurred copy (the pencil-sketch zone just beyond the charted edge).
/// The fog also clears a live circle of her current sight every frame (<see cref="Sight"/>), charted or not: with no
/// cartographer aboard nothing is inked, but she still sees the sea around her (Nolan, 2026-09-27). The shader draws
/// that circle analytically, so it works off the chart's rectangle too; the sea layer, which skips its work under
/// blank paper, reads <see cref="SeaTexture"/>: the mask with the circle stamped in.
/// </summary>
public partial class FogView : Node2D
{
    RevealMask mask = null!;
    ImageTexture texture = null!;
    Image image = null!;
    byte[] target = Array.Empty<byte>();
    byte[] shown = Array.Empty<byte>();
    float[] level = Array.Empty<float>();
    readonly List<int> animating = new();
    int version = -1;
    // The sea's copy of the mask with the live circle stamped in (no mipmaps: the sea only asks "is anything here", so
    // a cell counts from its first touch of ink and the copy changes only when a cell is first touched or she moves).
    ImageTexture seaTexture = null!;
    Image seaImage = null!;
    byte[] seaBase = Array.Empty<byte>(), seaBuf = Array.Empty<byte>();
    (int X, int Y, int R) seaStamp = (int.MinValue, 0, 0);
    Vector2 sightAt;       // metres
    float sightRadius;     // metres; 0 = none

    /// <summary>The charted mask alone (the chart page reads this: it shows only what was inked).</summary>
    public ImageTexture Texture => texture;
    /// <summary>The mask with her live sight stamped in, for the sea layer.</summary>
    public ImageTexture SeaTexture => seaTexture;
    /// <summary>The live circle's centre (m) and radius (m) as last drawn (the self-test reads it).</summary>
    public (Vector2 At, float Radius) SightNow => (sightAt, sightRadius);

    /// <summary>Whether the sea layer draws at a point (charted, or inside her sight), as last stamped (the self-test reads it).</summary>
    public bool SeaDrawsAt(Vec2 p)
    {
        int x = Math.Clamp(RevealMask.ToX(p.X), 0, mask.W - 1), y = Math.Clamp(RevealMask.ToY(p.Y), 0, mask.H - 1);
        return seaBuf[y * mask.W + x] > 0;
    }

    /// <summary>Metres beyond the sight radius the sea keeps drawing, under the fog's soft, ragged edge.</summary>
    const float SeaMargin = 40f;

    /// <summary>Main sets her live sight each frame: the interpolated ship position and the current vision radius.</summary>
    public void Sight(Vec2 centre, double radius)
    {
        sightAt = new Vector2((float)centre.X, (float)centre.Y);
        sightRadius = (float)Math.Max(0, radius);
    }

    /// <summary>Cells still inking themselves in (the self-test checks the bloom runs and finishes).</summary>
    public int Animating => animating.Count;

    public void Init(RevealMask m, int seed)
    {
        mask = m;
        ZIndex = 5;
        int n = mask.W * mask.H;
        target = new byte[n];
        mask.FillTexture(target);
        shown = (byte[])target.Clone();        // what was charted before the view existed is simply there
        level = new float[n];
        for (int i = 0; i < n; i++) level[i] = shown[i];
        image = Image.CreateFromData(mask.W, mask.H, false, Image.Format.L8, shown);
        image.GenerateMipmaps();
        texture = ImageTexture.CreateFromImage(image);
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/fog.gdshader") };
        mat.SetShaderParameter("reveal", texture);
        mat.SetShaderParameter("map_half", new Vector2((float)Map.HalfW, (float)Map.HalfH));
        mat.SetShaderParameter("px_per_m", Ink.PxPerM);
        mat.SetShaderParameter("seed", seed);
        mat.SetShaderParameter("sight", Vector3.Zero);
        if (Art.Tex("paper/paper") is { } paper) mat.SetShaderParameter("paper_tex", paper);
        if (Art.Tex("paper/noise") is { } noise) mat.SetShaderParameter("noise_tex", noise);
        Material = mat;
        version = mask.Version;
        seaBase = (byte[])shown.Clone();
        seaBuf = (byte[])shown.Clone();
        seaImage = Image.CreateFromData(mask.W, mask.H, false, Image.Format.L8, seaBuf);
        seaTexture = ImageTexture.CreateFromImage(seaImage);
    }

    public override void _Process(double delta)
    {
        bool dirty = false, seaDirty = false;
        if (mask.Version != version)
        {
            version = mask.Version;
            mask.FillTexture(target);
            for (int i = 0; i < target.Length; i++)
            {
                if (target[i] > shown[i] && level[i] <= 0)
                {
                    level[i] = 0.5f;          // a first touch of ink; it spreads from here
                    animating.Add(i);
                }
                else if (target[i] < shown[i])   // a loaded save can only be smaller if the world was swapped
                {
                    shown[i] = target[i];
                    level[i] = target[i];
                    seaBase[i] = target[i];
                    dirty = seaDirty = true;
                }
            }
        }
        if (animating.Count > 0)
        {
            float dt = (float)Math.Min(delta, 0.1);
            for (int k = animating.Count - 1; k >= 0; k--)
            {
                int i = animating[k];
                uint h = (uint)i * 2654435761u;
                float seconds = 0.6f + (h >> 24) / 255f;               // 0.6–1.6 s, fixed per cell
                level[i] = Math.Min(255f, level[i] + 255f * dt / seconds);
                byte b = (byte)level[i];
                if (b != shown[i]) { shown[i] = b; dirty = true; }
                if (b > 0 && seaBase[i] == 0) { seaBase[i] = 255; seaDirty = true; }
                if (level[i] >= 255f)
                {
                    animating[k] = animating[^1];
                    animating.RemoveAt(animating.Count - 1);
                }
            }
        }
        if (dirty)
        {
            image.SetData(mask.W, mask.H, false, Image.Format.L8, shown);
            image.GenerateMipmaps();
            texture.Update(image);
        }
        ((ShaderMaterial)Material).SetShaderParameter("sight", new Vector3(sightAt.X, sightAt.Y, sightRadius));
        UpdateSea(seaDirty);
        QueueRedraw();
    }

    /// <summary>
    /// Re-stamps the sea's mask when the charted cells changed or the live circle moved by a cell. Cells beyond the
    /// rectangle are clamped onto its border (the sea samples clamp-to-edge), so off the chart the sea still draws
    /// inside her sight.
    /// </summary>
    void UpdateSea(bool maskChanged)
    {
        var stamp = sightRadius <= 0 ? (int.MinValue, 0, 0)
            : (RevealMask.ToX(sightAt.X), RevealMask.ToY(sightAt.Y), (int)Math.Ceiling((sightRadius + SeaMargin) / RevealMask.Cell));
        if (!maskChanged && stamp == seaStamp) return;
        seaStamp = stamp;
        Buffer.BlockCopy(seaBase, 0, seaBuf, 0, seaBase.Length);
        if (stamp.Item1 != int.MinValue)
        {
            var (cx, cy, r) = stamp;
            int r2 = r * r;
            for (int y = cy - r; y <= cy + r; y++)
            {
                int dy = y - cy, row = Math.Clamp(y, 0, mask.H - 1) * mask.W;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    int dx = x - cx;
                    if (dx * dx + dy * dy > r2) continue;
                    seaBuf[row + Math.Clamp(x, 0, mask.W - 1)] = 255;
                }
            }
        }
        seaImage.SetData(mask.W, mask.H, false, Image.Format.L8, seaBuf);
        seaTexture.Update(seaImage);
    }

    public override void _Draw()
    {
        var inv = GetCanvasTransform().AffineInverse();
        var size = GetViewportRect().Size;
        var tl = inv * Vector2.Zero;
        var br = inv * size;
        var pad = (br - tl) * 0.05f;
        DrawRect(new Rect2(tl - pad, br - tl + pad * 2), Colors.White);
    }
}
