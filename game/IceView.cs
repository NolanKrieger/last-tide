using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The Ice Reach's drift ice: each floe a rough white plate with an inked rim, a pale blue shadow under its sunward edge
/// and a thin wash of broken water round it, drifting as the sim moves it. Under the fog, like everything at sea.
/// </summary>
public partial class IceView : Node2D
{
    World world = null!;
    readonly List<Floe> near = new();
    const int Sides = 11;
    readonly Vector2[] rim = new Vector2[Sides];
    readonly Vector2[] loop = new Vector2[Sides + 1];

    public void Init(World w) => world = w;

    public int Showing => near.Count;

    public override void _Process(double delta)
    {
        near.Clear();
        var cam = GetViewport().GetCamera2D();
        if (cam == null) return;
        var centre = Ink.M(cam.GetScreenCenterPosition());
        var half = GetViewportRect().Size / cam.Zoom * 0.5f / Ink.PxPerM;
        Ice.Near(world.Map, centre, half.Length(), world.Time, near);
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var f in near)
        {
            var c = Ink.V(f.Pos);
            float r = (float)f.Radius * Ink.PxPerM;
            for (int i = 0; i < Sides; i++)
            {
                // A ragged plate: the same shape every frame, from the floe's id.
                float a = (float)f.Heading + i * Mathf.Tau / Sides;
                float k = 0.72f + 0.34f * Ink.Jitter((int)(f.Id & 0xFFFF), i, (int)(f.Id >> 16));
                rim[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * k;
            }
            for (int i = 0; i < Sides; i++) loop[i] = rim[i] + new Vector2(0.6f, 0.9f) * Ink.PxPerM;
            loop[Sides] = loop[0];
            DrawColoredPolygon(loop[..Sides], new Color(0.36f, 0.52f, 0.64f, 0.45f));   // the drowned foot, offset down-right
            DrawColoredPolygon(rim, new Color(0.90f, 0.95f, 0.98f));
            // A lit upper face, inset toward the north-west light.
            for (int i = 0; i < Sides; i++) loop[i] = c + (rim[i] - c) * 0.62f - new Vector2(0.5f, 0.7f) * Ink.PxPerM;
            DrawColoredPolygon(loop[..Sides], new Color(1f, 1f, 1f, 0.8f));
            for (int i = 0; i < Sides; i++) loop[i] = rim[i];
            loop[Sides] = rim[0];
            DrawPolyline(loop, Ink.Black with { A = 0.85f }, Mathf.Max(1.6f, r * 0.05f), true);
            // A crack or two across the bigger plates.
            if (f.Radius > 12)
                DrawLine(rim[1].Lerp(c, 0.2f), rim[6].Lerp(c, 0.35f), Ink.Black with { A = 0.3f }, 1f, true);
        }
    }
}
