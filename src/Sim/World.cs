using System.Text.Json;

namespace LastTide.Sim;

public readonly record struct LogEntry(long Tick, ShipInput Input);

public sealed class Pin
{
    /// <summary>Saved as X and Y: serialising the Vec2 itself recursed through <c>Normalized</c> and threw, so a voyage
    /// with any chart pin could not be saved at all (dock autosave, Save and quit).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public Vec2 Pos { get; set; }
    public double X { get => Pos.X; set => Pos = new Vec2(value, Pos.Y); }
    public double Y { get => Pos.Y; set => Pos = new Vec2(Pos.X, value); }
    public string Note { get; set; } = "";
}

/// <summary>
/// One run: the seeded RNG, the clock, the wind, the map and its markets, the player's ship and
/// purse. Advances in fixed 30 Hz ticks from a <see cref="ShipInput"/>; port actions are
/// <see cref="PortCommand"/>s applied between ticks. Both logs replay to the same <see cref="Hash"/>.
/// </summary>
public sealed partial class World
{
    public const double Dt = Tuning.Dt;

    public int Seed { get; }
    public string HullId { get; }
    public Rng Rng { get; private set; }
    public WindField Wind { get; private set; }
    public Ship Ship { get; private set; }
    public Map Map { get; }
    public Player Player { get; } = new();
    public RunStats Stats { get; } = new();
    /// <summary>The islands the ship can strike (the map's list; tests may clear it).</summary>
    public List<Island> Islands => Map.Islands;
    public RevealMask Reveal { get; } = new();
    /// <summary>How far the lookout sees; day value until the weather and night rules (M6).</summary>
    public double VisionRadius { get; set; } = 500;
    public bool MangroveBlocked { get; private set; }
    /// <summary>Ink pins the player dropped on the chart (GDD §5).</summary>
    public List<Pin> Pins { get; } = new();
    /// <summary>One-line events for the HUD to show (crew deserted, provisions out…), drained by the view.</summary>
    public Queue<string> Notices { get; } = new();
    Vec2 lastPaint = new(double.NaN, double.NaN);
    Vec2 lastPos;
    public long Ticks { get; private set; }
    /// <summary>Seconds since the run began, derived from the tick count so day boundaries are exact.</summary>
    public double Time => Ticks / (double)Tuning.TicksPerSecond;
    public List<LogEntry> Log { get; } = new();
    ShipInput lastInput;
    /// <summary>Set by a load: the resumed voyage's log starts with its first input (the suspend save carries no logs).</summary>
    bool logNext;

    public World(int seed, string hullId = "sloop", Preset preset = Preset.RoughSeas, bool populate = true, int mapVersion = MapGen.Version)
    {
        Preset = preset;
        Weather = new Weather(seed);
        Seed = seed;
        HullId = hullId;
        Rng = new Rng((ulong)seed * 0x9E3779B97F4A7C15UL + 12345);
        // The run opens with a fresh breeze from the north-east; it wanders from there.
        Wind = new WindField(seed, Angles.FromCompassDeg(225), Tuning.WindMeanSpeed);
        Map = MapGen.Generate(seed, mapVersion);
        foreach (var port in Map.Ports)
            port.Market = new Market(port, Rng);
        var start = Map.StartPort;
        var outward = (start.Harbor - start.Pos).Normalized;
        Ship = new Ship(Hulls.Get(hullId), start.Harbor, outward.Angle) { Id = 0, IsPlayer = true, Faction = Faction.FreeTraders, Crew = 4 };
        lastPos = Ship.Pos;
        Reveal.PaintRegion(Map, RegionType.TradeIsles);
        Reveal.Paint(Ship.Pos, VisionRadius);
        lastPaint = Ship.Pos;
        foreach (var port in Map.Ports)
            if (!port.Secret && port.Region == RegionType.TradeIsles) port.Discovered = true;
        // The lean start (GDD §3): 200 gold, 4 crew, 6 provisions, 12 munitions, 2 timber (6.7 of 20 slots).
        Player.Cargo[(int)Good.Provisions] = 6;
        Player.Cargo[(int)Good.Munitions] = 12;
        Player.Cargo[(int)Good.Timber] = 2;
        Stats.RegionsEntered.Add(RegionType.TradeIsles);
        if (populate) PopulateFleet();
    }

