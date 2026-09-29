using System.Diagnostics;
using LastTide.Sim;

namespace Sim.Tests;

public class MapGenTests
{
    static void AssertValid(Map map, int seed)
    {
        Assert.Equal(Enum.GetValues<RegionType>().Length, map.Regions.Select(r => r.Type).Distinct().Count());
        Assert.InRange(map.Ports.Count, MapGen.MinPorts, MapGen.MaxPorts);
        Assert.InRange(map.Ports.Count(p => p.Secret), MapGen.MinCoves, MapGen.MaxCoves);
        Assert.InRange(map.Treasures.Count, 20, 40);
        Assert.InRange(map.Wrecks.Count, 6, 15);
        Assert.Equal(RegionType.TradeIsles, map.StartPort.Region);
        Assert.Equal(Faction.FreeTraders, map.StartPort.Faction);
        Assert.All(map.Ports, p => Assert.InRange(p.Produces.Count, 1, 6));
        Assert.All(map.Ports, p => Assert.True(p.Consumes.Count >= 2, $"seed {seed} port {p.Name} consumes {p.Consumes.Count}"));
        Assert.All(map.Ports, p => Assert.True(map.Nav.IsSea(p.Harbor) && map.Nav.IsOpenSea(p.Harbor), $"seed {seed}: harbour of {p.Name} is on land or walled in"));
        Assert.All(map.Treasures, t => Assert.True(map.Islands[t.IslandId].Contains(t.Pos) && map.Nav.IsOpenSea(t.DigRing), $"seed {seed}: dig {t.Id} misplaced"));
        Assert.All(map.Wrecks, w => Assert.Equal(RegionType.Sargasso, map.RegionAt(w.Pos).Type));
        Assert.Equal(map.Ports.Count, map.Ports.Select(p => p.Name).Distinct().Count());

        // Every port reachable by sea from the start, and a profitable route within half a day.
        var d = map.Nav.Distances(map.StartPort.Harbor);
        Assert.All(map.Ports, p => Assert.True(map.Nav.DistanceAt(d, p.Harbor) >= 0, $"seed {seed}: {p.Name} unreachable"));
        bool route = map.StartPort.Produces.Any(g => map.Ports.Any(p => p != map.StartPort && !p.Produces.Contains(g)
            && map.Nav.DistanceAt(d, p.Harbor) is >= 0 and <= MapGen.StartRouteRange));
        Assert.True(route, $"seed {seed}: no start route");

        // Islands never overlap; islands of different landforms keep a channel of 30 m or more between them (within one
        // landform a narrow gut is allowed, as between real islets); the chart stays inside its margin.
        foreach (var a in map.Islands)
        {
            Assert.All(a.Points, p => Assert.True(Math.Abs(p.X) < Map.HalfW - 200 && Math.Abs(p.Y) < Map.HalfH - 200, $"seed {seed}: island {a.Id} runs off the chart"));
            foreach (var b in map.IslandsNear(a.Centre, a.BoundRadius + 60))
            {
                if (b.Id <= a.Id || a.Centre.DistanceTo(b.Centre) > a.BoundRadius + b.BoundRadius + 60) continue;
                double gap = Math.Min(a.Points.Min(p => b.Closest(p).Dist), b.Points.Min(p => a.Closest(p).Dist));
                Assert.False(a.Points.Any(b.Contains) || b.Points.Any(a.Contains), $"seed {seed}: islands {a.Id} and {b.Id} overlap");
                if (a.Group != b.Group) Assert.True(gap >= 30, $"seed {seed}: islands {a.Id} and {b.Id} are {gap:F0} m apart");
            }
        }
    }

