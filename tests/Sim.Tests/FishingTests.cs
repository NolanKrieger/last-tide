using LastTide.Sim;

namespace Sim.Tests;

/// <summary>Fishing (Nolan, 2026-09-28): anywhere at sea, richer on the grounds; the crew eats the catch first and it spoils.</summary>
public class FishingTests
{
    /// <summary>A quiet world lying still in open water of <paramref name="region"/>, lean sloop, wind pinned.</summary>
    static World Still(RegionType region = RegionType.TradeIsles, int seed = 4)
    {
        var w = Sea.Fixed(seed: seed);
        w.Ship.Pos = OpenWater(w, region);
        w.Ship.SailTarget = 0;
        w.Ship.SailFraction = 0;
        return w;
    }

    static Vec2 OpenWater(World w, RegionType region)
    {
        var seed = w.Map.RegionOf(region).Seed;
        for (double r = 0; r < 4000; r += 100)
            for (int a = 0; a < 24; a++)
            {
                var p = seed + Vec2.FromAngle(a * Angles.Tau / 24) * r;
                if (Map.InBounds(p) && w.Map.RegionAt(p).Type == region && w.Map.Nav.IsOpenSea(p)
                    && !w.Map.IslandsNear(p, 150).Any(i => i.Closest(p).Dist < 150)) return p;
            }
        throw new InvalidOperationException($"no open water in {region}");
    }

    static void Run(World w, double seconds, bool action = false)
    {
        int n = (int)Math.Round(seconds * Tuning.TicksPerSecond);
        for (int i = 0; i < n; i++) w.Tick(new ShipInput(0, 0, Action: action && i == 0));
    }

    [Fact]
    public void GroundsArePureFunctionsOfSeedPlaceAndDay()
    {
        var map = MapGen.Generate(5);
        var a = new List<Fishing.Ground>();
        var b = new List<Fishing.Ground>();
        Fishing.GroundsNear(map, new Vec2(0, 0), 4000, 3, a);
        Fishing.GroundsNear(map, new Vec2(0, 0), 4000, 3, b);
        Assert.True(a.Count > 10, $"only {a.Count} grounds within 4 km");
        Assert.Equal(a, b);
        // A new day, a new layout.
        b.Clear();
        Fishing.GroundsNear(map, new Vec2(0, 0), 4000, 4, b);
        Assert.NotEqual(a.Select(g => g.Pos), b.Select(g => g.Pos));
        Assert.All(a, g => Assert.True(!Map.InBounds(g.Pos) || map.Nav.IsSea(g.Pos), "a ground over land"));
    }

    [Fact]
    public void AnywhereHoldsFishAndTheGroundsHoldMore()
    {
        var map = MapGen.Generate(5);
        var scratch = new List<Fishing.Ground>();
        var grounds = new List<Fishing.Ground>();
        Fishing.GroundsNear(map, new Vec2(0, 0), 4000, 0, grounds);
        // Every point at sea has its region's run at least; a ground's heart holds its strength times that.
        for (double x = -3000; x <= 3000; x += 250)
            for (double y = -3000; y <= 3000; y += 250)
            {
                var p = new Vec2(x, y);
                if (!map.Nav.IsSea(p)) continue;
                double run = Fishing.RegionRun(map.RegionAt(p).Type);
                Assert.True(Fishing.Richness(map, p, 0, scratch) >= run - 1e-9);
            }
        var g = grounds.First(g => Map.InBounds(g.Pos));
        double heart = Fishing.Richness(map, g.Pos, 0, scratch);
        Assert.True(heart >= Fishing.RegionRun(map.RegionAt(g.Pos).Type) * g.Strength - 1e-9, $"a ground's heart holds {heart:F2}");
        // Some waters are richer than others, and the open ocean past the chart is thin but not empty.
        Assert.True(Fishing.RegionRun(RegionType.Shoals) > Fishing.RegionRun(RegionType.TradeIsles));
        Assert.True(Fishing.RegionRun(RegionType.Sargasso) < Fishing.RegionRun(RegionType.Volcanic));
        Assert.True(Fishing.Richness(map, new Vec2(Map.HalfW + 5000, 0), 0, scratch) >= Fishing.BeyondRun);
    }

