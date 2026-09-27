using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The blank parchment over everything the chart has not inked yet (GDD §5, §15: "the chart draws itself").
/// Newly charted cells are not switched on: each one blooms in over ~0.6–1.6 s (a per-cell rate), so the spyglass
/// cone, the dawn and the edge of sight ink themselves in like wet ink spreading. The mask texture carries mipmaps so
/// the shader can read a blurred copy (the pencil-sketch zone just beyond the charted edge).
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

    public ImageTexture Texture => texture;
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
        if (Art.Tex("paper/paper") is { } paper) mat.SetShaderParameter("paper_tex", paper);
        if (Art.Tex("paper/noise") is { } noise) mat.SetShaderParameter("noise_tex", noise);
        Material = mat;
        version = mask.Version;
    }

    public override void _Process(double delta)
    {
        bool dirty = false;
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
                    dirty = true;
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
        QueueRedraw();
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
