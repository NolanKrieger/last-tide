using System.Diagnostics;
using LastTide.Sim;

namespace Sim.Tests;

public class MapGenTests
{
    static void AssertValid(Map map, int seed)
    {
        Assert.Equal(9, map.Regions.Select(r => r.Type).Distinct().Count());
        Assert.InRange(map.Ports.Count, MapGen.MinPorts, MapGen.MaxPorts);
        Assert.InRange(map.Ports.Count(p => p.Secret), MapGen.MinCoves, MapGen.MaxCoves);
        Assert.InRange(map.Treasures.Count, 8, 14);
        Assert.InRange(map.Wrecks.Count, 3, 6);
        Assert.Equal(RegionType.TradeIsles, map.StartPort.Region);
        Assert.Equal(Faction.FreeTraders, map.StartPort.Faction);
        Assert.All(map.Ports, p => Assert.InRange(p.Produces.Count, 1, 6));
        Assert.All(map.Ports, p => Assert.True(p.Consumes.Count >= 2, $"seed {seed} port {p.Name} consumes {p.Consumes.Count}"));
        Assert.All(map.Ports, p => Assert.True(map.Nav.IsSea(p.Harbor), $"seed {seed}: harbour of {p.Name} is on land"));
        Assert.All(map.Wrecks, w => Assert.Equal(RegionType.Sargasso, map.RegionAt(w.Pos).Type));
        Assert.Equal(map.Ports.Count, map.Ports.Select(p => p.Name).Distinct().Count());

        // Every port reachable by sea from the start, and a profitable route within half a day.
        var d = map.Nav.Distances(map.StartPort.Harbor);
        Assert.All(map.Ports, p => Assert.True(map.Nav.DistanceAt(d, p.Harbor) >= 0, $"seed {seed}: {p.Name} unreachable"));
        bool route = map.StartPort.Produces.Any(g => map.Ports.Any(p => p != map.StartPort && !p.Produces.Contains(g)
            && map.Nav.DistanceAt(d, p.Harbor) is >= 0 and <= MapGen.StartRouteRange));
        Assert.True(route, $"seed {seed}: no start route");

        // Islands never overlap and keep their channels.
        for (int i = 0; i < map.Islands.Count; i++)
            for (int j = i + 1; j < map.Islands.Count; j++)
            {
                var a = map.Islands[i];
                var b = map.Islands[j];
                if (a.Centre.DistanceTo(b.Centre) > a.BoundRadius + b.BoundRadius + 60) continue;
                double gap = a.Points.Min(p => b.Closest(p).Dist);
                Assert.True(gap >= 30, $"seed {seed}: islands {i} and {j} are {gap:F0} m apart");
            }
    }

