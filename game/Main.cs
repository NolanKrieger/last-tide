using System.Globalization;
using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Owns the run and turns input into sim commands. The sim advances once per physics tick (30/s);
/// rendering interpolates between the last two ticks so motion is smooth at any frame rate.
///
/// Command-line extras (after `--`): `--seed=N`, `--hull=id`, `--sail=0..3` (start with canvas set),
/// `--wind=FROM,SPEED` (compass degrees the wind comes from, m/s; pinned, no wander),
/// `--zoom=Z`, `--screenshot=path.png [--frames=N]` saves the screen after N frames and quits,
/// `--fps` prints the measured frame rate after `--frames`, `--selftest` drives the controls and exits 0 or 1,
/// `--playtest=DAYS[,VOYAGES]` plays whole voyages through this layer (Main.Playtest.cs), `--uiscale=S`.
/// Every way out goes through <see cref="QuitGame"/> / <see cref="ExitGame"/> (the suspend save, then a clean exit).
/// </summary>
public partial class Main : Node2D
{
    public const float MinZoom = 0.3f, MaxZoom = 2.5f;

    World world = null!;
    Camera2D camera = null!;
    SeaLayer sea = null!;
    ChartView chart = null!;
    MarksView marks = null!;
    LifeView life = null!;
    WhirlpoolView whirlpools = null!;
    IceView ice = null!;
    FishView fish = null!;
    FogView fog = null!;
    WakeView wake = null!;
    ShipView shipView = null!;
    Hud hud = null!;
    ChartScreen chartScreen = null!;
    PortScreen portScreen = null!;
    ShipCard shipCard = null!;
    CrewPanel crewPanel = null!;
    FleetView fleet = null!;
    EffectsView effects = null!;
    WeatherView weather = null!;
    MonsterView monsters = null!;
    TitleScreen title = null!;
    LogbookScreen logbook = null!;
    PauseMenu pauseMenu = null!;
    Hints hintsView = null!;
    Profile profile = null!;
    OptionsScreen options = null!;
    Settings settings = null!;
    Audio audio = null!;
    public Audio Sound => audio;
    public WeatherView Weather => weather;
    public static FontFile Fell = null!;
    enum Mode { Title, Run }
    Mode mode = Mode.Run;
    bool suspendEnabled;          // real voyages autosave on dock and may Save & Quit; debug runs never touch the profile
    double overTime;
    bool viewsBuilt;

    bool portHeld, starboardHeld, paused;
    bool aimPort, aimStarboard, firePortQueued, fireStarboardQueued, lanternQueued, spyglass, actionQueued;
    Vector2 mouse;
    public bool SpyglassHeld => spyglass;
    int sailQueue;
    (Vec2 Pos, double Heading) prevPose, curPose;
    float zoomTarget = 1.25f;
    Vector2 camPos;
    float renderTime;

    string? screenshotPath;
    int screenshotFrames = 120, frame;
    bool selfTest, reportFps, openChart, revealAll, sparring, quiet, stormAtStart;
    Vec2? atArg;   // --at=x,y: start the voyage at this point (metres), for screenshots of far waters
    int cartographerArg = -1;   // --cartographer[=tier]: a debug run starts with one aboard (--chart implies a green one)
    double skipSeconds;
    string? regionKey, monsterKey;
    bool treasureAtStart, dockCove, deliverAtStart, hoverArg, showLogbook, pauseAtStart, debugHints, muteArg, soundArg, broadsideArg, flotsamArg, fishArg;
    bool crewAtStart, optionsAtStart;   // --crew, --pause=options: review captures of the crew panel and Options over the pause menu
    HashSet<string> off = new();   // --off=fog,weather,sea,marks,fleet,effects,hud,wake,monsters,chart: perf bisection
    int fleetNear;                 // --fleet=N: N extra ships within 1.2 km of the player (the GDD §17 perf target)
    string? titlePage;
    int startPage;
    float uiScaleArg;
    bool titleLaunched;   // the real game (no arguments, or --title): developer logging stays quiet
    double fpsClock;
    string? lastStandArg, noticeArg;   // --laststand[=water], --notice=KEY: HUD captures
    double[]? damageArg;           // --damage=HULL,WATER[,LEAKS]: hull fraction, water %, open leaks

