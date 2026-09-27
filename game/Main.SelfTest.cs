using Godot;
using LastTide.Sim;

namespace LastTide;

public partial class Main
{
    /// <summary>A sea point with no island within <paramref name="clear"/> metres, nearest to the ship.</summary>
    Vec2 OpenWater(double clear)
    {
        Vec2 best = world.Ship.Pos;
        double bestD = double.MaxValue;
        for (double x = -Map.HalfW + 300; x < Map.HalfW - 300; x += 150)
            for (double y = -Map.HalfH + 300; y < Map.HalfH - 300; y += 150)
            {
                var p = new Vec2(x, y);
                if (world.Map.IslandsNear(p, clear).Any()) continue;
                double d = p.DistanceTo(world.Ship.Pos);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
        return best;
    }

    /// <summary>
    /// Drives the real input path (viewport → _UnhandledInput) with synthetic events and checks the
    /// world, camera and HUD afterwards. Run with `godot --path . -- --selftest [--screenshot=path]`.
    /// </summary>
    async void RunSelfTest()
    {
        var failures = new List<string>();
        void Check(bool ok, string what)
        {
            if (!ok) failures.Add(what);
            GD.Print((ok ? "ok   " : "FAIL ") + what);
        }
        void Push(InputEvent e) { e.Device = RealInputFilter.Synthetic; GetViewport().PushInput(e); }
        void Key(Key k, bool pressed) => Push(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = pressed });
        void Tap(Key k)
        {
            Key(k, true);
            Key(k, false);
        }
        void ClickAt(Vector2 p)
        {
            Push(new InputEventMouseMotion { Position = p, GlobalPosition = p });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p, GlobalPosition = p });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p, GlobalPosition = p });
        }
        void Click(Control c) => ClickAt(c.GetGlobalRect().GetCenter());
        // The built-in ui_* actions (Enter, the arrows, Tab: GUI focus and keyboard navigation) are bound to device 0,
        // so they never matched the test's own device id and keyboard navigation could not be tested. Let them match
        // any device for the test (real play is untouched).
        foreach (var action in InputMap.GetActions())
            if (action.ToString().StartsWith("ui_"))
                foreach (var ev in InputMap.ActionGetEvents(action)) ev.Device = -1;
        async Task Frames(int n)
        {
            for (int i = 0; i < n; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            // The signal fires before _Process runs, so two waits guarantee one full render frame:
            // the HUD and camera then reflect the ticks just run.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // Waits for a state, not a frame count: on a loaded machine a frame can be a quarter second of real time,
        // and the hint, camera and audio clocks run on real time (GC-30).
        async Task Until(Func<bool> done, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !done(); i++) await Frames(1);
        }

        // Pin the wind so the checks are exact: from the east at standard strength, and start in open
        // water heading north so the wind is abeam.
        world.Wind.SetFixed(Angles.Wrap(Angles.FromCompassDeg(90) + Math.PI), Tuning.StandardWind);
        var open = OpenWater(600);
        world.Ship.Pos = open;
        world.Ship.Heading = Angles.FromCompassDeg(0);
        world.Ship.Vel = Vec2.Zero;
        prevPose = curPose = (open, world.Ship.Heading);
        camPos = Ink.V(open);
        await Frames(2);
        Check(chart.BuiltChunks > 0, $"the chart inks the chunks in view first ({chart.BuiltChunks} of 30 so far)");

        // Strings: every HUD line, sail level, point of sail, watch, compass point and hull has text.
        bool strings = true;
        foreach (var k in new[] { "HUD_WIND", "HUD_HEADING", "HUD_SPEED", "HUD_DAY", "HUD_HINTS", "HUD_PAUSED", "HUD_AGROUND", "GAME_TITLE" })
            strings &= Text.Has(k);
        for (int i = 0; i < 4; i++) strings &= Text.Has("SAIL_" + i);
        foreach (var p in Enum.GetValues<PointOfSail>()) strings &= Text.Has("POS_" + p.ToString().ToUpperInvariant());
        for (int i = 0; i < 6; i++) strings &= Text.Has("WATCH_" + i);
        for (int i = 0; i < 16; i++) strings &= Text.Has("PT_" + i);
        foreach (var h in Hulls.All) strings &= Text.Has("HULL_" + h.Id);
        Check(strings, "every HUD line, sail level, point of sail, watch, compass point and hull has a string");

        // W raises the sail a step per press, clamped at full; S lowers it.
        long t0 = world.Ticks;
        Tap(Godot.Key.W);
        await Frames(2);
        Check(world.Ticks > t0, "the sim ticks while running");
        Check(world.Ship.SailTarget == 1, "W sets one-third sail");
        Tap(Godot.Key.W);
        Tap(Godot.Key.W);
        Tap(Godot.Key.W);
        await Frames(2);
        Check(world.Ship.SailTarget == 3, "three more W presses reach full sail and clamp there");
        Check(hud.SailText == Text.Get("SAIL_3"), "the HUD names the sail level");
        Tap(Godot.Key.S);
        await Frames(2);
        Check(world.Ship.SailTarget == 2, "S takes in a step");
        Check(world.Ship.SailFraction < 0.75, "canvas comes in over time, not instantly");
        Tap(Godot.Key.W);
        await Frames(30 * 8);
        Check(Math.Abs(world.Ship.SailFraction - 1.0) < 1e-9, "full sail is set after a few seconds");
        Check(world.Ship.ForwardSpeed > 4, $"she gathers way on a beam reach ({world.Ship.ForwardSpeed:F1} m/s)");
        Check(hud.PointText == Text.Get("POS_BEAMREACH"), "the HUD reads 'beam reach' with the wind abeam");

        // D held turns to starboard (clockwise on screen); releasing lets the rudder centre.
        double h0 = world.Ship.Heading;
        Key(Godot.Key.D, true);
        await Frames(30);
        Key(Godot.Key.D, false);
        Check(Angles.Wrap(world.Ship.Heading - h0) > Angles.Rad(8), "holding D turns her to starboard");
        Check(world.Ship.Rudder > 0.9, "the rudder is hard over while D is held");
        await Frames(45);
        Check(Math.Abs(world.Ship.Rudder) < 1e-9, "the rudder centres itself after release");

        // A held turns to port.
        h0 = world.Ship.Heading;
        Key(Godot.Key.A, true);
        await Frames(30);
        Key(Godot.Key.A, false);
        Check(Angles.Wrap(world.Ship.Heading - h0) < -Angles.Rad(8), "holding A turns her to port");
        await Frames(30);

        // Wheel zooms in and out within limits.
        float z0 = zoomTarget;
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new Vector2(400, 300) });
        Check(Mathf.IsEqualApprox(zoomTarget, Mathf.Clamp(z0 * 1.2f, MinZoom, MaxZoom)), "wheel up zooms in");
        await Frames(20);
        Check(Mathf.Abs(camera.Zoom.X - zoomTarget) < 0.05f, "the camera eases to the new zoom");
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = new Vector2(400, 300) });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = new Vector2(400, 300) });
        Check(Mathf.IsEqualApprox(zoomTarget, Mathf.Clamp(z0 / 1.2f, MinZoom, MaxZoom)), "wheel down zooms out");
        for (int i = 0; i < 20; i++)
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = new Vector2(400, 300) });
        Check(Mathf.IsEqualApprox(zoomTarget, MinZoom), "zoom stops at the wide limit");
        for (int i = 0; i < 40; i++)
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new Vector2(400, 300) });
        Check(Mathf.IsEqualApprox(zoomTarget, MaxZoom), "zoom stops at the close limit");
        zoomTarget = 1.25f;

        // The camera follows the ship.
        await Frames(30);
        var shipPx = Ink.V(world.Ship.Pos);
        Check(camera.Position.DistanceTo(shipPx) < Tuning.CameraLeadMax * Ink.PxPerM + 40, "the camera stays with the ship, leading her heading");

        // Esc pauses: ticks stop, the notice shows; Esc again resumes.
        Tap(Godot.Key.Escape);
        long tp = world.Ticks;
        await Frames(10);
        Check(paused && world.Ticks == tp, "Esc pauses the sim");
        Tap(Godot.Key.W);
        Check(sailQueue == 0, "sail orders are ignored while paused");
        Tap(Godot.Key.Escape);
        await Frames(5);
        Check(!paused && world.Ticks > tp, "Esc again resumes");

        // M opens the chart and stops the clock; a click drops a pin; right-click removes it; M closes.
        Tap(Godot.Key.M);
        await Frames(2);
        long tc = world.Ticks;
        await Frames(5);
        Check(chartScreen.IsOpen && world.Ticks == tc, "M opens the chart and the sim waits");
        var chartCanvas = chartScreen.Canvas;
        var pinScreen = chartCanvas.ToScreen(world.Ship.Pos + new Vec2(300, 200));
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = pinScreen, GlobalPosition = pinScreen });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = pinScreen, GlobalPosition = pinScreen });
        await Frames(1);
        Check(chartScreen.NoteHasFocus, "clicking the chart opens a note field for the pin");
        chartScreen.SubmitNote("Reef here");
        Check(world.Pins.Count == 1 && world.Pins[0].Note == "Reef here" && world.Pins[0].Pos.DistanceTo(world.Ship.Pos + new Vec2(300, 200)) < 30, "Enter keeps the pin with its note");
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = pinScreen, GlobalPosition = pinScreen });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = pinScreen, GlobalPosition = pinScreen });
        Check(world.Pins.Count == 0, "right-click removes the pin");
        Check(world.Reveal.IsRevealed(world.Map.StartPort.Harbor) && world.Map.StartPort.Discovered, "the home port is on the chart from the start");
        var homeOnChart = chartCanvas.ToScreen(world.Map.StartPort.Pos);
        Push(new InputEventMouseMotion { Position = homeOnChart, GlobalPosition = homeOnChart });
        Check(chartCanvas.Hover == world.Map.StartPort, "hovering a port on the chart brings up its ledger card");
        Tap(Godot.Key.Equal);
        Check(chartCanvas.Zoom > 1.2f, $"+ zooms the chart in ({chartCanvas.Zoom:0.00})");
        var probe = chartCanvas.ToScreen(world.Ship.Pos);
        Tap(Godot.Key.Left);
        Check(chartCanvas.ToScreen(world.Ship.Pos).X > probe.X + 20, "the arrow keys pan the chart");
        Tap(Godot.Key.Minus);
        Check(Mathf.IsEqualApprox(chartCanvas.Zoom, 1f), "− zooms back out");
        Tap(Godot.Key.M);
        await Frames(3);
        Check(!chartScreen.IsOpen && world.Ticks > tc, "M closes the chart and the clock runs again");

        // Running aground: aim at the nearest island and hold on; she stops outside it and the HUD says so.
        var island = world.Islands.OrderBy(i => i.Centre.DistanceTo(world.Ship.Pos)).First(i => i.Radius >= 60);
        var toIsland = (island.Centre - world.Ship.Pos).Normalized;
        world.Ship.Pos = island.Centre - toIsland * (island.BoundRadius + 60);
        world.Ship.Heading = toIsland.Angle;
        world.Ship.Vel = toIsland * 8;
        world.Wind.SetFixed(Angles.Wrap(toIsland.Angle), Tuning.StandardWind);   // wind astern, so she keeps driving in
        bool struck = false;
        for (int i = 0; i < 30 * 25 && !(struck && world.Ship.Speed < 2); i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            struck |= world.Ship.Aground;
        }
        await Frames(15);
        Check(struck, "she strikes the island");
        Check(!island.Contains(world.Ship.Pos), "the hull is held outside the coastline");
        Check(world.Ship.Speed < 2, $"she is stopped against the shore ({world.Ship.Speed:F1} m/s)");

        Check(world.Ship.Order == CrewOrder.Balanced, "the crew starts on the balanced order");
        Check(hud.ThreatText == Text.Get("TIER_flat_calm"), $"the Threat bar names the first tier ({hud.ThreatText})");

        // Broadsides: Q/E held show the arcs, released fire; a target abeam takes a hit; 1–4 set crew orders.
        world.Ship.Cannons = 4;
        world.Ship.Crew = 8;
        world.Player.Cargo[(int)Good.Munitions] = 40;
        world.Ship.Pos = OpenWater(600);
        world.Ship.Heading = Angles.FromCompassDeg(0);
        world.Ship.Vel = Vec2.Zero;
        // Furled, so she lies still beside the dummy: under load several ticks run per frame and, with canvas set, she
        // gathered way before the shot and the broadside passed ahead of a target lying still (flaky "takes the broadside").
        world.Ship.SailTarget = 0;
        world.Ship.SailFraction = 0;
        var dummy = world.Spawn("sloop", world.Ship.Pos + world.Ship.Right * 100, world.Ship.Heading, Faction.Brethren, null, crew: 4, cannons: 0);
        await Frames(2);
        Key(Godot.Key.E, true);
        await Frames(1);
        Check(shipView.AimStarboard, "holding E shows the starboard arc");
        int ammo = world.Player.Units(Good.Munitions);
        double hpDummy = dummy.HullHp;   // before the shot: on a loaded machine the ball can land during the next waits (GC-30)
        Key(Godot.Key.E, false);
        await Frames(2);
        Check(!shipView.AimStarboard && world.Player.Units(Good.Munitions) == ammo - 2, "releasing E fires two guns to starboard");
        Check(!world.Ship.Loaded[1] && world.Ship.Loaded[0], "the starboard battery is now reloading");
        bool hitDummy = dummy.HullHp < hpDummy;
        for (int i = 0; i < 60 && !hitDummy; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            hitDummy |= dummy.HullHp < hpDummy;
        }
        Check(hitDummy, "the ship abeam takes the broadside");
        Check(hud.GunGauge.Munitions == world.Player.Units(Good.Munitions), "the HUD counts the shot left");
        Tap(Godot.Key.Key3);
        await Frames(2);
        Check(world.Ship.Order == CrewOrder.Repair, "3 orders repair and pump");
        Tap(Godot.Key.Key1);
        await Frames(2);
        Check(world.Ship.Order == CrewOrder.Battle, "1 orders battle stations");
        world.Ship.HullHp = 0;
        await Frames(2);
        Check(world.Ship.Foundering && hud.LastStandVisible, "hull gone: the last stand and its hourglass show");
        var refuge = world.Map.Ports.Where(p => p.Discovered && world.IsOpen(p) && !world.RescuedAt.Contains(p.Id))
            .OrderBy(p => p.Harbor.DistanceTo(world.Ship.Pos) - p.RingRadius).FirstOrDefault();
        Check(refuge != null && hud.LastStandRefuge == refuge.Name, $"the last stand points to the nearest harbour that will take her in ({hud.LastStandRefuge})");
        world.Ship.Foundering = false;
        world.Ship.HullHp = world.Ship.Hull.HullHp;
        world.Ship.Water = 0;
        world.Ship.Leaks = 0;
        world.Others.Remove(dummy);
        await Frames(2);
        Check(!hud.LastStandVisible, "the stand clears once she floats again");

        // Edge markers: a hostile in sight but off the screen is marked on the rim, clear of the HUD's plates; beyond
        // sight only a lookout reports her (GDD §7: warnings 100/200/300 m beyond vision).
        var brig = world.Spawn("brig", world.Ship.Pos + Vec2.FromAngle(world.Ship.Heading) * 350, world.Ship.Heading + Math.PI, Faction.Brethren, null, crew: 4, cannons: 0);
        brig.Ai = new AiState { Role = Role.Raider };   // a raider's colours, no captain: she lies still
        camPos = Ink.V(world.Ship.Pos);
        await Frames(4);
        var brigMark = hud.MarkerFor(brig.Id);
        Check(brigMark is { } bm && !bm.Reported && !hud.Plates.Any(r => r.Grow(2).HasPoint(bm.At)), $"a hostile in sight off the screen is marked on the rim, clear of the plates ({hud.MarkerCount})");
        // Dead ahead she sits above the middle of the screen, where the day plate and a notice ribbon meet: the badge must
        // clear both (it used to escape one plate into the other).
        world.Notices.Enqueue("NOTICE_SAIL_SIGHTED");
        brig.Pos = world.Ship.Pos + Vec2.FromAngle(Angles.FromCompassDeg(0)) * 420;
        await Until(() => hud.NoticeShown.Length > 0, 60);
        await Frames(2);
        Check(hud.MarkerFor(brig.Id) is { } bm3 && !hud.KeepOut.Any(r => r.HasPoint(bm3.At)),
            $"a badge between two plates clears both ({hud.MarkerFor(brig.Id)?.At})");
        brig.Pos = world.Ship.Pos + Vec2.FromAngle(world.Ship.Heading) * 650;
        await Frames(4);
        Check(hud.MarkerFor(brig.Id) == null, "beyond sight, with no lookout aboard, she is not marked");
        world.Player.Officers.Add(new Officer { Type = OfficerType.Lookout, Tier = 0 });
        world.ApplyOfficers();
        await Frames(4);
        Check(hud.MarkerFor(brig.Id) is { Reported: true }, "a lookout reports her from beyond sight");
        // In sight but hidden under a HUD plate (here the purse, top right): marked at the plate's edge.
        var purse = hud.Plates.Where(r => r.Position.Y < 60).OrderByDescending(r => r.End.X).First();
        brig.Pos = Ink.M(GetCanvasTransform().AffineInverse() * purse.GetCenter());
        await Frames(2);
        Check(world.PlayerSees(brig.Pos) && hud.MarkerFor(brig.Id) is { Reported: false } bm2 && !purse.HasPoint(bm2.At),
            $"a hostile hidden under a HUD plate is marked beside it ({brig.Pos.DistanceTo(world.Ship.Pos):0} m)");
        world.Player.Officers.Clear();
        world.ApplyOfficers();
        world.Others.Remove(brig);
        await Frames(2);

        // The key legend: shown for the first two minutes at sea, then it steps aside; pausing or resting the pointer
        // on the bottom of the screen brings it back.
        Check(hud.LegendVisible, "the key legend shows at the start of a voyage");
        hud.AgeForTest(125);
        var away2 = new Vector2(GetViewport().GetVisibleRect().Size.X / 2, 200);
        Push(new InputEventMouseMotion { Position = away2, GlobalPosition = away2 });
        await Until(() => !hud.LegendVisible, 300);
        Check(!hud.LegendVisible, "after two minutes at sea the key legend steps aside");
        Tap(Godot.Key.Escape);
        await Until(() => hud.LegendVisible, 120);
        Check(paused && hud.LegendVisible, "pausing brings the key legend back");
        Tap(Godot.Key.Escape);
        await Until(() => !hud.LegendVisible, 300);
        var bottom = new Vector2(GetViewport().GetVisibleRect().Size.X / 2, GetViewport().GetVisibleRect().Size.Y - 20);
        Push(new InputEventMouseMotion { Position = bottom, GlobalPosition = bottom });
        await Until(() => hud.LegendVisible, 120);
        Check(hud.LegendVisible, "and so does resting the pointer on the bottom of the screen");
        Push(new InputEventMouseMotion { Position = away2, GlobalPosition = away2 });

        // Weather: L douses and lights the lantern; a storm at the ship rains on the wash and reads on the HUD.
        Tap(Godot.Key.L);
        await Frames(2);
        Check(!world.Lantern, "L douses the lantern");
        Tap(Godot.Key.L);
        await Frames(2);
        Check(world.Lantern, "L again lights it");
        world.Weather.Storms.Add(new StormCell { Pos = world.Ship.Pos, Radius = 300, Strength = 0.8, Life = 60, Age = 20 });
        await Frames(2);
        Check(weather.Now.Rain > 0.5 && hud.WeatherText.Contains(Text.Get("WX_STORM")), "inside a storm the wash rains and the HUD says so");
        Check(world.VisionRadius < 500, "and the lookout sees less");
        int strikes = weather.Strikes;
        audio.Play("thunder", 1.0, 0);
        await Frames(2);
        Check(weather.Strikes > strikes, "a thunderclap brings a lightning strike");
        world.Weather.Storms.Clear();
        await Frames(2);
        Check(weather.Now.Rain == 0, "clear again once it passes");

        // A beast: in the Shoals the serpent is named on the HUD, drawn when surfaced, and gone once beaten.
        world.Ship.Pos = world.SeaPointNear(world.Map.RegionOf(RegionType.Shoals).Seed, 0, 200);
        world.Ship.Vel = Vec2.Zero;
        var serpent = world.SpawnMonster(MonsterType.ReefSerpent);
        await Frames(2);
        Check(life.Letters.Showing == RegionType.Shoals, "crossing into the Shoals letters its name on the sea");
        Check(fog.Animating > 0, "newly charted water inks itself in rather than appearing at once");
        Check(world.Monster == serpent && hud.WeatherText.Contains(Text.Get("MONSTER_reef_serpent")), "the HUD names the beast");
        Check(hud.RegionCalled == Text.Get("REGION_shoals") && hud.WeatherText.StartsWith(Text.Get("REGION_shoals")), $"entering a region inks its name on the conditions line ({hud.RegionCalled})");
        serpent.Hp = 0;
        world.Ship.Pos = world.Map.RegionOf(RegionType.Shoals).Seed;
        await Frames(3);
        Check(world.Monster == null && world.Player.Achievements.Contains("serpent_slayer"), "a dead serpent clears and unlocks Serpent Slayer");
        world.Ship.Pos = OpenWater(600);
        world.Ship.Vel = Vec2.Zero;
        camPos = Ink.V(world.Ship.Pos);   // the spyglass check below aims through the camera: don't let it still be easing across the sea

        // The crew panel: C opens it and the sim waits; + moves a hand to the guns; C closes it.
        Tap(Godot.Key.C);
        await Frames(2);
        Check(crewPanel.IsOpen && Paused, "C opens the crew panel and the sim waits");
        Click(crewPanel.OrderButton((int)CrewOrder.MakeSail));
        await Frames(1);
        Check(world.Ship.Order == CrewOrder.MakeSail && crewPanel.Figures(1).Count == world.Ship.Stations()[1] && crewPanel.Figures(1).Count > 0,
            "clicking an order on the crew panel sets it, and the sailors drawn match the stations");
        Tap(Godot.Key.Key3);
        await Frames(1);
        Check(world.Ship.Order == CrewOrder.Repair && crewPanel.IsOpen, "3 on the open crew panel sets the order too");
        world.SetStations(1, 2, 1);
        Check(world.Ship.Order == CrewOrder.Custom && world.Ship.Stations()[0] == 1, "stations can be set by hand");
        Tap(Godot.Key.C);
        await Frames(2);
        Check(!crewPanel.IsOpen, "C closes it");
        Tap(Godot.Key.Key4);
        await Frames(2);

        // The spyglass: holding the right button extends sight along a cone toward the cursor.
        camPos = Ink.V(world.Ship.Pos);   // the camera is still easing after the jump to open water: the cursor must point where it was aimed (GC-30)
        await Until(() => Mathf.Abs(camera.Zoom.X - zoomTarget) < 0.001f && camera.Position.DistanceTo(camPos) < 0.5f && effects.Shake <= 0, 120);
        var far = world.Ship.Pos + Vec2.FromAngle(world.Ship.Heading) * 700;
        Check(!world.PlayerSees(far), "700 m ahead is beyond plain sight");
        var aheadScreen = GetCanvasTransform() * Ink.V(far);
        Push(new InputEventMouseMotion { Position = aheadScreen, GlobalPosition = aheadScreen });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = aheadScreen, GlobalPosition = aheadScreen });
        await Frames(2);
        Check(world.SpyglassOn && world.PlayerSees(far), "the spyglass sees 700 m along the cone");
        Check(!world.PlayerSees(world.Ship.Pos - Vec2.FromAngle(world.Ship.Heading) * 700), "but not astern");
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = aheadScreen, GlobalPosition = aheadScreen });
        await Frames(2);
        Check(!world.SpyglassOn, "releasing the button lowers the glass");

        // Treasure: a bottle map matches when the coast is sighted; F in the dig ring, furled, starts the dig.
        var siteT = world.Map.Treasures.First(t => !t.Dug);
        world.GiveBottleMap(siteT.Id);
        world.Ship.Pos = siteT.DigRing;
        world.Ship.Vel = Vec2.Zero;
        world.Ship.SailTarget = 0;
        world.Ship.SailFraction = 0;
        await Frames(2);
        Check(world.Player.BottleMaps.Any(m => m.Treasure == siteT.Id && m.Solved) && world.CanDigHere, "the sketch is matched beside its island");
        Check(hud.DockPromptVisible && hud.DockPromptText == Text.Get("HUD_DIG"), $"the HUD offers the dig ({hud.DockPromptText})");
        Tap(Godot.Key.F);
        await Frames(3);
        Check(world.Digging && world.DigProgress > 0, "F starts digging");
        Check(hud.DockPromptText.StartsWith(Text.Get("HUD_DIGGING", 15).Split(' ')[0]), "the prompt counts the dig down");
        var away = OpenWater(300);
        if (away.DistanceTo(siteT.DigRing) < 200) away = siteT.DigRing + new Vec2(400, 0);
        world.Ship.Pos = away;
        await Frames(2);
        Check(!world.Digging, "sailing off interrupts it");

        // A wreck inside an unmatched dig ring: F salvages it, and the prompt counts a salvage (10 s), not a dig.
        var wreckS = world.Map.Wrecks.FirstOrDefault(w => !w.Salvaged);
        var unmatched = world.Map.Treasures.FirstOrDefault(t => !t.Dug && !world.Player.BottleMaps.Any(m => m.Treasure == t.Id));
        if (wreckS != null && unmatched != null)
        {
            var ringWas = unmatched.DigRing;
            unmatched.DigRing = wreckS.Pos;
            world.Ship.Pos = wreckS.Pos;
            world.Ship.Vel = Vec2.Zero;
            await Frames(2);
            Check(hud.DockPromptText == Text.Get("HUD_SALVAGE") && hud.DockPromptKey == Settings.Label(settings.KeyFor("Dock")), $"a wreck inside an unmatched dig ring offers the salvage ({hud.DockPromptText})");
            Tap(Godot.Key.F);
            await Frames(3);
            Check(world.Salvaging && hud.DockPromptText.StartsWith(Text.Get("HUD_SALVAGING", 10).Split(' ')[0]), $"and the prompt counts the salvage down ({hud.DockPromptText})");
            world.Ship.Pos = away;
            await Frames(2);
            unmatched.DigRing = ringWas;
        }

        // Run wrapper: title, a new voyage, the pause menu, suspend save and resume, the dock autosave, the logbook, hints.
        ShowTitle("home");
        await Frames(2);
        Check(title.IsOpen && Paused && title.Page == "home", "the title opens over a still sea");
        Check(GetViewport().GuiGetFocusOwner() == title.NewVoyageButton, "the title's first line has the keyboard focus");
        Tap(Godot.Key.Down);
        await Frames(1);
        Check(GetViewport().GuiGetFocusOwner() == title.LogbookButton, "the down arrow walks the title menu");
        Tap(Godot.Key.Enter);
        await Frames(2);
        int scrolled0 = title.AchievementScroll;
        Tap(Godot.Key.Pagedown);
        await Frames(1);
        Check(title.Page == "logbook" && title.AchievementScroll > scrolled0, $"Enter opens the logbook, and Page Down scrolls its medallions ({scrolled0}→{title.AchievementScroll})");
        Tap(Godot.Key.Escape);
        await Frames(2);
        Check(title.Page == "home" && GetViewport().GuiGetFocusOwner() == title.NewVoyageButton, "Esc goes home with the focus back on New voyage");
        Tap(Godot.Key.Enter);
        await Frames(1);
        Check(title.Page == "voyage", "Enter on it opens the voyage page");
        profile.Cosmetics.Add("sails_ochre");
        Click(title.Cycler(1, +1));
        Check(title.Loadout[1] == "sails_ochre" && title.Preview.ShownLoadout[1] == "sails_ochre", "the sails cycler (clicked) dresses the live preview in ochre");
        Click(title.Cycler(1, -1));
        profile.Cosmetics.Remove("sails_ochre");
        Check(title.Loadout[1] == "", "and back to plain");
        title.Show("voyage");
        title.SelectPreset(Preset.Tempest);
        title.SetShipName("Test Wren");
        title.PressSetSail();
        await Frames(3);
        Check(!title.IsOpen && world.Preset == Preset.Tempest && world.Player.ShipName == "Test Wren" && !Paused && world.Ticks > 0,
            "Set sail starts a voyage on the chosen preset with the chosen name");
        Check(profile.LastShipName == "Test Wren" && profile.LastPreset == "Tempest", "the profile remembers the choice");
        Tap(Godot.Key.Escape);
        await Frames(1);
        Check(pauseMenu.IsOpen && Paused, "Esc opens the pause menu");
        Check(pauseMenu.WhereText.Contains("Test Wren") && pauseMenu.WhereText.Contains(Text.Get("PRESET_TEMPEST")), $"the pause card says where she is ({pauseMenu.WhereText})");
        pauseMenu.PressSaveQuit();
        await Frames(2);
        Check(title.IsOpen && profile.HasSuspend, "Save & quit writes the suspend save and returns to the title");
        // A new voyage would write over that save: Set sail asks first (audit R-1), and keeping her is the default.
        long ticksSaved = world.Ticks;
        title.Show("voyage");
        await Frames(1);
        Click(title.SetSailButton);
        await Frames(1);
        Check(title.ConfirmOpen && title.IsOpen && world.Ticks == ticksSaved && GetViewport().GuiGetFocusOwner() == title.KeepButton,
            "with a voyage saved, Set sail (clicked) first asks to abandon it, with Keep her focused");
        Tap(Godot.Key.Escape);
        await Frames(1);
        Check(!title.ConfirmOpen && title.Page == "voyage" && profile.HasSuspend, "Esc keeps the saved voyage");
        Click(title.SetSailButton);
        await Frames(1);
        Click(title.KeepButton);
        await Frames(1);
        Check(!title.ConfirmOpen && title.IsOpen && profile.HasSuspend, "and so does Keep her (clicked)");
        var suspended = World.LoadJson(profile.ReadSuspend()!);
        ResumeVoyage();
        Check(!title.IsOpen && world.Hash() == suspended.Hash() && world.Player.ShipName == "Test Wren" && !profile.HasSuspend,
            "Resume restores the same voyage and consumes the save");
        await Frames(2);
        world.Ship.Pos = world.Map.StartPort.Harbor;
        world.Ship.Vel = Vec2.Zero;
        await Frames(2);
        Tap(Godot.Key.F);
        await Frames(2);
        Check(world.IsDocked && profile.HasSuspend, "docking writes the suspend save");
        Tap(Godot.Key.Escape);
        await Frames(2);
        // Only the make-sail note is due here: earlier notes may already have fired (the test's own timing decides which),
        // and a random voyage can put a raider or a storm near the start.
        foreach (var k in new[] { "wind_rose", "irons", "tack", "dock", "trade", "hostile", "leak", "night", "threat", "chart", "spyglass", "storm" })
            profile.MarkHint(k);
        profile.HintsSeen.Remove("first_sail");   // the dig above sits furled long enough to show it in slow runs (audit R-11)
        hintsView.Bind(world);
        profile.HintsSeen.Remove("first_sail");   // one long frame (a fresh build) can show it earlier than this check (GC-22)
        world.Ship.Pos = OpenWater(300);
        world.Ship.SailTarget = 0;
        world.Ship.SailFraction = 0;
        await Until(() => hintsView.Current != null, 600);   // it shows after 1.5 s at sea and holds for 9 s: wait for it, not 60 frames (GC-30)
        Check(hintsView.Current == "first_sail" && profile.HintSeen("first_sail"), $"the first-voyage hint shows and is remembered ({hintsView.Current})");
        await Until(() => hintsView.NoteVisible, 60);   // it drops in over a third of a second
        Check(hintsView.NoteVisible && hintsView.NoteText == Text.Get("HINT_first_sail") && hintsView.NoteText.Contains(" " + Settings.Label(settings.KeyFor("SailUp")) + " "),
            $"the note letters the bound key ({hintsView.NoteText})");
        Check(!hud.Plates.Any(r => r.Intersects(hintsView.NoteRect)), "the note is pinned clear of the HUD's plates");
        Tap(Godot.Key.W);
        await Frames(140);
        Check(hintsView.Current != "first_sail", "making sail retires it");
        world.Ship.HullHp = 0;
        world.Ship.Foundering = true;
        world.Ship.Hourglass = 0.05;
        await Frames(5);
        Check(world.RunOver, "the last stand runs out");
        await Frames(90);
        Check(logbook.IsOpen && logbook.CauseText == Text.Get(world.CauseOfSinking) && logbook.BestVisible, "the logbook opens with the cause and a first best");
        Check(logbook.SealVisible, "a first best stamps the captain's seal on the log page");
        Check(profile.Bests.TryGetValue("Tempest", out var pb) && Math.Abs(pb.Days - Math.Round(world.DaysSurvived, 2)) < 1e-9 && !profile.HasSuspend,
            "the profile logs the best and drops the suspend save");
        logbook.PressNewVoyage();
        await Frames(1);
        Check(title.IsOpen && title.Page == "voyage", "New voyage from the logbook goes straight to the voyage page");
        title.SelectPreset(Preset.RoughSeas);
        title.SetShipName("");
        title.NameField.GrabFocus();
        await Frames(1);
        Tap(Godot.Key.Enter);   // Enter in the name field sets sail (audit R-5)
        await Frames(2);
        Check(!title.IsOpen && world.Preset == Preset.RoughSeas && world.Player.ShipName.Length > 0, "a blank name gets a random one");

        // Audio: every loop loaded, the sea is audible, a sail change and a broadside make their sounds, harbour bustle in port.
        Check(audio.Loaded, "all eleven ambience loops loaded from assets/audio");
        world.Ship.Pos = OpenWater(300);
        world.Ship.SailTarget = 0;
        world.Ship.SailFraction = 0;
        await Frames(45);
        Check(audio.LoopLevel("wind_low") > 0.05f && audio.LoopLevel("waves") > 0.05f, $"wind and waves play at sea ({audio.LoopLevel("wind_low"):0.00}/{audio.LoopLevel("waves"):0.00})");
        bool windMatches = !world.Wind.Fixed;
        string windWhy = "";
        sea.RefreshGrid();   // the same instant as WindAt below (in play the grid trails by at most five frames)
        var shipRegion = world.Map.RegionAt(world.Ship.Pos).Type;
        foreach (var off0 in new[] { new Vec2(0, 0), new Vec2(90, -40), new Vec2(-120, 60) })
        {
            // Region wind factors step at a region's border while the drawn grid blends across it, so compare
            // inside the ship's own region only (the offset shrinks toward her until it is).
            var off = off0;
            for (int k = 0; k < 6 && world.Map.RegionAt(world.Ship.Pos + off).Type != shipRegion; k++) off = off * 0.5;
            var p = world.Ship.Pos + off;
            var (gdir, gspeed) = sea.GridWindAt(p);
            var sim = world.WindAt(p);
            double err = Math.Abs(Angles.Wrap(Math.Atan2(gdir.Y, gdir.X) - sim.Direction));
            windMatches &= err < Angles.Rad(3) && Math.Abs(gspeed - sim.Speed) < 0.35;
            windWhy += $" {Angles.Deg(err):0.0}°/{Math.Abs(gspeed - sim.Speed):0.00}";
        }
        Check(windMatches, $"the sea's strokes follow the sim's own wind (grid vs WindAt:{windWhy})");
        int slaps = audio.Plays("sail_up");   // counted by name: another one-shot (a gust, a notice) may land last (GC-30)
        Tap(Godot.Key.W);
        await Until(() => audio.Plays("sail_up") > slaps, 30);
        Check(audio.Plays("sail_up") == slaps + 1, $"setting sail slaps the rigging ({audio.LastOneShot})");
        Tap(Godot.Key.S);
        await Frames(2);
        world.Ship.Cannons = Math.Max(world.Ship.Cannons, 2);
        world.Player.Cargo[(int)Good.Munitions] = Math.Max(world.Player.Cargo[(int)Good.Munitions], 10);
        world.Ship.Loaded[1] = true;
        int booms = audio.Plays("cannon");
        Key(Godot.Key.E, true);
        await Frames(1);
        Key(Godot.Key.E, false);
        await Until(() => audio.Plays("cannon") > booms, 30);
        Check(audio.Plays("cannon") > booms, $"a broadside booms ({audio.LastOneShot})");
        world.Ship.Pos = world.Map.StartPort.Harbor;
        world.Ship.Vel = Vec2.Zero;
        await Frames(60);
        Check(audio.LoopLevel("harbour") > 0.2f, $"harbour bustle near the quay ({audio.LoopLevel("harbour"):0.00})");
        Check(marks.Glow.Active == world.Map.StartPort && marks.Glow.Strength > 0.5f, "the harbour ring stands out while she is in it");
        Check(world.IsNight || life.Gulls > 0, "gulls wheel over the harbour by day");

        // Options: rebinding through the real key path, the key line, the palette, UI scale, persistence.
        settings.Bind("SailUp", Godot.Key.K);
        ApplySettings();
        int levelBefore = world.Ship.SailTarget;
        Tap(Godot.Key.W);
        await Frames(2);
        Check(world.Ship.SailTarget == levelBefore, "W no longer sets sail once rebound");
        Tap(Godot.Key.K);
        await Frames(2);
        Check(world.Ship.SailTarget == levelBefore + 1 && hud.KeyLine.StartsWith("K /"), $"K sets sail and the key line says so ({hud.KeyLine[..12]})");
        settings.ResetKeys();
        ApplySettings();
        options.Open();
        await Frames(1);
        Check(options.IsOpen && Paused, "Options opens and holds the sim");
        Click(options.KeyButton("Lantern"));
        Check(options.Capturing == "Lantern", "clicking a key cap starts a capture");
        Tap(Godot.Key.Escape);
        await Frames(1);
        Check(options.Capturing == null && options.IsOpen && settings.KeyFor("Lantern") == Godot.Key.L, "Esc keeps the old key and leaves Options open");
        options.BeginCapture("Dock");
        Tap(Godot.Key.G);
        await Frames(1);
        Check(settings.KeyFor("Dock") == Godot.Key.G && options.Capturing == null, "a pressed key binds the captured action");
        Check(hud.DockPromptVisible && hud.DockPromptKey == "G", $"the dock prompt's key cap follows the binding ({hud.DockPromptKey})");
        options.BeginCapture("Chart");
        Tap(Godot.Key.G);
        await Frames(1);
        Check(settings.KeyFor("Chart") == Godot.Key.G && settings.KeyFor("Dock") == Godot.Key.M, "a key holds one action; the action that had it takes the old key, so none is left unbound (GC-10)");
        settings.ResetKeys();
        options.SetColorblind(true);
        Check(Ink.Colorblind && Ink.Crown.R > 0.8f && Ink.Brethren.B > 0.6f, "the colorblind palette swaps the faction inks");
        options.SetColorblind(false);
        options.SetUiScale(1.25);
        Check(Mathf.Abs(GetWindow().ContentScaleFactor - 1.25f) < 1e-4f, "UI scale applies to the window");
        options.SetUiScale(1.5);
        await Frames(2);
        Check(!hud.PlatesOverlap(), "at 150% UI scale no HUD plate overlaps another or leaves the screen");
        options.SetUiScale(1.0);
        var reloaded = Settings.Load("user://selftest/");
        Check(reloaded.Keys["SailUp"] == "W" && Math.Abs(reloaded.UiScale - 1.0) < 1e-9 && !reloaded.Colorblind, "settings persist and reload");
        Tap(Godot.Key.Escape);
        await Frames(1);
        Check(!options.IsOpen, "Esc closes Options");

        // Docking: sail into the home harbour, F opens the port panel, trades move gold and cargo, Esc casts off.
        var home = world.Map.StartPort;
        world.Ship.Pos = home.Harbor;
        world.Ship.Vel = Vec2.Zero;
        await Frames(2);
        Check(hud.DockPromptVisible, "inside the harbour ring the HUD offers F to dock");
        Check(hud.DockPromptKey == Settings.Label(settings.KeyFor("Dock")) && hud.DockPromptText == Text.Get("HUD_DOCK", home.Name) && !hud.PlatesOverlap(),
            $"the prompt names the port beside the bound key, clear of the other plates ({hud.DockPromptKey} {hud.DockPromptText})");
        int goldBefore = world.Player.Gold;
        Tap(Godot.Key.F);
        await Frames(2);
        Check(world.IsDocked && portScreen.IsOpen, "F docks and opens the port panel");
        world.Notices.Enqueue("NOTICE_DESERTED");   // what docking raises when unpaid hands walk
        long td = world.Ticks;
        await Frames(5);
        Check(world.Ticks == td, "the clock is frozen in port");
        var cheapest = home.Produces.OrderBy(g => home.Market.Price(g)).First();
        int unitsBefore = world.Player.Units(cheapest);
        portScreen.Show(0);
        portScreen.Select(cheapest);
        Check(portScreen.Do(new PortCommand(PortAction.Buy, cheapest, 2)) == PortResult.Ok, "the market sells two units of the local produce");
        Check(world.Player.Gold < goldBefore && world.Player.Units(cheapest) == unitsBefore + 2, "gold and cargo move on a purchase");
        Check(portScreen.Do(new PortCommand(PortAction.Sell, cheapest, 2)) == PortResult.Ok, "and buys them back");
        Check(world.Player.Units(cheapest) == unitsBefore && world.Player.Gold <= goldBefore, "selling returns the goods at a small loss");
        Check(portScreen.Do(new PortCommand(PortAction.Buy, Good.Relics, 1)) == PortResult.NoStock, "a good the market lacks is refused");
        await Frames(1);
        ClickAt(portScreen.Ledger.RowCentre(Good.Munitions)!.Value);
        Check(portScreen.Selected == Good.Munitions, "clicking a ledger line picks the good");
        Tap(Godot.Key.Down);
        Check(portScreen.Selected == Good.Timber, "the arrow keys walk the ledger");
        portScreen.Select(cheapest);
        await Frames(1);
        Click(portScreen.MoreButton);
        Click(portScreen.MoreButton);
        int quoted = world.BuyQuote(cheapest, 3), goldNow = world.Player.Gold;
        Check((int)portScreen.Quantity.Value == 3 && portScreen.BuyText == Text.Get("PORT_BUY_FOR", 3, quoted), $"+ raises the quantity and the Buy button shows the sim's total ({portScreen.BuyText})");
        Click(portScreen.BuyButton);
        Check(world.Player.Gold == goldNow - quoted && world.Player.Units(cheapest) == unitsBefore + 3, "Buy (clicked) charges exactly that total");
        Click(portScreen.SellButton);
        Check(world.Player.Units(cheapest) == unitsBefore, "Sell (clicked) sells them back");
        Tap(Godot.Key.E);
        await Frames(1);
        Check(portScreen.Page == 1 && portScreen.FocusOnPage(1), "E turns to the next page of the ledger, and the keyboard follows it");
        Tap(Godot.Key.Q);
        Check(portScreen.Page == 0, "Q turns back");
        await Frames(1);
        Click(portScreen.Tabs[2]);
        Check(portScreen.Page == 2, "a ribbon tab (clicked) opens its page");
        portScreen.Show(1);
        Check(portScreen.Page == 1, "the shipwright tab shows");
        world.Player.Gold += 5000;
        Check(portScreen.Do(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Sails)) == PortResult.Ok && world.Ship.Grade(Part.Sails) == 1, "a grade of sails is bought");
        Check(portScreen.Do(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Cannons)) == PortResult.Ok && world.CannonCost > World.CannonPrice
              && portScreen.CannonOffer == Text.Get("PORT_BUY_CANNON", world.CannonCost), $"a new gun is quoted at the battery's grade ({portScreen.CannonOffer})");
        Check(portScreen.Do(new PortCommand(PortAction.BuyHull, Text: "cutter")) == PortResult.Ok && world.Ship.Hull.Id == "cutter", "a cutter is bought against the sloop's trade-in");
        shipView.Init(world.Ship);
        portScreen.Show(2);
        Check(portScreen.Page == 2, "the tavern tab shows");
        var offer = world.TavernOfficers(home).First(o => o.Type == OfficerType.Lookout);
        Check(portScreen.Do(new PortCommand(PortAction.HireOfficer, Amount: (int)OfficerType.Lookout * 10 + offer.Tier)) == PortResult.Ok && world.Player.Officers.Count == 1, "a lookout signs on");
        await Frames(170);
        Check(hud.NoticeShown != Text.Get("NOTICE_DESERTED"), "a notice raised in port waits behind the panel");
        Tap(Godot.Key.Escape);
        await Frames(2);
        Check(!world.IsDocked && !portScreen.IsOpen, "Esc casts off");
        bool noticeRead = false;
        for (int i = 0; i < 300 && !noticeRead; i++)
        {
            noticeRead = hud.NoticeShown == Text.Get("NOTICE_DESERTED");
            if (!noticeRead) await Frames(1);
        }
        Check(noticeRead, "and it is read once she casts off");
        Check(world.Ticks > td, "the clock runs again after casting off");

        Check(Parchment.RimBox != null && Parchment.GoodIcon(Good.Relics) != null && Parchment.PartIcon(Part.Lantern) != null && Parchment.AchievementIcon("last_tide", false) != null
              && Art.Has("title/keyart") && Art.Has("ui/cartouche") && Parchment.Crest(Faction.Brethren, false) != null, "the UI kit, icon atlases, crests and key art load");
        await AuditChecks(Check, Frames, Key, Tap, Push);

        // The frame has ink on it: dark pixels from the ship, islands and HUD.
        await Frames(2);
        var img = GetViewport().GetTexture().GetImage();
        int dark = 0, samples = 0;
        for (int y = 0; y < img.GetHeight(); y += 2)
            for (int x = 0; x < img.GetWidth(); x += 2)
            {
                samples++;
                if (img.GetPixel(x, y).Luminance < 0.5f) dark++;
            }
        Check(dark > 150 && dark < samples / 2, $"the frame has ink on parchment ({dark} dark of {samples} sampled pixels)");
        if (screenshotPath != null)
            img.SavePng(screenshotPath);

        GD.Print(failures.Count == 0 ? "SELFTEST PASS" : $"SELFTEST FAIL ({failures.Count})");
        ExitGame(failures.Count == 0 ? 0 : 1);
    }
}
