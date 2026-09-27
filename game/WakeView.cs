using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Wakes, drawn under the hulls: a pale foam slick along the track and the two Kelvin arms as short ink
/// strokes that fan out, lengthen and fade. One trail per ship, sampled ten times a second; every trail
/// of this view is a single triangle batch. <see cref="InkColor"/> is the player's cosmetic wake ink.
/// </summary>
public partial class WakeView : Node2D
{
    struct Sample
    {
        public Vector2 Pos, Fwd;
        public float Age, Strength, Speed, HalfBeam;
    }

    sealed class Trail
    {
        public readonly Sample[] S = new Sample[Cap];
        public int Head, Count, Tick;
        public bool Fresh;
    }

    const float Life = 5.5f;
    const int Cap = 64;
    const int Every = 3;   // ticks between samples
    readonly Dictionary<int, Trail> trails = new();
    readonly List<int> dead = new();
    readonly InkBatch mesh = new();
    public Color InkColor = Ink.Black;

    /// <summary>Called once per physics tick per ship.</summary>
    public void Record(Ship ship)
    {
        if (!trails.TryGetValue(ship.Id, out var t)) trails[ship.Id] = t = new Trail();
        t.Fresh = true;
        if (t.Tick++ % Every != 0) return;
        if (ship.Speed < 0.6 || ship.Sunk) return;   // a sunk ship keeps her last velocity but no longer moves
        var fwd = Ink.V(ship.Forward).Normalized();
        t.Head = (t.Head + 1) % Cap;
        t.S[t.Head] = new Sample
        {
            Pos = Ink.V(ship.Pos - ship.Forward * (ship.Hull.Length * 0.46)),
            Fwd = fwd,
            Strength = (float)Math.Min(1, ship.Speed / 10.0),
            Speed = (float)ship.Speed * Ink.PxPerM,
            HalfBeam = (float)ship.Hull.Beam * Ink.PxPerM * 0.45f,
        };
        t.Count = Math.Min(Cap, t.Count + 1);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        dead.Clear();
        foreach (var (id, t) in trails)
        {
            int alive = 0;
            for (int k = 0; k < t.Count; k++)
            {
                int i = (t.Head - k + Cap) % Cap;
                t.S[i].Age += dt;
                if (t.S[i].Age < Life) alive = k + 1;
            }
            t.Count = alive;
            if (alive == 0 && !t.Fresh) dead.Add(id);
            t.Fresh = false;
        }
        foreach (var id in dead) trails.Remove(id);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var xf = GetViewport().GetFinalTransform() * GetGlobalTransformWithCanvas();
        float px = 1f / Mathf.Max(1e-4f, xf.X.Length());
        mesh.Clear();
        mesh.Px = px;
        var foam = new Color(0.985f, 0.975f, 0.94f);
        // One set of buffers for every trail (stackalloc inside the loop grew the stack with each ship).
        Span<Vector2> left = stackalloc Vector2[Cap];
        Span<Vector2> right = stackalloc Vector2[Cap];
        Span<Color> cl = stackalloc Color[Cap];
        Span<Vector2> crest = stackalloc Vector2[3];
        foreach (var t in trails.Values)
        {
            if (t.Count < 2) continue;
            // Foam slick: a pale band along the track that widens and thins out.
            for (int k = 0; k < t.Count; k++)
            {
                ref var s = ref t.S[(t.Head - k + Cap) % Cap];
                float u = s.Age / Life;
                var perp = new Vector2(-s.Fwd.Y, s.Fwd.X);
                float w = s.HalfBeam * (0.4f + 0.9f * u);
                left[k] = s.Pos + perp * w;
                right[k] = s.Pos - perp * w;
                float patch = 0.55f + 0.45f * Mathf.Sin((t.Tick / Every - k) * 1.9f);
                cl[k] = foam with { A = 0.32f * s.Strength * Mathf.Pow(1 - u, 1.6f) * patch };
            }
            for (int k = 0; k < t.Count - 1; k++)
                mesh.Quad(left[k], left[k + 1], right[k + 1], right[k], cl[k], cl[k + 1], cl[k + 1], cl[k]);
            // Kelvin arms: feathered ink strokes at ±19.5°, fanning out with age.
            for (int k = 0; k < t.Count; k++)
            {
                ref var s = ref t.S[(t.Head - k + Cap) % Cap];
                float u = s.Age / Life;
                float a = (1 - u) * (1 - u) * 0.6f * s.Strength;
                if (a < 0.02f) continue;
                var perp = new Vector2(-s.Fwd.Y, s.Fwd.X);
                float spread = s.HalfBeam + s.Age * s.Speed * 0.354f;
                float len = (3f + 7f * u) * Mathf.Max(0.6f, s.Strength) * Mathf.Max(1f, s.HalfBeam / 10f);
                var col = InkColor with { A = a };
                float w = Mathf.Max(0.9f * px, 1.1f + 1.2f * u);
                for (int side = -1; side <= 1; side += 2)
                {
                    var c = s.Pos + perp * (side * spread);
                    // A wavelet crest: slanted back from the arm, curled a little.
                    var d = (perp * side * 0.55f - s.Fwd * 0.83f).Normalized();
                    var bend = new Vector2(-d.Y, d.X) * (side * len * 0.18f);
                    crest[0] = c - d * len * 0.5f;
                    crest[1] = c + bend;
                    crest[2] = c + d * len * 0.5f;
                    mesh.PolylineWidths(crest, w * 0.5f, w, col with { A = a * 0.5f }, col);
                }
            }
        }
        mesh.Flush(this);
    }
}
