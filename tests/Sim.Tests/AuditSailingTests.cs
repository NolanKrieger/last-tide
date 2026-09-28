using LastTide.Sim;

namespace Sim.Tests;

/// <summary>Regressions for the sim-sailing audit (2026-09-23): each test failed on the baseline build.</summary>
public class AuditSailingTests
{
    static ShipInput Scripted(Rng rng, int tick)
    {
        double rudder = tick % 90 < 45 ? Math.Round(rng.Range(-1, 1), 2) : 0;
        int sail = tick % 300 == 0 ? (rng.Next(2) == 0 ? 1 : -1) : 0;
        return new ShipInput(rudder, sail);
    }

    /// <summary>Sinks a few AI ships quietly through the real tick, so the fleet's ids have gaps (as in any real voyage).</summary>
    static void PunchGapsInTheFleet(World w)
    {
        foreach (int k in new[] { 0, 2, 5 })
            if (k < w.Others.Count) w.Others[k].Sunk = true;
        w.Tick(new ShipInput(0, 0));
    }

    [Fact]
    public void AResumedFleetKeepsEveryCaptain()
    {
        var a = World.NewRun(5);
        PunchGapsInTheFleet(a);
        Assert.Contains(a.Others, s => s.Id > a.Others.IndexOf(s) + 1);   // the ids really have gaps
        var b = World.LoadJson(a.SaveJson());
        Assert.Equal(a.Others.Count, b.Others.Count);
        foreach (var ship in b.Others)
        {
            Assert.True(b.Captains.ContainsKey(ship.Id), $"ship {ship.Id} ({ship.Ai!.Role}) has no captain after the load");
            Assert.Equal(a.Captains[ship.Id].GetType(), b.Captains[ship.Id].GetType());
        }
        Assert.Equal(a.Captains.Count, b.Captains.Count);
        // And the voyage goes on exactly as it would have.
        var script = new Rng(9);
        for (int t = 0; t < 600; t++)
        {
            var i = Scripted(script, t);
            a.Tick(i);
            b.Tick(i);
        }
        Assert.Equal(a.Hash(), b.Hash());
    }