    [Fact]
    public void FishGatherNearLand()
    {
        // Nolan, 2026-09-28: "the fish should be more common near land".
        var map = MapGen.Generate(5);
        // The run rises toward the shore: nearly the full boost at the water's edge, none far out.
        var island = map.Islands.Where(i => Map.InBounds(i.Centre) && i.Radius > 80).OrderBy(i => i.Centre.LengthSq).First();
        Vec2? edge = null;
        foreach (var v in island.Points)
        {
            var q = v + (v - island.Centre).Normalized * 6;
            if (map.Nav.IsSea(q) && !island.Contains(q) && Shore.Distance(map, q, 50) < 10) { edge = q; break; }
        }
        Assert.NotNull(edge);
        Assert.True(Fishing.Inshore(map, edge!.Value) > Fishing.InshoreBoost - 0.1, $"at the shore {Fishing.Inshore(map, edge.Value):F2}");
        Vec2 open = default;
        for (double x = -4000; x <= 4000 && open == default; x += 100)
            for (double y = -4000; y <= 4000; y += 100)
                if (Shore.Distance(map, new Vec2(x, y), Fishing.InshoreReach + 10) > Fishing.InshoreReach) { open = new Vec2(x, y); break; }
        Assert.NotEqual(default, open);
        Assert.Equal(1, Fishing.Inshore(map, open), 9);
        // The grounds gather by the coasts: over a month, several times as many to the square kilometre within 200 m of
        // land as beyond 500 m.
        var grounds = new List<Fishing.Ground>();
        for (long day = 0; day < 30; day++) Fishing.GroundsNear(map, new Vec2(0, 0), 3000, day, grounds);
        int nearG = 0, farG = 0;
        foreach (var g in grounds.Where(g => g.Pos.LengthSq < 3000.0 * 3000))
        {
            double d = Shore.Distance(map, g.Pos, 600);
            if (d < 200) nearG++;
            else if (d > 500) farG++;
        }
        int nearA = 0, farA = 0;
        for (double x = -3000; x <= 3000; x += 50)
            for (double y = -3000; y <= 3000; y += 50)
            {
                var q = new Vec2(x, y);
                if (q.LengthSq > 3000.0 * 3000 || !map.Nav.IsSea(q)) continue;
                double d = Shore.Distance(map, q, 600);
                if (d < 200) nearA++;
                else if (d > 500) farA++;
            }
        Assert.True(nearA > 100 && farA > 100, $"sea near {nearA}, far {farA}");
        double nearDensity = nearG / (double)nearA, farDensity = farG / (double)farA;
        Assert.True(nearDensity > 2 * farDensity, $"grounds per cell: inshore {nearDensity:F4}, offshore {farDensity:F4} ({nearG} / {farG})");
    }

    [Fact]
    public void LinesGoOutOnlyUnderAThirdOfSail()
    {
        var w = Still();
        w.Ship.SailTarget = 3;
        w.Ship.SailFraction = 1;
        Run(w, 0.1, action: true);
        Assert.False(w.LinesOut);
        Assert.Contains("NOTICE_FISH_SLOW", w.Notices);
        w.Ship.SailTarget = 1;
        Run(w, 0.1, action: true);
        Assert.True(w.LinesOut);
        // Making sail hauls them in.
        w.Tick(new ShipInput(0, 1));
        Run(w, 0.1);
        Assert.False(w.LinesOut);
        Assert.Contains("NOTICE_LINES_IN_SAIL", w.Notices);
    }

    [Fact]
    public void TheGunCrewsAndSpareHandsFishAndTheCatchFollowsTheWater()
    {
        double bottle = World.BottleMapChance;
        World.BottleMapChance = 0;   // every bite a fish, so the hold counts the catch
        try
        {
            var w = Still();
            w.Ship.Crew = 8;
            var st = w.Ship.Stations();
            Assert.Equal(Math.Min(st[0] + st[3], World.MaxLines), w.Fishers);   // guns and spare; the riggers and carpenters keep at their work
            Assert.True(w.Fishers >= 5);
            Run(w, 0.1, action: true);
            Assert.True(w.LinesOut);
            Assert.Equal(World.FishPerHandMinute * w.Fishers * w.FishRichnessHere * (w.IsNight && w.Lantern ? World.LanternNightCatch : 1)
                * (w.Ship.Speed > World.FishStillSpeed ? 0.5 : 1), w.CatchPerMinute, 6);
            // Over a stretch that runs into the night (a lit lantern doubles the catch), the hold gains what the water gave.
            double expected = 0;
            int midnight = (int)w.MidnightsPassed;
            for (int i = 0; i < 30 * 80; i++)
            {
                expected += w.CatchPerMinute * Tuning.Dt / 60;
                w.Tick(new ShipInput(0, 0));
            }
            Assert.Equal(midnight, (int)w.MidnightsPassed);   // no supper eaten in the stretch
            Assert.InRange(w.Player.Units(Good.Fish) + w.FishCatch, expected - 0.2, expected + 0.2);
            Assert.True(w.Player.Units(Good.Fish) >= 2);
            // A big crew still fishes with only so many lines.
            w.Ship.Crew = w.Ship.Hull.CrewMax;
            Assert.True(w.Fishers <= World.MaxLines);
        }
        finally { World.BottleMapChance = bottle; }
        // No hands free: no lines.
        var v = Still();
        v.SetStations(guns: 0, sails: 2, repair: 1);
        v.Ship.Crew = 3;
        Assert.Equal(0, v.Fishers);
        Run(v, 0.1, action: true);
        Assert.False(v.LinesOut);
        Assert.Contains("NOTICE_FISH_NO_HANDS", v.Notices);
    }

