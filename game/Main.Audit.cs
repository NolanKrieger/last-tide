using System.Reflection;
using Godot;
using LastTide.Sim;

namespace LastTide;

public partial class Main
{
    /// <summary>
    /// Run-lifecycle and input checks from the game-core audit (AUDIT-game-core.md, ids GC-n). Runs inside the
    /// self-test on a real voyage started from the title, through the same input path the player uses.
    /// </summary>
    async Task AuditChecks(Action<bool, string> Check, Func<int, Task> Frames, Action<Godot.Key, bool> KeyEv, Action<Godot.Key> Tap, Action<InputEvent> Push)
    {
        int Particles() => ((System.Collections.ICollection)typeof(EffectsView).GetField("particles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(effects)!).Count;
        void Afloat()
        {
            world.Ship.Foundering = false;
            world.Ship.HullHp = world.Ship.MaxHp;
            world.Ship.Water = 0;
            world.Ship.Leaks = 0;
        }
        async Task Until(Func<bool> done, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !done(); i++) await Frames(1);
        }
        async Task ToOpenWater()
        {
            if (world.IsDocked) LeavePort();
            if (paused) SetPaused(false, "the audit");
            world.Ship.Pos = OpenWater(300);
            world.Ship.Vel = Vec2.Zero;
            prevPose = curPose = (world.Ship.Pos, world.Ship.Heading);
            await Frames(2);
        }

        // GC-7: a muted run (the self-test, --mute, debug runs) stays muted after the saved settings are applied.
        Check(AudioServer.IsBusMute(AudioServer.GetBusIndex("Master")), "GC-7 a muted run stays muted after the settings apply");

        // GC-14: a notice on the HUD scratches the quill (the usual case: none showing, so the HUD inks it the frame it arrives).
        bool NoticeUp() => hud.NoticeShown.Length > 0;   // the notice ribbon (hud branch: the HUD paints it, no Label node)
        for (int i = 0; i < 400 && (NoticeUp() || world.Notices.Count > 0); i++) await Frames(1);
        int quills = audio.Plays("quill");
        world.Notices.Enqueue("NOTICE_COVE");
        await Frames(2);
        Check(NoticeUp() && world.Notices.Count == 0 && audio.Plays("quill") > quills, "GC-14 a notice plays the quill when the HUD shows it");

        // GC-11: text that names a key follows the bindings (HUD dock prompt, hints, chart, crew).
        var home = world.Map.StartPort;
        await ToOpenWater();
        settings.Bind("Dock", Godot.Key.G);
        ApplySettings();
        world.Ship.Pos = home.Harbor;
        world.Ship.Vel = Vec2.Zero;
        await Frames(2);
        Check(hud.DockPromptVisible && hud.DockPromptKey == "G", $"GC-11 the dock prompt names the rebound key (a key cap beside the words: {hud.DockPromptKey} · {hud.DockPromptText})");
        Check(Text.Get("HINT_dock").StartsWith("G "), $"GC-11 the harbour hint names the rebound key ({Text.Get("HINT_dock")})");
        settings.ResetKeys();
        ApplySettings();

        // GC-1: a harbour rescue (the sim docks a foundering ship itself) opens the port and autosaves.
        world.RescuedAt.Clear();
        world.Ship.Pos = home.Harbor;
        world.Ship.Vel = Vec2.Zero;
        profile.DeleteSuspend();
        world.Ship.HullHp = 0;
        await Frames(3);
        Check(world.IsDocked && portScreen.IsOpen, $"GC-1 a harbour rescue docks her and opens the port (docked {world.IsDocked}, port screen {portScreen.IsOpen})");
        Check(profile.HasSuspend, "GC-1 the rescue dock writes the suspend save");
        // GC-1b: that save (like every dock's crash-protection autosave) resumes in port with the port screen up.
        ShowTitle("home");
        ResumeVoyage();
        await Frames(2);
        Check(!title.IsOpen && world.IsDocked && portScreen.IsOpen && Paused, "GC-1 a voyage resumed from a dock save opens in port, not frozen");
        if (portScreen.IsOpen) { Tap(Godot.Key.Escape); await Frames(2); }
        Check(!world.IsDocked && !paused, "GC-1 Esc casts off after a rescue");
        Afloat();
        await ToOpenWater();

        // GC-19: a voyage with a chart pin still autosaves at the dock (serialising the pin threw an object-cycle JsonException).
        Tap(Godot.Key.M);
        await Frames(2);
        var pinAt = chartScreen.Canvas.ToScreen(world.Ship.Pos + new Vec2(250, 150));
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = pinAt, GlobalPosition = pinAt });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = pinAt, GlobalPosition = pinAt });
        await Frames(1);
        chartScreen.SubmitNote("Shoal water");
        Tap(Godot.Key.M);
        await Frames(2);
        profile.DeleteSuspend();
        world.Ship.Pos = home.Harbor;
        world.Ship.Vel = Vec2.Zero;
        await Frames(2);
        Tap(Godot.Key.F);
        await Frames(2);
        Check(world.IsDocked && world.Pins.Count > 0 && profile.HasSuspend && World.LoadJson(profile.ReadSuspend()!).Pins.Count == world.Pins.Count,
            $"GC-19 a voyage with a chart pin autosaves at the dock ({world.Pins.Count} pins, save {profile.HasSuspend})");
        if (world.IsDocked) LeavePort();
        await ToOpenWater();

        // GC-25: a notice raised on docking (unpaid hands desert) is not lost under the port screen: it waits for the sea.
        world.Player.Unpaid = true;
        int crewBefore = world.Ship.Crew = Math.Max(world.Ship.Crew, 4);
        world.Ship.Pos = home.Harbor;
        world.Ship.Vel = Vec2.Zero;
        await Frames(2);
        Tap(Godot.Key.F);
        await Frames(3);
        bool heldInPort = world.IsDocked && portScreen.IsOpen && world.Notices.Contains("NOTICE_DESERTED");
        Tap(Godot.Key.Escape);
        bool deserted = false;
        for (int i = 0; i < 400 && !deserted; i++)   // queued notices show in turn, 5 s each
        {
            await Frames(1);
            deserted = hud.NoticeShown == Text.Get("NOTICE_DESERTED");   // the ribbon the HUD paints (hud branch)
        }
        Check(heldInPort && !world.IsDocked && deserted, $"GC-25 the desertion notice raised on docking is kept for the sea, not lost under the port screen (held {heldInPort}, shown {deserted})");
        world.Ship.Crew = crewBefore;   // the deserters come back for the checks that fire guns
        await ToOpenWater();

        // GC-26: Esc in the chart's pin note abandons the pin instead of trapping the keys (M and Esc did nothing).
        Tap(Godot.Key.M);
        await Frames(2);
        int pinsBefore = world.Pins.Count;
        var noteAt = chartScreen.Canvas.ToScreen(world.Ship.Pos + new Vec2(-200, 120));
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = noteAt, GlobalPosition = noteAt });
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = noteAt, GlobalPosition = noteAt });
        await Frames(1);
        bool noteOpen = chartScreen.NoteHasFocus;
        Tap(Godot.Key.Escape);
        await Frames(1);
        bool noteGone = !chartScreen.NoteHasFocus && chartScreen.IsOpen;
        Tap(Godot.Key.Escape);
        await Frames(2);
        Check(noteOpen && noteGone && !chartScreen.IsOpen && world.Pins.Count == pinsBefore, $"GC-26 Esc abandons the pin note, a second Esc closes the chart (note {noteOpen}, gone {noteGone}, chart {chartScreen.IsOpen})");
        if (chartScreen.IsOpen) { chartScreen.Close(); await Frames(1); }

        // GC-2: a key released while a screen is up is not held for ever: helm and aim.
        KeyEv(Godot.Key.A, true);
        await Frames(12);
        Check(world.Ship.Rudder < -0.5, "GC-2 A puts the helm over");
        Tap(Godot.Key.M);
        await Frames(1);
        KeyEv(Godot.Key.A, false);
        Tap(Godot.Key.M);
        await Frames(20);
        Check(Math.Abs(world.Ship.Rudder) < 0.05 && chartScreen.IsOpen == false, $"GC-2 A released under the chart lets the rudder centre ({world.Ship.Rudder:0.00})");
        KeyEv(Godot.Key.A, true);   // a player taps it again: whatever the build, nothing stays held for the next checks
        KeyEv(Godot.Key.A, false);
        world.Ship.Cannons = Math.Max(world.Ship.Cannons, 2);
        world.Player.Cargo[(int)Good.Munitions] = Math.Max(world.Player.Cargo[(int)Good.Munitions], 10);
        world.Ship.Loaded[0] = world.Ship.Loaded[1] = true;
        int shot = world.Player.Units(Good.Munitions);
        KeyEv(Godot.Key.E, true);
        await Frames(1);
        Tap(Godot.Key.C);
        await Frames(1);
        KeyEv(Godot.Key.E, false);
        Tap(Godot.Key.C);
        await Frames(3);
        Check(!shipView.AimStarboard && world.Player.Units(Good.Munitions) == shot, "GC-2 E released under the crew panel neither sticks the arc nor fires");
        KeyEv(Godot.Key.E, true);
        KeyEv(Godot.Key.E, false);
        await Frames(2);

        // GC-17: a key held while the window loses focus is let go (Godot sends no key-up to an unfocused window).
        KeyEv(Godot.Key.D, true);
        await Frames(12);
        bool heldOver = world.Ship.Rudder > 0.5;
        Notification((int)NotificationApplicationFocusOut);
        await Frames(20);
        Check(heldOver && Math.Abs(world.Ship.Rudder) < 0.05, $"GC-17 alt-tabbing away with D held lets the rudder centre ({world.Ship.Rudder:0.00})");
        KeyEv(Godot.Key.D, false);

        // GC-18: the self-test ignores the real keyboard and mouse: a stray key on the desktop cannot pause or steer it.
        GetViewport().PushInput(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = true, Device = (int)InputEvent.DeviceIdKeyboard });
        GetViewport().PushInput(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = false, Device = (int)InputEvent.DeviceIdKeyboard });
        await Frames(1);
        Check(!paused && GetTree().Root.GetChildren().OfType<RealInputFilter>().Any(f => f.Swallowed >= 2), "GC-18 a real Esc during the self-test is swallowed (no stray pause)");

        // GC-3: inputs are ignored while paused (GDD §19): a broadside key released in the pause menu does not fire on resume.
        world.Ship.Loaded[0] = world.Ship.Loaded[1] = true;
        shot = world.Player.Units(Good.Munitions);
        Tap(Godot.Key.Escape);
        await Frames(1);
        KeyEv(Godot.Key.E, true);
        KeyEv(Godot.Key.E, false);
        Tap(Godot.Key.Escape);
        await Frames(3);
        Check(!paused && world.Player.Units(Good.Munitions) == shot && !shipView.AimStarboard, "GC-3 E pressed and released while paused does not fire on resume");

        // GC-12: a hint card showing when she saves and quits is not left sitting on the title.
        bool HintCardUp() => hintsView.NoteShown;   // the pinned note (hud branch), in any stage of its animation
        profile.HintsSeen.Remove("first_sail");
        hintsView.Bind(world);
        world.Ship.SailTarget = 0;
        world.Ship.SailFraction = 0;
        for (int i = 0; i < 200 && hintsView.Current != "first_sail"; i++) await Frames(1);
        bool hintShown = HintCardUp();
        Tap(Godot.Key.Escape);
        await Frames(1);
        pauseMenu.PressSaveQuit();
        await Frames(2);
        Check(hintShown && title.IsOpen && !HintCardUp(), "GC-12 a hint card showing at Save and quit is not left on the title");
        ResumeVoyage();
        await Frames(2);

        // GC-4: closing the window mid-voyage keeps the voyage (GDD §19 "Quitting mid-run: suspend save").
        profile.DeleteSuspend();
        exitHeldForTest = true;
        int exits = exitRequested;
        Notification((int)NotificationWMCloseRequest);
        Check(profile.HasSuspend && World.LoadJson(profile.ReadSuspend()!).Hash() == world.Hash() && exitRequested == exits + 1,
            "GC-4 closing the window mid-voyage writes the suspend save, then quits");

        // GC-5 / GC-4b / GC-6: the ship goes down with D held.
        await ToOpenWater();
        KeyEv(Godot.Key.D, true);
        await Frames(5);
        profile.WriteSuspend(world.SaveJson());   // as if she had docked earlier this voyage
        int voyages = profile.Voyages;
        int sinks = audio.Plays("sink");
        int particlesBefore = Particles();
        world.Ship.HullHp = 0;
        world.Ship.Foundering = true;
        world.Ship.Hourglass = 0.05;
        await Until(() => world.RunOver, 60);
        // The window closes in the 2.5 s between the loss and the recap (conditions, not frame counts: GC-30).
        Notification((int)NotificationWMCloseRequest);
        exitHeldForTest = false;
        Check(world.RunOver && !logbook.IsOpen && !profile.HasSuspend && profile.Voyages == voyages + 1, "GC-4 closing the window while she sinks logs the voyage and drops the old save");
        for (int i = 0; i < 30 && !logbook.IsOpen; i++) await Frames(1);   // a second of ticks with the ship gone
        Check(audio.Plays("sink") - sinks <= 2, $"GC-5 the sinking sounds once, not every frame after ({audio.Plays("sink") - sinks} plays)");
        Check(Particles() - particlesBefore < 60, $"GC-5 the sinking whirl is drawn once, not every tick after ({Particles() - particlesBefore} particles)");
        await Until(() => logbook.IsOpen, 400);
        Check(logbook.IsOpen && profile.Voyages == voyages + 1, $"GC-4 the recap still opens and the voyage is logged once ({profile.Voyages - voyages})");
        KeyEv(Godot.Key.D, false);   // released while the recap is up
        logbook.PressNewVoyage();
        await Frames(1);

        // GC-8: an unreadable suspend save is set aside instead of haunting the Resume button.
        profile.WriteSuspend("{ \"Seed\": 7, \"Ticks\": ");
        ShowTitle("home");
        ResumeVoyage();
        await Frames(1);
        Check(title.IsOpen && mode == Mode.Title && !profile.HasSuspend, "GC-8 a corrupt suspend save is moved aside and the title stays usable");

        // GC-6: nothing held when the last voyage ended steers the next one.
        // GC-37 rides along: a long, non-Latin ship's name survives the name field, the save, the resume and the recap.
        const string wideName = "Ψυχή 海の星 🚢 «Wren» of the Very Long Name";
        // GC-38 rides along too: the profile owns every colour but one; a dig's cosmetic roll must find that one.
        var ownedBefore = profile.Cosmetics.ToList();
        profile.Cosmetics.Clear();
        profile.Cosmetics.AddRange(World.CosmeticKeys.Where(k => k != "wake_indigo"));
        title.Show("voyage");
        title.SetShipName(wideName);
        title.PressSetSail();
        string sailedAs = world.Player.ShipName;
        bool seeded = World.CosmeticKeys.Where(k => k != "wake_indigo").All(world.Player.Cosmetics.Contains) && !world.Player.Cosmetics.Contains("wake_indigo");
        typeof(World).GetMethod("UnlockCosmetic", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, null);
        Check(seeded && world.Player.Cosmetics.Contains("wake_indigo") && world.Notices.Contains("NOTICE_COSMETIC_wake_indigo"),
            $"GC-38 a dig's cosmetic roll unlocks one the profile lacks, not one it owns (seeded {seeded})");
        profile.Cosmetics.Clear();
        profile.Cosmetics.AddRange(ownedBefore);
        await Frames(20);
        Check(!title.IsOpen && Math.Abs(world.Ship.Rudder) < 0.05, $"GC-6 a key held when the last voyage ended does not steer the next ({world.Ship.Rudder:0.00})");
        Tap(Godot.Key.Escape);
        await Frames(1);
        pauseMenu.PressSaveQuit();
        await Frames(1);
        ResumeVoyage();
        await Frames(1);
        Check(new System.Globalization.StringInfo(sailedAs).LengthInTextElements is > 0 and <= 24 && wideName.StartsWith(sailedAs) && world.Player.ShipName == sailedAs && Text.Get("LOGBOOK_TITLE", sailedAs).Contains(sailedAs),
            $"GC-37 a long non-Latin ship's name is kept (cut to the field's 24) through Set sail, the save and the resume (\"{sailedAs}\")");

        // GC-9: corrupt profile and settings files never brick startup, and a corrupt profile is kept aside.
        var root = "user://selftest/corrupt/";
        var dir = ProjectSettings.GlobalizePath(root);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
        string why = "";
        foreach (var (name, text) in new[]
                 {
                     ("truncated", "{\"Bests\": {\"Tempest\": {\"Days\": 3."), ("empty", ""), ("array", "[1,2]"),
                     ("nulls", "{\"Bests\":null,\"Achievements\":null,\"Cosmetics\":null,\"HintsSeen\":null,\"LastLoadout\":null,\"LastPreset\":null,\"LastShipName\":null}"),
                     ("null slot", "{\"LastLoadout\":[null,\"\",\"\",\"\",\"\"],\"Bests\":{\"Tempest\":null}}"),
                 })
        {
            File.WriteAllText(dir + "profile.json", text);
            try
            {
                var p = Profile.Load(root);
                _ = p.Bests.Count + p.Achievements.Count + p.Cosmetics.Count + p.HintsSeen.Count + p.LastPreset.Length + p.LastShipName.Length;
                foreach (var slot in p.LastLoadout) _ = p.HasCosmetic(slot);
                foreach (var b in p.Bests.Values) _ = b.Days + b.ShipName.Length + b.Cause.Length;
            }
            catch (Exception e) { why += $"{name}: {e.GetType().Name}; "; }
        }
        Check(why.Length == 0, $"GC-9 a corrupt profile.json loads as a usable fresh profile ({why})");
        File.WriteAllText(dir + "profile.json", "{\"Voyages\": 12, \"Bests\": {");
        Profile.Load(root);
        Check(Directory.GetFiles(dir).Any(f => Path.GetFileName(f).StartsWith("profile.json.corrupt")), "GC-9 the unreadable profile is kept beside it, not overwritten");
        why = "";
        foreach (var (name, text) in new[]
                 {
                     ("truncated", "{\"Keys\": {\"SailUp\": \"K\""), ("nulls", "{\"Keys\":null}"), ("null key", "{\"Keys\":{\"SailUp\":null}}"),
                     ("silly window", "{\"Width\":0,\"Height\":-5,\"UiScale\":40}"),
                 })
        {
            File.WriteAllText(dir + "settings.json", text);
            try
            {
                var s = Settings.Load(root);
                foreach (var a in Settings.Actions) _ = s.KeyFor(a);
                _ = s.ActionOf(Godot.Key.W);
                if (s.Width < 640 || s.Height < 360 || s.UiScale > 1.5) why += $"{name}: window {s.Width}x{s.Height} scale {s.UiScale}; ";
            }
            catch (Exception e) { why += $"{name}: {e.GetType().Name}; "; }
        }
        Check(why.Length == 0, $"GC-9 a corrupt settings.json loads as usable settings ({why})");

        // GC-34: the Siren arrives with her own cue (she used to borrow the Ghost Ship's moan).
        await ToOpenWater();
        world.Ship.Pos = world.SeaPointNear(world.Map.RegionOf(RegionType.SirenRuins).Seed, 0, 200);
        world.Ship.Vel = Vec2.Zero;
        int sirenCues = audio.Plays("monster_siren"), ghostCues = audio.Plays("monster_ghost");
        var siren = world.SpawnMonster(MonsterType.Siren);
        await Frames(3);
        Check(audio.Plays("monster_siren") == sirenCues + 1 && audio.Plays("monster_ghost") == ghostCues, "GC-34 the Siren's arrival plays monster_siren, not the Ghost Ship's cue");
        siren.Hp = 0;
        await Frames(3);
        await ToOpenWater();

        // GC-35: a write that fails (read-only or full disk, a file held by a sync client) never throws into the game:
        // Set sail, hints and the recap all save the profile. A directory squatting on each temp-file path makes every
        // write fail on Linux and Windows alike (no chmod, which Windows does not have).
        var roRoot = "user://selftest/blocked/";
        var roDir = ProjectSettings.GlobalizePath(roRoot);
        foreach (var f in new[] { "profile.json.tmp", "suspend.json.tmp", "settings.json.tmp" }) Directory.CreateDirectory(Path.Combine(roDir, f));
        string thrown = "";
        try
        {
            var ro = Profile.Load(roRoot);
            bool savedP = ro.Save(), savedS = ro.WriteSuspend("{}"), savedT = Settings.Load(roRoot).Save();
            ro.MarkHint("first_sail");
            if (savedP || savedS || savedT) thrown = "a write that cannot happen reported success";
        }
        catch (Exception e) { thrown = e.GetType().Name; }
        Check(thrown.Length == 0, $"GC-35 an unwritable save file is reported, not thrown ({thrown})");

        // GC-29: every table row the game names has its strings (a new good, hull, part, officer, monster, cosmetic,
        // achievement, key action or hint without a row would show its raw key to the player).
        var need = new List<string>();
        foreach (var g in Goods.All) need.Add("GOOD_" + g.Key);
        foreach (var h in Hulls.All) need.Add("HULL_" + h.Id);
        foreach (var r in RegionDef.All) need.Add("REGION_" + r.Key);
        foreach (var f in Enum.GetValues<Faction>()) need.Add("FACTION_" + f.ToString().ToLowerInvariant());
        foreach (var r in Enum.GetValues<PortResult>()) if (r != PortResult.Ok) need.Add("PORT_RESULT_" + r.ToString().ToUpperInvariant());
        foreach (var w in new[] { "scarce", "short", "steady", "plenty", "glut" }) need.Add("STOCK_" + w);
        foreach (var p in PartDef.All) { need.Add("PART_" + p.Key); need.Add("PART_" + p.Key + "_EFFECT"); }
        foreach (var u in BlackMarketDef.All) { need.Add("UNIQUE_" + u.Key); need.Add("UNIQUE_" + u.Key + "_EFFECT"); }
        foreach (var o in Enum.GetValues<OfficerType>())
        {
            need.Add("OFFICER_" + o.ToString().ToLowerInvariant());
            for (int t = 0; t < Officers.TierKey.Length; t++) need.Add($"OFFICER_{o.ToString().ToLowerInvariant()}_EFFECT_{t}");
        }
        foreach (var t in Officers.TierKey.Concat(Threat.TierKey)) need.Add("TIER_" + t);
        foreach (var r in Enum.GetValues<Rig>()) need.Add("RIG_" + r.ToString().ToLowerInvariant());
        foreach (var m in MonsterDefs.All) { need.Add("MONSTER_" + m.Key); need.Add("NOTICE_MONSTER_" + m.Key); need.Add("SUNK_" + m.Type.ToString().ToUpperInvariant()); }
        foreach (var c in new[] { "SUNK_ERUPTION", "SUNK_WATER", "SUNK_SEA", "SUNK_GUNS" }) need.Add(c);
        foreach (var a in Achievements.All) { need.Add("ACH_" + a + "_NAME"); need.Add("ACH_" + a + "_DESC"); need.Add("ACHIEVEMENT_" + a); }
        foreach (var slot in Cosmetics.Slots)
        {
            need.Add("SLOT_" + slot);
            foreach (var c in Cosmetics.Options(slot).Where(c => c.Length > 0)) { need.Add("COSMETIC_" + c); need.Add("NOTICE_COSMETIC_" + c); }
        }
        foreach (var pr in Enum.GetValues<Preset>()) { need.Add("PRESET_" + pr.ToString().ToUpperInvariant()); need.Add("PRESET_LINE_" + pr.ToString().ToUpperInvariant()); }
        foreach (var o in Enum.GetValues<CrewOrder>()) need.Add("ORDER_" + o.ToString().ToUpperInvariant());
        foreach (var a in Settings.Actions) need.Add("ACT_" + a);
        var hintTable = (System.Collections.IEnumerable)typeof(Hints).GetField("Table", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        foreach (var h in hintTable) need.Add("HINT_" + (string)h.GetType().GetProperty("Key")!.GetValue(h)!);
        foreach (var k in (string[])typeof(LogbookScreen).GetField("RowKeys", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!) need.Add("LOGBOOK_" + k);
        var missing = need.Where(k => !Text.Has(k)).Distinct().ToList();
        Check(missing.Count == 0, $"GC-29 every table row the game names has its strings ({need.Count} checked; missing: {string.Join(", ", missing)})");
        var tokens = need.Select(Text.Get).Concat(new[] { "HUD_DOCK", "HUD_DIG", "HUD_DIG_FURL", "HUD_SALVAGE", "HUD_SALVAGE_FURL", "CHART_HINT", "CREW_NOTE", "HUD_HINTS", "HUD_GUN_Q", "HUD_GUN_E" }.Select(Text.Get))
            .Where(t => t.Contains("[[")).ToList();
        Check(tokens.Count == 0, $"GC-11 every [[Action]] token names a real action ({string.Join(" | ", tokens)})");

        // GC-23: nothing in the run so far logged an engine or script error (an exception thrown in an input or
        // frame callback used to print ERROR and let the self-test pass anyway).
        Check(ErrorCounter.Errors == 0, $"GC-23 no engine or script errors were logged ({ErrorCounter.Errors}: {string.Join(" | ", ErrorCounter.Recent)})");
    }
}

/// <summary>
/// Self-test only: swallows events from the real keyboard and mouse before any node sees them, so a stray key or
/// mouse move on the desktop (the test window used to pop up over a live session) cannot pause, steer or aim the
/// run. The test's own events are synthetic (device 0). Added as the root's last child, it gets <c>_Input</c> first.
/// </summary>
public partial class RealInputFilter : Node
{
    /// <summary>The device id the self-test stamps on its events (Godot gives new events the keyboard/mouse ids).</summary>
    public const int Synthetic = 0x5E1F;
    public int Swallowed { get; private set; }

    public override void _Input(InputEvent e)
    {
        if (e.Device == Synthetic) return;
        Swallowed++;
        GetViewport().SetInputAsHandled();
    }
}
