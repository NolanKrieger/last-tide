using System.Diagnostics;
using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// <c>--playtest=DAYS[,VOYAGES]</c> (with <c>--seed=N</c>, default 7): whole voyages played through the real game layer,
/// fast-forwarded. Every voyage starts from the title's Set sail; the sim's <see cref="Autopilot"/> picks the decisions
/// and they reach the ship as synthetic key taps through <c>_UnhandledInput</c> (helm as held A/D, sails as W/S taps,
/// broadsides as Q/E press-release, orders, lantern, F); docks open the port screen (each tab shown) before the
/// autopilot trades; mid-voyage it saves and quits from the pause menu and resumes from the title (hash checked);
/// it opens the chart (and drops a pin), the crew panel and the pause menu along the way; at DAYS the ship goes down
/// and the recap's New voyage starts the next one. Any engine/script error fails the run.
/// Prints one line per voyage and <c>PLAYTEST PASS</c> / <c>PLAYTEST FAIL</c>; exits 0 / 1.
/// </summary>
public partial class Main
{
    double playtestDays;
    int playtestVoyages = 1, playtestSeed = 7, playtestVoyage;
    bool Playtesting => playtestDays > 0;
    int? nextVoyageSeed;
    Autopilot? pilot;
    int ptFrames, ptPortFrames, ptUiFrames, ptDocks, ptResumes, ptPins, ptDone = -1;
    double ptNextUi;
    string ptSaveNote = "";
    string ptUi = "";
    bool ptSaved, ptResumePending, ptSinking;
    ulong ptSavedHash;
    readonly Stopwatch ptClock = new();
    readonly List<string> ptFailures = new();
    int ptMonsters, ptMaxTier, ptTrades, ptUpgrades, ptFights, ptFlights;
    bool ptHadMonster;
    HashSet<string> ptHulls = new();

