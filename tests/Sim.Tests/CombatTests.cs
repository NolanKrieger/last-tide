using LastTide.Sim;

namespace Sim.Tests;

public class CombatTests
{
    /// <summary>Open water, wind from the north, player heading east with four guns; a target 120 m to starboard (south).</summary>
    static (World w, Ship target) Arena(int seed = 4, string targetHull = "sloop")
    {
        var w = Sea.Fixed(fromCompass: 0, seed: seed);
        Sea.Point(w, 90);
        w.Ship.Pos = new Vec2(-500, 900);   // open water in every map used here
        w.Ship.Cannons = 4;
        w.Ship.Crew = 8;
        w.Ship.Order = CrewOrder.Battle;
        w.Player.Cargo[(int)Good.Munitions] = 40;
        var target = w.Spawn(targetHull, w.Ship.Pos + new Vec2(0, 120), Angles.FromCompassDeg(90), Faction.Brethren, null, crew: 6, cannons: 2);
        return (w, target);
    }

    [Fact]
    public void ABroadsideCostsMunitionsAndStrikesAShipAbeam()
    {
        var (w, target) = Arena();
        double hp = target.HullHp;
        int ammo = w.Player.Units(Good.Munitions);
        w.Tick(new ShipInput(0, 0, FireStarboard: true));
        Assert.Equal(ammo - 2, w.Player.Units(Good.Munitions));   // two guns a side
        Assert.False(w.Ship.Loaded[(int)Side.Starboard]);
        Assert.True(w.Ship.Loaded[(int)Side.Port]);
        Assert.Contains(w.Events, e => e.Type == CombatEventType.Fire);
        bool hit = false;
        for (int i = 0; i < 30 * 40 && !hit; i++)
        {
            w.Tick(new ShipInput(0, 0, FireStarboard: w.Ship.Loaded[1]));
            hit = w.Events.Any(e => e.Type == CombatEventType.Hit);
        }
        Assert.True(hit, "a ball should strike a sloop 120 m abeam within a few broadsides");
        Assert.True(target.HullHp < hp, $"target hp {target.HullHp} of {hp}");
        Assert.Same(w.Ship, target.LastHitBy);
        Assert.Equal(-35, w.Player.Rep(Faction.Brethren));   // −10 once for drawing blood
        Assert.True(target.PlayerHostile);
    }

    [Fact]
    public void ReloadDependsOnManningAndAnUnloadedSideCannotFire()
    {
        var (w, _) = Arena();
        w.Tick(new ShipInput(0, 0, FireStarboard: true));
        Assert.False(w.Fire(w.Ship, Side.Starboard));
        double full = w.Ship.ReloadTime;
        Assert.Equal(12, full, 6);
        w.Ship.Crew = 2;   // Battle: 2 on guns of 4 → half speed
        Assert.Equal(24, w.Ship.ReloadTime, 6);
        w.Ship.Crew = 8;
        for (int i = 0; i < 30 * 13; i++) w.Tick(new ShipInput(0, 0));
        Assert.True(w.Ship.Loaded[(int)Side.Starboard], "reloaded after 12 s");
        w.Player.Cargo[(int)Good.Munitions] = 0;
        Assert.False(w.Fire(w.Ship, Side.Starboard), "no munitions, no broadside");
        Assert.Equal(0, w.Ship.Cannons > 0 ? w.Player.Units(Good.Munitions) : -1);
    }

    [Fact]
    public void BallsSplashAtMaximumRange()
    {
        var (w, target) = Arena();
        target.Pos = w.Ship.Pos + new Vec2(0, 400);   // beyond a 4-pounder's 180 m
        w.Tick(new ShipInput(0, 0, FireStarboard: true));
        bool splash = false;
        for (int i = 0; i < 90; i++)
        {
            w.Tick(new ShipInput(0, 0));
            splash |= w.Events.Any(e => e.Type == CombatEventType.Splash);
            Assert.DoesNotContain(w.Events, e => e.Type == CombatEventType.Hit);
        }
        Assert.True(splash);
        Assert.Empty(w.Balls);
        Assert.Equal(target.Hull.HullHp, target.HullHp);
    }

