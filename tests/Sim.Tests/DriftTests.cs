using LastTide.Sim;

namespace Sim.Tests;

/// <summary>Barrels adrift on the open sea (Nolan, 2026-09-28), and the events that tell the view whose hit and what pickup.</summary>
public class DriftTests
{
    static readonly Vec2 OpenWater = new(-500, 900);   // open water on seed 4's chart (CombatTests' arena)
    static long TicksPerHour => (long)Math.Round(Tuning.SecondsPerHour * Tuning.TicksPerSecond);

    /// <summary>Seed 4, the wind pinned from the north, beasts and director off, drift on; she is hove to in open water heading east.</summary>
    static World Drifting(int seed = 4)
    {
        var w = Sea.Fixed(fromCompass: 0, seed: seed);
        w.DriftEnabled = true;
        w.Ship.Pos = OpenWater;
        Sea.Point(w, 90);
        return w;
    }

    static long FirstRoll(World w, long after) => Enumerable.Range((int)after + 1, 20000).First(h => w.DriftRoll(h));

    [Fact]
    public void ABarrelComesByAboutOnceInFiftyHours()
    {
        var w = World.NewRun(11, populate: false);
        const int hours = 20000;
        int hits = 0;
        for (long h = 1; h <= hours; h++) if (w.DriftRoll(h)) hits++;
        Assert.InRange(hits / (double)hours, 0.016, 0.024);   // 2% an hour: ~ one every two in-game days at sea
        Assert.Equal(w.DriftRoll(1234), World.NewRun(11, populate: false).DriftRoll(1234));   // a function of the seed alone
    }

    [Fact]
    public void TheBarrelLiesAheadInSightOnOneBowAndHoldsAFewUnits()
    {
        var w = Drifting();
        bool produce = false, stores = false, port = false, starboard = false;
        for (int i = 0; i < 60; i++)
        {
            var b = w.SpawnDrift(new Rng((ulong)i * 7919 + 1));
            Assert.NotNull(b);
            var rel = b!.Pos - w.Ship.Pos;
            double ahead = rel.Dot(w.Ship.Forward), abeam = rel.Dot(w.Ship.Right);
            Assert.True(ahead > 80 && rel.Length <= w.VisionRadius, $"ahead {ahead:F0} m, {rel.Length:F0} m off, sight {w.VisionRadius}");
            Assert.InRange(Math.Abs(abeam), 25, 70);
            Assert.Null(w.LandAlong(w.Ship.Pos, b.Pos));
            Assert.InRange(b.Units, 2, 5);
            Assert.Equal(0, b.Gold);
            Assert.Equal(World.DriftLife, b.Life);
            var good = b.Good!.Value;
            if (good is Good.Provisions or Good.Munitions or Good.Timber) stores = true; else produce = true;
            if (abeam > 0) starboard = true; else port = true;
        }
        Assert.True(produce && stores, "both the region's produce and ship's stores come adrift");
        Assert.True(port && starboard, "on either bow");
        Assert.Contains("NOTICE_FLOTSAM_PORT", w.Notices);
        Assert.Contains("NOTICE_FLOTSAM_STARBOARD", w.Notices);
    }

    [Fact]
    public void NoBarrelIsPutBehindLand()
    {
        // In the home harbour, bows to the island: whatever spot the lookout picks, the run to it must be open water.
        var w = World.NewRun(4, populate: false);
        var home = w.Map.StartPort;
        w.Ship.Heading = (home.Pos - home.Harbor).Angle;
        int placed = 0;
        for (int i = 0; i < 60; i++)
            if (w.SpawnDrift(new Rng((ulong)i + 3)) is { } b)
            {
                placed++;
                Assert.Null(w.LandAlong(w.Ship.Pos, b.Pos));
                Assert.DoesNotContain(w.Islands, isl => isl.Contains(b.Pos));
            }
        Assert.True(placed < 60, "facing the coast, some spots ahead are behind land and are refused");
    }

