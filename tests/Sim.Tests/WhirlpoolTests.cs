using LastTide.Sim;

namespace Sim.Tests;

/// <summary>
/// Nolan, 2026-09-28: "i saw a whirlpool that overlapped an island ... it should never overlap anything" and "whirlpools
/// shouldnt happen near land, also icebergs and sea monsters ... should be less common closer to land".
/// </summary>
public class WhirlpoolTests
{
    [Fact]
    public void TheMaelstromsWhirlpoolsKeepWellClearOfLandAndOfEachOther()
    {
        int total = 0, maps = 0;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 11, 21 })
        {
            var map = MapGen.Generate(seed);
            maps++;
            total += map.Whirlpools.Count;
            foreach (var w in map.Whirlpools)
            {
                double shore = Shore.Distance(map, w.Pos, w.Radius + MapGen.WhirlpoolShoreMargin + 50);
                Assert.True(shore >= w.Radius + MapGen.WhirlpoolShoreMargin - 0.5,
                    $"seed {seed}: a whirlpool of r {w.Radius:F0} m lies {shore - w.Radius:F0} m from land (wants {MapGen.WhirlpoolShoreMargin})");
                Assert.Equal(RegionType.Maelstrom, map.RegionAt(w.Pos).Type);
                foreach (var o in map.Whirlpools)
                    if (o != w) Assert.True(w.Pos.DistanceTo(o.Pos) >= w.Radius + o.Radius + 300, $"seed {seed}: two whirlpools crowd each other");
                foreach (var p in map.Ports)
                    Assert.True(w.Pos.DistanceTo(p.Harbor) >= w.Radius + p.RingRadius, $"seed {seed}: {p.Name}'s ring lies in a whirlpool");
                foreach (var t in map.Treasures)
                    Assert.True(w.Pos.DistanceTo(t.DigRing) >= w.Radius + 70, $"seed {seed}: a dig ring lies in a whirlpool");
                var floes = new List<Floe>();
                Ice.Near(map, w.Pos, w.Radius + 40, 0, floes);
                Assert.DoesNotContain(floes, f => f.Pos.DistanceTo(w.Pos) < w.Radius + f.Radius);
            }
        }
        Assert.True(total >= maps * 3, $"the Straits still turn: {total} whirlpools on {maps} charts");
    }

    [Fact]
    public void BeyondTheChartNoTwoWhirlpoolsTouchAndNoneComesNearTheChart()
    {
        var seen = new List<Whirlpool>();
        int cells = (int)Math.Ceiling(4000 / Whirlpools.OuterCell);
        int cx0 = (int)Math.Floor(Map.HalfW / Whirlpools.OuterCell) - 1, cy0 = (int)Math.Floor(-Map.HalfH / Whirlpools.OuterCell);
        for (int cy = cy0; cy <= cy0 + 12; cy++)
            for (int cx = cx0; cx <= cx0 + cells; cx++)
                if (Whirlpools.Outer(4, cx, cy) is { } w)
                {
                    Assert.True(Map.BeyondEdge(w.Pos) - w.Radius >= Whirlpools.EdgeMargin - 1e-6, $"a whirlpool's rim {Map.BeyondEdge(w.Pos) - w.Radius:F0} m past the edge");
                    foreach (var o in seen) Assert.True(w.Pos.DistanceTo(o.Pos) >= w.Radius + o.Radius, "two whirlpools overlap");
                    seen.Add(w);
                }
        Assert.True(seen.Count > 20, $"only {seen.Count} outer whirlpools sampled");
        Assert.Contains(seen, w => w.Radius > 150);   // far out they are still big
    }

    [Fact]
    public void DriftIceThinsOutTowardLand()
    {
        var map = MapGen.Generate(4);
        var centre = map.RegionOf(RegionType.IceReach).Seed;
        const double half = 1500, step = 50;
        // Sea area and floes by distance from land: within 100 m, and 300 m or more out.
        double nearArea = 0, farArea = 0;
        int nearFloes = 0, farFloes = 0;
        for (double y = centre.Y - half; y < centre.Y + half; y += step)
            for (double x = centre.X - half; x < centre.X + half; x += step)
            {
                var p = new Vec2(x, y);
                if (!Map.InBounds(p) || map.RegionAt(p).Type != RegionType.IceReach) continue;
                double d = Shore.Distance(map, p, 320);
                if (d <= 0) continue;
                if (d < 100) nearArea += step * step; else if (d >= 300) farArea += step * step;
            }
        var floes = new List<Floe>();
        Ice.Near(map, centre, half, 0, floes);
        foreach (var f in floes)
        {
            if (map.RegionAt(f.Pos).Type != RegionType.IceReach) continue;
            double d = Shore.Distance(map, f.Pos, 320);
            if (d < 100) nearFloes++; else if (d >= 300) farFloes++;
        }
        Assert.True(nearArea > 0 && farArea > 0, "the sample holds both coastal and open water");
        double nearDensity = nearFloes / nearArea, farDensity = farFloes / farArea;
        Assert.True(nearDensity < farDensity * 0.6, $"floes per km²: {nearDensity * 1e6:F1} within 100 m of land, {farDensity * 1e6:F1} 300 m out");
    }

    [Fact]
    public void DeepWaterBeastsRiseLessOftenNearLand()
    {
        var w = World.NewRun(4, populate: false);
        w.Ship.Pos = w.Map.StartPort.Harbor;   // a harbour lies just off its coast
        Assert.True(w.ShoreFactor(MonsterType.Kraken) < 0.6, $"by the quay the Kraken rises at {w.ShoreFactor(MonsterType.Kraken):F2}");
        Assert.Equal(1, w.ShoreFactor(MonsterType.Crocodile));   // the bank is the crocodile's home
        Assert.Equal(1, w.ShoreFactor(MonsterType.Siren));
        w.Ship.Pos = new Vec2(Map.HalfW + 2000, 0);   // open sea beyond the chart: no land for miles
        Assert.Equal(1, w.ShoreFactor(MonsterType.Kraken), 6);
        Assert.Equal(1, w.ShoreFactor(MonsterType.GhostShip), 6);
        Assert.InRange(Shore.Openness(0, 0.3, 400), 0.3, 0.3);
        Assert.True(Shore.Openness(200, 0.3, 400) is > 0.3 and < 1);
    }
}
