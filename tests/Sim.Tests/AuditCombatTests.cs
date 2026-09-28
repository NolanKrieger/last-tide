using LastTide.Sim;

namespace Sim.Tests;

/// <summary>Regressions from the sim-combat audit (2026-09-23): each test failed on the audited baseline.</summary>
public class AuditCombatTests
{
    /// <summary>A world with no fleet, no director and no beasts; the wind pinned from the given compass bearing.</summary>
    static World Quiet(int seed, double windFromCompass = 0, Preset preset = Preset.RoughSeas)
    {
        var w = World.NewRun(seed, preset: preset, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        w.Wind.SetFixed(Angles.Wrap(Angles.FromCompassDeg(windFromCompass) + Math.PI), 8);
        return w;
    }

    static void Run(World w, double seconds, ShipInput input = default)
    {
        for (int i = 0; i < seconds * Tuning.TicksPerSecond; i++) w.Tick(input);
    }

    /// <summary>
    /// A sea point with no land on the nav grid within <paramref name="clear"/> metres, in plain water: not the weed, the
    /// ice, the whirlpools' straits, the maze or the volcano, whose own rules would muddle a test of seamanship.
    /// </summary>
    static Vec2 OpenWater(World w, double clear = 450, Func<Vec2, bool>? ok = null)
    {
        var plain = new[] { RegionType.TradeIsles, RegionType.Deep, RegionType.StormReach, RegionType.FogBanks, RegionType.Shoals };
        for (double y = -1800; y <= 1800; y += 150)
            for (double x = -2600; x <= 2600; x += 150)
                if (Check(new Vec2(x, y)) is { } near) return near;
        for (double y = -Map.HalfH + 600; y <= Map.HalfH - 600; y += 150)
            for (double x = -Map.HalfW + 600; x <= Map.HalfW - 600; x += 150)
                if (Check(new Vec2(x, y)) is { } far) return far;
        throw new InvalidOperationException("no open water on this map");

        Vec2? Check(Vec2 c)
        {
            if (!plain.Contains(w.Map.RegionAt(c).Type) || (ok != null && !ok(c))) return null;
            for (int a = 0; a < 16; a++)
                for (double r = 0; r <= clear; r += 50)
                {
                    var p = c + Vec2.FromAngle(a * Angles.Tau / 16) * r;
                    if (!Map.InBounds(p) || !w.Map.Nav.IsSea(p)) return null;
                }
            return c;
        }
    }

    [Fact]
    public void AShipAgroundOnRocksTheGridCannotSeeWorksHerWayOff()
    {
        // The nav grid marks a cell as land only when its centre lies within 10 m of a coast, so a thin spit can fall
        // between cell centres: a ship's look-ahead reads clear water while her hull is on the rocks. Here a 20 m-thick
        // bar (not on the grid) lies across a patrol's course. Baseline: she drove into it for the rest of the run.
        var w = Quiet(4, windFromCompass: 90);
        var at = OpenWater(w);
        w.Ship.Pos = at + new Vec2(0, 900);
        var bar = new Island(new[] { at + new Vec2(-120, -90), at + new Vec2(120, -90), at + new Vec2(120, -70), at + new Vec2(-120, -70) });
        w.Islands.Add(bar);
        var home = w.Map.Ports.Where(p => p.Faction == Faction.Crown).OrderBy(p => p.Harbor.DistanceTo(at)).First();
        var patrol = w.Spawn("cutter", at, Angles.FromCompassDeg(0), Faction.Crown, new PatrolCaptain());
        var goal = at + new Vec2(0, -300);
        patrol.Ai = new AiState { Role = Role.Patrol, HomePort = home.Id, HasWaypoint = true, Waypoint = goal, Repath = 1000 };
        patrol.SailTarget = 2;
        patrol.SailFraction = Tuning.SailFraction[2];
        double closest = double.MaxValue;
        for (int i = 0; i < 30 * 180 && closest > Seamanship.ArriveRadius; i++)
        {
            w.Tick(default);
            closest = Math.Min(closest, patrol.Pos.DistanceTo(goal));
        }
        Assert.True(closest <= Seamanship.ArriveRadius + 5, $"she should have worked round the bar to her waypoint; closest {closest:0} m");
    }

    // ---- AI captains ----

    [Fact]
    public void AMerchantThatLosesSightOfARaiderKeepsRunningFromWhereItWas()
    {
        // Baseline: the twelve seconds of flight after the threat drops out of sight ran from AiState.Waypoint, which a
        // merchant never sets: she fled from the map's origin, here straight back toward the raider.
        var w = Quiet(4);
        var m = OpenWater(w);
        var raiderAt = m + m.Normalized * 300;   // running from the raider means running toward the origin
        w.Ship.Pos = m + new Vec2(0, 900);
        w.Ship.Vel = Vec2.Zero;
        var away = (m - raiderAt).Normalized;
        var merchant = w.Spawn("schooner", m, away.Angle, Faction.FreeTraders, new MerchantCaptain(), crew: 8, cannons: 0);
        merchant.Ai = new AiState { Role = Role.Merchant, HomePort = w.Map.StartPort.Id, Wait = 1000 };
        merchant.SailTarget = 3;
        merchant.SailFraction = 1;
        merchant.Vel = away * 7;   // already running
        var raider = w.Spawn("sloop", raiderAt, 0, Faction.Brethren, null, crew: 6, cannons: 2);
        raider.Ai = new AiState { Role = Role.Raider, HomePort = w.Map.StartPort.Id };
        Run(w, 3);
        Assert.True(merchant.Ai.Fleeing);
        w.Others.Remove(raider);   // out of sight
        w.Captains.Remove(raider.Id);
        double gone = merchant.Pos.DistanceTo(raiderAt);
        Run(w, 10);
        Assert.True(merchant.Pos.DistanceTo(raiderAt) > gone + 20,
            $"she should keep running from where the raider was: {gone:0} m → {merchant.Pos.DistanceTo(raiderAt):0} m");
    }

    // ---- AI seamanship ----

    [Fact]
    public void FromAStandstillACaptainWearsRoundInsteadOfStallingHeadToWind()
    {
        // A merchant lying still 54° off the wind, the raider dead ahead: the shortest turn to run away passes through
        // the eye of the wind. Baseline: she took it, stalled in irons and was still wallowing twelve seconds later.
        var w = Quiet(4);
        double north = Angles.FromCompassDeg(0);
        var m = OpenWater(w, ok: c => Angles.Deg(Math.Abs(Angles.Wrap(c.Angle - north))) is >= 45 and <= 65);
        var raiderAt = m + m.Normalized * 300;
        w.Ship.Pos = m + new Vec2(0, 900);
        w.Ship.Vel = Vec2.Zero;
        var merchant = w.Spawn("schooner", m, (raiderAt - m).Angle, Faction.FreeTraders, new MerchantCaptain(), crew: 8, cannons: 0);
        merchant.Ai = new AiState { Role = Role.Merchant, HomePort = w.Map.StartPort.Id, Wait = 1000 };
        merchant.SailTarget = 3;
        merchant.SailFraction = 1;
        var raider = w.Spawn("sloop", raiderAt, 0, Faction.Brethren, null, crew: 6, cannons: 2);
        raider.Ai = new AiState { Role = Role.Raider, HomePort = w.Map.StartPort.Id };
        Assert.InRange(Angles.Deg(Math.Abs(Angles.Wrap((raiderAt - m).Angle - Angles.Wrap(Angles.FromCompassDeg(0))))), 45, 65);
        double minOff = 180;
        for (int i = 0; i < 30 * 12; i++)
        {
            w.Tick(default);
            if (i > 30) minOff = Math.Min(minOff, merchant.AngleOffWindDeg);
        }
        Assert.True(minOff >= merchant.PointDeg, $"she should never lie in irons: closest {minOff:0}° off the wind");
        Assert.True(merchant.Speed > 7, $"and be running at speed: {merchant.Speed:0.0} m/s");
    }

    /// <summary>
    /// The whole fleet for six minutes with the player parked (kept afloat, at peace with everyone): no merchant on a
    /// voyage, patrol or raider may go two minutes without making 30 m of way. On the baseline 10+ of ~30 ships froze
    /// against the coast grid or in irons within six minutes (28 of 32 within twenty).
    /// </summary>
    [Fact]
    public void TheFleetKeepsMoving()
    {
        // Seed 3 under map generator v5 (seed 7's map changed with v5 and trades slowly); the stuck rule is the point.
        var w = World.NewRun(3, populate: true);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        for (int i = 0; i < 3; i++) w.Player.Reputation[i] = 0;
        var anchor = new Dictionary<int, (Vec2 Pos, double Time)>();
        var stuck = new HashSet<int>();
        for (int t = 0; t < 30 * 360; t++)
        {
            var s = w.Ship;
            s.HullHp = s.MaxHp; s.Water = 0; s.Leaks = 0; s.Foundering = false;
            w.Tick(default);
            if (t % 30 != 0) continue;
            foreach (var o in w.Others)
            {
                bool underWay = o.Ai!.Role is Role.Patrol or Role.Raider || o.Ai.Role == Role.Merchant && o.Ai.DestPort >= 0;
                if (!underWay || !anchor.TryGetValue(o.Id, out var a) || o.Pos.DistanceTo(a.Pos) > 30) { anchor[o.Id] = (o.Pos, w.Time); continue; }
                if (w.Time - a.Time > 120 && o.Water < 90) stuck.Add(o.Id);
            }
        }
        Assert.True(stuck.Count <= 1, $"{stuck.Count} ships stuck: " + string.Join(", ", stuck.Select(id => w.Others.FirstOrDefault(o => o.Id == id) is { } o ? $"{o.Ai!.Role} {o.Hull.Id} at {o.Pos}" : $"#{id}")));
        Assert.True(w.MerchantArrivals >= 8, $"merchant arrivals {w.MerchantArrivals}");
    }

    [Fact]
    public void ADistantPatrolAgainstAShoalCellTurnsAwayInsteadOfFreezing()
    {
        // Seed 7, as found by the fleet diagnostic: a coarse-ticked patrol whose next step is a land cell of the
        // nav grid while the look-ahead 78 m out is clear. On the baseline it froze there for the rest of the run.
        var w = Quiet(7, windFromCompass: 36);
        var start = new Vec2(-587.08, -1475.04);
        var home = w.Map.Ports.Where(p => p.Faction == Faction.Crown).OrderBy(p => p.Harbor.DistanceTo(start)).First();
        w.Ship.Pos = w.SeaPointNear(start + new Vec2(2400, 1400), 0, 300);   // far away: the patrol takes coarse ticks
        Assert.True(w.Ship.Pos.DistanceTo(start) > World.NearRadius);
        var patrol = w.Spawn("cutter", start, Angles.FromCompassDeg(132), Faction.Crown, new PatrolCaptain());
        patrol.Ai = new AiState { Role = Role.Patrol, HomePort = home.Id, HasWaypoint = true, Waypoint = new Vec2(-358.34, -998.32) };
        patrol.SailTarget = 2;
        patrol.SailFraction = Tuning.SailFraction[2];
        Run(w, 60);
        Assert.True(patrol.Pos.DistanceTo(start) > 150, $"the patrol should have got clear and sailed on; it is {patrol.Pos.DistanceTo(start):0} m from where it started");
    }

    // ---- AI gunnery ----

    [Fact]
    public void AnAiCaptainHoldsHisFireUntilTheBroadsideWouldCrossHerHull()
    {
        // Baseline: Engage fired whenever the target lay within 22° of the beam, at any range. The balls fly square off
        // the side (±3°): at 170 m and 21° off the beam they pass 60 m wide, and a whole reload was wasted.
        var w = Quiet(4);
        w.Islands.Clear();
        var at = OpenWater(w);
        var hunter = w.SpawnHunter("sloop", at, Faction.Brethren);
        hunter.Heading = Angles.FromCompassDeg(90);
        hunter.Vel = Vec2.Zero;
        hunter.Cannons = 4;
        hunter.Crew = 6;
        var mark = w.Spawn("sloop", at + Vec2.FromAngle(hunter.Heading + Angles.Rad(90 - 21)) * 170, hunter.Heading, Faction.FreeTraders, null);
        var wide = Seamanship.Engage(w, hunter, mark);
        Assert.False(wide.FireStarboard || wide.FirePort, "21° off the beam at 170 m the broadside cannot hit");
        mark.Pos = at + hunter.Right * 150;   // square on the beam
        var square = Seamanship.Engage(w, hunter, mark);
        Assert.True(square.FireStarboard, "square on the starboard beam within range: fire");
        Assert.True(w.Fire(hunter, Side.Starboard));
        double hp = mark.HullHp;
        for (int i = 0; i < 60; i++) w.Tick(default);
        Assert.True(mark.HullHp < hp, "and the ripple strikes her");
    }

    // ---- Ramming ----

    [Fact]
    public void BeingRammedIsNotDrawingBlood()
    {
        // Baseline: any collision with the player charged her −10 with the other ship's faction and marked that ship
        // hostile — a Crown patrol that sailed into her side then engaged her.
        var w = Quiet(4);
        w.Islands.Clear();
        var at = OpenWater(w);
        w.Ship.Pos = at;
        w.Ship.Vel = Vec2.Zero;
        w.Ship.Heading = Angles.FromCompassDeg(90);
        var patrol = w.Spawn("cutter", at + new Vec2(0, -60), Angles.FromCompassDeg(180), Faction.Crown, null);
        patrol.Ai = new AiState { Role = Role.Patrol, HomePort = w.Map.StartPort.Id };
        patrol.SailTarget = 3;
        patrol.SailFraction = 1;
        patrol.Vel = new Vec2(0, 8);
        bool rammed = false;
        for (int i = 0; i < 30 * 15 && !rammed; i++)
        {
            w.Tick(default);
            rammed = w.Events.Any(e => e.Type == CombatEventType.Ram);
        }
        Assert.True(rammed, "the patrol should strike her side");
        Assert.True(w.Ship.HullHp < w.Ship.MaxHp, "and it hurts");
        Assert.Equal(0, w.Player.Rep(Faction.Crown));
        Assert.False(patrol.PlayerHostile);
        Assert.False(w.Hostile(patrol, w.Ship));
    }

    // ---- Damage control ----

    [Fact]
    public void AnAiShipWithoutPlanksDoesNotRunUpATimberDebt()
    {
        // Baseline: the AI's plank counter was decremented on every failed patching attempt (−30 a second).
        var w = Quiet(4);
        var ship = w.Spawn("sloop", w.Ship.Pos + new Vec2(200, 0), 0, Faction.Brethren, null, crew: 8, cannons: 2);
        ship.Ai = new AiState { Role = Role.Raider, HomePort = w.Map.StartPort.Id };
        ship.HullHp = ship.MaxHp * 0.5;
        ship.Timber = 0;
        Run(w, 2);
        Assert.Equal(0, ship.Timber);
        Assert.Equal(ship.MaxHp * 0.5, ship.HullHp, 6);   // and no planks, no patching
    }

    // ---- The director ----

    [Fact]
    public void NightBringsMoreCreditsButNotHigherCardsOrAHigherCap()
    {
        // GDD §19: hunters earn credits 1.5x at night. Baseline: the whole Threat was multiplied, so night also unlocked
        // cards early (brig at Threat 1.34, pack at 2.34, galleon at 3.0) and raised the hunter cap.
        var w = Quiet(5, preset: Preset.Tempest);
        while (!(w.IsNight && w.ThreatNow >= 1.34)) w.Tick(default);   // the second night, Threat ≈ 1.37
        Assert.InRange(w.ThreatNow, 1.3, 1.99);
        Assert.True(w.ThreatNow * 1.5 >= 2.0, "the old night multiplier would reach the brig card");
        w.DirectorEnabled = true;
        w.SpawnHunter("sloop", w.Ship.Pos + new Vec2(900, 0), Faction.Brethren);
        w.SpawnHunter("sloop", w.Ship.Pos + new Vec2(-900, 0), Faction.Brethren);
        w.Director.Credits = 300;
        w.Director.Clock = 0;
        w.Director.QuietFor = 0;
        w.Tick(default);
        Assert.Equal(2, w.HuntersAlive);   // cap 1 + ⌊1.3⌋ = 2, not 1 + ⌊2.0⌋ = 3
        // With room under the cap, twenty purchases at night never reach the brig.
        foreach (var h in w.Others.ToList()) { w.Others.Remove(h); w.Captains.Remove(h.Id); }
        var bought = new List<string>();
        for (int i = 0; i < 20; i++)
        {
            w.Director.Credits = 300;
            w.Director.Clock = 0;
            w.Director.QuietFor = 0;
            w.Tick(default);
            bought.Add(w.Director.LastCard);
            foreach (var h in w.Others.ToList()) { w.Others.Remove(h); w.Captains.Remove(h.Id); }
        }
        Assert.True(w.IsNight);
        Assert.DoesNotContain("brethren_brig", bought);
        Assert.Contains("brethren_sloop", bought);
    }

    // ---- Monsters ----

    static World InRegion(RegionType region, int seed = 21)
    {
        var w = Quiet(seed);
        var centre = w.Map.RegionOf(region).Seed;
        Vec2 spot = centre;
        for (int r = 0; r < 1200 && !(w.Map.RegionAt(spot).Type == region && w.Map.Nav.IsSea(spot) && !w.Map.IslandsNear(spot, 60).Any()); r += 40)
            spot = centre + Vec2.FromAngle(r * 0.7) * r;
        w.Ship.Pos = spot;
        w.Ship.Vel = Vec2.Zero;
        w.Ship.Heading = Angles.FromCompassDeg(90);
        return w;
    }

    [Theory]
    [InlineData(MonsterType.Kraken, RegionType.Deep)]
    [InlineData(MonsterType.WeedKraken, RegionType.Sargasso)]
    public void ABeastStillHuntingDoesNotFollowHerOutOfItsWaters(MonsterType type, RegionType home)
    {
        // Baseline: the Kraken (20 m/s) and the Weed-Kraken (13 m/s) chased a ship anywhere on the chart; one she
        // outran (a cutter makes 13.2 m/s) chased forever, and while any beast is in play no other can wake.
        var w = InRegion(home);
        var m = w.SpawnMonster(type);
        Run(w, 1);
        Assert.Equal(MonsterState.Approach, m.State);
        var elsewhere = InRegion(RegionType.TradeIsles).Ship.Pos;
        w.Ship.Pos = elsewhere;
        Run(w, 20);
        Assert.True(m.Done && !m.Beaten, $"{type} should give up once she has left its waters (state {m.State})");
        Assert.Null(w.Monster);
        Assert.False(w.Pinned);
    }

    // ---- The logbook's cause ----

    [Fact]
    public void TheGhostShipCanSinkHerAndTheLogbookSaysSo()
    {
        // SUNK_GHOSTSHIP ("Sunk by the Ghost Ship") could never be written: the ghost's balls carried no shooter, so
        // they neither set the monster clock nor LastHitBy, and a ghost-sunk ship was "Lost at sea".
        var w = InRegion(RegionType.FogBanks);
        while (!w.IsNight) w.Tick(default);
        w.Islands.Clear();
        var m = w.SpawnMonster(MonsterType.GhostShip);
        m.Pos = w.Ship.Pos + w.Ship.Right * 110;
        m.GunClock = 0;
        w.Ship.HullHp = 3;
        w.Ship.Crew = 2;
        for (int i = 0; i < 30 * 60 && !w.RunOver; i++) w.Tick(default);
        Assert.True(w.RunOver, "her last three points of hull and a twenty-second glass should not outlast the ghost's guns");
        Assert.Equal("SUNK_GHOSTSHIP", w.CauseOfSinking);
    }

    [Fact]
    public void AFortThatSinksHerShotHerToPieces()
    {
        // Fort balls carry no shooter either: a ship sunk by a closed port's fort was "Lost at sea".
        var w = Quiet(4);
        w.Islands.Clear();
        var fort = w.Map.Ports.First(p => p.Fort);
        w.Player.Reputation[(int)fort.Faction] = -60;   // closed: no harbour will take her in here
        w.Ship.Pos = fort.Harbor;
        w.Ship.HullHp = 5;
        w.Ship.Crew = 2;
        for (int i = 0; i < 30 * 90 && !w.RunOver; i++)
        {
            w.Ship.Pos = fort.Harbor;
            w.Ship.Vel = Vec2.Zero;
            w.Tick(default);
        }
        Assert.True(w.RunOver);
        Assert.Equal("SUNK_GUNS", w.CauseOfSinking);
    }

    // ---- The director's cards ----

    [Fact]
    public void AHunterPackIsNotDealtIntoAFullSea()
    {
        // The cap (1 + ⌊Threat⌋) was checked before the draw, so a three-ship pack could be dealt with one berth left:
        // at Threat 3.5 with 3 hunters out, the pack (cost 80) was the likeliest card and put 6 at sea against a cap of 4.
        var d = new Director();
        var rng = new Rng(7);
        var dealt = new List<string>();
        for (int i = 0; i < 40; i++)
        {
            d.Credits = 300;
            d.Clock = 0;
            d.QuietFor = 0;
            var card = d.Tick(Tuning.Dt, 3.5, crownHostile: false, huntersAlive: 3, rng);
            if (card != null) dealt.Add(card.Key);
        }
        Assert.NotEmpty(dealt);
        Assert.DoesNotContain("hunter_pack", dealt);
    }

    [Fact]
    public void ACardWithNowhereToSpawnIsNotPaidFor()
    {
        // Baseline: when no sea point was found beyond her sight, the card's cost was already spent and nothing came.
        var w = Quiet(5, preset: Preset.Tempest);
        w.VisionMult = 100;   // sight past the chart's edge: nowhere beyond it to put a hunter
        w.DirectorEnabled = true;
        w.Tick(default);
        w.Director.Credits = 100;
        w.Director.Clock = 0;
        w.Director.QuietFor = 0;
        int spawned = w.Director.Spawned;
        w.Tick(default);
        Assert.Equal(0, w.HuntersAlive);
        Assert.Equal(spawned, w.Director.Spawned);
        Assert.True(w.Director.Credits >= 100, $"credits {w.Director.Credits:0.0}: the card should be refunded");
    }

    // ---- Merchants in port ----

    [Fact]
    public void AMerchantMendsWhileSheLiesInPort()
    {
        // World.Recover ("in port an AI crew mends and reloads") ran once, on arrival: 0.7 HP. A merchant waiting out her
        // 20–45 s in harbour never mended, however long she lay there.
        var w = Quiet(4);
        var home = w.Map.StartPort;
        w.Ship.Pos = home.Harbor + new Vec2(0, 600);
        var merchant = w.Spawn("schooner", home.Harbor, 0, Faction.FreeTraders, new MerchantCaptain(), crew: 8, cannons: 0);
        merchant.Ai = new AiState { Role = Role.Merchant, HomePort = home.Id, Wait = 40 };
        merchant.HullHp = merchant.MaxHp * 0.4;
        merchant.Leaks = 2;
        Run(w, 10);
        Assert.Equal(0, merchant.Leaks);
        Assert.True(merchant.HullHp > merchant.MaxHp * 0.9, $"hull {merchant.HullHp:0} of {merchant.MaxHp:0}");
    }

    // ---- Hunters and sanctuary ----

    [Fact]
    public void AFortThatFiresOnHerIsNoSanctuaryFromHunters()
    {
        // Baseline: a hunter treated any fort of another faction whose port was still open to her (rep > −50) as a
        // sanctuary and stood off, while that fort (hostile at rep ≤ −20) fired on her.
        var w = Quiet(4);
        w.Islands.Clear();
        var fort = w.Map.Ports.First(p => p.Fort && p.Faction == Faction.Crown);
        w.Player.Reputation[(int)Faction.Crown] = -30;
        w.Ship.Pos = fort.Harbor;
        var hunter = w.SpawnHunter("sloop", fort.Harbor + (fort.Harbor - fort.Pos).Normalized * 300, Faction.Brethren);
        Run(w, 5);
        Assert.Equal(0, hunter.Ai!.Lost);
    }

    // ---- The autopilot (balance yardstick) ----

    [Fact]
    public void TheAutopilotShootsTheKrakensTentaclesToBreakFree()
    {
        // Baseline: it aimed at the beast's centre, which in a grip is her own position, so it fired only when her heading
        // happened to put "that bearing" abeam, and died pinned.
        var w = InRegion(RegionType.Deep);
        w.Ship.Cannons = 4;
        w.Ship.Crew = 10;
        w.Player.Cargo[(int)Good.Munitions] = 60;
        w.Ship.SailTarget = 3;
        w.Ship.SailFraction = 1;
        var a = new Autopilot();
        var m = w.SpawnMonster(MonsterType.Kraken);
        for (int i = 0; i < 30 * 150 && !w.RunOver && !w.Player.Achievements.Contains("unkrakened"); i++)
        {
            var input = a.Tick(w);
            if (!w.IsDocked) w.Tick(input);
        }
        Assert.Contains("unkrakened", w.Player.Achievements);
        Assert.False(w.RunOver);
    }

    [Fact]
    public void TheAutopilotDoesNotShelterUnderAFortThatFiresOnHer()
    {
        var w = Quiet(4);
        w.Islands.Clear();
        var fort = w.Map.Ports.First(p => p.Fort && p.Faction == Faction.Crown);
        fort.Discovered = true;
        w.Player.Reputation[(int)Faction.Crown] = -30;   // open, but its fort fires on her
        var outward = (fort.Harbor - fort.Pos).Normalized;
        w.Ship.Pos = fort.Harbor + outward * 250;
        w.Ship.Heading = (-outward).Angle;
        var a = new Autopilot();
        w.SpawnHunter("frigate", w.Ship.Pos + outward * 350, Faction.Brethren);
        for (int i = 0; i < 30 * 120 && !w.RunOver; i++)
        {
            var input = a.Tick(w);
            if (!w.IsDocked) w.Tick(input);
        }
        Assert.Equal(0, a.Shelters);
    }

    // ---- More monsters ----

    [Fact]
    public void TheGhostShipDoesNotFireFromClearDaylight()
    {
        // "Only visible in fog and at night" (GDD §12). Baseline: a ghost that woke in clear daylight fired a broadside
        // on its first tick, invisible, and kept its gun clock through the twelve seconds it took to fade.
        var w = InRegion(RegionType.TradeIsles);
        Run(w, 10);   // mid-morning
        Assert.False(w.IsNight);
        Assert.Equal(0, w.ConditionsAt(w.Ship.Pos).Fog);
        var m = w.SpawnMonster(MonsterType.GhostShip);
        bool fired = false;
        for (int i = 0; i < 30 * 14 && !m.Done; i++)
        {
            w.Tick(default);
            fired |= w.Events.Any(e => e.Type == CombatEventType.Fire && e.ShipId == -3);
        }
        Assert.False(fired, "an unseen ghost in clear air should not fire");
        Assert.True(m.Done && !m.Beaten, "and it fades");
    }

    [Fact]
    public void TheGhostShipOnlyWakesInFogOrAtNight()
    {
        // The hourly roll in the Fog Banks woke the ghost whatever the weather: in clear daylight it faded again at once,
        // spending the encounter and the 60 s rest on a notice ("A lantern in the fog…") with no fog.
        var w = InRegion(RegionType.FogBanks);
        w.MonstersEnabled = true;
        w.Player.Reputation[0] = 0;
        int woken = 0, clear = 0;
        for (int i = 0; i < 30 * 120 * 6; i++)
        {
            bool before = w.Monster != null;
            w.Tick(default);
            if (!before && w.Monster is { Type: MonsterType.GhostShip })
            {
                woken++;
                var c = w.ConditionsAt(w.Ship.Pos);
                if (c.Fog <= 0.05 && !c.Night) clear++;
                w.Monster.Done = true;   // dismiss it: keep rolling
            }
            if (w.Monster is { Done: true }) w.Tick(default);
        }
        Assert.True(woken > 0, "six days in the Fog Banks should wake the ghost");
        Assert.Equal(0, clear);
    }

    [Fact]
    public void TheCrocodileSlidesBackToItsBankRatherThanJumping()
    {
        // Baseline: after five seconds sliding back surfaced at 6 m/s it was put straight onto its bank, a jump of up to
        // ~200 m. It now slides home within the same five seconds (same timing, so the same danger), without the jump.
        var w = InRegion(RegionType.Mangrove);
        w.Islands.Clear();   // she lies still where it bites her
        var croc = w.SpawnMonster(MonsterType.Crocodile);
        w.Ship.Pos = croc.Perch + (w.Ship.Pos - croc.Perch).Normalized * 80;
        Assert.Equal(RegionType.Mangrove, w.Map.RegionAt(w.Ship.Pos).Type);
        var last = croc.Pos;
        double biggest = 0;
        bool bit = false;
        for (int i = 0; i < 30 * 40; i++)
        {
            w.Tick(default);
            if (w.Monster == null) break;
            bit |= w.RudderJam > 0;
            biggest = Math.Max(biggest, croc.Pos.DistanceTo(last));
            last = croc.Pos;
        }
        Assert.True(bit, "it should have lunged and bitten");
        Assert.True(biggest < 2, $"it moved {biggest:0.0} m in one tick");
    }

    // ---- Hunters in the dark ----

    [Fact]
    public void AHunterThatLosesHerSearchesWhereSheWasNotWhereSheIs()
    {
        // GDD §9: doused, she is a shadow at 200 m and can slip past. Baseline: a hunter that reached the spot where it
        // last saw her took her *current* position as its next waypoint whenever she was within 900 m — it homed on her.
        var w = Quiet(4);
        w.Islands.Clear();
        while (!w.IsNight) w.Tick(default);
        w.Tick(new ShipInput(0, 0, ToggleLantern: true));   // douse
        Assert.False(w.Lantern);
        var at = OpenWater(w);
        w.Ship.Pos = at;
        var hunter = w.SpawnHunter("cutter", at + new Vec2(-500, 0), Faction.Brethren);
        hunter.Ai!.HasWaypoint = true;
        hunter.Ai.Waypoint = hunter.Pos;   // arrived where it last saw her
        Assert.False(w.CanSpotPlayer(hunter));
        w.Tick(default);
        Assert.True(hunter.Ai.Waypoint.DistanceTo(w.Ship.Pos) > 100, $"its next search point is {hunter.Ai.Waypoint.DistanceTo(w.Ship.Pos):0} m from her");
    }

    // ---- Flotsam ----

    [Fact]
    public void ALongHullPicksUpABarrelAlongHerSide()
    {
        // Baseline: pickup was measured from the ship's centre (12 m + half the beam), so a man-o'-war sailing a barrel
        // down her side 25 m forward of midships left it bobbing.
        var w = World.NewRun(4, "man_o_war", populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        w.Wind.SetFixed(Angles.Wrap(Angles.FromCompassDeg(0) + Math.PI), 8);
        w.Islands.Clear();
        var at = OpenWater(w);
        w.Ship.Pos = at;
        w.Ship.Vel = Vec2.Zero;
        var barrel = new Flotsam { Pos = at + w.Ship.Forward * 25 + w.Ship.Right * 6, Good = Good.Rum, Units = 3 };
        w.Flotsam.Add(barrel);
        Assert.True(barrel.Pos.DistanceTo(at) > World.CollectRadius + w.Ship.Hull.Beam * 0.5, "beyond the old centre-based reach");
        w.Tick(default);
        Assert.DoesNotContain(barrel, w.Flotsam);
        Assert.Equal(3, w.Player.Units(Good.Rum));
    }

    // ---- The harbour's one mercy ----

    [Fact]
    public void AHarbourThatHasTakenHerInOnceDoesNotLetHerDockWhileFoundering()
    {
        // Baseline: at a spent harbour she could still press F: dock, repair to 100/100, cast off — and sink twenty
        // seconds later with a sound hull, because only the harbour's rescue ends a hull-zero last stand.
        var w = World.NewRun(9, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        var port = w.Map.StartPort;
        w.Ship.Pos = port.Harbor;
        w.Ship.HullHp = 0;
        w.Ship.Foundering = true;
        w.Ship.Hourglass = 20;
        w.Tick(default);
        Assert.True(w.IsDocked && w.RescuedAt.Contains(port.Id));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Ship.Pos = port.Harbor;
        w.Ship.HullHp = 0;
        w.Ship.Foundering = true;
        w.Ship.Hourglass = 20;
        w.Player.Gold = 5000;
        w.Tick(default);
        Assert.False(w.IsDocked);
        Assert.Equal(PortResult.PortClosed, w.Apply(new PortCommand(PortAction.Dock)));
        for (int i = 0; i < 30 * 25 && !w.RunOver; i++) w.Tick(default);
        Assert.True(w.RunOver, "she founders at its mouth");
    }

    [Fact]
    public void TheAutopilotRunsForAHarbourThatWillStillTakeHerIn()
    {
        var w = World.NewRun(9, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        var spent = w.Map.StartPort;
        // The nearest open harbour in plain sight across open water (the choice is under test, not the pathfinding round
        // a headland: on the landform charts a big island's next port can lie round its point).
        var other = w.Map.Ports.Where(p => p != spent && !p.Secret && w.IsOpen(p) && w.Map.Nav.SegmentClear(spent.Harbor, p.Harbor))
            .OrderBy(p => p.Harbor.DistanceTo(spent.Harbor)).First();
        spent.Discovered = other.Discovered = true;
        w.RescuedAt.Add(spent.Id);
        // Foundering between the two, nearer the spent one.
        w.Ship.Pos = Vec2.Lerp(spent.Harbor, other.Harbor, 0.35);
        w.Ship.Heading = (spent.Harbor - w.Ship.Pos).Angle;   // pointing at the spent harbour, sails set
        w.Ship.SailTarget = 3;
        w.Ship.SailFraction = 1;
        w.Ship.HullHp = 0;
        w.Ship.Foundering = true;
        w.Ship.Hourglass = 35;
        var a = new Autopilot();
        double dSpent = w.Ship.Pos.DistanceTo(spent.Harbor), dOther = w.Ship.Pos.DistanceTo(other.Harbor);
        Assert.True(dOther < 1200, $"the other harbour is {dOther:0} m off");
        for (int i = 0; i < 30 * 25; i++) w.Tick(a.Tick(w));
        Assert.Equal(Autopilot.Mode.Rescue, a.State);
        // She turns away from the harbour that has used its mercy, toward one that hasn't (on the 145-port chart the
        // nearest such may be another than `other`).
        Assert.True(w.Ship.Pos.DistanceTo(spent.Harbor) > dSpent + 10, $"she should leave the spent harbour for one that has not used its mercy ({dSpent:0} → {w.Ship.Pos.DistanceTo(spent.Harbor):0} m from it)");
    }

    // ---- Ramming dynamics ----

    [Fact]
    public void ARammedBowIsPushedAwayNotIntoTheRammer()
    {
        // A drives north into the starboard bow of B (lying still, heading east): the blow pushes B's bow north, to port.
        // Baseline: the spin given to the second hull of a pair had its sign flipped, so B's bow swung into A.
        var w = Quiet(4);
        w.Islands.Clear();
        var at = OpenWater(w);
        w.Ship.Pos = at + new Vec2(0, 900);   // near enough for physics, far from the collision
        var a = w.Spawn("sloop", at + new Vec2(5, 16), Angles.FromCompassDeg(0), Faction.Brethren, null);
        a.Vel = new Vec2(0, -8);
        a.SailTarget = 3;
        a.SailFraction = 1;
        var b = w.Spawn("sloop", at, Angles.FromCompassDeg(90), Faction.Brethren, null);
        bool rammed = false;
        for (int i = 0; i < 60 && !rammed; i++)
        {
            w.Tick(default);
            rammed = w.Events.Any(e => e.Type == CombatEventType.Ram);
        }
        Assert.True(rammed);
        Assert.True(b.AngVel < 0, $"B's bow should swing to port, away from the rammer (AngVel {b.AngVel:0.000})");
    }

    // ---- Munitions ----

    [Fact]
    public void AHoldWithoutShotFiresNothingEvenIfTheLedgerRunsNegative()
    {
        // Fire took min(guns, munitions) and only refused exactly zero: a negative count "fired" −n guns, paid −n shot
        // (so the hold gained shot) and emptied the side's reload for nothing.
        var w = Quiet(4);
        w.Ship.Cannons = 4;
        w.Player.Cargo[(int)Good.Munitions] = -3;
        Assert.False(w.Fire(w.Ship, Side.Starboard));
        Assert.Equal(-3, w.Player.Cargo[(int)Good.Munitions]);
        Assert.True(w.Ship.Loaded[(int)Side.Starboard]);
    }

    // ---- Saves ----

    [Fact]
    public void AFleetUnderWaySavesAndContinuesIdentically()
    {
        // The AI fixes reuse saved fields (AiState.Repath as the lane/waypoint check clock, Waypoint as a merchant's
        // last threat): a populated sea saved mid-voyage must continue exactly as the unsaved one.
        static void Keep(World w)
        {
            w.Ship.HullHp = w.Ship.MaxHp;   // she lies at anchor, afloat, while the fleet goes about its business
            w.Ship.Water = 0;
            w.Ship.Leaks = 0;
        }
        var a = World.NewRun(7);
        a.DirectorEnabled = false;
        a.MonstersEnabled = false;
        for (int i = 0; i < 3; i++) a.Player.Reputation[i] = 0;
        for (int t = 0; t < 30 * 90; t++) { Keep(a); a.Tick(default); }
        var b = World.LoadJson(a.SaveJson());
        Assert.Equal(a.Hash(), b.Hash());
        for (int t = 0; t < 30 * 90; t++)
        {
            Keep(a);
            Keep(b);
            a.Tick(default);
            b.Tick(default);
        }
        Assert.False(a.RunOver);
        Assert.Equal(30 * 180, a.Ticks);
        Assert.Equal(a.Hash(), b.Hash());
        Assert.Equal(a.MerchantArrivals, b.MerchantArrivals);
        Assert.True(a.MerchantArrivals > 0, "merchants should have reached port in three minutes");
    }

    // ==== Round 2 (lead's decisions) ====

    /// <summary>A square islet, <paramref name="half"/> metres from its centre to each face.</summary>
    static Island Islet(Vec2 centre, double half) => new(new[]
    {
        centre + new Vec2(-half, -half), centre + new Vec2(half, -half), centre + new Vec2(half, half), centre + new Vec2(-half, half),
    });

    [Fact]
    public void AnIslandStopsShot()
    {
        // P-01 (decided): balls burst where they meet the shore. Baseline: they flew through islands.
        var w = Quiet(4);
        w.Islands.Clear();
        var at = OpenWater(w);
        w.Ship.Pos = at;
        w.Ship.Vel = Vec2.Zero;
        w.Ship.Heading = Angles.FromCompassDeg(90);   // starboard beam = south (+y)
        w.Ship.Cannons = 4;
        w.Ship.Crew = 8;
        w.Player.Cargo[(int)Good.Munitions] = 20;
        var islet = Islet(at + new Vec2(0, 60), 20);   // its north face at y + 40
        w.Islands.Add(islet);
        var target = w.Spawn("sloop", at + new Vec2(0, 110), Angles.FromCompassDeg(90), Faction.Brethren, null);
        double hp = target.HullHp;
        var splashes = new List<Vec2>();
        w.Tick(new ShipInput(0, 0, FireStarboard: true));
        for (int i = 0; i < 90; i++)
        {
            w.Tick(default);
            splashes.AddRange(w.Events.Where(e => e.Type == CombatEventType.Splash).Select(e => e.Pos));
            Assert.DoesNotContain(w.Events, e => e.Type == CombatEventType.Hit);
        }
        Assert.Equal(hp, target.HullHp);
        Assert.Equal(2, splashes.Count);
        Assert.All(splashes, p => Assert.InRange(p.Y - at.Y, 38, 42));   // on the islet's near shore
    }

    [Fact]
    public void AnIslandBetweenGivesCover()
    {
        // A hunter with her square on its beam but an island between holds its fire; with the island gone, it fires.
        var w = Quiet(4);
        w.Islands.Clear();
        var at = OpenWater(w);
        w.Ship.Pos = at + new Vec2(0, 900);
        var hunter = w.SpawnHunter("sloop", at, Faction.Brethren);
        hunter.Heading = Angles.FromCompassDeg(90);
        hunter.Vel = Vec2.Zero;
        hunter.Cannons = 4;
        w.Ship.Pos = at + hunter.Right * 120;
        w.Islands.Add(Islet(at + hunter.Right * 60, 15));
        Assert.False(Seamanship.Engage(w, hunter, w.Ship).FireStarboard, "an island between: no broadside");
        w.Islands.Clear();
        Assert.True(Seamanship.Engage(w, hunter, w.Ship).FireStarboard, "open water: fire");
    }

    [Fact]
    public void AFortsShotLeavesFromTheHarbourSideOfItsCoast()
    {
        // Guard for P-01: with land stopping shot, a fort firing from its coastline must not hit its own island.
        var w = Quiet(4);
        var fort = w.Map.Ports.First(p => p.Fort);
        w.Player.Reputation[(int)fort.Faction] = -30;
        double hp = w.Ship.HullHp;
        bool fired = false;
        for (int i = 0; i < 30 * 25; i++)
        {
            w.Ship.Pos = fort.Harbor;
            w.Ship.Vel = Vec2.Zero;
            w.Tick(default);
            fired |= w.Events.Any(e => e.Type == CombatEventType.Fire && e.ShipId == -2);
            if (w.RunOver) break;
        }
        Assert.True(fired);
        Assert.True(w.Ship.HullHp < hp, "the fort's shot reaches her in its ring");
    }

    /// <summary>She sails at 6 m/s due south into the north face of an islet, a Siren singing on that face or not.</summary>
    static (World w, Island islet) RockRun(bool siren, double hull = 100)
    {
        var w = Quiet(4);
        w.Islands.Clear();
        var at = OpenWater(w);
        var islet = Islet(at + new Vec2(0, 60), 20);
        w.Islands.Add(islet);
        w.Ship.Pos = at + new Vec2(0, 20);
        w.Ship.Heading = Angles.FromCompassDeg(180);
        w.Ship.Vel = new Vec2(0, 6);
        w.Ship.HullHp = hull;
        w.Ship.Crew = 2;
        if (siren) w.SpawnMonster(MonsterType.Siren);
        return (w, islet);
    }

    [Fact]
    public void TheSirensRocksBite()
    {
        // P-02 (decided): while she sings, striking the coast hurts; elsewhere grounding stays harmless.
        var (w, _) = RockRun(siren: true);
        Assert.Equal(MonsterType.Siren, w.Monster!.Type);
        int leaks = w.Ship.Leaks;
        bool struck = false;
        for (int i = 0; i < 30 * 4 && !struck; i++) { w.Tick(default); struck = w.Ship.Aground; }
        Assert.True(struck, "she should reach the rocks");
        Assert.InRange(w.Ship.HullHp, 100 - 2 * 7, 100 - 2 * 3);   // 2 HP per m/s of a 3–7 m/s strike
        Assert.True(w.Ship.Leaks > leaks, "a strike from 3 m/s opens a leak");
        var (calm, _) = RockRun(siren: false);
        bool calmStruck = false;
        for (int i = 0; i < 30 * 6; i++) { calm.Tick(default); calmStruck |= calm.Ship.Aground; }
        Assert.True(calmStruck);
        Assert.Equal(100, calm.Ship.HullHp, 6);   // no Siren: the same strike is harmless
    }

    [Fact]
    public void WreckedOnTheSirensRocksIsReachable()
    {
        var (w, _) = RockRun(siren: true, hull: 4);
        for (int i = 0; i < 30 * 60 && !w.RunOver; i++) w.Tick(default);
        Assert.True(w.RunOver);
        Assert.Equal("SUNK_SIREN", w.CauseOfSinking);
    }

    // ---- P-03 / P-08: the Kraken ----

    /// <summary>A lean sloop gripped by the Kraken in the Deep: 4 hands, one gun a side, the given order and shot.</summary>
    static (World w, Monster m) Gripped(CrewOrder order, int shot)
    {
        var w = InRegion(RegionType.Deep);
        w.Ship.Crew = 4;
        w.Ship.Cannons = 2;
        w.Ship.Order = order;
        w.Player.Cargo[(int)Good.Munitions] = shot;
        var m = w.SpawnMonster(MonsterType.Kraken);
        for (int i = 0; i < 30 * 60 && m.State != MonsterState.Grip; i++) w.Tick(default);
        Assert.Equal(MonsterState.Grip, m.State);
        return (w, m);
    }

    [Fact]
    public void OneGradeZeroBallCutsATentacle()
    {
        // GDD §12: "each one hit frees a hold". Baseline: tentacles had 12 HP, two or three 4-pounder hits each.
        var (w, m) = Gripped(CrewOrder.Battle, shot: 10);
        Assert.True(w.Fire(w.Ship, Side.Starboard));
        for (int i = 0; i < 30; i++) w.Tick(default);
        Assert.Equal(1, m.Targets.Count(t => !t.Alive));
        Assert.All(m.Targets, t => Assert.True(!t.Alive || t.Hp <= 6 * 0.8, "every tentacle falls to the weakest 4-pounder ball"));
    }

    [Fact]
    public void WithNoShotHerCarpentersCutHerFree()
    {
        // P-03: a ship with no shot used to be pinned until she sank; a free carpenter now hacks at the tentacles.
        var (w, m) = Gripped(CrewOrder.Repair, shot: 0);
        for (int i = 0; i < 30 * 60 && m.State == MonsterState.Grip && !w.RunOver; i++) w.Tick(default);
        Assert.NotEqual(MonsterState.Grip, m.State);
        Assert.Contains("unkrakened", w.Player.Achievements);
        Assert.False(w.RunOver);
        Assert.True(w.Ship.HullHp > w.Ship.MaxHp * 0.4, $"and she lives to tell it: hull {w.Ship.HullHp:0}");
    }

    [Fact]
    public void TheKrakensGrindOpensNoSeams()
    {
        // P-08: the grind (1.5 HP/s) crossed a 10% leak threshold every ~7 s on top of the smash's leak, so a lean
        // sloop was at 100% water before her guns could reload. Now only the smash (every 12 s) opens a leak.
        var (w, m) = Gripped(CrewOrder.Battle, shot: 0);   // battle stations with 4 hands: no carpenter
        double hp = w.Ship.HullHp;
        for (int i = 0; i < 30 * 25; i++) w.Tick(default);
        Assert.True(hp - w.Ship.HullHp > 0.3 * w.Ship.MaxHp, "three thresholds' worth of hull ground away");
        Assert.Equal(2, w.Ship.Leaks);   // the smashes at 12 s and 24 s
    }

    // ---- R-01 / R-02 / R-03 / R-11: the ship's combat state ----

    [Fact]
    public void CrewingTheGunsSpeedsTheReloadUnderWay()
    {
        // R-01: the reload was fixed at the moment of firing: a broadside fired half-crewed took 24 s even if the
        // hands went to the guns the next second. §7: reload scales with manning.
        var w = Quiet(4);
        w.Islands.Clear();
        w.Ship.Cannons = 4;
        w.Ship.Crew = 4;
        w.Ship.Order = CrewOrder.MakeSail;   // riggers first: 2 on the sails, 2 on 4 guns
        w.Player.Cargo[(int)Good.Munitions] = 10;
        Assert.Equal(24, w.Ship.ReloadTime, 6);
        w.Tick(new ShipInput(0, 0, FireStarboard: true));
        Assert.False(w.Ship.Loaded[(int)Side.Starboard]);
        w.Tick(new ShipInput(0, 0, Order: 1));   // battle stations: all four guns manned
        Assert.Equal(12, w.Ship.ReloadTime, 6);
        Run(w, 12.2);
        Assert.True(w.Ship.Loaded[(int)Side.Starboard], "fully manned, the reload finishes in 12 s");
    }

    [Fact]
    public void AWaterStandShotToPiecesIsAHullStand()
    {
        // R-11: shot to 0 hull during a stand for water, she could still drain under 80% to end it (and start a fresh
        // glass), and if the glass ran out the logbook said "Foundered".
        var w = Quiet(4);
        var ship = w.Ship;
        ship.Pos = OpenWater(w);
        ship.Crew = 10;
        ship.Cannons = 0;
        ship.Order = CrewOrder.Repair;
        ship.HullHp = 50;
        ship.Water = 100;
        w.Tick(default);
        Assert.True(ship.Foundering && ship.WaterOnlyStand);
        var raider = w.Spawn("sloop", ship.Pos + new Vec2(0, 400), 0, Faction.Brethren, null);
        ship.Hit(ship.HullHp, raider, w.Rng);   // shot to pieces
        ship.Leaks = 0;
        ship.Water = 50;                           // and somehow drained
        double glass = ship.Hourglass;
        w.Tick(default);
        Assert.True(ship.Foundering, "draining no longer ends a stand for a hull that is gone");
        Assert.False(ship.WaterOnlyStand);
        Assert.True(ship.Hourglass < glass, "the same glass runs on");
        for (int i = 0; i < 30 * 40 && !w.RunOver; i++) w.Tick(default);
        Assert.True(w.RunOver);
        Assert.Equal("SUNK_GUNS", w.CauseOfSinking);
    }

    [Fact]
    public void AScaledHuntersLeaksCountFromItsScaledHull()
    {
        // R-03: SpawnHunter set HullHp to the scaled hull (×1.5 at Threat 6) but leak thresholds counted 10% of the
        // unscaled hull, so it opened 15 leaks on the way down instead of 10.
        var hunter = new Ship(Hulls.Get("sloop"), Vec2.Zero, 0) { DamageMult = 1.5 };
        hunter.HullHp = hunter.Hull.HullHp * 1.5;   // what SpawnHunter gives it
        Assert.Equal(150, hunter.MaxHp, 6);
        var rng = new Rng(3);
        for (int i = 0; i < 15; i++) hunter.Hit(10, null, rng, casualties: false);
        Assert.Equal(0, hunter.HullHp, 6);
        Assert.Equal(10, hunter.Leaks);
    }

    // ---- P-07 ----

    [Fact]
    public void ARamDeathSaysSo()
    {
        // P-07: a ship stove in by a ram was logged "Shot to pieces" (the rammer became LastHitBy).
        var w = Quiet(4);
        w.Islands.Clear();
        var at = OpenWater(w);
        w.Ship.Pos = at;
        w.Ship.Vel = Vec2.Zero;
        w.Ship.Heading = Angles.FromCompassDeg(90);
        w.Ship.HullHp = 3;
        var frigate = w.Spawn("frigate", at + new Vec2(0, -70), Angles.FromCompassDeg(180), Faction.Brethren, null);
        frigate.SailTarget = 3;
        frigate.SailFraction = 1;
        frigate.Vel = new Vec2(0, 10);
        for (int i = 0; i < 30 * 60 && !w.RunOver; i++) w.Tick(default);
        Assert.True(w.RunOver);
        Assert.Equal("SUNK_RAM", w.CauseOfSinking);
    }

    [Fact]
    public void EveryCauseOfSinkingHasItsLine()
    {
        // The logbook prints Text.Get(CauseOfSinking): each cause the sim can write needs a row in en.csv.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "assets", "text", "en.csv"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var keys = File.ReadAllLines(Path.Combine(dir!.FullName, "assets", "text", "en.csv")).Select(l => l.Split(',')[0]).ToHashSet();
        var causes = new[] { "SUNK_SEA", "SUNK_GUNS", "SUNK_WATER", "SUNK_RAM", "SUNK_ERUPTION" }
            .Concat(Enum.GetValues<MonsterType>().Where(t => t != MonsterType.None).Select(t => "SUNK_" + t.ToString().ToUpperInvariant()));
        Assert.All(causes, c => Assert.Contains(c, keys));
    }

    // ---- R-09: lanes ----

    [Fact]
    public void LanesDependOnTheMapAloneAndKeepToTheSea()
    {
        // R-09: lanes were A* runs inside a tick, wind-weighted by the wind of the moment the first merchant took each
        // pair (so a reloaded voyage could plan different lanes). They now come from per-harbour sea-distance fields.
        var a = World.NewRun(12, populate: false);
        var b = World.NewRun(12, populate: false);
        b.Wind.SetFixed(1.3, 12);
        for (int i = 0; i < 90; i++) b.Tick(default);
        var ports = a.Map.Ports.Where(p => !p.Secret).ToList();
        int checkedLanes = 0;
        for (int i = 0; i < ports.Count; i += 3)
            for (int j = 1; j < ports.Count; j += 5)
            {
                if (i == j || ports[i].Harbor.DistanceTo(ports[j].Harbor) > 2200) continue;
                var la = a.Lane(ports[i], ports[j]);
                var lb = b.Lane(b.Map.Ports[ports[i].Id], b.Map.Ports[ports[j].Id]);
                Assert.Equal(la, lb);
                Assert.NotEmpty(la);
                Assert.Equal(ports[j].Harbor, la[^1]);
                for (int k = 1; k < la.Count; k++)
                    Assert.True(Pathing.Clear(a.Map.Nav, la[k - 1], la[k]), $"leg {k} of {ports[i].Name}→{ports[j].Name} crosses land");
                checkedLanes++;
            }
        Assert.True(checkedLanes >= 10, $"{checkedLanes} lanes checked");
    }

    // ---- R-08 ----

    [Fact]
    public void TheFleetCrowdIsRealTraffic()
    {
        // R-08: `--fleet=N` spawned its crowd through SpawnHunter, so every "merchant" and "patrol" got a hunter's captain
        // and attacked the player. SpawnTraffic gives each role its own captain and a home port of its kind.
        var w = World.NewRun(7, populate: false);
        foreach (var role in new[] { Role.Merchant, Role.Patrol, Role.Raider })
        {
            var ship = w.SpawnTraffic(role, w.SeaPointNear(w.Ship.Pos, 250, 1200));
            var captain = w.Captains[ship.Id];
            Assert.Equal(role switch { Role.Merchant => typeof(MerchantCaptain), Role.Patrol => typeof(PatrolCaptain), _ => typeof(RaiderCaptain) }, captain.GetType());
            var home = w.Map.Ports[ship.Ai!.HomePort];
            Assert.False(home.Secret);
            if (role == Role.Patrol) Assert.Equal(Faction.Crown, home.Faction);
            if (role == Role.Raider) Assert.Equal(Faction.Brethren, home.Faction);
            if (role == Role.Merchant) Assert.False(w.Hostile(ship, w.Ship));
        }
        for (int i = 0; i < 30 * 20; i++) w.Tick(default);   // and they go about their business without a hitch
        Assert.Equal(3, w.Others.Count);
    }
}
