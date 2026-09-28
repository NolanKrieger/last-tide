namespace LastTide.Sim;

/// <summary>
/// A whirlpool: water turning round a core and drawn down into it. Anything afloat in its <see cref="Radius"/> drifts
/// with the water, swung round and pulled in; inside the <see cref="Core"/> the hull is wrenched and holed. Strength is
/// the swirl (m/s) at the rim of the core.
/// </summary>
public readonly record struct Whirlpool(Vec2 Pos, double Radius, double Strength)
{
    public double Core => Radius * 0.2;

    /// <summary>
    /// The water's velocity at a point (m/s): a vortex whose swirl falls off gently outside the core (as r^-0.7; solid-body
    /// rotation inside it), fading to nothing at the rim, with an inward pull of 60% of the swirl — enough that a ship
    /// that lies idle is drawn in, and one that makes sail away from the eye gets out. It turns clockwise on screen.
    /// </summary>
    public Vec2 Current(Vec2 p)
    {
        var d = p - Pos;
        double r = d.Length;
        if (r >= Radius || r < 1e-6) return Vec2.Zero;
        double swirl = Strength * (r < Core ? r / Core : Math.Pow(Core / r, 0.7));
        double fade = 1 - Smooth((r - Radius * 0.6) / (Radius * 0.4));
        var radial = d / r;
        return (radial.Perp * swirl - radial * (swirl * 0.6)) * fade;
    }

    static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>
/// Where the whirlpools are (GDD §5 "Beyond the chart", §19). The Maelstrom Straits keep theirs in the map, placed by
/// the generator in the narrows. Beyond the chart's edge they are procedural: one may turn in each
/// <see cref="OuterCell"/>-metre cell, none near the edge, more and bigger the further out, until by
/// <see cref="OuterFull"/> they crowd every cell and their pulls overlap — the sea's own end to the world.
/// </summary>
public static class Whirlpools
{
    public const double OuterCell = 420, OuterStart = 300, OuterFull = 3200;

    /// <summary>Every whirlpool whose reach comes within <paramref name="radius"/> of a point.</summary>
    public static void Near(Map map, Vec2 p, double radius, List<Whirlpool> into)
    {
        foreach (var w in map.Whirlpools)
            if (w.Pos.DistanceTo(p) <= w.Radius + radius) into.Add(w);
        if (Map.BeyondEdge(p) + radius + OuterCell < OuterStart) return;
        double reach = radius + 300;   // the biggest outer whirlpool's radius, and then some
        int x0 = CellOf(p.X - reach), x1 = CellOf(p.X + reach), y0 = CellOf(p.Y - reach), y1 = CellOf(p.Y + reach);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (Outer(map.Seed, x, y) is { } w && w.Pos.DistanceTo(p) <= w.Radius + radius) into.Add(w);
    }

    static int CellOf(double v) => (int)Math.Floor(v / OuterCell);

    /// <summary>The whirlpool turning in one outer cell, if any: a pure function of the seed and the cell.</summary>
    public static Whirlpool? Outer(int seed, int cx, int cy)
    {
        var centre = new Vec2((cx + 0.5) * OuterCell, (cy + 0.5) * OuterCell);
        double beyond = Map.BeyondEdge(centre);
        if (beyond < OuterStart) return null;
        double depth = Math.Clamp((beyond - OuterStart) / (OuterFull - OuterStart), 0, 1);
        ulong h = Hash(seed, cx, cy);
        double U() { h = h * 6364136223846793005UL + 1442695040888963407UL; return (h >> 11) * (1.0 / 9007199254740992.0); }
        if (U() > 0.12 + 0.88 * depth) return null;
        var pos = centre + new Vec2(U() - 0.5, U() - 0.5) * (OuterCell * 0.5);
        double radius = 80 + 90 * U() + 110 * depth;
        double strength = 2.5 + 2 * U() + 4 * depth;
        if (Map.BeyondEdge(pos) < OuterStart) return null;
        return new Whirlpool(pos, radius, strength);
    }

    static ulong Hash(int seed, int x, int y)
    {
        unchecked
        {
            ulong h = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)x * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(uint)y * 0x165667B19E3779F9UL;
            h ^= h >> 31;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 29;
            return h;
        }
    }
}