    public static World NewRun(int seed, string hullId = "sloop", Preset preset = Preset.RoughSeas, bool populate = true, int mapVersion = MapGen.Version) =>
        new(seed, hullId, preset, populate, mapVersion);

    // ---- optional phase profiling (scratch/tune profile, --fps) ----
    public static bool Profiling;
    public static readonly Dictionary<string, long> ProfileTicks = new();
    static long profileMark;
    static void Mark(string phase)
    {
        if (!Profiling) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        ProfileTicks[phase] = ProfileTicks.GetValueOrDefault(phase) + (now - profileMark);
        profileMark = now;
    }

    public void Tick(ShipInput input)
    {
        if (Docked != null || RunOver)
        {
            Events.Clear();   // nothing happens in this tick, so the last tick's events must not be read again
            return;           // the clock freezes in port (GDD §3) and stops when she is gone
        }
        if (Profiling) profileMark = System.Diagnostics.Stopwatch.GetTimestamp();
        if (Ticks == 0 || logNext || input != lastInput)
        {
            Log.Add(new LogEntry(Ticks, input));
            lastInput = input;
            logNext = false;
        }
        SetSpyglass(input.Spyglass, input.SpyglassDir);
        long midnightsBefore = MidnightsPassed;
        Events.Clear();
        Wind.Tick(Dt, Rng);
        WeatherTick(input);
        var w = WindAt(Ship.Pos);
        Mark("weather");
        var before = Ship.Pos;
        Swells(Ship);
        var helm = input;
        if (RudderJam > 0) helm = helm with { Rudder = 0 };
        else if (SirenPull != 0) helm = helm with { Rudder = Math.Clamp(input.Rudder + SirenPull, -1, 1) };
        Ship.Step(Dt, w, helm, Map.IslandsAround(Ship.Pos));
        WhirlpoolTick();
        IceTick();
        KeepOnTheChart(before);
        Mark("ship");
        MonsterTick();
        MarinesTick();
        Mark("monsters");
        CombatTick(input);
        Mark("combat+fleet");
        MatchMaps();
        ActionTick(input.Action);
        OfficeTick();
        AchievementsTick();
        Mark("exploration");
        foreach (var port in Map.Ports)
            port.Market.Tick(Dt, Rng);
        Mark("markets");
        // Only a cartographer keeps the chart (Nolan, 2026-09-27): with none aboard she still sees, but nothing is inked
        // and no port is marked; what was charted before stays on the chart for the next one.
        bool charting = HasCartographer;
        if (SpyglassOn && Ticks % 10 == 0 && charting)
        {
            // Ink a thin fan of the chart along the glass.
            for (double f = VisionRadius; f < SpyglassRange; f += RevealMask.Cell * 2)
                for (int k = -2; k <= 2; k++)
                    Reveal.Paint(Ship.Pos + Vec2.FromAngle(SpyglassDir + k * SpyglassHalfAngle * 0.45) * f, RevealMask.Cell * 1.6);
            foreach (var port in Map.Ports)
                if (!port.Discovered && PlayerSees(port.Harbor, port.RingRadius)) Discover(port);
        }
        if (double.IsNaN(lastPaint.X) || Ship.Pos.DistanceTo(lastPaint) >= 6)
        {
            ChartAround();   // the vision radius × his reach (1 / 1.25 / 1.5); nothing with no cartographer
            lastPaint = Ship.Pos;
            var region = Map.RegionAt(Ship.Pos).Type;
            Stats.RegionsEntered.Add(region);
        }
        Stats.LeaguesSailed += Ship.Pos.DistanceTo(lastPos) / 5556.0;
        lastPos = Ship.Pos;
        Mark("reveal");
        Ticks++;
        if (MidnightsPassed != midnightsBefore)
            Midnight();
    }