    /// <summary>A quiet world (no fleet, director or new beasts) with the ship in open sea of one region.</summary>
    static World QuietIn(RegionType region, int seed = 5)
    {
        var w = World.NewRun(seed, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        var centre = w.Map.RegionOf(region).Seed;
        for (int i = 0; i < 200; i++)
        {
            var p = w.SeaPointNear(centre, 0, 250 + 20 * i);
            if (p != centre && w.Map.RegionAt(p).Type == region && !w.Map.IslandsNear(p, 150).Any()) { w.Ship.Pos = p; break; }
        }
        Assert.Equal(region, w.Map.RegionAt(w.Ship.Pos).Type);
        return w;
    }

    static void TickBoth(World a, World b, ShipInput input, int ticks)
    {
        for (int t = 0; t < ticks; t++) { a.Tick(input); b.Tick(input); }
    }

    [Fact]
    public void AResumedMerchantSailsTheLaneTheRunningWorldRemembers()
    {
        // Lanes are cached per port pair with the wind of the day they were first laid. A resumed voyage must reuse
        // the same lanes, or its merchants take different routes from the voyage that was saved.
        var a = World.NewRun(5);
        foreach (var m in a.Others.Where(s => s.Ai?.Role == Role.Merchant))
        {
            var rng = a.Rng.State;
            int home = m.Ai!.HomePort;
            var stock = a.Map.Ports.Select(p => (double[])p.Market.Stock.Clone()).ToList();
            a.PlanVoyage(m);
            if (m.Ai.DestPort < 0) continue;
            // The wind swings round; she is put back in port with the dice as they were, so she picks the same lane again.
            a.Wind.BaseDirection = Angles.Wrap(a.Wind.BaseDirection + 2.2);
            m.Ai.HomePort = home; m.Ai.DestPort = -1; m.Ai.Path.Clear(); m.Ai.PathIndex = 0;
            a.Rng.State = rng;
            for (int i = 0; i < stock.Count; i++) stock[i].CopyTo(a.Map.Ports[i].Market.Stock, 0);
            var b = World.LoadJson(a.SaveJson());
            var mb = b.Others.Single(s => s.Id == m.Id);
            a.PlanVoyage(m);
            b.PlanVoyage(mb);
            Assert.Equal(m.Ai.DestPort, mb.Ai!.DestPort);
            if (m.Ai.Path.SequenceEqual(Pathing.Find(a.Map.Nav, a.Map.Ports[home].Harbor, a.Map.Ports[m.Ai.DestPort].Harbor, a.Wind.BaseDirection + Math.PI, m.Hull.PointDeg)))
                continue;   // this lane happens not to depend on the wind: try the next merchant
            Assert.Equal(m.Ai.Path, mb.Ai.Path);
            return;
        }
        Assert.Fail("no wind-dependent lane found to test");
    }

    [Fact]
    public void TheSirensPullSurvivesASave()
    {
        // Her song is worked out after the helm each tick and bends the next tick's rudder, so it is state.
        var a = World.NewRun(5, populate: false);
        a.DirectorEnabled = false;
        a.MonstersEnabled = false;
        a.SpawnMonster(MonsterType.Siren);
        a.Tick(new ShipInput(0, 0));
        Assert.NotEqual(0, a.SirenPull);
        var b = World.LoadJson(a.SaveJson());
        Assert.Equal(a.SirenPull, b.SirenPull);
        TickBoth(a, b, new ShipInput(0, 0), 30);
        Assert.Equal(a.Ship.Rudder, b.Ship.Rudder);
        Assert.Equal(a.Hash(), b.Hash());
    }

    [Fact]
    public void TheSargassoDragSurvivesASave()
    {
        // The weed's drag on the player is set after her step, so the next step reads last tick's value.
        var a = QuietIn(RegionType.Sargasso);
        a.Ship.SailTarget = 3;
        a.Ship.SailFraction = 1;
        a.Tick(new ShipInput(0, 0));
        Assert.Equal(World.SargassoDrag, a.Ship.SpeedMult);
        var b = World.LoadJson(a.SaveJson());
        TickBoth(a, b, new ShipInput(0, 0), 30);
        Assert.Equal(a.Ship.Vel, b.Ship.Vel);
        Assert.Equal(a.Hash(), b.Hash());
    }

    [Fact]
    public void AReplayOfAResumedVoyageMatchesIt()
    {
        // The input log records changes only. A resumed world used to compare its first input with a last input
        // restored without its pulses, so a one-tick pulse before the save (here the lantern) stood forever in a
        // replay. Now the resumed log opens with its first input and replays from the save.
        var a = World.NewRun(11);
        a.Tick(new ShipInput(0.3, 0));
        a.Tick(new ShipInput(0, 0, ToggleLantern: true));
        string json = a.SaveJson();
        var b = World.LoadJson(json);
        for (int t = 0; t < 91; t++) b.Tick(new ShipInput(0, 0));   // an odd count, so a standing toggle shows
        Assert.Equal(new ShipInput(0, 0), b.Log[0].Input);
        var r = World.Replay(json, b.Log, b.Commands, b.Ticks);
        Assert.Equal(b.Lantern, r.Lantern);
        Assert.Equal(b.Hash(), r.Hash());
    }

    [Fact]
    public void TheChartInksTheSameAfterASave()
    {
        // The chart is inked every 6 m of way from the last spot painted; a resumed voyage must remember that spot.
        var a = World.NewRun(3, populate: false);
        a.DirectorEnabled = false;
        a.MonstersEnabled = false;
        a.Islands.Clear();
        a.Wind.SetFixed(a.Ship.Heading, 8);
        a.Ship.SailTarget = 1;
        a.Ship.SailFraction = 0.4;
        a.Ship.Vel = a.Ship.Forward * 2;
        for (int t = 0; t < 40; t++) a.Tick(new ShipInput(0, 0));
        var b = World.LoadJson(a.SaveJson());
        for (int t = 0; t < 30 * 60; t++)
        {
            var input = new ShipInput(t % 200 < 100 ? 0.4 : -0.4, 0);
            a.Tick(input);
            b.Tick(input);
            Assert.True(a.Reveal.RevealedCells == b.Reveal.RevealedCells, $"tick {t}: {a.Reveal.RevealedCells} vs {b.Reveal.RevealedCells} cells inked");
        }
    }

    [Fact]
    public void TheMarketTrendArrowsSurviveASaveInPort()
    {
        // The suspend save is written on every dock; the port's "since your last visit" prices must come back with it.
        var a = World.NewRun(5, populate: false);
        Assert.Equal(PortResult.Ok, a.Apply(new PortCommand(PortAction.Dock)));
        Assert.Equal(PortResult.Ok, a.Apply(new PortCommand(PortAction.CastOff)));
        for (int t = 0; t < 30 * 125; t++) a.Tick(new ShipInput(0, 0));   // a later day: a new visit (a same-day return is the same visit)
        a.Ship.Pos = a.Map.StartPort.Harbor;
        Assert.Equal(PortResult.Ok, a.Apply(new PortCommand(PortAction.Dock)));
        Assert.Contains(a.Docked!.PricesLastVisit!, p => p != null);
        var b = World.LoadJson(a.SaveJson());
        Assert.NotNull(b.Docked!.PricesLastVisit);
        Assert.Equal(a.Docked.PricesLastVisit, b.Docked.PricesLastVisit);
    }

    [Fact]
    public void TheCauseOfSinkingSurvivesASave()
    {
        // A rock shower lands on her, the save is written, and she founders within the minute: the logbook blames the eruption.
        var a = QuietIn(RegionType.TradeIsles);
        a.Eruptions.Add(new Eruption { Pos = a.Ship.Pos, Radius = 60, Warning = 0.01 });
        a.Tick(new ShipInput(0, 0));
        Assert.True(a.MonsterHitTime >= 0);
        var b = World.LoadJson(a.SaveJson());
        Assert.Equal(a.MonsterHitTime, b.MonsterHitTime);
        Assert.Equal(a.LastMonsterHit, b.LastMonsterHit);
        foreach (var w in new[] { a, b }) w.Ship.HullHp = 0;
        for (int t = 0; t < 30 * 40 && !(a.RunOver && b.RunOver); t++) { a.Tick(default); b.Tick(default); }
        Assert.True(a.RunOver && b.RunOver);
        Assert.Equal("SUNK_ERUPTION", a.CauseOfSinking);
        Assert.Equal(a.CauseOfSinking, b.CauseOfSinking);

        // Shot through by a ship that is gone by the time of the save: still "guns", not "the sea".
        var c = QuietIn(RegionType.TradeIsles);
        var raider = c.Spawn("sloop", c.Ship.Pos + new Vec2(200, 0), 0, Faction.Brethren, null);
        raider.Ai = new AiState { Role = Role.Raider, HomePort = 0 };
        c.Ship.Hit(30, raider, c.Rng);
        raider.Sunk = true;
        c.Tick(default);
        Assert.Empty(c.Others);
        var d = World.LoadJson(c.SaveJson());
        foreach (var w in new[] { c, d }) w.Ship.HullHp = 0;
        for (int t = 0; t < 30 * 40 && !(c.RunOver && d.RunOver); t++) { c.Tick(default); d.Tick(default); }
        Assert.Equal("SUNK_GUNS", c.CauseOfSinking);
        Assert.Equal(c.CauseOfSinking, d.CauseOfSinking);
    }

    [Fact]
    public void ASaveFromBeforeTheAuditStillLoads()
    {
        // Saves written by the build before these fixes lack the new fields; they must load and play on.
        var a = World.NewRun(7);
        var script = new Rng(3);
        for (int t = 0; t < 900; t++) a.Tick(Scripted(script, t));
        var node = System.Text.Json.Nodes.JsonNode.Parse(a.SaveJson())!.AsObject();
        foreach (var key in new[] { "LastPaint", "LastPos", "VisionRadius", "PlayerSpeedMult", "PlayerLastHitBy", "SirenPull",
                                    "LastMonsterHit", "MonsterHitTime", "PricesLastVisit", "Lanes" })
            Assert.True(node.Remove(key), $"{key} is not in the save any more");
        var b = World.LoadJson(node.ToJsonString());
        Assert.Equal(a.Hash(), b.Hash());
        Assert.Equal(-1, b.MonsterHitTime);
        for (int t = 0; t < 300; t++) b.Tick(Scripted(script, t));
        Assert.Equal(1200, b.Ticks);
    }

    [Fact]
    public void UpkeepFallsDueAtMidnight()
    {
        // GDD §6: wages and provisions are charged at each in-game midnight (hour 0), not at dawn.
        var w = World.NewRun(4, populate: false);
        w.Islands.Clear();
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        int gold = w.Player.Gold;
        double chargedAt = -1;
        for (int t = 0; t < 30 * 125 && chargedAt < 0; t++)
        {
            w.Tick(new ShipInput(0, 0));
            if (w.Player.Gold != gold) chargedAt = w.HourOfDay;
        }
        Assert.True(chargedAt >= 0, "no upkeep in the first day");
        Assert.InRange(chargedAt, 0, 0.05);
        Assert.Equal(1, w.Day);   // the day number still turns at dawn
    }

    [Fact]
    public void ACoveFoundThroughTheSpyglassCounts()
    {
        // Sighting a secret cove down the glass must count it (Smuggler's Welcome, the logbook) like any other sighting.
        var w = World.NewRun(5, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        w.Islands.Clear();
        w.Player.Officers.Add(new Officer { Type = OfficerType.Cartographer, Tier = 0 });   // he marks what the glass finds
        var cove = w.Map.Ports.First(p => p.Secret && !p.Discovered);
        var dir = Vec2.FromAngle(0.7);
        w.Ship.Pos = cove.Harbor - dir * 780;   // beyond plain sight, inside the glass
        w.Ship.Vel = Vec2.Zero;
        for (int t = 0; t < 20; t++) w.Tick(new ShipInput(0, 0, Spyglass: true, SpyglassDir: dir.Angle));
        Assert.True(cove.Discovered);
        Assert.Equal(w.Map.Ports.Count(p => p.Secret && p.Discovered), w.Stats.CovesFound);
        Assert.Contains("NOTICE_COVE", w.Notices);
    }

    [Fact]
    public void ACalmOrTempestVoyageReplaysToTheSameState()
    {
        // The replay has to rebuild the run on its own preset: the Threat drives the director, storms and beasts.
        var a = World.NewRun(11, "sloop", Preset.Tempest);
        a.Player.Loadout[0] = "flag_black";
        var script = new Rng(2024);
        for (int t = 0; t < 30 * 240; t++) a.Tick(Scripted(script, t));
        var r = World.Replay(a.Seed, a.HullId, a.Log, a.Commands, a.Ticks, a.Preset, a.Player.Loadout);
        Assert.Equal(a.Director.Credits, r.Director.Credits);
        Assert.Equal(a.Hash(), r.Hash());
    }

    // ---- Weather continuity: nothing the ship feels may jump between one tick's travel (0.4 m) and the next ----

    [Fact]
    public void WeatherHasNoSeamsAtRegionBorders()
    {
        foreach (int seed in new[] { 5, 6 })
        {
            var w = World.NewRun(seed, populate: false);
            double worstWind = 0, worstFog = 0, worstDold = 0;
            string at = "";
            for (int line = 0; line < 12; line++)
            {
                double y = -2000 + line * 330;
                var prev = w.WindAt(new Vec2(-2900, y));
                var pc = w.ConditionsAt(new Vec2(-2900, y));
                for (double x = -2899.6; x < 2900; x += 0.4)
                {
                    var p = new Vec2(x, y);
                    var wind = w.WindAt(p);
                    var c = w.ConditionsAt(p);
                    // Measured against the open-sea wind here, so a doldrums patch's own (steep but smooth) edge counts
                    // at its true size and a seam where one region's weather meets the next stands out.
                    double jw = Math.Abs(wind.Speed - prev.Speed) / w.Wind.Sample(p, w.Time).Speed;
                    if (jw > worstWind) { worstWind = jw; at = $"seed {seed} ({x:F1}, {y:F0}) {w.Map.RegionAt(p).Type}"; }
                    worstFog = Math.Max(worstFog, Math.Abs(c.Fog - pc.Fog));
                    worstDold = Math.Max(worstDold, Math.Abs(c.Doldrums - pc.Doldrums));
                    prev = wind;
                    pc = c;
                }
            }
            Assert.True(worstWind < 0.05, $"wind speed jumps {worstWind:P0} of the open-sea wind in 0.4 m at {at}");
            Assert.True(worstDold < 0.1, $"doldrums jump {worstDold:F2} in 0.4 m (seed {seed})");
            Assert.True(worstFog < 0.2, $"fog jumps {worstFog:F2} in 0.4 m (seed {seed})");
        }
    }

    [Fact]
    public void StormWindTurnsWithoutJumps()
    {
        var w = World.NewRun(5, populate: false);
        var cell = new StormCell { Pos = new Vec2(100, 100), Radius = 350, Strength = 0.7, Life = 100, Age = 50 };
        w.Weather.Storms.Add(cell);
        double worst = 0;
        string where = "";
        void Walk(Func<double, Vec2> path, double length, string name)
        {
            var prev = w.WindAt(path(0));
            for (double u = 0.4; u <= length; u += 0.4)
            {
                var wind = w.WindAt(path(u));
                double d = Angles.Deg(Math.Abs(Angles.Wrap(wind.Direction - prev.Direction)));
                if (d > worst) { worst = d; where = $"{name} at {u:F1} m"; }
                prev = wind;
            }
        }
        foreach (double frac in new[] { 0.15, 0.3, 0.6, 0.85 })
        {
            double r = frac * cell.Radius;
            Walk(u => cell.Pos + Vec2.FromAngle(u / r) * r, Angles.Tau * r, $"ring {frac:P0}");
        }
        foreach (double a in new[] { 0.0, 0.3, 1.1, 2.0, 2.9 })
            Walk(u => cell.Pos + Vec2.FromAngle(a) * (u - 400) + Vec2.FromAngle(a + Math.PI / 2) * 0.3, 800, $"line {a:F1}");
        Assert.True(worst < 15, $"the storm wind swings {worst:F0}° in 0.4 m ({where})");
    }

    [Fact]
    public void AStormGathersAndBlowsOutGradually()
    {
        var w = QuietIn(RegionType.TradeIsles);
        w.Islands.Clear();
        var cell = new StormCell { Pos = w.Ship.Pos + new Vec2(60, 0), Radius = 350, Strength = 0.8, Life = 40, Age = 0 };
        w.Weather.Storms.Add(cell);
        // The storm's share of the wind: the wind here over the same wind with the cell taken away. (The open-sea wind
        // itself wanders tick by tick; that is not the storm's doing.)
        double StormShare()
        {
            double with = w.WindAt(w.Ship.Pos).Speed;
            var cells = w.Weather.Storms.ToList();
            w.Weather.Storms.Clear();
            double without = w.WindAt(w.Ship.Pos).Speed;
            w.Weather.Storms.AddRange(cells);
            return with / without;
        }
        double prevSpeed = StormShare(), prevRain = w.ConditionsAt(w.Ship.Pos).Rain, worstWind = 0, worstRain = 0, peak = 0;
        for (int t = 0; t < 30 * 45; t++)
        {
            w.Ship.Pos = cell.Pos - new Vec2(60, 0);   // hold her in the storm
            w.Ship.Vel = Vec2.Zero;
            w.Tick(new ShipInput(0, 0));
            double speed = StormShare(), rain = w.ConditionsAt(w.Ship.Pos).Rain;
            worstWind = Math.Max(worstWind, Math.Abs(speed - prevSpeed));
            worstRain = Math.Max(worstRain, Math.Abs(rain - prevRain));
            peak = Math.Max(peak, rain);
            prevSpeed = speed;
            prevRain = rain;
        }
        Assert.Empty(w.Weather.Storms);
        Assert.True(peak > 0.6, $"the storm never blew (rain peaked at {peak:F2})");
        // The gusts inside a cell swing ±35% every 3.7 s (a few hundredths a tick); blowing out used to drop ~1.8× at once.
        Assert.True(worstWind < 0.12, $"the storm's share of the wind jumped {worstWind:F2}× in one tick");
        Assert.True(worstRain < 0.05, $"the rain jumped {worstRain:F2} in one tick");
    }

    [Fact]
    public void DawnFogComesAndGoesGradually()
    {
        var w = World.NewRun(6, populate: false);
        var def = WeatherDef.Of(RegionType.Mangrove);
        var centre = w.Map.RegionOf(RegionType.Mangrove).Seed;
        double worst = 0, when = 0;
        for (int k = 0; k < 100; k++)
        {
            var p = centre + new Vec2((k % 10) * 60 - 300, (k / 10) * 60 - 300);
            for (double t = 0; t < 120; t += Tuning.Dt)
            {
                double h0 = (Tuning.DawnHour + t / Tuning.SecondsPerHour) % 24, h1 = (Tuning.DawnHour + (t + Tuning.Dt) / Tuning.SecondsPerHour) % 24;
                double step = Math.Abs(w.Weather.Fog(p, t + Tuning.Dt, def, h1) - w.Weather.Fog(p, t, def, h0));
                if (step > worst) { worst = step; when = h1; }
            }
        }
        Assert.True(worst < 0.1, $"the mangrove dawn fog jumps {worst:F2} in one tick at hour {when:F2}");
    }

    // ---- Grounding ----

    /// <summary>A long straight coast along y = 0 with the land to the south (+Y), and the ship 40 m off it.</summary>
    static World OffAStraightCoast(double headingDeg, double windFromCompass, string hull = "sloop")
    {
        var w = Sea.Fixed(fromCompass: windFromCompass, hull: hull);
        w.Islands.Add(new Island(new[] { new Vec2(-3000, 0), new Vec2(3000, 0), new Vec2(3000, 900), new Vec2(-3000, 900) }));
        w.Ship.Pos = new Vec2(0, -40);
        w.Ship.Heading = Angles.Rad(headingDeg);
        Sea.SetSail(w, 3);
        w.Ship.Vel = w.Ship.Forward * 6;
        return w;
    }

    [Fact]
    public void AShipPressedAgainstACoastCanTurnAwayAndSailOff()
    {
        // She runs onto a lee shore at 45° with the wind behind her and puts the helm over to claw off.
        // While she was touching land her turn was halved every tick, which held her at ~8% of her turn
        // rate: she ground along the coast for minutes (AI ships were seen stuck for up to eight).
        var w = OffAStraightCoast(headingDeg: 45, windFromCompass: 0);
        int t = 0;
        while (!w.Ship.Aground && t++ < 30 * 20) w.Tick(new ShipInput(0, 0));
        Assert.True(w.Ship.Aground, "she should reach the coast");
        double struckAt = w.Time, clearAt = -1;
        for (int i = 0; i < 30 * 60; i++)
        {
            w.Tick(new ShipInput(-1, 0));   // hard to port: away from the land
            if (!w.Ship.Aground && w.Ship.Pos.Y < -Hulls.Sloop.Length && w.Ship.Vel.Y < 0) { clearAt = w.Time; break; }
        }
        Assert.True(clearAt > 0, $"still aground after a minute (heading {Angles.CompassDeg(w.Ship.Heading):F0}°)");
        Assert.True(clearAt - struckAt < 15, $"took {clearAt - struckAt:F1} s to claw off");
    }

    [Fact]
    public void AGlancingBlowScrapesAlongTheCoastInsteadOfStickingToIt()
    {
        // Heading 20° into a straight coast on a beam reach: she should scrape along it. Friction took 40% of her
        // sliding speed every tick she touched, so she stuck at ~0.05 m/s: AI ships were seen glued to coasts for
        // minutes (6–18% of near-ship ticks aground over 20-minute voyages; one merchant 472 s at a stretch).
        var w = OffAStraightCoast(headingDeg: 20, windFromCompass: 0);
        bool touched = false;
        double slide = 0;
        for (int i = 0; i < 30 * 30; i++)
        {
            w.Tick(new ShipInput(0, 0));
            touched |= w.Ship.Aground;
            if (touched && i >= 30 * 25) slide = Math.Max(slide, w.Ship.Vel.X);
        }
        Assert.True(touched, "she should touch the coast");
        Assert.False(w.Islands[0].Contains(w.Ship.Pos));
        // Crabbing 20° into the coast her keel resists the sideways slip, so ~1.7 m/s is the honest speed; glued was 0.1.
        Assert.True(slide > 1, $"stuck to the coast at {slide:F2} m/s");
    }

    [Fact]
    public void RunningStraightOntoACoastStillPinsHer()
    {
        // The other half of the contract: head-on under full sail she stops against the shore and stays (the
        // self-test's grounding check), even with the leeway of a beam wind pushing her along it.
        var w = OffAStraightCoast(headingDeg: 90, windFromCompass: 270);
        for (int i = 0; i < 30 * 30; i++) w.Tick(new ShipInput(0, 0));
        Assert.True(w.Ship.Aground);
        Assert.True(w.Ship.Speed < 1, $"sliding at {w.Ship.Speed:F2} m/s");
    }

    // ---- Lanes ----

    [Fact]
    public void LanesNeverCutAcrossLand()
    {
        // String-pulling tested a leg every 8–16 m against 25 m grid cells, so a straightened leg could clip a coast
        // by several metres (74 legs over land on 37 of 60 maps, up to 5.7 m in); merchants then steered into it.
        // Straightened legs must now keep off the land entirely. A single step between two neighbouring sea cells
        // (the A* grid itself) may still graze a spit of coast between their centres, by a metre or so.
        int legs = 0;
        var bad = new List<string>();
        for (int seed = 30; seed < 36; seed++)
        {
            var map = MapGen.Generate(seed);
            for (int i = 0; i < map.Ports.Count; i += 2)
                for (int j = 1; j < map.Ports.Count; j += 3)
                {
                    if (i == j || map.Ports[i].Harbor.DistanceTo(map.Ports[j].Harbor) > 2200) continue;
                    var path = Pathing.Find(map.Nav, map.Ports[i].Harbor, map.Ports[j].Harbor, (i * 0.7 + j) % Angles.Tau, 50);
                    for (int k = 0; k + 1 < path.Count; k++)
                    {
                        legs++;
                        double len = path[k].DistanceTo(path[k + 1]), deepest = 0;
                        for (double u = 0; u <= len; u += 0.5)
                        {
                            var pt = Vec2.Lerp(path[k], path[k + 1], u / Math.Max(len, 1e-9));
                            if (!map.OnLand(pt)) continue;
                            foreach (var island in map.IslandsNear(pt, 1))
                                if (island.Closest(pt) is { Inside: true } c) deepest = Math.Max(deepest, c.Dist);
                        }
                        bool gridStep = len <= NavGrid.Cell * 1.5;
                        if (deepest > (gridStep ? 2 : 0)) bad.Add($"seed {seed} lane {i}->{j} leg {k} ({len:F0} m) {deepest:F1} m into land");
                    }
                }
        }
        Assert.True(bad.Count == 0, $"{bad.Count} of {legs} legs cross land, e.g. {string.Join("; ", bad.Take(3))}");
    }

    [Fact]
    public void TheSpyglassIsReplayedWithTheHelm()
    {
        // The glass inks the chart and spots ports (which changes what a tavern rumour can point at), so a replay
        // must see it: it used to be set on the world beside the input, and replays sailed blind.
        // A home harbour within the glass's reach of uncharted water: the Trade Isles start charted, so on the 18 km
        // chart many homes lie too deep inside them. Take the first seed whose home is near their edge.
        World a = null!;
        double dir = double.NaN;
        for (int seed = 4; seed < 60 && double.IsNaN(dir); seed++)
        {
            a = World.NewRun(seed);
            // Only a cartographer keeps the chart (Nolan, 2026-09-27): sign one on at home first, through the logged
            // commands (signing on inks the water round the harbour, so look only after).
            Assert.Equal(PortResult.Ok, a.Apply(new PortCommand(PortAction.Dock)));
            var keeper = a.TavernOfficers(a.Docked!).First(o => o.Type == OfficerType.Cartographer);
            Assert.Equal(PortResult.Ok, a.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)OfficerType.Cartographer * 10 + keeper.Tier)));
            Assert.Equal(PortResult.Ok, a.Apply(new PortCommand(PortAction.CastOff)));
            // Look out of the inked home waters, toward the nearest uncharted water the glass can reach.
            double nearest = double.MaxValue;
            for (int k = 0; k < 72; k++)
                for (double f = a.VisionRadius; f < a.SpyglassRange - 50; f += RevealMask.Cell)
                {
                    var p = a.Ship.Pos + Vec2.FromAngle(k * Angles.Tau / 72) * f;
                    if (!Map.InBounds(p)) break;   // past the chart's edge nothing can be inked
                    if (a.Reveal.IsRevealed(p)) continue;
                    if (f < nearest) { nearest = f; dir = Math.Round(k * Angles.Tau / 72, 2); }
                    break;
                }
        }
        Assert.False(double.IsNaN(dir), "uncharted water within the glass's reach of home");
        int before = a.Reveal.RevealedCells;
        for (int t = 0; t < 600; t++)
            a.Tick(new ShipInput(0, 0, Spyglass: t < 400, SpyglassDir: t < 400 ? dir : 0));
        Assert.True(a.Reveal.RevealedCells > before, "the glass inked nothing new");
        var r = World.Replay(a.Seed, a.HullId, a.Log, a.Commands, a.Ticks);
        Assert.Equal(a.Reveal.RevealedCells, r.Reveal.RevealedCells);
        Assert.Equal(a.Map.Ports.Where(p => p.Discovered).Select(p => p.Id), r.Map.Ports.Where(p => p.Discovered).Select(p => p.Id));
        Assert.Equal(a.Hash(), r.Hash());
    }

    // ---- Folded in from the sim-trade audit (lead, 2026-09-23) ----

    [Fact]
    public void TheLeanStartNeverCountsAHiddenCove()
    {
        // S-13 (sim-trade): the start-route guarantee must name a charted market. On these seeds the only port within
        // 600 m of the start was an uncharted secret cove (seed 7: cove 570 m, nearest charted market 847 m).
        foreach (int seed in new[] { 7, 25, 63, 69, 125 })
        {
            var map = MapGen.Generate(seed);
            var start = map.StartPort;
            var d = map.Nav.Distances(start.Harbor);
            bool route = start.Produces.Any(g => map.Ports.Any(p => p != start && !p.Secret && !p.Produces.Contains(g)
                && map.Nav.DistanceAt(d, p.Harbor) is >= 0 and <= MapGen.StartRouteRange));
            Assert.True(route, $"seed {seed}: no charted market within {MapGen.StartRouteRange} m of the start");
        }
    }

    [Fact]
    public void ALanternLightsTheNightNotTheNoon()
    {
        // R-03 (sim-trade): the lantern part is "+10% night radius and spyglass reach a grade" (GDD §19 M8); it was
        // folded into the lookout's multiplier, so a grade-5 lantern also widened noon sight from 500 m to 750 m.
        var w = QuietIn(RegionType.TradeIsles);
        w.Islands.Clear();
        for (int t = 0; t < 30 * 25; t++) w.Tick(new ShipInput(0, 0));   // mid-morning, clear
        var here = w.Ship.Pos;
        Assert.Equal(0, w.ConditionsAt(here).Fog);
        double noon = w.VisionAt(here);
        w.Ship.Parts[(int)Part.Lantern] = 5;
        w.ApplyOfficers();
        Assert.Equal(noon, w.VisionAt(here), 6);
        Assert.Equal(900 * 1.5, w.SpyglassRange, 6);
        for (int t = 0; t < 30 * 70; t++) w.Tick(new ShipInput(0, 0));   // night
        Assert.True(w.IsNight);
        Assert.True(w.Lantern);
        Assert.Equal(250 * 1.5, w.VisionAt(here), 6);
        w.Tick(new ShipInput(0, 0, ToggleLantern: true));
        Assert.Equal(150, w.VisionAt(here), 6);   // a doused lantern lights nothing, however good
    }

    [Fact]
    public void AVoyageFromAnOlderChartCannotResume()
    {
        // Generator v6 (the 18 × 13.5 km landform chart) makes no older map: a suspend save that carries an earlier
        // MapVersion, or none (a save from before versions were recorded), is refused rather than resumed on a
        // different sea. The title then sets it aside (Main.ResumeVoyage).
        var w = World.NewRun(7);
        var node = System.Text.Json.Nodes.JsonNode.Parse(w.SaveJson())!.AsObject();
        node["MapVersion"] = 5;
        Assert.Throws<NotSupportedException>(() => World.LoadJson(node.ToJsonString()));
        Assert.True(node.Remove("MapVersion"));
        Assert.Throws<NotSupportedException>(() => World.LoadJson(node.ToJsonString()));
        // And a new save keeps the version it was made with.
        Assert.Equal(MapGen.Version, World.LoadJson(World.NewRun(7).SaveJson()).Map.Version);
    }

    [Fact]
    public void HowFarSheCanBeSeenChangesWithTheLightNotAtAStroke()
    {
        // Her own sight blends through dusk and dawn; how far hunters spot her jumped 500 → 650 m (lantern lit)
        // or 500 → 200 m (doused) in the tick the clock struck 22:00, and back at 06:00.
        foreach (bool lit in new[] { true, false })
        {
            var w = QuietIn(RegionType.TradeIsles);
            w.Islands.Clear();
            if (!lit) w.Tick(new ShipInput(0, 0, ToggleLantern: true));
            Assert.Equal(lit, w.Lantern);
            double prev = w.DetectRangeOfPlayer(), worst = 0, at = 0;
            for (int t = 0; t < 30 * 125; t++)
            {
                w.Tick(new ShipInput(0, 0));
                double d = w.DetectRangeOfPlayer();
                if (Math.Abs(d - prev) > worst) { worst = Math.Abs(d - prev); at = w.HourOfDay; }
                prev = d;
            }
            Assert.True(worst < 5, $"lantern {(lit ? "lit" : "doused")}: spotting range jumps {worst:F0} m in one tick at hour {at:F2}");
        }
    }

    [Fact]
    public void APinnedWindCannotGoNegative()
    {
        // `--wind=FROM,SPEED` pins the wind; a negative speed made the target speed NaN, the rudder NaN and
        // Math.Sign(NaN) threw on the next tick. A pinned wind is at least a flat calm.
        var w = Sea.Fixed(fromCompass: 90, speed: -5);
        Sea.SetSail(w, 3);
        Sea.Run(w, 2, rudder: 0.5);
        Assert.False(double.IsNaN(w.Ship.Pos.X) || double.IsNaN(w.Ship.Heading));
        Assert.Equal(0, w.Wind.BaseSpeed);
    }

    [Fact]
    public void TheSuspendSaveDoesNotGrowWithTheHelm()
    {
        // The suspend save is written on every dock (and synced to Steam Cloud). It used to carry the whole input
        // and command logs — every helm tap: 2.1 MB by day 12 of a real voyage. Resuming needs only the state.
        var w = World.NewRun(8);
        w.DirectorEnabled = false;   // a long, safe voyage: this is about the helm, not the hunters
        w.MonstersEnabled = false;
        var script = new Rng(5);
        int Size() => System.Text.Encoding.UTF8.GetByteCount(w.SaveJson());
        for (int t = 0; t < 30 * 60; t++) w.Tick(new ShipInput(t % 20 < 10 ? 1 : -1, 0));
        int early = Size();
        for (int t = 0; t < 30 * 600; t++)
        {
            w.Tick(new ShipInput(t % 20 < 10 ? Math.Round(script.Range(-1, 1), 2) : 0, 0));
            w.Ship.HullHp = w.Ship.MaxHp;   // keep her afloat whatever the raiders do
            w.Ship.Water = 0;
            w.Ship.Leaks = 0;
            w.Ship.Foundering = false;
        }
        Assert.False(w.RunOver);
        int late = Size();
        Assert.True(w.Log.Count > 10_000, $"only {w.Log.Count} inputs logged");
        Assert.True(late - early < 60_000, $"the save grew {early / 1024} → {late / 1024} KB over {w.Log.Count} inputs");
        Assert.True(late < 400_000, $"a {late / 1024} KB suspend save");
        // An old save's logs are read past, not carried on.
        var node = System.Text.Json.Nodes.JsonNode.Parse(w.SaveJson())!.AsObject();
        node["Log"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"Tick\":0,\"Input\":{\"Rudder\":0.5,\"SailDelta\":1}}]");
        var old = World.LoadJson(node.ToJsonString());
        Assert.Equal(w.Hash(), old.Hash());
        Assert.Empty(old.Log);
    }

    [Fact]
    public void ACorruptSuspendSaveIsRefusedAtTheLoad()
    {
        // A save that parses but points past the map's ports, maps, goods or tables used to load, and the resumed
        // voyage then crashed on its first tick — after the title had already deleted the save. It must be refused
        // by the load itself, where the caller keeps the file and the title (a truncated file already was).
        var w = World.NewRun(5);
        w.Apply(new PortCommand(PortAction.Dock));
        string good = w.SaveJson();
        System.Text.Json.Nodes.JsonObject Doc() => System.Text.Json.Nodes.JsonNode.Parse(good)!.AsObject();
        var corrupt = new List<(string What, string Json)>();
        void Add(string what, Action<System.Text.Json.Nodes.JsonObject> spoil) { var d = Doc(); spoil(d); corrupt.Add((what, d.ToJsonString())); }
        Add("docked at port 999", d => d["Docked"] = 999);
        Add("discovered port 999", d => d["DiscoveredPorts"]!.AsArray().Add(999));
        Add("a merchant from port 999", d => d["Ships"]![0]!["HomePort"] = 999);
        Add("a merchant with no home", d => d["Ships"]!.AsArray().First(x => (int)x!["Role"]! == (int)Role.Merchant)!["HomePort"] = -1);
        Add("cargo good 99", d => d["Ships"]![0]!["CargoGood"] = 99);
        Add("a map of treasure 999", d => d["BottleMaps"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"Treasure\":999}]"));
        Add("preset 7", d => d["Preset"] = 7);
        Add("a barrel of good 99", d => d["Flotsam"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"Good\":99,\"Units\":3,\"Life\":30}]"));
        Add("beast 42", d => { d["MonsterType"] = 42; d["MonsterData"] = System.Text.Json.Nodes.JsonNode.Parse("[0,0,0,0,0,10,1,0,0,0,0,0,0,0,1]"); });
        Add("an officer of tier 9", d => d["Officers"] = System.Text.Json.Nodes.JsonNode.Parse("[[0,9]]"));
        Add("a rumour of port 999", d => d["CoveHints"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"Port\":999,\"Radius\":400}]"));
        Add("a price from port 999", d => d["Ledger"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("{\"Port\":999,\"Good\":0,\"Price\":9}")));
        Add("hull 'raft'", d => d["Hull"] = "raft");
        Add("two words of dice", d => d["Rng"] = System.Text.Json.Nodes.JsonNode.Parse("[1,2]"));
        corrupt.Add(("truncated", good[..(good.Length / 2)]));
        foreach (var (what, json) in corrupt)
        {
            var e = Record.Exception(() => World.LoadJson(json));
            Assert.True(e is InvalidDataException or System.Text.Json.JsonException or ArgumentException, $"{what}: {(e == null ? "loaded" : e.GetType().Name)}");
        }
        Assert.Equal(w.Hash(), World.LoadJson(good).Hash());   // and the sound one still loads
    }

    [Fact]
    public void TheCrewPanelIsReplayed()
    {
        // R-13 (sim-combat): the crew panel wrote the order and the stations straight into the ship, logged nowhere, so a
        // replay of a voyage that used it went on with the old hands (reloads, sail handling and repairs all differ).
        var a = World.NewRun(9);
        var script = new Rng(8);
        for (int t = 0; t < 900; t++)
        {
            if (t == 100) Assert.Equal(PortResult.Ok, a.Apply(World.CrewStationsCommand(0, 1, 2)));
            if (t == 500) Assert.Equal(PortResult.Ok, a.Apply(new PortCommand(PortAction.CrewOrder, Amount: (int)CrewOrder.Battle)));
            a.Tick(Scripted(script, t));
        }
        Assert.Equal(CrewOrder.Battle, a.Ship.Order);
        Assert.Equal(PortResult.Nothing, a.Apply(new PortCommand(PortAction.CrewOrder, Amount: 7)));
        var r = World.Replay(a.Seed, a.HullId, a.Log, a.Commands, a.Ticks);
        Assert.Equal(a.Ship.Order, r.Ship.Order);
        Assert.Equal(a.Ship.CustomStations, r.Ship.CustomStations);
        Assert.Equal(a.Hash(), r.Hash());
    }

    [Fact]
    public void NoSeaCellOfTheGridHoldsLand()
    {
        // R-12 (sim-combat): a 25 m cell counted as land only when its centre lay within 10 m of a coast, so the corner
        // of a "sea" cell could hold real land — ~630 such cells a map, up to 11 m into the land — and lanes and the
        // AI's look-aheads read open water over it. Every cell a (grown) coastline crosses is land now.
        foreach (int seed in new[] { 3, 4, 5 })
        {
            var map = MapGen.Generate(seed);
            int wet = 0;
            double deepest = 0;
            foreach (var island in map.Islands)
            {
                double r = island.BoundRadius;
                for (double x = island.Centre.X - r; x <= island.Centre.X + r; x += 2)
                    for (double y = island.Centre.Y - r; y <= island.Centre.Y + r; y += 2)
                    {
                        var p = new Vec2(x, y);
                        if (!map.Nav.IsSea(p) || !island.Contains(p)) continue;
                        wet++;
                        deepest = Math.Max(deepest, island.Closest(p).Dist);
                    }
            }
            Assert.True(wet == 0, $"seed {seed}: {wet} points of land read as sea, up to {deepest:F1} m in");
        }
    }

    [Fact]
    public void ATickThatDoesNothingLeavesNoEvents()
    {
        // art-ships R1: once the run is over (or she is docked) Tick returned before clearing the last tick's events,
        // so every later frame re-read the sinking (or the rescue) and fired its effects again.
        var w = QuietIn(RegionType.TradeIsles);
        w.Ship.HullHp = 0;
        for (int t = 0; t < 30 * 60 && !w.RunOver; t++) w.Tick(new ShipInput(0, 0));
        Assert.True(w.RunOver);
        Assert.Contains(w.Events, e => e.Type == CombatEventType.Sink);   // the tick she went down reports it
        w.Tick(new ShipInput(0, 0));
        Assert.Empty(w.Events);

        var d = QuietIn(RegionType.TradeIsles);
        d.Ship.Pos = d.Map.StartPort.Harbor;
        d.Ship.Foundering = true;
        d.Ship.Hourglass = 10;
        d.Ship.HullHp = 0;
        d.Tick(new ShipInput(0, 0));   // hauled into the harbour: the rescue is this tick's event
        Assert.True(d.IsDocked);
        Assert.Contains(d.Events, e => e.Type == CombatEventType.Rescued);
        d.Tick(new ShipInput(0, 0));
        Assert.Empty(d.Events);
    }

    [Fact]
    public void EveryHarbourOutsideTheMazeTakesABigHull()
    {
        // Generator v5: an island near a border could put its harbour across it; ~30% of maps had a
        // port outside the Mangrove Maze whose harbour lay inside it (1.2% the start port), which no big hull can enter.
        for (int seed = 1; seed <= 40; seed++)
        {
            var map = MapGen.Generate(seed);
            Assert.NotEqual(RegionType.Mangrove, map.RegionAt(map.StartPort.Harbor).Type);
            var big = map.Nav.Distances(map.StartPort.Harbor, (x, y) => map.RegionAt(NavGrid.Centre(x, y)).Type == RegionType.Mangrove);
            foreach (var port in map.Ports.Where(p => p.Region != RegionType.Mangrove))
            {
                Assert.Equal(port.Region, map.RegionAt(port.Harbor).Type);
                Assert.True(map.Nav.DistanceAt(big, port.Harbor) >= 0, $"seed {seed}: {port.Name} ({port.Region}) is cut off from big hulls");
            }
        }
    }
}