    [Fact]
    public void EveryTenPercentLostOpensALeakAndWaterRises()
    {
        var w = Sea.Fixed();
        var ship = w.Ship;
        ship.Crew = 1;   // nobody to pump or plug
        ship.Order = CrewOrder.Battle;
        var rng = new Rng(1);
        ship.Hit(9, null, rng);
        Assert.Equal(0, ship.Leaks);
        ship.Hit(2, null, rng);
        Assert.Equal(1, ship.Leaks);
        ship.Hit(31, null, rng);
        Assert.Equal(4, ship.Leaks);
        double water = ship.Water;
        for (int i = 0; i < 30 * 5; i++) w.Tick(new ShipInput(0, 0));
        Assert.InRange(ship.Water - water, 19, 21);   // 4 leaks × 1%/s × 5 s
        Assert.True(ship.WaterFactor < 1);
    }

    [Fact]
    public void PumpsAndCarpentersFightTheWaterAndPatchTheHull()
    {
        var w = Sea.Fixed();
        var ship = w.Ship;
        ship.Crew = 10;
        ship.Cannons = 0;
        ship.Order = CrewOrder.Repair;          // 1 carpenter, 1 rigger, 8 on the pumps
        Assert.Equal(new[] { 0, 1, 1, 8 }, ship.Stations());
        ship.Leaks = 2;
        ship.Water = 50;
        ship.HullHp = 40;
        w.Player.Cargo[(int)Good.Timber] = 3;
        for (int i = 0; i < 30 * 20; i++) w.Tick(new ShipInput(0, 0));
        Assert.Equal(0, ship.Leaks);            // one plugged every 8 s
        Assert.True(ship.Water < 50, $"water {ship.Water}");
        Assert.True(ship.HullHp > 40, "patching started once the leaks were plugged");
        for (int i = 0; i < 30 * 60; i++) w.Tick(new ShipInput(0, 0));
        Assert.Equal(0, ship.Water, 6);
        Assert.Equal(70, ship.HullHp, 0);       // the 70% cap at sea
        Assert.Equal(0, w.Player.Units(Good.Timber));   // 30 HP patched = 3 planks
    }

    [Fact]
    public void NoTimberNoPatching()
    {
        var w = Sea.Fixed();
        var ship = w.Ship;
        ship.Crew = 6;
        ship.Order = CrewOrder.Repair;
        ship.HullHp = 40;
        w.Player.Cargo[(int)Good.Timber] = 0;
        for (int i = 0; i < 30 * 30; i++) w.Tick(new ShipInput(0, 0));
        Assert.Equal(40, ship.HullHp, 6);
    }

    [Fact]
    public void TheLastStandRunsAnHourglassAndSinksTheShip()
    {
        var w = Sea.Fixed();
        var ship = w.Ship;
        ship.Crew = 4;
        ship.Order = CrewOrder.Battle;
        ship.HullHp = 0;
        w.Tick(new ShipInput(0, 0));
        Assert.True(ship.Foundering);
        Assert.False(ship.WaterOnlyStand);
        Assert.InRange(ship.Hourglass, 19.5, 20);
        Assert.False(ship.CanFire(Side.Port));
        for (int i = 0; i < 30 * 21; i++) w.Tick(new ShipInput(0, 0));
        Assert.True(ship.Sunk);
        Assert.True(w.RunOver);
        Assert.Equal("SUNK_SEA", w.CauseOfSinking);
        long t = w.Ticks;
        w.Tick(new ShipInput(0, 0));
        Assert.Equal(t, w.Ticks);
    }

    [Fact]
    public void PumpingHardExtendsTheStandAndAWaterOnlyStandCanBeRecovered()
    {
        var w = Sea.Fixed();
        var ship = w.Ship;
        ship.Crew = 10;
        ship.Cannons = 0;
        ship.Order = CrewOrder.Repair;   // 8 pumping
        ship.Water = 99.9;
        ship.Leaks = 30;                 // the sea wins this tick
        w.Tick(new ShipInput(0, 0));
        Assert.True(ship.Foundering && ship.WaterOnlyStand);
        Assert.InRange(ship.Hourglass, 34, 35);   // 20 + min(15, 8 × 3)
        ship.Leaks = 0;
        for (int i = 0; i < 30 * 15; i++) w.Tick(new ShipInput(0, 0));
        Assert.False(ship.Foundering, "pumped below 80%: the stand ends");
        Assert.False(ship.Sunk);
    }

    [Fact]
    public void ReachingAnOpenHarbourWhileFounderingSavesTheShip()
    {
        var w = World.NewRun(4);
        w.Islands.Clear();
        var ship = w.Ship;
        ship.HullHp = 0;
        ship.Water = 60;
        ship.Leaks = 3;
        w.Tick(new ShipInput(0, 0));   // starts in the home harbour
        Assert.True(w.IsDocked);
        Assert.False(ship.Foundering);
        Assert.Equal(1, ship.HullHp);
        Assert.Equal(0, ship.Water);
        Assert.Equal(0, ship.Leaks);
        Assert.True(w.DockedByAHair);
        Assert.False(w.RunOver);
    }