    /// <summary>A port comes into sight (plain or down the glass): coves count for the logbook and Smuggler's Welcome.</summary>
    void Discover(Port port)
    {
        port.Discovered = true;
        if (port.Secret)
        {
            Stats.CovesFound++;
            Notices.Enqueue("NOTICE_COVE");
        }
    }

    /// <summary>
    /// Only small hulls fit the Mangrove Maze (GDD §5). There is no wall at the chart's edge: the sea goes on, and beyond
    /// it the whirlpools and the beasts are the only limit (§5 "Beyond the chart").
    /// </summary>
    void KeepOnTheChart(Vec2 before)
    {
        MangroveBlocked = false;
        bool Maze(Vec2 p) => Map.InBounds(p) && Map.RegionAt(p).Type == RegionType.Mangrove;
        if (Map.Regions.Length > 0 && !RegionDef.MangroveHulls.Contains(Ship.Hull.Id) && Maze(Ship.Pos) && !Maze(before))
        {
            Ship.Pos = before;
            Ship.Vel = Ship.Vel * 0.2;
            MangroveBlocked = true;
        }
    }

    public void AddPin(Vec2 pos, string note) => Pins.Add(new Pin { Pos = pos, Note = note });

    /// <summary>The crew panel's hands per station as a logged command (<see cref="PortAction.CrewStations"/>), so replays see it.</summary>
    public static PortCommand CrewStationsCommand(int guns, int sails, int repair) =>
        new(PortAction.CrewStations, Amount: Math.Clamp(guns, 0, 1023) | Math.Clamp(sails, 0, 1023) << 10 | Math.Clamp(repair, 0, 1023) << 20);

    public void RemovePin(int index)
    {
        if (index >= 0 && index < Pins.Count) Pins.RemoveAt(index);
    }

    public Wind WindAtShip => Wind.Sample(Ship.Pos, Time);

    // ---- Clock (GDD §3, §9) ----
    public double DaysSurvived => Time / Tuning.SecondsPerDay;
    public int Day => 1 + (int)Math.Floor(DaysSurvived);
    public double HourOfDay => (Tuning.DawnHour + (Time % Tuning.SecondsPerDay) / Tuning.SecondsPerHour) % 24;
    /// <summary>0 middle, 1 morning, 2 forenoon, 3 afternoon, 4 dog, 5 first.</summary>
    public int WatchIndex => (int)(HourOfDay / 4) % 6;
    public bool IsNight => HourOfDay >= Tuning.DuskHour || HourOfDay < Tuning.DawnHour;
    /// <summary>In-game midnights since the run began (the first falls 18 h after the dawn start); upkeep is due at each (GDD §6).</summary>
    public long MidnightsPassed => (long)Math.Floor((Time + Tuning.DawnHour * Tuning.SecondsPerHour) / Tuning.SecondsPerDay);

