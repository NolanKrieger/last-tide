namespace LastTide.Sim;

/// <summary>
/// Where the fish are (Nolan, 2026-09-28: "you should be able to fish anywhere but some places have more fish", "the
/// fishing grounds should be very subtle", and "the fish should be more common near land"). All water holds its
/// region's run of fish, richer toward the shore. Here and there a ground (a shoal feeding near the surface) holds
/// several times more, strongest at its heart, and grounds gather along the coasts. Grounds are a pure function of the
/// seed, the place and the day, like the drift ice: each midnight they settle somewhere new, there is nothing to save,
/// and every replay gets the same ones.
/// </summary>
public static class Fishing
{
    /// <summary>At most one ground a cell a day.</summary>
    public const double Cell = 600;
    /// <summary>Chance a cell holds a ground on a given day: by a coast, and out in open water (<see cref="InshoreReach"/> off).</summary>
    public const double GroundChanceInshore = 0.65, GroundChanceOpen = 0.2;
    /// <summary>The run at the water's edge, as a multiple of the open-water run, easing to 1 at <see cref="InshoreReach"/> m out.</summary>
    public const double InshoreBoost = 1.8;
    public const double InshoreReach = 500;
    public const double MinRadius = 70, MaxRadius = 150;
    /// <summary>The catch at a ground's heart, as a multiple of the region's run.</summary>
    public const double MinStrength = 2, MaxStrength = 3.5;
    /// <summary>The open ocean past the chart's edge: fished by no one, but thin.</summary>
    public const double BeyondRun = 0.7;

    public readonly record struct Ground(long Id, Vec2 Pos, double Radius, double Strength);

    /// <summary>The run of fish in each region's water (1 = an ordinary sea).</summary>
    public static double RegionRun(RegionType r) => r switch
    {
        RegionType.Shoals => 1.4,
        RegionType.IceReach => 1.3,
        RegionType.FogBanks => 1.2,
        RegionType.CorsairKeys => 1.2,
        RegionType.Mangrove => 1.1,
        RegionType.Maelstrom => 1.1,
        RegionType.Deep => 0.8,
        RegionType.StormReach => 0.9,
        RegionType.Volcanic => 0.6,   // ash and warm water
        RegionType.Sargasso => 0.35,  // the weed chokes the lines
        _ => 1.0,
    };

    /// <summary>The grounds of <paramref name="day"/> (midnights passed) whose water comes within <paramref name="radius"/> of a point.</summary>
    public static void GroundsNear(Map map, Vec2 p, double radius, long day, List<Ground> into)
    {
        double reach = radius + MaxRadius + Cell * 0.35;
        int x0 = CellOf(p.X - reach), x1 = CellOf(p.X + reach), y0 = CellOf(p.Y - reach), y1 = CellOf(p.Y + reach);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                ulong h = Hash(map.Seed, x, y, day);
                double U() { h = h * 6364136223846793005UL + 1442695040888963407UL; return (h >> 11) * (1.0 / 9007199254740992.0); }
                double roll = U();
                if (roll >= GroundChanceInshore) continue;   // not even by a coast: skip the shore measure
                var pos = new Vec2((x + 0.5 + (U() - 0.5) * 0.7) * Cell, (y + 0.5 + (U() - 0.5) * 0.7) * Cell);
                double r = MinRadius + (MaxRadius - MinRadius) * U();
                double strength = MinStrength + (MaxStrength - MinStrength) * U();
                if (pos.DistanceTo(p) > radius + r) continue;
                if (Map.InBounds(pos) && !map.Nav.IsSea(pos)) continue;   // a shoal feeds in the water, not over land
                long id = ((long)x << 36) ^ ((long)y << 12) ^ (day & 0xFFF);
                // Grounds gather by the coasts: the chance eases from the inshore to the open-water one with distance.
                double shore = Map.InBounds(pos) ? Shore.DistanceCached(map, id ^ GroundKeySalt, pos, InshoreReach) : InshoreReach;
                if (roll >= GroundChanceOpen * Shore.Openness(shore, GroundChanceInshore / GroundChanceOpen, InshoreReach)) continue;
                into.Add(new Ground(id, pos, r, strength));
            }
    }

    /// <summary>
    /// How many fish the water at <paramref name="p"/> holds, as a multiple of an ordinary sea: the region's run, raised
    /// toward a ground's strength the nearer its heart. <paramref name="scratch"/> is reused to spare an allocation.
    /// </summary>
    public static double Richness(Map map, Vec2 p, long day, List<Ground> scratch)
    {
        double run = Map.InBounds(p) ? RegionRun(map.RegionAt(p).Type) * Inshore(map, p) : BeyondRun;
        scratch.Clear();
        GroundsNear(map, p, 0, day, scratch);
        double best = 1;
        foreach (var g in scratch)
        {
            double d = g.Pos.DistanceTo(p) / g.Radius;
            if (d < 1) best = Math.Max(best, 1 + (g.Strength - 1) * (1 - d * d));
        }
        return run * best;
    }

    /// <summary>The run's rise toward land: <see cref="InshoreBoost"/> at the water's edge, 1 from <see cref="InshoreReach"/> m out.</summary>
    public static double Inshore(Map map, Vec2 p) => Shore.Openness(Shore.Distance(map, p, InshoreReach), InshoreBoost, InshoreReach);

    /// <summary>Keeps the grounds' cached shore distances apart from the floes' in <see cref="Shore"/>'s per-map cache.</summary>
    const long GroundKeySalt = 0x5F15_0000_0000_0000;

    static int CellOf(double v) => (int)Math.Floor(v / Cell);

    static ulong Hash(int seed, int x, int y, long day)
    {
        unchecked
        {
            ulong h = (ulong)(uint)(seed * 37 + 11) * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)x * 0xD6E8FEB86659FD93UL
                ^ (ulong)(uint)y * 0xA0761D6478BD642FUL ^ (ulong)day * 0xE7037ED1A0B428DBUL;
            h ^= h >> 32;
            h *= 0x8EBC6AF09C88C6E3UL;
            h ^= h >> 29;
            return h;
        }
    }
}