    [Fact]
    public void AFullHoldHaulsTheLinesIn()
    {
        var w = Still();
        w.Ship.Crew = 8;
        Array.Clear(w.Player.Cargo);
        w.Player.Cargo[(int)Good.Timber] = (int)(w.Ship.CargoCapacity / 2) - 1;   // timber is 2 slots a unit
        w.Player.Cargo[(int)Good.Provisions] = (int)Math.Round((w.Ship.CargoCapacity - w.Player.SlotsUsed - 0.25) / 0.25);
        Assert.Equal(0.25, w.Ship.CargoCapacity - w.Player.SlotsUsed, 6);   // room for one fish
        Run(w, 0.1, action: true);
        Run(w, 80);
        Assert.False(w.LinesOut);
        Assert.Contains("NOTICE_FISH_HOLD_FULL", w.Notices);
        Assert.True(w.Player.SlotsUsed <= w.Ship.CargoCapacity + 1e-9);
    }

    [Fact]
    public void TheCrewEatsFishFirstAndTheRestSpoils()
    {
        var w = World.NewRun(4, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        w.Islands.Clear();
        Assert.Equal(1, w.DailyProvisions);
        int provisions = w.Player.Units(Good.Provisions);
        w.Player.Cargo[(int)Good.Fish] = 7;
        for (int t = 0; t < 30 * 120; t++) w.Tick(new ShipInput(0, 0));   // one midnight
        Assert.Equal(provisions, w.Player.Units(Good.Provisions));   // fish eaten instead
        Assert.Equal(4, w.Player.Units(Good.Fish));   // 7 − 1 eaten = 6, a third (2) spoiled
        Assert.Contains("NOTICE_FISH_SPOILED", w.Notices);
        // Fish runs out in a few days; then the provisions go as before.
        for (int t = 0; t < 30 * 120 * 4; t++) w.Tick(new ShipInput(0, 0));
        Assert.Equal(0, w.Player.Units(Good.Fish));
        Assert.True(w.Player.Units(Good.Provisions) < provisions);
    }

    [Fact]
    public void PortsBuyFishButDoNotSellIt()
    {
        var w = World.NewRun(4, populate: false);
        w.Player.Cargo[(int)Good.Fish] = 10;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.Equal(PortResult.NoStock, w.Apply(new PortCommand(PortAction.Buy, Good.Fish, 1)));
        int gold = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Sell, Good.Fish, 10)));
        Assert.True(w.Player.Gold > gold, "fish sells");
        Assert.True(w.Player.Gold - gold < 10 * Goods.Of(Good.Fish).BasePrice * Market.ConsumerMult, "cheaply");
        // No port makes it or lists it among its wants: the generator leaves it out.
        Assert.All(w.Map.Ports, p => Assert.False(p.Produces.Contains(Good.Fish) || p.Consumes.Contains(Good.Fish)));
    }

    [Fact]
    public void ALineCanBringUpABeastOrAPearl()
    {
        double hook = World.BeastHookChance, pearl = World.PearlChance;
        try
        {
            // In the Shoals the serpent may take the line: she hauls in and the beast is on her.
            World.BeastHookChance = 1;
            var w = Still(RegionType.Shoals);
            w.MonstersEnabled = true;
            Run(w, 0.1, action: true);
            for (int i = 0; i < 30 * 90 && w.Monster == null; i++) w.Tick(new ShipInput(0, 0));
            Assert.NotNull(w.Monster);
            Assert.Equal(MonsterType.ReefSerpent, w.Monster!.Type);
            Assert.False(w.LinesOut);
            Assert.Contains("NOTICE_FISH_BEAST", w.Notices);
            // With the beasts away, a Shoals oyster may hold a pearl.
            World.BeastHookChance = 0;
            World.PearlChance = 1;
            var v = Still(RegionType.Shoals);
            Run(v, 0.1, action: true);
            Run(v, 90);
            Assert.True(v.Player.Units(Good.Pearls) > 0);
            Assert.Equal(0, v.Player.Units(Good.Fish));
        }
        finally
        {
            World.BeastHookChance = hook;
            World.PearlChance = pearl;
        }
    }

    [Fact]
    public void FishingSavesAndReplays()
    {
        // Not Sea.Fixed: it clears the islands and pins the wind, which a save does not keep, and the catch reads the
        // distance to land.
        var w = World.NewRun(4, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        w.DriftEnabled = false;
        w.Ship.Pos = OpenWater(w, RegionType.TradeIsles);
        w.Ship.Vel = Vec2.Zero;
        w.Ship.SailTarget = 0;
        w.Ship.SailFraction = 0;
        Run(w, 0.1, action: true);
        Run(w, 50);
        Assert.True(w.LinesOut);
        var loaded = World.LoadJson(w.SaveJson());
        Assert.True(loaded.LinesOut);
        Assert.Equal(w.FishCatch, loaded.FishCatch, 9);
        Assert.Equal(w.Player.Units(Good.Fish), loaded.Player.Units(Good.Fish));
        Assert.Equal(w.Hash(), loaded.Hash());
        Run(w, 40);
        Run(loaded, 40);
        Assert.Equal(w.Hash(), loaded.Hash());
    }
}