    // ---- Determinism ----
    public ulong Hash()
    {
        var h = new Hasher();
        h.Add(Seed);
        h.Add(Ticks);
        h.Add(Ship.Pos.X); h.Add(Ship.Pos.Y);
        h.Add(Ship.Vel.X); h.Add(Ship.Vel.Y);
        h.Add(Ship.Heading); h.Add(Ship.AngVel);
        h.Add(Ship.SailTarget); h.Add(Ship.SailFraction); h.Add(Ship.Rudder); h.Add(Ship.HullHp);
        h.Add(Wind.BaseDirection); h.Add(Wind.BaseSpeed); h.Add(Wind.DirRate);
        foreach (var s in Rng.State) h.Add((long)s);
        h.Add(Player.Gold); h.Add(Ship.Crew); h.Add(Player.Unpaid ? 1 : 0);
        h.Add(Ship.Water); h.Add(Ship.Leaks); h.Add(Ship.Cannons); h.Add(Ship.Foundering ? 1 : 0); h.Add((int)Ship.Order);
        h.Add(Others.Count); h.Add(Balls.Count); h.Add(Flotsam.Count);
        h.Add(Lantern ? 1 : 0); h.Add(Ship.TornSails ? 1 : 0); h.Add(Weather.Storms.Count);
        foreach (var g in Ship.Parts) h.Add(g);
        h.Add(Director.QuietFor); h.Add(RescuedAt.Count); h.Add(Player.Unique.Count); h.Add(Player.BottleMaps.Count); h.Add(Player.Cosmetics.Count); h.Add(Digging ? 1 : 0); h.Add(DigProgress);
        foreach (var c in Player.Loadout) h.Add(c.Length == 0 ? 0 : c.Sum(ch => (long)ch));
        h.Add(Player.Officers.Count); h.Add(Ship.Hull.Id.Length);
        h.Add(Monster == null ? -1 : (int)Monster.Type); h.Add(Monster?.Hp ?? 0); h.Add(MonsterClock); h.Add(Eruptions.Count); h.Add(Player.Achievements.Count);
        foreach (var c in Weather.Storms) { h.Add(c.Pos.X); h.Add(c.Life); }
        foreach (var o in Others) { h.Add(o.Pos.X); h.Add(o.Pos.Y); h.Add(o.HullHp); h.Add(o.Crew); }
        foreach (var c in Player.Cargo) h.Add(c);
        foreach (var r in Player.Reputation) h.Add(r);
        h.Add(Player.Ledger.Count);
        h.Add(Docked?.Id ?? -1);
        double stock = 0;
        foreach (var port in Map.Ports)
            foreach (var s in port.Market.Stock) stock += s;
        h.Add(stock);
        // Everything else a resumed or replayed voyage has to reproduce exactly (audit 2026-09-23): a hash that
        // missed the fleet's helms and the captains let a load that lost half the captains pass as identical.
        h.Add(Ship.Hull.Id);
        h.Add(Reveal.RevealedCells);
        foreach (var port in Map.Ports)
        {
            if (port.Discovered) h.Add(port.Id);
            foreach (var s in port.Market.Stock) h.Add(s);
        }
        h.Add(Ship.Reload[0]); h.Add(Ship.Reload[1]); h.Add(Ship.Loaded[0] ? 1 : 0); h.Add(Ship.Loaded[1] ? 1 : 0);
        h.Add(Ship.Hourglass); h.Add(Ship.CarpenterWork); h.Add(Ship.PatchWork); h.Add(Ship.RamCooldown); h.Add(Ship.SpeedMult);
        foreach (var c in Ship.CustomStations) h.Add(c);
        h.Add(Director.Credits); h.Add(Director.Clock); h.Add(Director.Spawned); h.Add(Weather.SpawnClock);
        foreach (var c in Weather.Storms) { h.Add(c.Pos.Y); h.Add(c.Radius); h.Add(c.Strength); h.Add(c.Age); }
        foreach (var o in Others)
        {
            h.Add(o.Id); h.Add(o.Vel.X); h.Add(o.Vel.Y); h.Add(o.Heading); h.Add(o.AngVel); h.Add(o.Rudder);
            h.Add(o.SailFraction); h.Add(o.Water); h.Add(o.Leaks); h.Add(Captains.ContainsKey(o.Id) ? 1 : 0);
            if (o.Ai is { } ai) { h.Add(ai.DestPort); h.Add(ai.PathIndex); h.Add(ai.Wait); h.Add(ai.TargetShip); h.Add(ai.Path.Count); }
        }
        foreach (var b in Balls) { h.Add(b.Pos.X); h.Add(b.Pos.Y); h.Add(b.Life); }
        foreach (var f in Flotsam) { h.Add(f.Pos.X); h.Add(f.Pos.Y); h.Add(f.Life); }
        h.Add(Stats.GoldEarned); h.Add(Stats.ShipsSunk); h.Add(Stats.LeaguesSailed); h.Add(Stats.TreasuresDug);
        h.Add(Stats.CovesFound); h.Add(Stats.MonstersBeaten); h.Add(Stats.RegionsEntered.Count);
        foreach (var e in Player.Ledger) { h.Add(e.Port); h.Add(e.Price); h.Add(e.Day); }
        foreach (var m in Player.BottleMaps) { h.Add(m.Treasure); h.Add(m.Solved ? 1 : 0); h.Add(m.Rotation); }
        foreach (var o in Player.Officers) { h.Add((int)o.Type); h.Add(o.Tier); }
        h.Add(RudderJam); h.Add(SirenPull); h.Add(EruptionClock); h.Add(MonsterHitTime); h.Add(WhirlpoolHitTime); h.Add(coreClock); h.Add(IceHitTime); h.Add(lastUpkeepDay); h.Add(tradedThisVisit ? 1 : 0);
        foreach (var c in Player.Contracts) { h.Add((int)c.Kind); h.Add(c.From); h.Add(c.To); h.Add(c.Slots); h.Add(c.Pay); h.Add(c.Advance); h.Add(c.Deadline); h.Add(c.Offer); }
        h.Add(Player.TakenOffers.Count); h.Add(Player.BountyOwed); h.Add(Player.BountyShips); h.Add(searchedBy.Count);
        return h.Value;
    }