    /// <summary>A polygon is simple when no two of its non-adjacent edges cross.</summary>
    static bool Simple(Vec2[] pts)
    {
        int n = pts.Length;
        for (int i = 0; i < n; i++)
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                Vec2 a = pts[i], b = pts[(i + 1) % n], c = pts[j], d = pts[(j + 1) % n];
                double d1 = (b - a).Cross(c - a), d2 = (b - a).Cross(d - a), d3 = (d - c).Cross(a - c), d4 = (d - c).Cross(b - c);
                if ((d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0)) return false;
            }
        return true;
    }

    [Fact]
    public void AThousandSeedsValidate()
    {
        // Maps are independent, so the thousand run across every core; the counts are summed at the end.
        var sw = Stopwatch.StartNew();
        int attempts = 0, crown = 0, free = 0, brethren = 0, ports = 0;
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(1, 1001, seed =>
        {
            try
            {
                var map = MapGen.Generate(seed);
                AssertValid(map, seed);
                Interlocked.Add(ref attempts, map.Attempts);
                foreach (var p in map.Ports.Where(p => !p.Secret))
                {
                    Interlocked.Increment(ref ports);
                    if (p.Faction == Faction.Crown) Interlocked.Increment(ref crown);
                    else if (p.Faction == Faction.FreeTraders) Interlocked.Increment(ref free);
                    else Interlocked.Increment(ref brethren);
                }
            }
            catch (Exception e)
            {
                failures.Add($"seed {seed}: {e.Message}");
            }
        });
        sw.Stop();
        Assert.True(failures.IsEmpty, string.Join("\n", failures.Take(10)));
        Assert.True(sw.Elapsed.TotalSeconds < 600, $"1000 maps took {sw.Elapsed.TotalSeconds:F0} s");
        Assert.True(attempts < 2500, $"too many retries: {attempts} attempts for 1000 maps");
        Assert.InRange(crown / (double)ports, 0.34, 0.50);
        Assert.InRange(free / (double)ports, 0.30, 0.46);
        Assert.InRange(brethren / (double)ports, 0.12, 0.28);
    }

    [Fact]
    public void CoastlinesAreSimpleAndWoundAlike()
    {
        foreach (int seed in new[] { 3, 17 })
        {
            var map = MapGen.Generate(seed);
            foreach (var island in map.Islands)
            {
                Assert.True(Coastlines.Area(island.Points) > 0, $"seed {seed}: island {island.Id} wound backwards");
                Assert.True(Simple(island.Points), $"seed {seed}: island {island.Id} ({island.Form}) crosses itself");
            }
        }
    }

    [Fact]
    public void EveryRegionShowsItsSignatureAndTheMapIsVaried()
    {
        foreach (int seed in new[] { 2, 9, 40 })
        {
            var map = MapGen.Generate(seed);
            var kinds = map.Islands.Select(i => i.Form).Distinct().Count();
            Assert.True(kinds >= 9, $"seed {seed}: only {kinds} kinds of landform");
            foreach (var region in map.Regions)
            {
                var sig = RegionLayout.Of(region.Type).Signature;
                Assert.True(map.Islands.Any(i => i.Form == sig && i.Centre.DistanceTo(region.Seed) < 2500), $"seed {seed}: {region.Type} lacks its {sig}");
            }
            // Island sizes span orders of magnitude, as in a real archipelago: rocks, islets and a few big islands.
            var areas = map.Islands.Select(i => Coastlines.Area(i.Points)).OrderBy(a => a).ToList();
            Assert.True(areas[^1] > 1_000_000, $"seed {seed}: biggest island only {areas[^1]:F0} m²");
            Assert.True(areas[areas.Count / 2] < 60_000, $"seed {seed}: median island {areas[areas.Count / 2]:F0} m²");
        }
    }

    [Fact]
    public void LagoonsAndBaysHoldHarbours()
    {
        // Ports favour shelter: on average they sit in more enclosed water than points just off the same coasts.
        var map = MapGen.Generate(12);
        double ports = map.Ports.Average(p => MapGen.Shelter(map, p.Harbor));
        var coast = new List<double>();
        foreach (var island in map.Ports.Select(p => map.Islands[p.IslandId]).Distinct())
            for (int i = 0; i < island.Points.Length; i += Math.Max(1, island.Points.Length / 12))
            {
                var v = island.Points[i];
                var at = v + (v - island.Centre).Normalized * 76;   // about where a harbour ring's centre lies off its coast
                if (map.Nav.IsOpenSea(at) && !island.Contains(at)) coast.Add(MapGen.Shelter(map, at));
            }
        Assert.True(ports > coast.Average() + 0.02, $"mean harbour shelter {ports:F2}, off the same coasts {coast.Average():F2}");
        // Somewhere on the map a harbour lies inside a lagoon or deep bay.
        Assert.Contains(map.Ports, p => MapGen.Shelter(map, p.Harbor) >= 0.8);
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
        for (int seed = 1; seed <= 8; seed++)
        {
            var map = MapGen.Generate(seed);
            foreach (var good in Goods.All)
            {
                if (!Goods.IsTraded(good.Id))
                {
                    // Her own catch: no port makes it or lists it; every market buys it (FishingTests).
                    Assert.All(map.Ports, p => Assert.False(p.Produces.Contains(good.Id) || p.Consumes.Contains(good.Id)));
                    continue;
                }
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
                // Most buyers are far from every producer; a few near ones stay where taking the luxury off would strip a
                // small market bare (MapGen keeps every market at 3+ goods).
                int near = consumers.Count(c => producers.Any(p => p.Harbor.DistanceTo(c.Harbor) < MapGen.LuxuryDistance));
                Assert.True(consumers.Count - near >= Math.Min(3, consumers.Count) || near <= 3, $"seed {seed}: {g} sells next door to its producers ({near} of {consumers.Count})");
                Assert.All(consumers.Where(c => producers.Any(p => p.Harbor.DistanceTo(c.Harbor) < MapGen.LuxuryDistance)), c => Assert.True(c.Consumes.Count <= 3 || near <= 3, $"seed {seed}: {c.Name} could have lost {g}"));
            }
            Assert.All(map.Ports.Where(p => p.Secret), p => Assert.Contains(p.Produces, g => Goods.IsRare(g)));
        }
    }

    [Fact]
    public void RegionsCoverTheMapAndMangroveIsDense()
    {
        var map = MapGen.Generate(5);
        var seen = new HashSet<RegionType>();
        for (double x = -Map.HalfW; x <= Map.HalfW; x += 200)
            for (double y = -Map.HalfH; y <= Map.HalfH; y += 200)
                seen.Add(map.RegionAt(new Vec2(x, y)).Type);
        Assert.Equal(Enum.GetValues<RegionType>().Length, seen.Count);
        double Land(RegionType t) => map.Islands.Where(i => i.Region == t).Sum(i => Coastlines.Area(i.Points));
        Assert.True(Land(RegionType.Mangrove) > Land(RegionType.Deep) * 2.5, $"mangrove {Land(RegionType.Mangrove):F0} m² vs deep {Land(RegionType.Deep):F0} m²");
        Assert.True(map.Islands.Count > 300, $"only {map.Islands.Count} islands");
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
        double cells = Math.Ceiling(Map.Width / RevealMask.Cell) * Math.Ceiling(Map.Height / RevealMask.Cell);
        Assert.InRange(m.Fraction, 180 / cells, 220 / cells);
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
        Assert.True(w.Reveal.Fraction > 0.03 && w.Reveal.Fraction < 0.2, $"charted {w.Reveal.Fraction:P0}");
    }

    [Fact]
    public void SailingInksTheChartAndTheChartSurvivesASave()
    {
        var w = World.NewRun(3);
        w.Player.Officers.Add(new Officer { Type = OfficerType.Cartographer, Tier = 0 });   // only a cartographer keeps the chart
        double before = w.Reveal.Fraction;
        w.Ship.SailTarget = 3;
        w.Ship.SailFraction = 1;
        w.Islands.Clear();
        // The Trade Isles are charted from the start: set out from just inside their charted edge on the way to the Deep.
        var deep = w.Map.RegionOf(RegionType.Deep).Seed;
        var dir = (deep - w.Ship.Pos).Normalized;
        var at = w.Ship.Pos;
        while (Map.InBounds(at) && w.Reveal.IsRevealed(at + dir * 50)) at += dir * 50;
        w.Ship.Pos = at - dir * 300;
        w.Ship.Heading = (deep - w.Ship.Pos).Angle;
        w.Wind.SetFixed(w.Ship.Heading, 8);
        for (int i = 0; i < 30 * 200; i++) w.Tick(new ShipInput(0, 0));
        Assert.True(w.Reveal.RevealedCells > 1000 + (int)(before * w.Reveal.W * w.Reveal.H), $"{before:P2} → {w.Reveal.Fraction:P2}");
        var loaded = World.LoadJson(w.SaveJson());
        Assert.Equal(w.Reveal.RevealedCells, loaded.Reveal.RevealedCells);
        Assert.Equal(w.Hash(), loaded.Hash());
    }

    [Fact]
    public void BigHullsCannotEnterTheMangroves()
    {
        var w = World.NewRun(9, "galleon");
        w.Islands.Clear();
        w.Map.Whirlpools.Clear();

        var mangrove = w.Map.RegionOf(RegionType.Mangrove);
        // Walk in from a neighbouring region with velocity aimed at the seed.
        var from = mangrove.Seed;
        bool found = false;
        foreach (var dir in new[] { new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, 1), new Vec2(0, -1) })
        {
            for (double step = 200; step < 2500 * Map.Stretch && !found; step += 50)
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
        w.Ship.Heading = (mangrove.Seed - from).Angle;
        w.Ship.Vel = (mangrove.Seed - from).Normalized * 6;
        w.Wind.SetFixed(w.Ship.Heading, 9);   // a run straight in
        w.Ship.SailTarget = 3;
        w.Ship.SailFraction = Tuning.SailFraction[3];
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