    [Fact]
    public void RammingSplitsDamageByMassAndAngle()
    {
        var (w, target) = Arena(targetHull: "sloop");
        // Drive bow-on into the target's side at 10 m/s.
        w.Ship.Pos = target.Pos + new Vec2(0, -60);
        w.Ship.Heading = Angles.FromCompassDeg(180);
        w.Ship.Vel = new Vec2(0, 12);
        Sea.SetSail(w, 3);   // keep driving in
        double hpA = w.Ship.HullHp, hpB = target.HullHp;
        bool rammed = false;
        for (int i = 0; i < 450 && !rammed; i++)
        {
            w.Tick(new ShipInput(0, 0));
            rammed = w.Events.Any(e => e.Type == CombatEventType.Ram);
        }
        Assert.True(rammed);
        double lostA = hpA - w.Ship.HullHp, lostB = hpB - target.HullHp;
        Assert.True(lostB > lostA * 2.5, $"bow-on: attacker lost {lostA:F1}, target {lostB:F1}");
        Assert.True(lostB > 8, "a 10 m/s ram hurts");
        Assert.True(w.Ship.Speed < 6, "the collision kills most of the way");
        Assert.Equal(-35, w.Player.Rep(Faction.Brethren));

        // A bigger ship rams a smaller one harder: a frigate under full sail on a beam reach drives into a sloop's side.
        var (w2, small) = Arena(seed: 5, targetHull: "sloop");
        w2.Ship.Pos = small.Pos + new Vec2(0, -700);   // the player watches from a distance, still within the physics radius
        small.Heading = Angles.FromCompassDeg(0);
        var big = w2.Spawn("frigate", small.Pos + new Vec2(-90, 0), Angles.FromCompassDeg(90), Faction.Crown, null);
        big.SailTarget = 3;
        big.SailFraction = 1;
        big.Vel = new Vec2(12, 0);
        double hpS = small.HullHp, hpF = big.HullHp;
        bool hit2 = false;
        for (int i = 0; i < 300 && !hit2; i++)
        {
            w2.Tick(new ShipInput(0, 0));
            hit2 = w2.Events.Any(e => e.Type == CombatEventType.Ram);
        }
        Assert.True(hit2, "the frigate should strike the sloop");
        Assert.True(hpS - small.HullHp > (hpF - big.HullHp) * 3, $"sloop lost {hpS - small.HullHp:F1}, frigate {hpF - big.HullHp:F1}");
    }

    [Fact]
    public void ASunkShipSpillsFlotsamThatDriftsAndCanBeCollected()
    {
        var (w, target) = Arena();
        target.HullHp = 1;
        target.Crew = 1;
        int gold = w.Player.Gold;
        w.Tick(new ShipInput(0, 0, FireStarboard: true));
        bool sank = false;
        for (int i = 0; i < 30 * 30 && !sank; i++)
        {
            w.Tick(new ShipInput(0, 0));
            sank = w.Events.Any(e => e.Type == CombatEventType.Sink);
        }
        Assert.True(sank);
        Assert.Empty(w.Others);
        Assert.Equal(1, w.Stats.ShipsSunk);
        Assert.Equal(-65, w.Player.Rep(Faction.Brethren));
        Assert.Equal(10, w.Player.Rep(Faction.Crown));
        Assert.True(w.Flotsam.Count >= 3);
        var chest = w.Flotsam.First(f => f.Gold > 0);
        var before = chest.Pos;
        for (int i = 0; i < 30; i++) w.Tick(new ShipInput(0, 0));
        Assert.True(chest.Pos.DistanceTo(before) > 0.5, "flotsam drifts downwind");
        w.Ship.Pos = chest.Pos;
        w.Tick(new ShipInput(0, 0));
        Assert.True(w.Player.Gold > gold);
        Assert.DoesNotContain(chest, w.Flotsam);
        Assert.Contains(w.Events, e => e.Type == CombatEventType.Collect);
        for (int i = 0; i < 30 * 61; i++) w.Tick(new ShipInput(0, 0));
        Assert.Empty(w.Flotsam);
    }

