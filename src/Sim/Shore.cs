using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace LastTide.Sim;

/// <summary>
/// How far a point lies from land, for the hazards of the open sea that thin out toward it (Nolan, 2026-09-28:
/// "whirlpools shouldnt happen near land, also icebergs and sea monsters ... should be less common closer to land").
/// </summary>
public static class Shore
{
    /// <summary>Metres from <paramref name="p"/> to the nearest shore within <paramref name="reach"/> (0 on land; reach if none).</summary>
    public static double Distance(Map map, Vec2 p, double reach)
    {
        double best = reach;
        foreach (var island in map.IslandsNear(p, reach))
        {
            var (_, d, inside) = island.Closest(p);
            if (inside) return 0;
            best = Math.Min(best, d);
        }
        return best;
    }

    /// <summary>A hazard's share of its open-sea rate at <paramref name="distance"/> metres from land: <paramref name="nearShare"/>
    /// at the shore, rising smoothly to 1 at <paramref name="open"/> metres out.</summary>
    public static double Openness(double distance, double nearShare, double open)
    {
        double t = Math.Clamp(distance / open, 0, 1);
        return nearShare + (1 - nearShare) * t * t * (3 - 2 * t);
    }

    static readonly ConditionalWeakTable<Map, ConcurrentDictionary<long, double>> cache = new();

    /// <summary><see cref="Distance"/> for a fixed point known by a stable key (a floe's home), measured once per map.</summary>
    public static double DistanceCached(Map map, long key, Vec2 p, double reach) =>
        cache.GetValue(map, _ => new ConcurrentDictionary<long, double>()).GetOrAdd(key, _ => Distance(map, p, reach));
}