    void ParsePlaytest(string spec)
    {
        var parts = spec.Split(',');
        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out playtestDays) || !(playtestDays > 0))
        {
            GD.PushWarning($"Ignored a malformed --playtest value: {spec}");
            playtestDays = 0;
            return;
        }
        if (parts.Length > 1 && int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var voyages)) playtestVoyages = voyages;
    }

    void PtKey(Godot.Key k, bool down) => GetViewport().PushInput(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down, Device = RealInputFilter.Synthetic });
    void PtTap(string action) { var k = settings.KeyFor(action); PtKey(k, true); PtKey(k, false); }
    void PtTapKey(Godot.Key k) { PtKey(k, true); PtKey(k, false); }

    void PtFail(string what)
    {
        ptFailures.Add(what);
        GD.Print($"PLAYTEST FAIL: {what}");
    }

    /// <summary>One physics frame of the playtest driver (before the game's own physics step).</summary>
    void PlaytestStep()
    {
        if (ptDone >= 0) { if (++ptDone == 3) ExitGame(ptFailures.Count == 0 && ErrorCounter.Errors == 0 ? 0 : 1); return; }
        ptFrames++;
        if (ptFrames == 1)
        {
            // Fast-forward: the sim ticks once per physics frame, so 8× the physics rate is 8× the voyage
            // (Engine.TimeScale only scales delta in Godot 4, not the tick count).
            Engine.PhysicsTicksPerSecond = 240;
            Engine.MaxPhysicsStepsPerFrame = 16;
            ptClock.Start();
            GD.Print($"PLAYTEST {playtestVoyages} voyage(s) to day {playtestDays} from seed {playtestSeed}");
        }

        if (mode == Mode.Title)
        {
            if (ptResumePending)
            {
                ptResumePending = false;
                if (!profile.HasSuspend) { PtFail("Save and quit left no suspend save"); return; }
                title.Show("home");
                title.PressResume();
                if (mode != Mode.Run || world.Hash() != ptSavedHash) PtFail($"resume from the title restored a different voyage (hash {world.Hash():X} vs {ptSavedHash:X})");
                else ptResumes++;
                if (pilot != null) { ptTrades += pilot.Trades; ptUpgrades += pilot.Upgrades; ptFights += pilot.Fights; ptFlights += pilot.Flights; }
                pilot = new Autopilot();   // a resumed voyage gets a fresh captain (its plans are not part of the save)
                return;
            }
            if (playtestVoyage >= playtestVoyages) { PlaytestFinish(); return; }
            // The next voyage, through the title's voyage page and its Set sail button.
            title.Show("voyage");
            title.SelectPreset((Preset)(playtestVoyage % 3));
            if (playtestVoyage % 2 == 1) title.SetShipName("");   // a blank name draws a random one
            nextVoyageSeed = playtestSeed + playtestVoyage;
            title.PressSetSail();
            if (mode != Mode.Run) { PtFail("Set sail did not start a voyage"); PlaytestFinish(); return; }
            pilot = new Autopilot();
            ptSaved = ptSinking = false;
            ptNextUi = 0.25;
            ptDocks = ptResumes = ptPins = ptMonsters = ptMaxTier = ptTrades = ptUpgrades = ptFights = ptFlights = 0;
            ptHadMonster = false;
            ptHulls = new() { world.Ship.Hull.Id };
            ptClock.Restart();
            return;
        }

        if (logbook.IsOpen)
        {
            var w = world;
            GD.Print($"PLAYTEST voyage {playtestVoyage + 1}: {w.Preset} seed {w.Seed} \"{w.Player.ShipName}\" · day {w.DaysSurvived:0.00} · {Text.Get(w.CauseOfSinking)}" +
                     $" · docks {ptDocks} trades {ptTrades + pilot?.Trades} upgrades {ptUpgrades + pilot?.Upgrades} fights {ptFights + pilot?.Fights} flights {ptFlights + pilot?.Flights}" +
                     $" · gold {w.Player.Gold} hulls {string.Join("/", ptHulls)} · monsters {ptMonsters} · tier {ptMaxTier} · pins {w.Pins.Count} · resumes {ptResumes}" +
                     $" · {ptClock.Elapsed.TotalSeconds:0} s real · errors {ErrorCounter.Errors} warnings {ErrorCounter.Warnings}");
            if (profile.HasSuspend) PtFail("the suspend save outlived the lost voyage");
            playtestVoyage++;
            logbook.PressNewVoyage();
            if (mode != Mode.Title || title.Page != "voyage") PtFail("the recap's New voyage did not open the voyage page");
            return;
        }
        if (world.RunOver) return;   // the recap opens 2.5 s after she goes down
        if (pilot == null) return;

        // Watch the voyage.
        ptMaxTier = Math.Max(ptMaxTier, world.ThreatTier);
        if (world.Monster != null && !ptHadMonster) ptMonsters++;
        ptHadMonster = world.Monster != null;
        ptHulls.Add(world.Ship.Hull.Id);

        if (world.IsDocked)
        {
            // In port: the screen is up (Main follows the world); show every tab, then let the autopilot trade and cast off.
            if (!portScreen.IsOpen) return;
            if (ptPortFrames == 0)
            {
                ptDocks++;
                if (!profile.HasSuspend) PtFail($"docking at {world.Docked!.Name} wrote no suspend save");
                var sw = Stopwatch.StartNew();
                int size = world.SaveJson().Length;
                ptSaveNote = $"save {size / 1024} KB in {sw.Elapsed.TotalMilliseconds:0} ms, log {world.Log.Count} inputs";
            }
            ptPortFrames++;
            if (ptPortFrames == 3) portScreen.Show(1);
            else if (ptPortFrames == 6) portScreen.Show(2);
            else if (ptPortFrames == 9) portScreen.Show(0);
            else if (ptPortFrames >= 12)
            {
                ptPortFrames = 0;
                string port = world.Docked!.Name, cargoIn = Cargo();
                int goldIn = world.Player.Gold, tradesIn = pilot.Trades;
                pilot.Tick(world);                // the port visit: trades, repairs, upgrades, then CastOff
                GD.Print($"PLAYTEST   day {world.DaysSurvived:0.00} {port}: gold {goldIn}→{world.Player.Gold}, sold {pilot.Trades - tradesIn}, hold {cargoIn} → {Cargo()}, plan {pilot.PlanGood}×{pilot.PlanUnits} to {(pilot.Dest >= 0 ? world.Map.Ports[pilot.Dest].Name : "-")} · {ptSaveNote}");
                if (world.IsDocked) LeavePort();   // Esc's path, if the autopilot stayed
            }
            return;
        }
        ptPortFrames = 0;

        // A screen or the pause menu opened by the driver stays up for a few frames.
        if (ptUi.Length > 0)
        {
            if (--ptUiFrames > 0) return;
            switch (ptUi)
            {
                case "chart":
                    if (ptPins == 0 && chartScreen.IsOpen)
                    {
                        var at = chartScreen.Canvas.ToScreen(world.Ship.Pos + new Vec2(180, -120));
                        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, Device = RealInputFilter.Synthetic });
                        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at, Device = RealInputFilter.Synthetic });
                        if (chartScreen.NoteHasFocus) { chartScreen.SubmitNote($"playtest {playtestVoyage}"); ptPins++; }
                    }
                    PtTap("Chart");
                    if (chartScreen.IsOpen) PtFail("the chart key did not close the chart");
                    break;
                case "crew":
                    PtTap("Crew");
                    if (crewPanel.IsOpen) PtFail("the crew key did not close the crew panel");
                    break;
                case "pause":
                    PtTapKey(Godot.Key.Escape);
                    if (paused) PtFail("Esc did not resume");
                    break;
            }
            ptUi = "";
            return;
        }

        // Mid-voyage: Save and quit from the pause menu, then Resume from the title (next frame).
        if (!ptSaved && world.DaysSurvived >= playtestDays / 2 && !world.Ship.Foundering)
        {
            ptSaved = true;
            PtTapKey(Godot.Key.Escape);
            if (!paused || !pauseMenu.IsOpen) { PtFail("Esc did not open the pause menu"); return; }
            ptSavedHash = world.Hash();
            pauseMenu.PressSaveQuit();
            if (mode != Mode.Title) { PtFail("Save and quit did not reach the title"); return; }
            ptResumePending = true;
            return;
        }

        // The end of the test voyage: she goes down (the autopilot may still reach a harbour first).
        if (world.DaysSurvived >= playtestDays && !world.Ship.Foundering)
        {
            ptSinking = true;
            world.Ship.HullHp = 0;
            world.Ship.Foundering = true;
            world.Ship.Hourglass = 0.1;
        }

        // Now and then open a screen: the chart (drops a pin once), the crew panel, the pause menu.
        if (!ptSinking && world.DaysSurvived >= ptNextUi)
        {
            ptNextUi += 0.37;
            int pick = (int)(ptNextUi * 100) % 3;
            ptUi = pick == 0 ? "chart" : pick == 1 ? "crew" : "pause";
            ptUiFrames = 6;
            if (ptUi == "chart") { PtTap("Chart"); if (!chartScreen.IsOpen) PtFail("the chart key did not open the chart"); }
            else if (ptUi == "crew") { PtTap("Crew"); if (!crewPanel.IsOpen) PtFail("the crew key did not open the crew panel"); }
            else { PtTapKey(Godot.Key.Escape); if (!paused) PtFail("Esc did not pause"); }
            return;
        }

        // The autopilot's decision for this tick, through the keys.
        var ai = pilot.Tick(world);
        if (world.IsDocked) return;   // it docked (the port screen follows on the next frame)
        // The keyboard only knows hard over or centred: hold the key while the rudder is short of the autopilot's
        // angle and let go past it (it swings at 2.5/s held and centres at 3/s released), as a player taps the helm.
        double want = Math.Clamp(ai.Rudder, -1, 1), rudder = world.Ship.Rudder;
        int helm = want > 0.02 && rudder < want ? 1 : want < -0.02 && rudder > want ? -1 : 0;
        if ((helm == 1) != starboardHeld) PtKey(settings.KeyFor("Starboard"), helm == 1);
        if ((helm == -1) != portHeld) PtKey(settings.KeyFor("Port"), helm == -1);
        for (int i = 0; i < ai.SailDelta; i++) PtTap("SailUp");
        for (int i = 0; i < -ai.SailDelta; i++) PtTap("SailDown");
        if (ai.FirePort) PtTap("FirePort");
        if (ai.FireStarboard) PtTap("FireStarboard");
        if (ai.Order is >= 1 and <= 4) PtTap("Order" + ai.Order);
        if (ai.ToggleLantern) PtTap("Lantern");
        if (ai.Action) PtTap("Dock");
    }

    string Cargo() => string.Join(",", Goods.All.Where(d => world.Player.Units(d.Id) > 0 && d.Id is not (Good.Provisions or Good.Munitions or Good.Timber)).Select(d => $"{d.Key}×{world.Player.Units(d.Id)}")) is { Length: > 0 } c ? c : "-";

    void PlaytestFinish()
    {
        if (ptDone >= 0) return;
        foreach (var e in ErrorCounter.Recent) GD.Print($"PLAYTEST error: {e}");
        bool pass = ptFailures.Count == 0 && ErrorCounter.Errors == 0;
        GD.Print(pass ? $"PLAYTEST PASS ({playtestVoyage} voyages)" : $"PLAYTEST FAIL ({ptFailures.Count} failures, {ErrorCounter.Errors} errors)");
        ptDone = 0;
    }
}

/// <summary>Counts engine and script errors (unhandled C# exceptions in callbacks arrive here too) for the self-test and the playtest.</summary>
public partial class ErrorCounter : Logger
{
    static ErrorCounter? installed;
    static int errors, warnings;
    public static int Errors => errors;
    public static int Warnings => warnings;
    public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Recent = new();

    public static void Install()
    {
        if (installed != null) return;
        installed = new ErrorCounter();
        OS.AddLogger(installed);
    }

    public static void Uninstall()
    {
        if (installed == null) return;
        OS.RemoveLogger(installed);
        installed = null;
    }

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (errorType == (int)ErrorType.Warning) { Interlocked.Increment(ref warnings); return; }
        Interlocked.Increment(ref errors);
        if (Recent.Count < 20) Recent.Enqueue($"{file}:{line} {function}: {rationale} {code}".Trim());
    }
}