    struct Hasher
    {
        ulong v = 14695981039346656037UL;
        public Hasher() { }
        public ulong Value => v;
        public void Add(double d) => Add(BitConverter.DoubleToInt64Bits(d));
        public void Add(string text)
        {
            Add(text.Length);
            foreach (char c in text) Add((long)c);
        }
        public void Add(long x)
        {
            for (int i = 0; i < 8; i++)
            {
                v ^= (byte)(x >> (8 * i));
                v *= 1099511628211UL;
            }
        }
    }

    /// <summary>Rebuilds a run from its seed, preset, loadout and both logs, ticking to <paramref name="ticks"/>.</summary>
    public static World Replay(int seed, string hullId, IReadOnlyList<LogEntry> log, IReadOnlyList<CommandEntry> commands, long ticks,
        Preset preset = Preset.RoughSeas, IReadOnlyList<string>? loadout = null, int mapVersion = MapGen.Version)
    {
        var w = new World(seed, hullId, preset, mapVersion: mapVersion);
        if (loadout != null)
            for (int k = 0; k < Math.Min(loadout.Count, w.Player.Loadout.Length); k++) w.Player.Loadout[k] = loadout[k] ?? "";
        return Play(w, log, commands, ticks);
    }

    /// <summary>
    /// Replays a resumed voyage: its suspend save, then the logs the resumed world kept (they start at the save — the
    /// suspend save carries the state, not the history).
    /// </summary>
    public static World Replay(string suspendJson, IReadOnlyList<LogEntry> log, IReadOnlyList<CommandEntry> commands, long ticks) =>
        Play(LoadJson(suspendJson), log, commands, ticks);

    static World Play(World w, IReadOnlyList<LogEntry> log, IReadOnlyList<CommandEntry> commands, long ticks)
    {
        int i = 0, c = 0;
        var current = default(ShipInput);
        while (w.Ticks < ticks)
        {
            long t = w.Ticks;
            bool acted = false;
            while (c < commands.Count && commands[c].Tick == t)
            {
                w.Apply(commands[c++].Command);
                acted = true;
            }
            while (i < log.Count && log[i].Tick == t)
                current = log[i++].Input;
            if (w.Docked != null)
            {
                if (!acted) break;   // the run stopped in port
                continue;
            }
            w.Tick(current);
        }
        return w;
    }
}
