using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Fishing in ink. A ground is barely there (Nolan, 2026-09-28: "the fishing grounds should be very subtle"): now and
/// then a faint ring where a fish rose, and once in a while a small fish arcing out of the water, drawn only in the
/// water round her, so a ground is found by sailing over it, not spied from afar. With her lines out, thin lines run
/// from both rails to bobbing floats.
/// </summary>
public partial class FishView : Node2D
{
    World world = null!;
    readonly List<Fishing.Ground> near = new();
    float time;
    Vec2 pos;
    double heading;
    /// <summary>How far round her the signs of a ground show (m).</summary>
    const double SignRange = 420;
    const float RisePeriod = 2.6f;
    static readonly Color Ring = new(0.18f, 0.24f, 0.28f);
    readonly Vector2[] arc = new Vector2[7];

    public void Init(World w) => world = w;

    /// <summary>Her interpolated pose this frame, for the lines.</summary>
    public void SetPose(Vec2 p, double h)
    {
        pos = p;
        heading = h;
    }

    /// <summary>How many grounds are showing signs (the self-test reads it).</summary>
    public int Showing => near.Count;

    public override void _Process(double delta)
    {
        time += (float)delta;
        near.Clear();
        Fishing.GroundsNear(world.Map, pos, SignRange, world.MidnightsPassed, near);
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var g in near) DrawRises(g);
        if (world.LinesOut) DrawLines();
    }

    void DrawRises(Fishing.Ground g)
    {
        // Fainter toward the edge of her range: the signs come up out of the paper as she closes.
        float fade = 1f - Mathf.Clamp((float)((g.Pos.DistanceTo(pos) - g.Radius) / SignRange), 0, 1);
        if (fade <= 0) return;
        int seed = (int)(g.Id & 0x7FFFFFFF);
        int rises = 2 + (int)g.Strength;   // 4–6 at a time: a richer ground stirs more
        for (int k = 0; k < rises; k++)
        {
            float t = time / RisePeriod + k / (float)rises + Ink.Jitter(seed, k, 3);
            int cycle = (int)Mathf.Floor(t);
            float f = t - cycle;
            // Each rise somewhere new in the ground, and not every cycle has one.
            if (Ink.Jitter(seed, k * 977 + cycle, 5) < 0.35f) continue;
            float ang = Ink.Jitter(seed, k * 977 + cycle, 7) * Mathf.Tau;
            float rad = Mathf.Sqrt(Ink.Jitter(seed, k * 977 + cycle, 11)) * (float)g.Radius * 0.85f;
            var at = Ink.V(g.Pos) + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad * Ink.PxPerM;
            float a = 0.22f * fade * (1 - f) * Mathf.Clamp(f * 6, 0, 1);
            float r = (1.2f + 3.2f * f) * Ink.PxPerM;
            DrawArc(at, r, 0, Mathf.Tau, 18, Ring with { A = a }, 1.1f, true);
            if (f < 0.5f) DrawArc(at, r * 0.45f, 0, Mathf.Tau, 12, Ring with { A = a * 0.7f }, 0.9f, true);
            // Once in a long while the fish itself shows: a little arc out of the water and back.
            if (f < 0.3f && Ink.Jitter(seed, k * 977 + cycle, 13) < 0.12f)
            {
                float jump = f / 0.3f, dir = Ink.Jitter(seed, cycle, 17) < 0.5f ? -1 : 1;
                for (int i = 0; i < arc.Length; i++)
                {
                    float s = i / (float)(arc.Length - 1) * jump;
                    arc[i] = at + new Vector2(dir * (s - 0.5f) * 3.2f, -Mathf.Sin(s * Mathf.Pi) * 1.4f) * Ink.PxPerM;
                }
                DrawPolyline(arc, Ink.Black with { A = 0.35f * fade }, 1.6f, true);
            }
        }
    }

    void DrawLines()
    {
        var fwd = new Vector2(Mathf.Cos((float)heading), Mathf.Sin((float)heading));
        var right = new Vector2(-fwd.Y, fwd.X);
        var hull = world.Ship.Hull;
        var c = Ink.V(pos);
        for (int side = -1; side <= 1; side += 2)
        {
            // From the rail just abaft amidships, out and a little astern to a float that bobs on the swell.
            var rail = c + (right * side * (float)hull.Beam * 0.5f - fwd * (float)hull.Length * 0.12f) * Ink.PxPerM;
            float bob = Mathf.Sin(time * 2.1f + side) * 0.35f;
            var buoy = c + (right * side * ((float)hull.Beam * 0.5f + 9f) - fwd * ((float)hull.Length * 0.25f + 4f + bob)) * Ink.PxPerM;
            var sag = rail.Lerp(buoy, 0.5f) - fwd * 1.2f * Ink.PxPerM;
            var line = Ink.Bezier(rail, sag, buoy, 8);
            DrawPolyline(line, Ink.Black with { A = 0.55f }, 1f, true);
            DrawCircle(buoy, 0.8f * Ink.PxPerM, new Color(0.72f, 0.22f, 0.16f));
            DrawArc(buoy, 0.8f * Ink.PxPerM, 0, Mathf.Tau, 12, Ink.Black with { A = 0.7f }, 1f, true);
            DrawArc(buoy, (1.6f + bob) * Ink.PxPerM, 0, Mathf.Tau, 16, Ring with { A = 0.18f }, 1f, true);
        }
    }
}
