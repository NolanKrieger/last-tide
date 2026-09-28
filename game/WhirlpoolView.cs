using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Whirlpools in ink: broken spiral arms winding clockwise into a dark eye, turning with the water, with a ring of foam
/// at the core. Drawn for every whirlpool near the view — the Maelstrom Straits' narrows and, beyond the chart, the
/// open sea's own — under the fog, so out there only those in sight show.
/// </summary>
public partial class WhirlpoolView : Node2D
{
    World world = null!;
    readonly List<Whirlpool> near = new();
    float time;
    const int Arms = 6, Steps = 36;
    readonly Vector2[] pts = new Vector2[Steps + 1];
    readonly Color[] cols = new Color[Steps + 1];

    public void Init(World w) => world = w;

    /// <summary>How many whirlpools are being drawn (the self-test reads it).</summary>
    public int Showing => near.Count;

    public override void _Process(double delta)
    {
        time += (float)delta;
        near.Clear();
        var cam = GetViewport().GetCamera2D();
        if (cam == null) return;
        var centre = Ink.M(cam.GetScreenCenterPosition());
        var half = GetViewportRect().Size / cam.Zoom * 0.5f / Ink.PxPerM;
        Whirlpools.Near(world.Map, centre, half.Length(), near);
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var w in near) DrawPool(w);
    }

    void DrawPool(Whirlpool w)
    {
        var c = Ink.V(w.Pos);
        float rim = (float)w.Radius * Ink.PxPerM, core = (float)w.Core * Ink.PxPerM;
        // The pattern turns at a slow share of the water's angular speed at the core, so it reads as turning, not spinning.
        float turn = time * (float)(w.Strength / w.Core) * 0.35f;
        float seed = (float)(w.Pos.X * 0.013 + w.Pos.Y * 0.007);
        // The water sinks toward the eye: washes of slate, deeper to the middle.
        for (int k = 0; k < 6; k++)
        {
            float f = 1 - k / 6f;
            DrawCircle(c, Mathf.Lerp(core, rim * 0.92f, f), new Color(0.20f, 0.27f, 0.33f, 0.035f + 0.02f * k));
        }
        float width = Mathf.Max(2.5f, rim * 0.011f);
        for (int a = 0; a < Arms; a++)
        {
            float phase = a * Mathf.Tau / Arms + seed + turn;
            float wind = 4.2f + 0.8f * Mathf.Sin(seed * 3 + a);
            for (int pass = 0; pass < 2; pass++)
            {
                // Ink arms, and between them thinner arms of foam.
                float off = pass == 0 ? 0 : Mathf.Pi / Arms;
                for (int i = 0; i <= Steps; i++)
                {
                    float t = i / (float)Steps;
                    float r = Mathf.Lerp(rim * 0.96f, core * 0.55f, Mathf.Pow(t, 0.8f));
                    float ang = phase + off + t * wind;
                    pts[i] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    // Faint at the rim, darkest toward the eye, broken into dashes that travel inward.
                    float dash = 0.5f + 0.5f * Mathf.Sin(t * 26 - time * 3.5f + a * 1.7f + pass * 2);
                    float body = Mathf.SmoothStep(0, 0.3f, t) * (1 - 0.45f * t) * dash;
                    cols[i] = pass == 0 ? Ink.Black with { A = body * 0.85f } : Ink.Paper with { A = body * 0.9f };
                }
                DrawPolylineColors(pts, cols, pass == 0 ? width : width * 0.7f, true);
            }
        }
        // The eye: a dark well with a broken ring of foam round it.
        DrawCircle(c, core * 0.62f, new Color(0.12f, 0.14f, 0.16f, 0.55f));
        DrawCircle(c, core * 0.34f, new Color(0.06f, 0.07f, 0.08f, 0.65f));
        int n = 28;
        for (int k = 0; k < n; k += 2)
        {
            float a0 = k * Mathf.Tau / n + turn * 1.6f, a1 = (k + 1) * Mathf.Tau / n + turn * 1.6f;
            DrawArc(c, core * 0.9f, a0, a1, 4, Ink.Paper with { A = 0.85f }, width * 1.2f, true);
        }
    }
}