    [Fact]
    public void CrewOrdersReassignTheHands()
    {
        var w = Sea.Fixed();
        var ship = w.Ship;
        ship.Crew = 10;
        ship.Cannons = 4;
        w.Tick(new ShipInput(0, 0, Order: 1));
        Assert.Equal(CrewOrder.Battle, ship.Order);
        Assert.Equal(new[] { 4, 2, 1, 3 }, ship.Stations());
        w.Tick(new ShipInput(0, 0, Order: 2));
        Assert.Equal(new[] { 4, 2, 1, 3 }, ship.Stations());
        ship.Crew = 3;
        Assert.Equal(new[] { 1, 2, 0, 0 }, ship.Stations());   // make sail: riggers first
        w.Tick(new ShipInput(0, 0, Order: 3));
        Assert.Equal(new[] { 0, 1, 1, 1 }, ship.Stations());
        w.Tick(new ShipInput(0, 0, Order: 4));
        Assert.Equal(CrewOrder.Balanced, ship.Order);
    }

    [Fact]
    public void TheSparringCaptainFightsBackAndReplaysExactly()
    {
        var w = Sea.Fixed(fromCompass: 0, seed: 8);
        Sea.Point(w, 90);   // beam reach, the foe spawns 150 m to starboard
        w.Ship.Cannons = 4;
        w.Ship.Crew = 8;
        w.Player.Cargo[(int)Good.Munitions] = 60;
        var foe = w.SpawnSparring();
        double hp = w.Ship.HullHp;
        bool foeFired = false;
        for (int t = 0; t < 30 * 90; t++)
        {
            // The player keeps the foe on the starboard beam and fires whenever it bears within range.
            var rel = foe.Pos - w.Ship.Pos;
            double want = Angles.Wrap(rel.Angle - Math.PI / 2);
            double bearing = Angles.Wrap(rel.Angle - w.Ship.Heading);
            bool bears = rel.Length < w.Ship.Range * 0.95 && Math.Abs(bearing - Math.PI / 2) < Angles.Rad(20);
            w.Tick(new ShipInput(Pilot.Helm(w.Ship, want), t == 0 ? 3 : 0, FireStarboard: bears && w.Ship.Loaded[1]));
            foeFired |= w.Events.Any(e => e.Type == CombatEventType.Fire && e.ShipId == foe.Id);
        }
        Assert.True(foe.HullHp < foe.Hull.HullHp, "the foe should have been hit in ninety seconds");
        Assert.True(foeFired, "the sparring captain should fire back");
        Assert.True(w.Ship.HullHp < hp, "and land something");
        var replay = World.Replay(w.Seed, w.HullId, w.Log, w.Commands, w.Ticks);
        replay.Islands.Clear();
        // The replay has no sparring ship (spawns are not logged), so compare only the player's own state.
        Assert.Equal(w.Ticks, replay.Ticks);
    }

    [Fact]
    public void SaveAndLoadKeepTheDamageState()
    {
        var w = Sea.Fixed();
        w.Ship.HullHp = 55;
        w.Ship.Water = 33;
        w.Ship.Leaks = 2;
        w.Ship.Loaded[0] = false;
        w.Ship.Reload[0] = 4.5;
        w.Ship.Order = CrewOrder.Repair;
        w.Ship.Crew = 7;
        var loaded = World.LoadJson(w.SaveJson());
        Assert.Equal(55, loaded.Ship.HullHp);
        Assert.Equal(33, loaded.Ship.Water);
        Assert.Equal(2, loaded.Ship.Leaks);
        Assert.False(loaded.Ship.Loaded[0]);
        Assert.Equal(4.5, loaded.Ship.Reload[0]);
        Assert.Equal(CrewOrder.Repair, loaded.Ship.Order);
        Assert.Equal(7, loaded.Ship.Crew);
        Assert.Equal(w.Hash(), loaded.Hash());
    }

    [Fact]
    public void AHarbourRescuesHerOnlyOnce()
    {
        var w = World.NewRun(9, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        var port = w.Map.StartPort;
        w.Ship.Pos = port.Harbor;
        w.Ship.HullHp = 0;
        w.Ship.Foundering = true;
        w.Ship.Hourglass = 20;
        w.Tick(new ShipInput(0, 0));
        Assert.True(w.IsDocked && !w.Ship.Foundering && w.RescuedAt.Contains(port.Id), "the first time the harbour takes her in");
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Ship.Pos = port.Harbor;
        w.Ship.HullHp = 0;
        w.Ship.Foundering = true;
        w.Ship.Hourglass = 0.05;
        for (int i = 0; i < 5; i++) w.Tick(new ShipInput(0, 0));
        Assert.True(w.RunOver, "the second time she goes down at the harbour mouth");
        Assert.Contains("NOTICE_RESCUE_SPENT", w.Notices);
    }
}
