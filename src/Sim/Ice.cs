namespace LastTide.Sim;

/// <summary>A floe of drifting ice: a rough disc of <see cref="Radius"/> metres. <see cref="Id"/> is stable per floe.</summary>
public readonly record struct Floe(long Id, Vec2 Pos, double Radius, double Heading);

/// <summary>
/// The Ice Reach's drift ice (GDD §5, §19): floes from a few metres to thirty across, each circling slowly round its own
/// patch of sea, so the ice shifts under the chart as the day goes by. Like the outer whirlpools they are a pure function
/// of the seed, the cell and the clock: nothing to save, the same ice on every replay. A hull that meets one fast is
/// holed (<see cref="World"/>'s ice tick).
/// </summary>
public static class Ice
{
    public const double Cell = 200;
    /// <summary>Floes are kept this far off every shore and harbour ring, so no coast or quay is iced in.</summary>
    public const double ShoreClear = 30;
    /// <summary>Floes thin toward land: <see cref="ShoreShare"/> of the pack at the shore, all of it <see cref="ShoreThin"/> m out.</summary>
    public const double ShoreThin = 300, ShoreShare = 0.3;

    public static void Near(Map map, Vec2 p, double radius, double time, List<Floe> into)
    {
        double reach = radius + 110;   // a floe's orbit plus its size
        int x0 = CellOf(p.X - reach), x1 = CellOf(p.X + reach), y0 = CellOf(p.Y - reach), y1 = CellOf(p.Y + reach);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var centre = new Vec2((x + 0.5) * Cell, (y + 0.5) * Cell);
                if (!Map.InBounds(centre) || map.RegionAt(centre).Type != RegionType.IceReach) continue;
                ulong h = Hash(map.Seed, x, y);
                double U() { h = h * 6364136223846793005UL + 1442695040888963407UL; return (h >> 11) * (1.0 / 9007199254740992.0); }
                int count = (int)(U() * 3.2);   // 0–3 a cell
                for (int k = 0; k < count; k++)
                {
                    var home = centre + new Vec2(U() - 0.5, U() - 0.5) * Cell;
                    double size = 4 + 26 * Math.Pow(U(), 2.2);
                    double orbit = 20 + 60 * U(), period = 300 + 500 * U(), phase = U() * Angles.Tau, turn = U() * Angles.Tau;
                    double keep = U();   // drawn with the rest before any skip, so a floe is the same floe from every query
                    double a = phase + Angles.Tau * time / period;
                    var pos = home + Vec2.FromAngle(a) * orbit;
                    if (pos.DistanceTo(p) > radius + size) continue;
                    // Its own patch lies in the Reach too, so by a border a floe strays over by its orbit at most.
                    if (map.RegionAt(home).Type != RegionType.IceReach) continue;
                    if (!Clear(map, pos, size)) continue;
                    // Nor on a whirlpool, nor off the chart (where the outer whirlpools turn): nothing overlaps them.
                    if (Map.BeyondEdge(pos) > -size || OnWhirlpool(map, pos, size)) continue;
                    // Thinner toward land (Nolan, 2026-09-28): a third as thick at the shore, the full pack 300 m out.
                    long id = ((long)x << 36) ^ ((long)y << 8) ^ k;
                    if (keep > Shore.Openness(Shore.DistanceCached(map, id, home, ShoreThin), ShoreShare, ShoreThin)) continue;
                    into.Add(new Floe(id, pos, size, turn + a * 0.3));
                }
            }
    }

    static bool OnWhirlpool(Map map, Vec2 pos, double size)
    {
        foreach (var w in map.Whirlpools)
            if (w.Pos.DistanceTo(pos) < w.Radius + size) return true;
        return false;
    }

    static bool Clear(Map map, Vec2 pos, double size)
    {
        foreach (var island in map.IslandsNear(pos, size + ShoreClear))
        {
            var (_, d, inside) = island.Closest(pos);
            if (inside || d < size + ShoreClear) return false;
        }
        foreach (var port in map.Ports)
            if (port.Harbor.DistanceTo(pos) < port.RingRadius + size + ShoreClear) return false;
        return true;
    }

    static int CellOf(double v) => (int)Math.Floor(v / Cell);

    static ulong Hash(int seed, int x, int y)
    {
        unchecked
        {
            ulong h = (ulong)(uint)(seed * 31 + 7) * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)x * 0xD6E8FEB86659FD93UL ^ (ulong)(uint)y * 0xA0761D6478BD642FUL;
            h ^= h >> 32;
            h *= 0xE7037ED1A0B428DBUL;
            h ^= h >> 29;
            return h;
        }
    }
}
