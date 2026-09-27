using LastTide.Sim;

namespace Sim.Tests;

public class MonsterTests
{
    /// <summary>A quiet world with the player parked at a sea point inside the given region, guns loaded.</summary>
    static World In(RegionType region, int seed = 21)
    {
        var w = World.NewRun(seed, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;   // each test spawns its own beast
        w.Wind.SetFixed(Angles.Wrap(Angles.FromCompassDeg(0) + Math.PI), 8);
        var centre = w.Map.RegionOf(region).Seed;
        Vec2 spot = centre;
        for (int r = 0; r < 1200 && !(w.Map.RegionAt(spot).Type == region && w.Map.Nav.IsSea(spot) && !w.Map.IslandsNear(spot, 60).Any()); r += 40)
            spot = centre + Vec2.FromAngle(r * 0.7) * r;
        w.Ship.Pos = spot;
        w.Ship.Heading = Angles.FromCompassDeg(90);
        w.Ship.Cannons = 4;
        w.Ship.Crew = 10;
        w.Ship.Order = CrewOrder.Battle;
        w.Player.Cargo[(int)Good.Munitions] = 200;
        return w;
    }

    static void Run(World w, double seconds, Func<ShipInput>? input = null)
    {
        for (int i = 0; i < seconds * 30; i++) w.Tick(input?.Invoke() ?? new ShipInput(0, 0));
    }

    /// <summary>Fires whichever broadside bears on a target within range.</summary>
    static ShipInput Gunnery(World w, Vec2 target)
    {
        var rel = target - w.Ship.Pos;
        double bearing = Angles.Wrap(rel.Angle - w.Ship.Heading);
        bool s = Math.Abs(bearing - Math.PI / 2) < Angles.Rad(24) && w.Ship.Loaded[1];
        bool p = Math.Abs(bearing + Math.PI / 2) < Angles.Rad(24) && w.Ship.Loaded[0];
        return new ShipInput(0, 0, p, s);
    }

    [Fact]
    public void MonstersRollOncePerHourInTheirRegionMoreOftenAtNight()
    {
        var w = In(RegionType.Shoals);
        w.MonstersEnabled = true;
        Assert.Null(w.Monster);
        int spawnedTicks = -1;
        for (int i = 0; i < 30 * 1800 && w.Monster == null; i++)
        {
            w.Tick(new ShipInput(0, 0));
            if (w.Monster != null) spawnedTicks = i;
        }
        Assert.NotNull(w.Monster);
        Assert.Equal(MonsterType.ReefSerpent, w.Monster!.Type);
        Assert.True(spawnedTicks >= 30 * World.MonsterRest, "a rest before the first beast");
        // Elsewhere nothing wakes: the Trade Isles have no monster.
        var quiet = In(RegionType.TradeIsles);
        quiet.MonstersEnabled = true;
        Run(quiet, 1200);
        Assert.Null(quiet.Monster);
    }

    [Fact]
    public void TheReefSerpentBitesOpensLeaksAndDiesToABroadside()
    {
        var w = In(RegionType.Shoals);
        var m = w.SpawnMonster(MonsterType.ReefSerpent);
        int leaks = w.Ship.Leaks;
        double hp = w.Ship.HullHp;
        int t = 0;
        while (m.Bites == 0 && t++ < 30 * 60) w.Tick(new ShipInput(0, 0));
        Assert.True(m.Surfaced, "it surfaces alongside to bite");
        Assert.Equal(leaks + 1, w.Ship.Leaks);
        Assert.True(w.Ship.HullHp < hp);
        // Shoot it while it is up.
        t = 0;
        while (!m.Done && t++ < 30 * 240) w.Tick(m.Surfaced ? Gunnery(w, m.Pos) : new ShipInput(0, 0));
        Assert.True(m.Beaten, "a serpent alongside eats broadsides");
        Assert.Null(w.Monster);
        Assert.Contains("serpent_slayer", w.Player.Achievements);
        Assert.Equal(1, w.Stats.MonstersBeaten);

        // Or leave it behind: it will not follow out of the Shoals.
        var w2 = In(RegionType.Shoals, seed: 22);
        var m2 = w2.SpawnMonster(MonsterType.ReefSerpent);
        var deep = w2.Map.RegionOf(RegionType.Deep).Seed;
        w2.Ship.Pos = deep;
        Run(w2, 20);
        Assert.True(m2.Done && !m2.Beaten);
    }

    [Fact]
    public void TheKrakenPinsTheShipUntilItsTentaclesAreShot()
    {
        var w = In(RegionType.Deep);
        w.Ship.Crew = 6;   // guns and sails manned, no carpenter to hack at the tentacles (that path: AuditCombatTests)
        w.Ship.SailTarget = 3;
        w.Ship.SailFraction = 1;
        var m = w.SpawnMonster(MonsterType.Kraken);
        int t = 0;
        while (m.State != MonsterState.Grip && t++ < 30 * 60) w.Tick(new ShipInput(0, 0));
        Assert.Equal(MonsterState.Grip, m.State);
        Assert.Equal(4, m.Targets.Count);
        Run(w, 3);
        Assert.True(w.Pinned);
        Assert.True(w.Ship.Speed < 1.5, $"pinned, speed {w.Ship.Speed:F1}");
        double hp = w.Ship.HullHp;
        Run(w, 10);   // past the first smash (every 12 s)
        Assert.True(w.Ship.HullHp < hp - 6, "the tentacles smash the hull");
        Assert.True(w.Ship.Leaks >= 1);
        // Both broadsides bear on two tentacles each.
        t = 0;
        while (m.State == MonsterState.Grip && t++ < 30 * 200)
            w.Tick(new ShipInput(0, 0, w.Ship.Loaded[0], w.Ship.Loaded[1]));
        Assert.NotEqual(MonsterState.Grip, m.State);
        Assert.Contains("unkrakened", w.Player.Achievements);
        Run(w, 10);
        Assert.Null(w.Monster);
        Assert.False(w.Pinned);
    }

    [Fact]
    public void TheGhostShipIsOnlySolidWhileItsLanternsFlare()
    {
        var w = In(RegionType.FogBanks);
        Run(w, 80);   // night: the ghost is seen even in clear air
        Assert.True(w.IsNight);
        var m = w.SpawnMonster(MonsterType.GhostShip);
        Assert.True(m.Visible(w.ConditionsAt(w.Ship.Pos)));
        bool ghostFired = false, passedThrough = false, struck = false;
        for (int i = 0; i < 30 * 120 && !m.Done; i++)
        {
            w.Tick(Gunnery(w, m.Pos));
            ghostFired |= w.Events.Any(e => e.Type == CombatEventType.Fire && e.ShipId == -3);
            passedThrough |= w.Events.Any(e => e.Type == CombatEventType.Splash && e.ShipId == -3);
            struck |= w.Events.Any(e => e.Type == CombatEventType.Hit && e.ShipId == -3);
        }
        Assert.True(ghostFired, "it fires broadsides");
        Assert.True(passedThrough, "balls pass through it between flares");
        Assert.True(struck, "and strike it during a flare");
    }

    [Fact]
    public void TheCrocodileJamsTheRudderAndTheSirenPullsIt()
    {
        var w = In(RegionType.Mangrove);
        var croc = w.SpawnMonster(MonsterType.Crocodile);
        w.Ship.Pos = croc.Perch + (w.Ship.Pos - croc.Perch).Normalized * 40;
        int t = 0;
        while (w.RudderJam <= 0 && t++ < 30 * 40) w.Tick(new ShipInput(1, 0));
        Assert.True(w.RudderJam > 0, "the bite jams the rudder");
        double rudder = w.Ship.Rudder;
        Run(w, 1, () => new ShipInput(1, 0));
        Assert.True(w.Ship.Rudder <= rudder, "helm orders do nothing while jammed");
        Run(w, 6, () => new ShipInput(1, 0));
        Assert.True(w.Ship.Rudder > 0.9, "and answer again afterwards");
        Assert.Contains("NOTICE_RUDDER_JAMMED", w.Notices);

        var s = In(RegionType.SirenRuins, seed: 23);
        var siren = s.SpawnMonster(MonsterType.Siren);
        s.Ship.Pos = siren.Perch + new Vec2(120, 0);
        s.Ship.Heading = Angles.FromCompassDeg(0);
        s.Tick(new ShipInput(0, 0));
        Assert.NotEqual(0, s.SirenPull);
        double heading = s.Ship.Heading;
        s.Ship.SailTarget = 2;
        s.Ship.SailFraction = 0.75;
        Run(s, 4);
        Assert.True(Math.Abs(Angles.Wrap(s.Ship.Heading - heading)) > Angles.Rad(5), "the song turns her toward the rocks");
        // Silence her: shoot the perch.
        t = 0;
        while (!siren.Done && t++ < 30 * 200)
        {
            s.Ship.Pos = siren.Perch + new Vec2(120, 0);
            s.Ship.Heading = Angles.FromCompassDeg(0);
            s.Tick(Gunnery(s, siren.Perch));
        }
        Assert.True(siren.Beaten);
        Assert.Contains("deaf_ears", s.Player.Achievements);
    }

    [Fact]
    public void TheWeedKrakenDragsHerDownUntilTheMassIsCut()
    {
        var w = In(RegionType.Sargasso);
        w.Ship.Order = CrewOrder.Repair;   // carpenters cut, the rest pump
        var m = w.SpawnMonster(MonsterType.WeedKraken);
        int t = 0;
        while (m.State != MonsterState.Grip && t++ < 30 * 60) w.Tick(new ShipInput(0, 0));
        Assert.Equal(MonsterState.Grip, m.State);
        double water = w.Ship.Water;
        Run(w, 3);
        Assert.True(w.Pinned);
        Assert.True(w.Ship.Water > water, "the weed drags her down");
        t = 0;
        while (m.State == MonsterState.Grip && t++ < 30 * 120) w.Tick(new ShipInput(0, 0));
        Assert.NotEqual(MonsterState.Grip, m.State);
        Assert.Contains("weeded_out", w.Player.Achievements);
        // The weed slows every hull in the region (but not in a pinned-wind laboratory).
        var live = World.NewRun(21, populate: false);
        live.MonstersEnabled = false;
        live.DirectorEnabled = false;
        live.Ship.Pos = w.Ship.Pos;
        live.Tick(new ShipInput(0, 0));
        Assert.Equal(World.SargassoDrag, live.Ship.SpeedMult, 6);
        Assert.Equal(1, w.Ship.SpeedMult, 6);
    }

    [Fact]
    public void EruptionsAreTelegraphedThenHurtWhatStandsInsideAndEverythingSaves()
    {
        var w = In(RegionType.Volcanic);
        w.EruptionClock = 0.5;
        Run(w, 2);
        Assert.Single(w.Eruptions);
        var e = w.Eruptions[0];
        Assert.False(e.Landed);
        Assert.InRange(e.Warning, 3, 5);
        w.Ship.Pos = e.Pos;
        double hp = w.Ship.HullHp;
        Run(w, 6, () => { w.Ship.Pos = e.Pos; return new ShipInput(0, 0); });
        Assert.True(e.Landed);
        Assert.Equal(hp - 20, w.Ship.HullHp, 6);
        Assert.Contains("NOTICE_ERUPTION", w.Notices);

        var k = In(RegionType.Deep, seed: 24);
        var m = k.SpawnMonster(MonsterType.Kraken);
        Run(k, 5);
        var loaded = World.LoadJson(k.SaveJson());
        Assert.NotNull(loaded.Monster);
        Assert.Equal(m.Type, loaded.Monster!.Type);
        Assert.Equal(m.Pos, loaded.Monster.Pos);
        Assert.Equal(k.Hash(), loaded.Hash());
        for (int i = 0; i < 90; i++)
        {
            k.Tick(new ShipInput(0, 0));
            loaded.Tick(new ShipInput(0, 0));
        }
        Assert.Equal(k.Hash(), loaded.Hash());
    }
}