    [Fact]
    public void UnderWayABarrelComesByAndSailingOverItHaulsItAboard()
    {
        var w = Drifting();
        Sea.SetSail(w, 3);
        long hour = FirstRoll(w, 0);
        while (w.Ticks <= hour * TicksPerHour) w.Tick(new ShipInput(0, 0));
        Assert.True(w.Ship.Speed > World.DriftMinSpeed);
        var barrel = Assert.Single(w.Flotsam);
        Assert.True(barrel.Life > World.DriftLife - 1);
        Assert.Contains(w.Notices, n => n.StartsWith("NOTICE_FLOTSAM_"));
        var good = barrel.Good!.Value;
        int units = barrel.Units, before = w.Player.Units(good);
        w.Ship.Pos = barrel.Pos;
        w.Tick(new ShipInput(0, 0));
        Assert.Empty(w.Flotsam);
        Assert.Equal(before + units, w.Player.Units(good));
        var pickup = Assert.Single(w.Events, e => e.Type == CombatEventType.Collect);
        Assert.Equal((int)good, pickup.Good);
        Assert.Equal(units, pickup.Strength);
    }

    [Fact]
    public void AShipHoveToMeetsNoBarrelAndTheSwitchStopsThem()
    {
        var w = Drifting();   // furled: no way on
        long hour = FirstRoll(w, 0);
        while (w.Ticks <= hour * TicksPerHour + 1) w.Tick(new ShipInput(0, 0));
        Assert.Empty(w.Flotsam);

        var off = Drifting();
        off.DriftEnabled = false;
        Sea.SetSail(off, 3);
        while (off.Ticks <= hour * TicksPerHour + 1) off.Tick(new ShipInput(0, 0));
        Assert.Empty(off.Flotsam);
    }

    [Fact]
    public void BarrelsReplayAlikeAndKeepThroughASave()
    {
        World Run()
        {
            var w = Drifting();
            Sea.SetSail(w, 3);
            long hour = FirstRoll(w, 0);
            while (w.Ticks <= hour * TicksPerHour + 30) w.Tick(new ShipInput(0, 0));
            return w;
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a.Hash(), b.Hash());
        var barrel = Assert.Single(a.Flotsam);
        Assert.Equal(barrel.Pos.X, b.Flotsam[0].Pos.X);

        var loaded = World.LoadJson(a.SaveJson());
        Assert.Equal(a.Hash(), loaded.Hash());
        Assert.True(loaded.DriftEnabled);
        Assert.Equal(barrel.Life, loaded.Flotsam[0].Life, 6);
        for (int i = 0; i < 60; i++) { a.Tick(new ShipInput(0, 0)); loaded.Tick(new ShipInput(0, 0)); }
        Assert.Equal(a.Hash(), loaded.Hash());
    }

    [Fact]
    public void AHitNamesWhoFiredAndAPickupNamesTheGood()
    {
        var w = Sea.Fixed(fromCompass: 0, seed: 4);
        w.Ship.Pos = OpenWater;
        Sea.Point(w, 90);
        w.Ship.Cannons = 4;
        w.Ship.Crew = 8;
        w.Ship.Order = CrewOrder.Battle;
        w.Player.Cargo[(int)Good.Munitions] = 40;
        var target = w.Spawn("sloop", w.Ship.Pos + new Vec2(0, 120), Angles.FromCompassDeg(90), Faction.Brethren, null, crew: 6, cannons: 2);
        CombatEvent? hit = null;
        for (int i = 0; i < 30 * 40 && hit == null; i++)
        {
            w.Tick(new ShipInput(0, 0, FireStarboard: w.Ship.Loaded[1]));
            foreach (var e in w.Events) if (e.Type == CombatEventType.Hit && e.ShipId == target.Id) hit = e;
        }
        Assert.NotNull(hit);
        Assert.Equal(w.Ship.Id, hit!.Value.By);

        w.Flotsam.Add(new Flotsam { Pos = w.Ship.Pos, Good = Good.Rum, Units = 3 });
        w.Flotsam.Add(new Flotsam { Pos = w.Ship.Pos, Gold = 40 });
        w.Tick(new ShipInput(0, 0));
        Assert.Contains(w.Events, e => e.Type == CombatEventType.Collect && e.Good == (int)Good.Rum && e.Strength == 3);
        Assert.Contains(w.Events, e => e.Type == CombatEventType.Collect && e.Good == -1 && e.Strength == 40);
    }
}