    [Fact]
    public void AThousandSeedsValidate()
    {
        var sw = Stopwatch.StartNew();
        int attempts = 0, crown = 0, free = 0, brethren = 0, ports = 0;
        for (int seed = 1; seed <= 1000; seed++)
        {
            var map = MapGen.Generate(seed);
            AssertValid(map, seed);
            attempts += map.Attempts;
            foreach (var p in map.Ports.Where(p => !p.Secret))
            {
                ports++;
                if (p.Faction == Faction.Crown) crown++;
                else if (p.Faction == Faction.FreeTraders) free++;
                else brethren++;
            }
        }
        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds < 240, $"1000 maps took {sw.Elapsed.TotalSeconds:F0} s");
        Assert.True(attempts < 2500, $"too many retries: {attempts} attempts for 1000 maps");
        Assert.InRange(crown / (double)ports, 0.36, 0.52);
        Assert.InRange(free / (double)ports, 0.30, 0.48);
        Assert.InRange(brethren / (double)ports, 0.10, 0.24);
    }

    [Fact]
    public void SameSeedSameMap()
    {
        var a = MapGen.Generate(77);
        var b = MapGen.Generate(77);
        Assert.Equal(a.Ports.Select(p => (p.Name, p.Harbor)), b.Ports.Select(p => (p.Name, p.Harbor)));
        Assert.Equal(a.Islands.Count, b.Islands.Count);
        Assert.NotEqual(a.Ports[0].Harbor, MapGen.Generate(78).Ports[0].Harbor);
    }

    [Fact]
    public void GoodsMeetTheGlobalConstraints()
    {
        for (int seed = 1; seed <= 30; seed++)
        {
            var map = MapGen.Generate(seed);
            foreach (var good in Goods.All)
            {
                int producers = map.Ports.Count(p => p.Produces.Contains(good.Id));
                int consumers = map.Ports.Count(p => p.Consumes.Contains(good.Id));
                if (good.Group == GoodGroup.Rare)
                {
                    Assert.True(map.Ports.Where(p => p.Produces.Contains(good.Id)).All(p => p.Secret), $"{good.Key} produced outside a cove");
                    Assert.True(consumers >= 1, $"seed {seed}: nobody buys {good.Key}");
                    continue;
                }
                Assert.True(producers >= 2, $"seed {seed}: {good.Key} has {producers} producers");
                Assert.True(consumers >= 3, $"seed {seed}: {good.Key} has {consumers} consumers");
                Assert.All(map.Ports, p => Assert.False(p.Produces.Contains(good.Id) && p.Consumes.Contains(good.Id)));
            }
            foreach (var g in Goods.All.Where(g => g.Group == GoodGroup.Luxury).Select(g => g.Id))
            {
                var producers = map.Ports.Where(p => p.Produces.Contains(g)).ToList();
                var consumers = map.Ports.Where(p => p.Consumes.Contains(g)).ToList();
                double near = consumers.Count(c => producers.Any(p => p.Harbor.DistanceTo(c.Harbor) < 1500));
                Assert.True(near <= 3, $"seed {seed}: {g} sells next door to its producers");
            }
            Assert.All(map.Ports.Where(p => p.Secret), p => Assert.Contains(p.Produces, g => Goods.IsRare(g)));
        }
    }

    [Fact]
    public void RegionsCoverTheMapAndMangroveIsDense()
    {
        var map = MapGen.Generate(5);
        var seen = new HashSet<RegionType>();
        for (double x = -Map.HalfW; x <= Map.HalfW; x += 150)
            for (double y = -Map.HalfH; y <= Map.HalfH; y += 150)
                seen.Add(map.RegionAt(new Vec2(x, y)).Type);
        Assert.Equal(9, seen.Count);
        int mangrove = map.Islands.Count(i => i.Region == RegionType.Mangrove);
        int deep = map.Islands.Count(i => i.Region == RegionType.Deep);
        Assert.True(mangrove > deep * 4, $"mangrove {mangrove} vs deep {deep}");
        Assert.True(map.Islands.Count > 90, $"only {map.Islands.Count} islands");
    }

    [Fact]
    public void RevealMaskPaintsCirclesAndRoundTrips()
    {
        var m = new RevealMask();
        Assert.Equal(0, m.RevealedCells);
        int added = m.Paint(Vec2.Zero, 100);
        Assert.InRange(added, 180, 220);   // π·8² cells of 12.5 m
        Assert.True(m.IsRevealed(new Vec2(50, 50)));
        Assert.False(m.IsRevealed(new Vec2(200, 0)));
        Assert.Equal(0, m.Paint(Vec2.Zero, 100));
        var copy = new RevealMask();
        copy.Load(m.Bytes);
        Assert.Equal(m.RevealedCells, copy.RevealedCells);
        Assert.True(copy.IsRevealed(new Vec2(50, 50)));
        Assert.InRange(m.Fraction, 0.0009, 0.0015);
    }

    [Fact]
    public void ARunStartsInTheHarbourWithTheHomeRegionCharted()
    {
        var w = World.NewRun(3);
        var start = w.Map.StartPort;
        Assert.True(start.InHarbor(w.Ship.Pos));
        Assert.True(w.Map.Nav.IsSea(w.Ship.Pos));
        Assert.True(w.Reveal.IsRevealed(w.Ship.Pos));
        Assert.True(w.Reveal.IsRevealed(w.Map.RegionOf(RegionType.TradeIsles).Seed));
        var farRegion = w.Map.Regions.First(r => r.Type != RegionType.TradeIsles && r.Seed.DistanceTo(start.Harbor) > 2500);
        Assert.False(w.Reveal.IsRevealed(farRegion.Seed));
        Assert.True(w.Reveal.Fraction > 0.05 && w.Reveal.Fraction < 0.3, $"charted {w.Reveal.Fraction:P0}");
    }

    [Fact]
    public void SailingInksTheChartAndTheChartSurvivesASave()
    {
        var w = World.NewRun(3);
        double before = w.Reveal.Fraction;
        w.Ship.SailTarget = 3;
        w.Ship.SailFraction = 1;
        w.Islands.Clear();
        w.Ship.Heading = (w.Map.RegionOf(RegionType.Deep).Seed - w.Ship.Pos).Angle;
        w.Wind.SetFixed(w.Ship.Heading, 8);
        for (int i = 0; i < 30 * 200; i++) w.Tick(new ShipInput(0, 0));
        Assert.True(w.Reveal.Fraction > before + 0.01, $"{before:P1} → {w.Reveal.Fraction:P1}");
        var loaded = World.LoadJson(w.SaveJson());
        Assert.Equal(w.Reveal.RevealedCells, loaded.Reveal.RevealedCells);
        Assert.Equal(w.Hash(), loaded.Hash());
    }

    [Fact]
    public void TheChartHasEdgesAndBigHullsCannotEnterTheMangroves()
    {
        var w = World.NewRun(9, "galleon");
        w.Islands.Clear();
        w.Ship.Pos = new Vec2(Map.HalfW - 100, 0);
        w.Ship.Vel = new Vec2(20, 0);
        for (int i = 0; i < 90; i++) w.Tick(new ShipInput(0, 0));
        Assert.True(w.Ship.Pos.X <= Map.HalfW - 30 + 1e-6);

        var mangrove = w.Map.RegionOf(RegionType.Mangrove);
        // Walk in from a neighbouring region with velocity aimed at the seed.
        var from = mangrove.Seed;
        bool found = false;
        foreach (var dir in new[] { new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, 1), new Vec2(0, -1) })
        {
            for (double step = 200; step < 2500 && !found; step += 50)
            {
                var candidate = mangrove.Seed + dir * step;
                if (Map.InBounds(candidate) && Math.Abs(candidate.X) < Map.HalfW - 100 && Math.Abs(candidate.Y) < Map.HalfH - 100
                    && w.Map.RegionAt(candidate).Type != RegionType.Mangrove)
                {
                    from = candidate;
                    found = true;
                }
            }
            if (found) break;
        }
        Assert.True(found, "no neighbouring region found beside the mangroves");
        w.Ship.Pos = from;
        w.Ship.Vel = (mangrove.Seed - from).Normalized * 15;
        bool blocked = false;
        for (int i = 0; i < 30 * 40; i++)
        {
            w.Tick(new ShipInput(0, 0));
            blocked |= w.MangroveBlocked;
            if (blocked) break;
        }
        Assert.True(blocked, "a galleon should be stopped at the mangrove edge");
        Assert.NotEqual(RegionType.Mangrove, w.Map.RegionAt(w.Ship.Pos).Type);
    }
}