    public World World => world;
    public bool Paused => paused || exiting || mode == Mode.Title || logbook.IsOpen || options.IsOpen || chartScreen.IsOpen || world.IsDocked || crewPanel.IsOpen;
    public TitleScreen Title => title;
    public LogbookScreen Logbook => logbook;
    public PauseMenu Pause => pauseMenu;
    public Hints HintCards => hintsView;
    public Profile ProfileStore => profile;
    public OptionsScreen Options => options;
    public Settings Config => settings;
    public ChartScreen Chart => chartScreen;
    public PortScreen Port => portScreen;
    public CrewPanel Crew => crewPanel;
    public Camera2D Camera => camera;
    public Hud HudLayer => hud;
    public float ZoomTarget => zoomTarget;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        int seed = 7, sail = 0;
        string hull = "sloop";
        var preset = Preset.RoughSeas;
        (double from, double speed)? wind = null;
        // Debug arguments: a malformed value is ignored with a warning instead of throwing out of _Ready (which left every
        // node unset and flooded the log with an exception each frame).
        static int ArgInt(string a, int fallback)
        {
            if (int.TryParse(a[(a.IndexOf('=') + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            GD.PushWarning($"Ignored a malformed argument: {a}");
            return fallback;
        }
        static double ArgNum(string a, double fallback)
        {
            if (double.TryParse(a[(a.IndexOf('=') + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)) return v;
            GD.PushWarning($"Ignored a malformed argument: {a}");
            return fallback;
        }
        foreach (var a in args)
        {
            if (a.StartsWith("--seed=")) seed = ArgInt(a, seed);
            else if (a.StartsWith("--hull=")) hull = a["--hull=".Length..];
            else if (a.StartsWith("--sail=")) sail = Math.Clamp(ArgInt(a, sail), 0, 3);
            else if (a.StartsWith("--zoom=")) zoomTarget = Mathf.Clamp((float)ArgNum(a, zoomTarget), MinZoom, MaxZoom);
            else if (a.StartsWith("--screenshot=")) screenshotPath = a["--screenshot=".Length..];
            else if (a.StartsWith("--frames=")) screenshotFrames = ArgInt(a, screenshotFrames);
            else if (a.StartsWith("--wind="))
            {
                var parts = a["--wind=".Length..].Split(',');
                if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var from))
                    wind = (from, parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sp) ? sp : Tuning.StandardWind);
                else GD.PushWarning($"Ignored a malformed argument: {a}");
            }
            else if (a == "--selftest") selfTest = true;
            else if (a == "--fps") reportFps = true;
            else if (a == "--chart") openChart = true;
            else if (a == "--cartographer" || a.StartsWith("--cartographer=")) cartographerArg = a.Contains('=') ? Math.Clamp(ArgInt(a, 0), 0, 2) : 0;
            else if (a == "--reveal") revealAll = true;
            else if (a == "--sparring") sparring = true;
            else if (a.StartsWith("--preset=")) preset = a["--preset=".Length..] switch { "calm" => Preset.CalmSeas, "tempest" => Preset.Tempest, _ => Preset.RoughSeas };
            else if (a == "--quiet") quiet = true;
            else if (a.StartsWith("--time=")) skipSeconds = ArgNum(a, skipSeconds);
            else if (a == "--storm") stormAtStart = true;
            else if (a.StartsWith("--at="))
            {
                var xy = a["--at=".Length..].Split(',');
                if (xy.Length == 2 && double.TryParse(xy[0], System.Globalization.CultureInfo.InvariantCulture, out double ax)
                    && double.TryParse(xy[1], System.Globalization.CultureInfo.InvariantCulture, out double ay)) atArg = new Vec2(ax, ay);
            }
            else if (a == "--treasure") treasureAtStart = true;
            else if (a == "--fish") fishArg = true;   // furled with a full crew and the lines out: fishing review
            else if (a == "--deliver") deliverAtStart = true;
            else if (a == "--broadside") broadsideArg = true;
            else if (a == "--flotsam") flotsamArg = true;
            else if (a == "--hover") hoverArg = true;
            else if (a == "--dock=cove") dockCove = true;
            else if (a == "--title") titlePage = "home";
            else if (a.StartsWith("--title=")) titlePage = a["--title=".Length..];
            else if (a == "--logbook") showLogbook = true;
            else if (a == "--pause") pauseAtStart = true;
            else if (a == "--pause=options") { pauseAtStart = true; optionsAtStart = true; }
            else if (a == "--crew") crewAtStart = true;
            else if (a == "--hints") debugHints = true;
            else if (a == "--mute") muteArg = true;
            else if (a == "--novsync") DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            else if (a.StartsWith("--fleet=")) fleetNear = ArgInt(a, fleetNear);
            else if (a.StartsWith("--off=")) off = a["--off=".Length..].Split(',').ToHashSet();
            else if (a == "--sound") soundArg = true;
            else if (a.StartsWith("--monster=")) monsterKey = a["--monster=".Length..];
            else if (a.StartsWith("--page=")) startPage = ArgInt(a, startPage);
            else if (a.StartsWith("--region=")) regionKey = a["--region=".Length..];
            else if (a.StartsWith("--uiscale=")) uiScaleArg = (float)ArgNum(a, uiScaleArg);
            else if (a.StartsWith("--playtest=")) ParsePlaytest(a["--playtest=".Length..]);
            else if (a == "--laststand" || a.StartsWith("--laststand=")) lastStandArg = a.Contains('=') ? a[(a.IndexOf('=') + 1)..] : "hull";
            else if (a.StartsWith("--notice=")) noticeArg = a["--notice=".Length..];
            else if (a.StartsWith("--damage=")) damageArg = a["--damage=".Length..].Split(',').Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
        }
        Fell = GD.Load<FontFile>("res://assets/fonts/IMFellEnglish-Roman.ttf");
        GetTree().AutoAcceptQuit = false;   // closing the window goes through QuitGame (suspend save, then a clean exit)
        Platform.Init();
        if (!Hulls.Exists(hull))
        {
            GD.PushError($"unknown hull '{hull}'");
            hull = "sloop";
        }

        if (Playtesting) playtestSeed = seed;
        bool titleLaunch = (args.Length == 0 || titlePage != null) && !Playtesting;
        titleLaunched = titleLaunch && !selfTest;
        if (selfTest || Playtesting)
        {
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, new RealInputFilter());   // only the test's own events drive it
            ErrorCounter.Install();
        }
        // Debug runs (every argument but --title) never touch the real profile or suspend save: --pause and --logbook
        // write to it (Save and quit, the recap), so they get the scratch profile --hints already used.
        profile = Profile.Load(selfTest ? "user://selftest/" : titleLaunch ? "user://" : "user://debug/");
        if (debugHints) { profile.HintsSeen.Clear(); profile.Save(); }
        // Debug runs also keep default keys (README): they read the scratch settings, never the player's.
        settings = Settings.Load(selfTest ? "user://selftest/" : titleLaunch ? "user://" : "user://debug/");
        if (selfTest) { settings = new Settings(); settings = Settings.Load("user://selftest/"); settings.ResetKeys(); settings.UiScale = 1; settings.Colorblind = false; settings.Fullscreen = false; settings.Width = 1600; settings.Height = 900; settings.Save(); }
        Settings.Current = settings;
        audio = new Audio();
        AddChild(audio);
        audio.Init(mute: muteArg || (!titleLaunch && !soundArg));   // debug runs and the self-test stay silent unless asked
        options = new OptionsScreen();
        AddChild(options);
        options.Init(settings, Fell);
        options.Changed += ApplySettings;
        options.Closed += () => { if (mode == Mode.Title) title.Show(title.Page); };
        if (selfTest) { profile.DeleteSuspend(); profile = new Profile(); profile = Profile.Load("user://selftest/"); ResetSelfTestProfile(); }

        // The persistent screens live outside the run so a new voyage can rebuild everything beneath them.
        title = new TitleScreen();
        AddChild(title);
        title.Init(profile, Fell);
        title.SetSail += StartVoyage;
        title.ResumePressed += ResumeVoyage;
        title.QuitPressed += QuitGame;
        title.OptionsPressed += () => options.Open();
        logbook = new LogbookScreen();
        AddChild(logbook);
        logbook.Init(Fell);
        logbook.NewVoyage += () => { logbook.Close(); ShowTitle("voyage"); };
        logbook.ToTitle += () => { logbook.Close(); ShowTitle("home"); };
        pauseMenu = new PauseMenu();
        AddChild(pauseMenu);
        pauseMenu.Init(Fell);
        pauseMenu.ResumePressed += () => SetPaused(false, "the Resume button");
        pauseMenu.SaveQuitPressed += SaveAndQuit;
        pauseMenu.QuitPressed += QuitGame;
        pauseMenu.OptionsPressed += () => options.Open();
        hintsView = new Hints();
        AddChild(hintsView);
        hintsView.Init(profile, Fell);

        bool titleMode = args.Length == 0 || titlePage != null || Playtesting;   // the playtest starts every voyage from the title
        // Debug runs keep the 1600×900 window and default keys so screenshots stay comparable; real play applies the saved settings.
        if (titleMode || selfTest) ApplySettings(); else { Ink.SetPalette(false); }
        if (uiScaleArg > 0) GetWindow().ContentScaleFactor = Math.Clamp(uiScaleArg, 0.5f, 3f);   // --uiscale=1.5: any screen at any UI scale
        if (titleMode)
        {
            // A fresh sea as the backdrop; the title holds it still.
            world = World.NewRun(new Random().Next(), "sloop", Preset.RoughSeas, populate: true);
            world.DirectorEnabled = false;
            prevPose = curPose = (world.Ship.Pos, world.Ship.Heading);
            audio.Bind(world);
            BuildViews();
            ShowTitle(titlePage ?? "home");
            return;
        }

        world = World.NewRun(seed, hull, preset, populate: !quiet);
        if (quiet) world.DirectorEnabled = false;
        if (wind is { } w)
            world.Wind.SetFixed(Angles.Wrap(Angles.FromCompassDeg(w.from) + Math.PI), w.speed);
        if (sail > 0)
        {
            world.Ship.SailTarget = sail;
            world.Ship.SailFraction = Tuning.SailFraction[sail];
        }
        if (revealAll)
            foreach (var r in world.Map.Regions) world.Reveal.PaintRegion(world.Map, r.Type);
        // The chart opens only with a cartographer aboard (Nolan, 2026-09-27), so --chart brings a green one.
        if (cartographerArg >= 0 || openChart)
            world.Player.Officers.Add(new Officer { Type = OfficerType.Cartographer, Tier = Math.Max(0, cartographerArg) });
        if (regionKey != null)
        {
            var def = RegionDef.All.FirstOrDefault(r => r.Key == regionKey);
            if (def != null)
            {
                var regionSeed = world.Map.RegionOf(def.Id).Seed;
                world.Ship.Pos = world.SeaPointNear(regionSeed, 0, 300);
                world.Ship.Vel = Vec2.Zero;
            }
        }
        if (atArg is { } at)
        {
            world.Ship.Pos = at;
            world.Ship.Vel = Vec2.Zero;
        }
        for (int i = 0; i < (int)(skipSeconds * Tuning.TicksPerSecond); i++)
            world.Tick(new ShipInput(0, 0));
        if (monsterKey != null && Enum.TryParse<MonsterType>(monsterKey, true, out var mt))
        {
            world.MonstersEnabled = true;
            world.SpawnMonster(mt);
        }
        if (fishArg)
        {
            world.Ship.SailTarget = 0;
            world.Ship.SailFraction = 0;
            world.Ship.Crew = world.Ship.Hull.CrewMax;
            world.Tick(new ShipInput(0, 0, Action: true));
        }
        if (treasureAtStart)
        {
            var t = world.Map.Treasures.First(t => !t.Dug);
            world.GiveBottleMap(t.Id);
            world.Ship.Pos = t.DigRing;
            world.Ship.Vel = Vec2.Zero;
            world.Ship.SailTarget = 0;
            world.Ship.SailFraction = 0;
        }
        if (dockCove)
        {
            var cove = world.Map.Ports.First(p => p.Secret);
            cove.Discovered = true;
            world.Ship.Pos = cove.Harbor;
            world.Ship.Vel = Vec2.Zero;
            world.Player.Gold = 2000;
        }
        if (stormAtStart)
            world.Weather.Storms.Add(new StormCell { Pos = world.Ship.Pos + Vec2.FromAngle(world.Ship.Heading) * 150, Radius = 350, Strength = 0.8, Life = 300, Age = 20 });
        if (fleetNear > 0)
        {
            // Real traffic (merchants, patrols, raiders with their own captains), not hunters: sim-combat R-08.
            for (int i = 0; i < fleetNear; i++)
                world.SpawnTraffic(i % 3 == 0 ? Role.Raider : i % 3 == 1 ? Role.Patrol : Role.Merchant, world.SeaPointNear(world.Ship.Pos, 250, 1200));
        }
        if (sparring)
        {
            world.Ship.Cannons = 4;
            world.Ship.Crew = 8;
            world.Player.Cargo[(int)Good.Munitions] = 60;
            world.SpawnSparring();
        }
        if (damageArg != null)
        {
            world.Ship.HullHp = world.Ship.MaxHp * Math.Clamp(damageArg[0], 0.01, 1);
            if (damageArg.Length > 1) world.Ship.Water = Math.Clamp(damageArg[1], 0, 99);
            if (damageArg.Length > 2) world.Ship.Leaks = (int)damageArg[2];
            world.Ship.Loaded[1] = false;
            world.Ship.Reload[1] = world.Ship.ReloadTime * 0.4;
        }
        if (lastStandArg != null)
        {
            // Out of the home harbour's ring (it would take her straight in), then hull or water gives out.
            world.Ship.Pos = world.SeaPointNear(world.Map.StartPort.Harbor, 380, 520);
            world.Ship.Vel = Vec2.Zero;
            if (lastStandArg == "water") { world.Ship.Water = 100; world.Ship.Leaks = 3; world.Ship.HullHp = world.Ship.MaxHp * 0.5; }   // under the flood line, or she drains at once
            else world.Ship.HullHp = 0;
        }
        if (deliverAtStart) Deliver();
        if (flotsamArg) world.SpawnDrift(new Rng((ulong)seed + 99));   // a barrel adrift ahead of her, for screenshots
        if (noticeArg != null) world.Notices.Enqueue(noticeArg);
        prevPose = curPose = (world.Ship.Pos, world.Ship.Heading);
        suspendEnabled = selfTest;
        hintsView.Enabled = selfTest || debugHints;
        hintsView.Bind(world);
        audio.Bind(world);
        BuildViews();
        if ((args.Contains("--dock") || dockCove) && TryDock()) portScreen.Show(startPage);
        if (pauseAtStart) { suspendEnabled = true; SetPaused(true, "--pause"); }
        if (optionsAtStart) options.Open();
        if (crewAtStart) crewPanel.Toggle();
        if (showLogbook)
        {
            // A real loss, in open water: in the start harbour she was rescued ("By a Hair") and the recap showed a
            // ship still afloat, with the port opening behind it.
            world.Ship.Pos = OpenWater(400);
            world.Ship.Vel = Vec2.Zero;
            prevPose = curPose = (world.Ship.Pos, world.Ship.Heading);
            camPos = Ink.V(world.Ship.Pos);
            world.Ship.HullHp = 0;
            world.Ship.Foundering = true;
            world.Ship.Hourglass = 0.05;
            for (int i = 0; i < 10 && !world.RunOver; i++) world.Tick(new ShipInput(0, 0));
            FinishRun();
        }

        if (selfTest)
            CallDeferred(MethodName.RunSelfTest);
    }

    void ResetSelfTestProfile()
    {
        profile.Bests.Clear();
        profile.Achievements.Clear();
        profile.Cosmetics.Clear();
        profile.HintsSeen.Clear();
        profile.Voyages = 0;
        profile.LastShipName = "";
        profile.DeleteSuspend();
        profile.Save();
    }

    /// <summary>Every node that shows or drives the current world. Rebuilt for each voyage.</summary>
    void BuildViews()
    {
        viewsBuilt = true;
        var coast = new CoastField(world.Map);
        sea = new SeaLayer();
        AddChild(sea);
        sea.Init(world, coast);
        chart = new ChartView();
        chart.Init(world.Map, Fell, coast);
        AddChild(chart);
        marks = new MarksView();
        marks.Init(world, Fell, coast);
        AddChild(marks);
        life = new LifeView();
        life.Init(world, coast, chart);
        AddChild(life);
        whirlpools = new WhirlpoolView();
        whirlpools.Init(world);
        AddChild(whirlpools);
        ice = new IceView();
        ice.Init(world);
        AddChild(ice);
        fish = new FishView();
        fish.Init(world);
        AddChild(fish);
        fog = new FogView();
        fog.Init(world.Reveal, world.Seed);
        sea.BindReveal(fog.SeaTexture);   // the charted mask with her live sight stamped in
        AddChild(fog);
        wake = new WakeView { ZIndex = 8 };
        AddChild(wake);
        fleet = new FleetView();
        fleet.Init(world);
        AddChild(fleet);
        fleet.Sync();
        shipView = new ShipView { ZIndex = 10 };
        shipView.Init(world.Ship, world.Player.Loadout);
        AddChild(shipView);
        effects = new EffectsView();
        effects.Init(world);
        AddChild(effects);
        weather = new WeatherView();
        weather.Init(world, sea, coast);
        AddChild(weather);
        monsters = new MonsterView();
        monsters.Init(world);
        AddChild(monsters);

        camera = new Camera2D { Zoom = Vector2.One * zoomTarget };
        camPos = Ink.V(world.Ship.Pos);
        camera.Position = camPos;
        AddChild(camera);
        camera.MakeCurrent();

        hud = new Hud();
        AddChild(hud);
        hud.Init(world);
        hud.KeyLabel = a => Settings.Label(settings.KeyFor(a));
        hintsView.Hud = hud;
        shipCard = new ShipCard();
        AddChild(shipCard);
        shipCard.Init(world, Fell);
        chartScreen = new ChartScreen();
        AddChild(chartScreen);
        chartScreen.Init(world, Fell, fog.Texture);
        chartScreen.VisibilityChanged += UpdateWorldShown;
        if (openChart) chartScreen.Toggle();
        hud.KeyLine = KeyLine();
        portScreen = new PortScreen();
        AddChild(portScreen);
        portScreen.Init(world, Fell);
        portScreen.CastOff += LeavePort;
        portScreen.VisibilityChanged += UpdateWorldShown;
        crewPanel = new CrewPanel();
        AddChild(crewPanel);
        crewPanel.Init(world, Fell);
        wake.InkColor = Cosmetic.Wake(world.Player.Cosmetic("wake"));
        camPos = Ink.V(world.Ship.Pos);
        renderTime = 0;
        if (off.Count > 0)
        {
            sea.Visible = !off.Contains("sea");
            chart.Visible = !off.Contains("chart");
            marks.Visible = !off.Contains("marks");
            fog.Visible = !off.Contains("fog");
            fog.SetProcess(!off.Contains("fog"));
            wake.Visible = !off.Contains("wake");
            fleet.Visible = !off.Contains("fleet");
            effects.Visible = !off.Contains("effects");
            weather.Visible = !off.Contains("weather");
            monsters.Visible = !off.Contains("monsters");
            hud.Visible = !off.Contains("hud");
        }
    }

    /// <summary>The chart and the port ledger are full pages: while one is up the sea and HUD beneath are not drawn at all.</summary>
    void UpdateWorldShown() => SetWorldShown(!(chartScreen.IsOpen || (IsInstanceValid(portScreen) && portScreen.IsOpen)));

    void SetWorldShown(bool on)
    {
        foreach (var (node, key) in new (CanvasItem, string)[] { (chart, "chart"), (marks, "marks"), (fog, "fog"), (wake, "wake"), (fleet, "fleet"), (shipView, "ship"), (effects, "effects"), (weather, "weather"), (monsters, "monsters") })
            node.Visible = on && !off.Contains(key);
        sea.Visible = on && !off.Contains("sea");
        hud.Visible = on && !off.Contains("hud") && mode == Mode.Run;
    }

    void FreeViews()
    {
        if (!viewsBuilt) return;
        foreach (Node n in new Node[] { sea, chart, marks, life, whirlpools, ice, fish, fog, wake, fleet, shipView, effects, weather, monsters, camera, hud, shipCard, chartScreen, portScreen, crewPanel })
        {
            RemoveChild(n);
            n.QueueFree();
        }
        viewsBuilt = false;
    }

    // ---- the run wrapper (GDD §3) ----

    void ShowTitle(string page)
    {
        mode = Mode.Title;
        paused = false;
        ReleaseHeld();
        pauseMenu.Close();
        hud.Visible = false;
        hintsView.Enabled = false;   // a hint card left from the voyage must not sit on the title
        if (page == "options") { title.Show("home"); options.Open(); }
        else title.Show(page);
    }

    /// <summary>Applies the settings to the window, palette, buses, key line and hints.</summary>
    public void ApplySettings()
    {
        settings.Apply(GetWindow());
        audio.ApplyMute();   // the saved master volume must not unmute a muted run (the self-test, --mute, debug runs)
        Settings.Current = settings;
        if (viewsBuilt)
        {
            chart.RedrawAll();
            hud.KeyLine = KeyLine();
        }
        hintsView.ShowHints = settings.ShowHints;
    }

    string KeyLine()
    {
        string K(string a) => Settings.Label(settings.KeyFor(a));
        return Text.Get("HUD_KEYS", K("SailUp"), K("SailDown"), K("Port"), K("Starboard"), K("FirePort"), K("FireStarboard"),
            K("Crew"), K("Lantern"), K("Dock"), K("Chart"));
    }

    void BeginRun(World w)
    {
        FreeViews();
        world = w;
        prevPose = curPose = (world.Ship.Pos, world.Ship.Heading);
        BuildViews();
        mode = Mode.Run;
        paused = false;
        ReleaseHeld();   // nothing held or queued in the last voyage steers this one
        runRecorded = false;
        overTime = 0;
        lastSailLevel = -1;
        suspendEnabled = true;
        hintsView.Enabled = true;
        hintsView.Bind(world);
        audio.Bind(world);
        title.Close();
        logbook.Close();
        hud.Visible = true;
    }

    public void StartVoyage(Preset preset, string name, string[] loadout)
    {
        var w = World.NewRun(nextVoyageSeed ?? new Random().Next(), "sloop", preset, populate: true);   // a seed only in the playtest
        nextVoyageSeed = null;
        // The voyage knows what the profile already owns, so a dig's cosmetic roll (World.UnlockCosmetic) draws from
        // what is still locked instead of "unlocking" a colour the player has (sim-trade R-09).
        foreach (var c in profile.Cosmetics) if (World.CosmeticKeys.Contains(c)) w.Player.Cosmetics.Add(c);
        w.Player.ShipName = name;
        Array.Copy(loadout, w.Player.Loadout, Math.Min(loadout.Length, w.Player.Loadout.Length));
        BeginRun(w);
        // She opens alongside the home quay on the ledger (Nolan, 2026-09-28): the board and the tavern are the first
        // minute's work. The clock does not run in port.
        TryDock();
    }

    public void ResumeVoyage()
    {
        var json = profile.ReadSuspend();
        World? w = null;
        try { if (json != null) w = World.LoadJson(json); }
        catch (Exception e) { GD.PushWarning($"suspend save unreadable, set aside: {e.Message}"); }
        if (w == null)
        {
            // An unreadable save is moved aside (kept for diagnosis) so the title stops offering it.
            profile.SetAsideSuspend();
            if (mode == Mode.Title) title.Show("home");
            return;
        }
        profile.DeleteSuspend();   // resuming consumes the save; the next dock writes a fresh one
        BeginRun(w);
    }

    public void SaveAndQuit()
    {
        if (!suspendEnabled || world.RunOver) return;
        if (!WriteSuspend()) return;   // never leave the voyage for the title without its save
        profile.RecordRun(world, finished: false);
        ShowTitle("home");
    }

    /// <summary>Writes the suspend save. A failure (disk, a state that will not serialise) is logged, never thrown into input handling.</summary>
    bool WriteSuspend()
    {
        try
        {
            return profile.WriteSuspend(world.SaveJson());
        }
        catch (Exception e)
        {
            GD.PushError($"suspend save failed: {e}");
            return false;
        }
    }

    /// <summary>The ship is gone: fold the run into the profile and open the logbook page.</summary>
    void FinishRun()
    {
        if (logbook.IsOpen) return;
        RecordFinished();
        ReleaseHeld();
        logbook.Open(world, recordedBest, recordedAchievements);
    }

    bool runRecorded, recordedBest;
    List<string> recordedAchievements = new();

    /// <summary>Folds a lost voyage into the profile once (the recap, or the window closing before it) and drops the suspend save.</summary>
    void RecordFinished()
    {
        if (runRecorded) return;
        runRecorded = true;
        (recordedBest, recordedAchievements, _) = suspendEnabled || selfTest || showLogbook ? profile.RecordRun(world, finished: true) : (false, new List<string>(), new List<string>());
        if (suspendEnabled) profile.DeleteSuspend();
    }

    /// <summary>
    /// Quitting mid-voyage keeps it (GDD §19 "Quitting mid-run: suspend save"): the pause menu's Quit, Q while paused
    /// and closing the window all write the suspend save first. A ship already lost is logged instead, so the last
    /// dock's save cannot bring her back.
    /// </summary>
    void SuspendOnQuit()
    {
        if (!viewsBuilt || mode != Mode.Run || !suspendEnabled) return;
        if (world.RunOver) { RecordFinished(); return; }
        if (WriteSuspend()) profile.RecordRun(world, finished: false);
    }

    void QuitGame()
    {
        try { SuspendOnQuit(); }
        finally { ExitGame(0); }
    }

    bool exiting, exitHeldForTest;
    int exitRequested;

    /// <summary>
    /// Every way out: silence the audio, give the mixer a few frames to release its playbacks (a stream still queued in
    /// the audio server at shutdown is reported as "ObjectDB instances were leaked at exit"), then quit. The world stands
    /// still meanwhile (<see cref="Paused"/>), so nothing happens after the suspend save.
    /// </summary>
    async void ExitGame(int code)
    {
        if (exitHeldForTest) { exitRequested++; return; }   // the self-test's close-button check (GC-4)
        if (exiting) return;
        exiting = true;
        try
        {
            audio.StopAll();
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        finally { GetTree().Quit(code); }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) QuitGame();   // the window's close button (auto-accept is off: Main quits)
        else if (what == NotificationWMMouseExit) mouse = new Vector2(-1e5f, -1e5f);   // off the window, no ship is under the pointer
        // Godot sends no key-up to a window that has lost focus: a key held while alt-tabbing away stayed held.
        else if (what is (int)NotificationApplicationFocusOut or (int)NotificationWMWindowFocusOut) LetGoAll();
    }

    /// <summary>Where a key event came from, for the developer-run pause log: a real keyboard or a synthetic (self-test) event.</summary>
    static string KeySource(InputEventKey k) => $"{k.Keycode} ({(k.Device == InputEvent.DeviceIdKeyboard ? "keyboard" : $"device {k.Device}, synthetic")})";

    /// <summary>Lets go of held keys only (focus lost): the helm centres, an aimed broadside stands down unfired, the glass comes down.</summary>
    void LetGoAll() => portHeld = starboardHeld = aimPort = aimStarboard = spyglass = false;

    public override void _ExitTree()
    {
        ErrorCounter.Uninstall();
        Platform.Current.Shutdown();
    }

    /// <summary>Lets go of every held key and drops every queued order (a new voyage, the title, a screen opening).</summary>
    void ReleaseHeld()
    {
        portHeld = starboardHeld = aimPort = aimStarboard = spyglass = false;
        firePortQueued = fireStarboardQueued = lanternQueued = actionQueued = false;
        sailQueue = 0;
    }

    /// <summary>While a screen is up nothing new starts, but a key let go is let go: the helm centres and an aimed broadside is stood down, not fired.</summary>
    void LetGo(InputEvent e)
    {
        if (e is InputEventKey { Pressed: false } k)
            switch (settings.ActionOf(k.Keycode))
            {
                case "Port": portHeld = false; break;
                case "Starboard": starboardHeld = false; break;
                case "FirePort": aimPort = false; break;
                case "FireStarboard": aimStarboard = false; break;
            }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }) spyglass = false;
    }

    bool NoticesCovered => mode == Mode.Title || logbook.IsOpen || options.IsOpen || portScreen.IsOpen || chartScreen.IsOpen;

    bool ScreenUp => mode == Mode.Title || options.IsOpen || logbook.IsOpen || (viewsBuilt && (portScreen.IsOpen || chartScreen.IsOpen || crewPanel.IsOpen));

    /// <param name="why">Logged in developer runs (the self-test and debug arguments) so a pause nobody asked for names its source.</param>
    void SetPaused(bool on, string why)
    {
        if (!titleLaunched) GD.Print($"SetPaused({on}) tick={world.Ticks} by {why}");
        paused = on;
        if (on) pauseMenu.Open(suspendEnabled && !world.RunOver, world); else pauseMenu.Close();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Playtesting) PlaytestStep();
        if (Paused) return;
        double rudder = (starboardHeld ? 1 : 0) - (portHeld ? 1 : 0);
        var mouseWorld = Ink.M(GetCanvasTransform().AffineInverse() * mouse);
        // The glass is logged input (it inks the chart); its bearing is rounded so a steady glass is not a new entry every tick.
        double glassDir = spyglass ? Math.Round((mouseWorld - world.Ship.Pos).Angle, 2) : 0;
        if (broadsideArg && world.Ship.Loaded[1] && world.Ticks > 30) fireStarboardQueued = true;   // `--broadside`: hit-feedback review
        var input = new ShipInput(rudder, sailQueue, firePortQueued, fireStarboardQueued, lanternQueued, actionQueued, spyglass, glassDir);
        actionQueued = false;
        sailQueue = 0;
        firePortQueued = fireStarboardQueued = lanternQueued = false;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        long ticksBefore = world.Ticks;
        world.Tick(input);
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        tickMs += ms; tickMax = Math.Max(tickMax, ms); tickCount++;
        if (ms > 5)
        {
            tickSpikes++;
            // Under --fps, name the first spikes (sim time and GC count) so a hitch can be traced to its phase.
            if (reportFps && tickSpikes <= 30) GD.Print($"tick spike {ms:0.0} ms at t={world.Time:0.0} s (gen2 GCs {GC.CollectionCount(2)})");
        }
        prevPose = curPose;
        curPose = (world.Ship.Pos, world.Ship.Heading);
        wake.Record(world.Ship);
        fleet.Sync();
        monsters.Sync();
        // World.Events holds the last tick's events until the next tick runs; once she is lost (or in port) no tick
        // runs, so they are consumed only when this call actually advanced the clock.
        if (world.Ticks != ticksBefore)
        {
            effects.Consume();
            audio.Consume();
            hud.Callouts.Consume(world);
            foreach (var ev in world.Events)
                if (ev.ShipId == world.Ship.Id && ev.Type is CombatEventType.Hit or CombatEventType.Ram) shipView.Flash();
                else if (ev.Type == CombatEventType.Ram && ev.Pos.DistanceTo(world.Ship.Pos) < world.Ship.Hull.Length * 2) shipView.Flash();
        }
        if (world.Ship.SailTarget != lastSailLevel)
        {
            if (lastSailLevel >= 0) { shipView.SailSnap(); hud.PulseSail(); }
            lastSailLevel = world.Ship.SailTarget;
        }
    }

    double tickMs, tickMax, procMs, procMax;
    int tickCount, tickSpikes, procSpikes;
    double gpuMs, cpuRenderMs;
    int lastSailLevel = -1;

    public override void _Process(double delta)
    {
        Platform.Current.Pump();
        long p0 = System.Diagnostics.Stopwatch.GetTimestamp();
        ProcessFrame(delta);
        double pms = (System.Diagnostics.Stopwatch.GetTimestamp() - p0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        procMs += pms; procMax = Math.Max(procMax, pms);
        if (pms > 5) procSpikes++;
    }

    void ProcessFrame(double delta)
    {
        // The port screen follows the world: the sim docks a foundering ship itself (a harbour rescue), and a
        // suspend save written at a dock resumes in port.
        if (mode == Mode.Run && world.IsDocked && !portScreen.IsOpen) OnDocked();
        else if (mode == Mode.Run && !world.IsDocked && portScreen.IsOpen) OnCastOff();
        float alpha = Paused ? 1f : (float)Engine.GetPhysicsInterpolationFraction();
        var pos = Vec2.Lerp(prevPose.Pos, curPose.Pos, alpha);
        double heading = Angles.LerpAngle(prevPose.Heading, curPose.Heading, alpha);
        shipView.AimPort = aimPort;
        shipView.AimStarboard = aimStarboard;
        shipView.Spyglass = world.SpyglassOn;
        shipView.SpyglassDir = world.SpyglassDir;
        shipView.SpyglassRange = world.SpyglassRange;
        shipView.SetPose(pos, heading);
        fish.SetPose(pos, heading);
        fog.Sight(pos, world.VisionRadius);   // she sees round her now, charted or not
        fleet.Render(alpha);
        monsters.Alpha = alpha;

        // The camera leads the ship along her heading, more with speed, and eases after her.
        var ship = world.Ship;
        double lead = Math.Min(ship.Speed * Tuning.CameraLeadPerSpeed, Tuning.CameraLeadMax);
        var target = Ink.V(pos + Vec2.FromAngle(heading) * lead);
        camPos += (target - camPos) * (1f - Mathf.Exp(-(float)delta * 3f));
        var shake = effects.Shake > 0 ? new Vector2(GD.Randf() - 0.5f, GD.Randf() - 0.5f) * effects.Shake * 2 : Vector2.Zero;
        camera.Position = camPos + shake;
        float z = camera.Zoom.X + (zoomTarget - camera.Zoom.X) * (1f - Mathf.Exp(-(float)delta * 8f));
        camera.Zoom = new Vector2(z, z);
        camera.ForceUpdateScroll();

        if (!Paused) renderTime += (float)delta;
        var inv = GetCanvasTransform().AffineInverse();
        var visible = GetViewport().GetVisibleRect().Size;
        var topLeft = inv * Vector2.Zero;
        var bottomRight = inv * visible;
        var viewMetres = (bottomRight - topLeft) / Ink.PxPerM;
        var camMetres = (inv * (visible * 0.5f)) / Ink.PxPerM;
        var w = ship.LocalWind;
        sea.Update(camMetres, viewMetres, (Vector2)DisplayServer.WindowGetSize(), Ink.V(Vec2.FromAngle(w.Direction)).Normalized(),
            (float)w.Speed, renderTime, world.Seed, world.Wind.Fixed);

        var shipScreen = GetCanvasTransform() * Ink.V(pos);
        weather.Update(shipScreen, camera.Zoom.X, delta, Paused);
        // The ship under the pointer, at sea with nothing over it (`--hover` points at the nearest ship on the screen).
        if (hoverArg && world.Others.Where(o => world.PlayerSees(o.Pos) && GetViewport().GetVisibleRect().HasPoint(GetCanvasTransform() * Ink.V(o.Pos)))
                .OrderBy(o => o.Pos.DistanceTo(ship.Pos)).FirstOrDefault() is { } near)
            mouse = GetCanvasTransform() * Ink.V(near.Pos);
        shipCard.Refresh(mouse, GetCanvasTransform(), mode == Mode.Run && !ScreenUp && !Paused && !world.RunOver && hud.Visible, hud.Plates);
        // A notice raised while a full screen covers the HUD (the port on docking: "Unpaid hands deserted", "By a
        // hair!") waits for the sea instead of timing out unseen underneath it.
        List<string>? held = null;
        if (NoticesCovered && world.Notices.Count > 0)
        {
            held = new List<string>(world.Notices);
            world.Notices.Clear();
        }
        int queuedNotices = world.Notices.Count;
        hud.Refresh(world, paused, delta, NoticesCovered);
        if (mode == Mode.Run && world.Notices.Count < queuedNotices) audio.Notice();   // the HUD just inked a new notice
        if (held != null) foreach (var n in held) world.Notices.Enqueue(n);
        hintsView.DockPrompt = hud.DockPromptVisible;
        hintsView.Update(delta, Paused);
        hud.HintCard = hintsView.NoteShown ? hintsView.NoteRect : null;
        audio.Update(delta, Paused, paused || options.IsOpen || mode == Mode.Title || logbook.IsOpen);
        if (mode == Mode.Run && world.RunOver && !logbook.IsOpen)
        {
            overTime += delta;
            if (overTime > 2.5) FinishRun();
        }

        frame++;
        fpsClock += delta;
        if (reportFps && frame == 1) RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        if (reportFps && frame > 1)
        {
            gpuMs += RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
            cpuRenderMs += RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid());
        }
        if (frame == screenshotFrames && !selfTest)
        {
            if (reportFps)
                GD.Print($"fps: {frame / fpsClock:F1} over {frame} frames ({fpsClock:F2} s); ship {ship.Knots:F1} kn; tick avg {tickMs / Math.Max(1, tickCount):0.00} ms max {tickMax:0.0} ms; process avg {procMs / frame:0.00} ms max {procMax:0.0} ms; spikes>5ms tick {tickSpikes} process {procSpikes}; GC gen0/1/2 {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}; render cpu {cpuRenderMs / frame:0.00} ms gpu {gpuMs / frame:0.00} ms; draw calls {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}; vsync {DisplayServer.WindowGetVsyncMode()}");
            if (screenshotPath != null)
                Snap(screenshotPath);
            if (reportFps || screenshotPath != null)
                ExitGame(0);
        }
    }

    public void Snap(string path) => GetViewport().GetTexture().GetImage().SavePng(path);

    /// <summary>F inside a harbour ring: dock if the port is open, else the HUD says why.</summary>
    /// <summary>
    /// `--deliver`: a pirate on the Crown's books, every contract on the home board signed, and the ship alongside the
    /// first one's port — docked there with `--dock`, so the office shows a real arrival.
    /// </summary>
    void Deliver()
    {
        world.Player.BountyOwed = world.Bounty(world.Spawn("brig", world.Ship.Pos, 0, Faction.Brethren, null));
        world.Player.BountyShips = 1;
        world.Others.RemoveAt(world.Others.Count - 1);
        if (world.Apply(new PortCommand(PortAction.Dock)) != PortResult.Ok) return;
        var board = world.ContractOffers(world.Docked!);
        for (int i = 0; i < board.Length; i++) world.Apply(new PortCommand(PortAction.SignContract, Amount: i));
        world.Apply(new PortCommand(PortAction.CastOff));
        if (world.Player.Contracts.Count == 0) return;
        var to = world.Map.Ports[world.Player.Contracts[0].To];
        world.Ship.Pos = to.Harbor;
        world.Ship.Vel = Vec2.Zero;
    }

    public bool TryDock()
    {
        if (world.Apply(new PortCommand(PortAction.Dock)) != PortResult.Ok) return false;
        OnDocked();
        return true;
    }

    /// <summary>She is in port (F, a harbour rescue, or a save resumed at a dock): the port screen, the bell, the autosave.</summary>
    void OnDocked()
    {
        ReleaseHeld();
        portScreen.Open();
        audio.Bell();
        if (suspendEnabled) WriteSuspend();   // a crash can't eat the voyage (GDD §3)
    }

    public void LeavePort()
    {
        if (world.Apply(new PortCommand(PortAction.CastOff)) != PortResult.Ok) return;
        OnCastOff();
    }

    /// <summary>She has left port: close the screen, and rebuild what a new hull changes.</summary>
    void OnCastOff()
    {
        portScreen.Close();
        audio.Bell();
        prevPose = curPose = (world.Ship.Pos, world.Ship.Heading);
        shipView.Init(world.Ship, world.Player.Loadout);   // a new hull is a new ship object
        hud.Rebind(world);
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouse m) mouse = m.Position;
        if (ScreenUp) LetGo(e);   // before the GUI, so a focused control cannot swallow the release
        // F11 or Alt+Enter flips fullscreen anywhere (fixed keys, like Esc), saved like the options checkbox.
        if (e is InputEventKey { Pressed: true, Echo: false } fk && (fk.Keycode == Key.F11 || (fk.Keycode == Key.Enter && fk.AltPressed)))
        {
            settings.Fullscreen = !settings.Fullscreen;
            settings.Save();
            ApplySettings();
            if (options.IsOpen) options.Refresh();
            GetViewport().SetInputAsHandled();
            return;
        }
        // Esc in the chart's pin note abandons the pin (the note field kept the focus: Esc and M did nothing).
        if (viewsBuilt && chartScreen.IsOpen && chartScreen.NoteHasFocus && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            chartScreen.CancelPin();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (options.IsOpen)
        {
            if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) options.Close();
            return;
        }
        if (mode == Mode.Title)
        {
            if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) title.Back();
            return;
        }
        if (logbook.IsOpen) return;
        if (portScreen.IsOpen)
        {
            if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
                LeavePort();
            return;
        }
        if (chartScreen.IsOpen)
        {
            if (e is InputEventKey { Pressed: true, Echo: false } ck && (settings.ActionOf(ck.Keycode) == "Chart" || ck.Keycode == Key.Escape) && !chartScreen.NoteHasFocus)
                chartScreen.Close();
            return;
        }
        if (crewPanel.IsOpen)
        {
            if (e is InputEventKey { Pressed: true, Echo: false } cpk && (settings.ActionOf(cpk.Keycode) == "Crew" || cpk.Keycode == Key.Escape))
                crewPanel.Close();
            return;
        }
        switch (e)
        {
            case InputEventKey { Echo: false } key:
                OnKey(key);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } rmb:
                spyglass = rmb.Pressed;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                zoomTarget = Mathf.Clamp(zoomTarget * 1.2f, MinZoom, MaxZoom);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                zoomTarget = Mathf.Clamp(zoomTarget / 1.2f, MinZoom, MaxZoom);
                break;
        }
    }

    void OnKey(InputEventKey key)
    {
        if (key.Keycode == Key.Escape) { if (key.Pressed) SetPaused(!paused, KeySource(key)); return; }
        if (paused && key.Pressed && key.Keycode == Key.Q) { QuitGame(); return; }
        switch (settings.ActionOf(key.Keycode))
        {
            case "Port": portHeld = key.Pressed; break;
            case "Starboard": starboardHeld = key.Pressed; break;
            case "SailUp": if (key.Pressed && !paused) sailQueue++; break;
            case "SailDown": if (key.Pressed && !paused) sailQueue--; break;
            case "FirePort":   // inputs are ignored while paused (GDD §19): no aim starts, and a release stands it down unfired
                if (key.Pressed) aimPort |= !paused;
                else if (aimPort) { aimPort = false; firePortQueued = !paused; }
                break;
            case "FireStarboard":
                if (key.Pressed) aimStarboard |= !paused;
                else if (aimStarboard) { aimStarboard = false; fireStarboardQueued = !paused; }
                break;
            case "Lantern": if (key.Pressed && !paused) lanternQueued = true; break;
            case "Crew": if (key.Pressed && !paused) crewPanel.Toggle(); break;
            case "Chart": if (key.Pressed && !paused) OpenChart(); break;
            case "Dock": if (key.Pressed && !paused && !TryDock()) actionQueued = true; break;
        }
    }

    /// <summary>
    /// The chart key at sea: the chart opens only with a cartographer aboard (Nolan, 2026-09-27); without one the HUD
    /// says why, once (a second press while it is up or queued adds nothing).
    /// </summary>
    void OpenChart()
    {
        if (world.HasCartographer)
        {
            chartScreen.Toggle();
            hintsView.ChartOpened = true;
            return;
        }
        const string why = "NOTICE_NO_CARTOGRAPHER";
        if (!world.Notices.Contains(why) && hud.NoticeShown != Text.Get(why)) world.Notices.Enqueue(why);
    }
}
