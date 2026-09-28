using System.Text.Json;
using System.Text.Json.Serialization;

namespace LastTide.Sim;

public sealed partial class World
{
    // ---- Save (GDD §3 suspend save) ----
    public sealed class SaveData
    {
        public int Version { get; set; } = 2;
        /// <summary>The generator version the map was made with; saves from before versioning were made with 2.</summary>
        public int MapVersion { get; set; } = 2;
        public int Seed { get; set; }
        public string Hull { get; set; } = "sloop";
        public string StartHull { get; set; } = "";
        public long Ticks { get; set; }
        public ulong[] Rng { get; set; } = Array.Empty<ulong>();
        public double WindDirection { get; set; }
        public double WindSpeed { get; set; }
        public double WindRate { get; set; }
        public bool WindFixed { get; set; }
        public double PosX { get; set; }
        public double PosY { get; set; }
        public double VelX { get; set; }
        public double VelY { get; set; }
        public double Heading { get; set; }
        public double AngVel { get; set; }
        public int SailTarget { get; set; }
        public double SailFraction { get; set; }
        public double Rudder { get; set; }
        public double HullHp { get; set; }
        public int Cannons { get; set; }
        public int CannonGrade { get; set; }
        public int Order { get; set; }
        public double Water { get; set; }
        public int Leaks { get; set; }
        public bool[] Loaded { get; set; } = { true, true };
        public double[] Reload { get; set; } = { 0, 0 };
        public bool Foundering { get; set; }
        public double Hourglass { get; set; }
        public bool WaterOnlyStand { get; set; }
        public int ShipsSunkStat { get; set; }
        public double PlayerCarpenterWork { get; set; }
        public double PlayerPatchWork { get; set; }
        public double PlayerRamCooldown { get; set; }
        public double PlayerWindDir { get; set; }
        public double PlayerWindSpeed { get; set; }
        public double PlayerAngleOff { get; set; } = 180;
        public double LastRudderInput { get; set; }
        public int LastSailDelta { get; set; }
        /// <summary>Where the chart was last inked from (null in older saves: the ship's position).</summary>
        public double[]? LastPaint { get; set; }
        /// <summary>Where the ship stood at the end of the last tick, for the leagues sailed (null: her position).</summary>
        public double[]? LastPos { get; set; }
        public double VisionRadius { get; set; }
        public double PlayerSpeedMult { get; set; } = 1;
        public int PlayerLastHitBy { get; set; } = -1;
        public string Reveal { get; set; } = "";
        public List<int> DiscoveredPorts { get; set; } = new();
        public List<int> DugTreasures { get; set; } = new();
        public List<int> SalvagedWrecks { get; set; } = new();
        public List<Pin> Pins { get; set; } = new();
        public int Gold { get; set; }
        public int[] Cargo { get; set; } = Array.Empty<int>();
        public double[] CostBasis { get; set; } = Array.Empty<double>();
        public int[] LotPort { get; set; } = Array.Empty<int>();
        public int[] LotUnits { get; set; } = Array.Empty<int>();
        public double[] LotGold { get; set; } = Array.Empty<double>();
        public int Crew { get; set; }
        public bool Unpaid { get; set; }
        public double[] Reputation { get; set; } = Array.Empty<double>();
        public List<LedgerEntry> Ledger { get; set; } = new();
        public List<int> PortsVisited { get; set; } = new();
        public string ShipName { get; set; } = "";
        public int Docked { get; set; } = -1;
        public bool TradedThisVisit { get; set; }
        public int LastDockPort { get; set; } = -1;
        public int LastDockDay { get; set; }
        public int LastUpkeepDay { get; set; }
        public double[][] Stocks { get; set; } = Array.Empty<double[]>();
        public int GoldEarned { get; set; }
        public int ShipsSunk { get; set; }
        public double LeaguesSailed { get; set; }
        public int BestTrade { get; set; }
        public string BestTradeGood { get; set; } = "";
        public int TreasuresDug { get; set; }
        public int CovesFound { get; set; }
        public List<int> RegionsEntered { get; set; } = new();
        public int Preset { get; set; }
        public double DirectorCredits { get; set; }
        public double DirectorQuiet { get; set; }
        public int LastHunters { get; set; }
        public double DirectorClock { get; set; }
        public int DirectorSpawned { get; set; }
        public int MerchantArrivals { get; set; }
        public List<ShipSave> Ships { get; set; } = new();
        public List<FlotsamSave> Flotsam { get; set; } = new();
        public List<BallSave> Balls { get; set; } = new();
        public Dictionary<int, double> FortClocks { get; set; } = new();
        public int NextShipId { get; set; } = 1;
        public bool Lantern { get; set; } = true;
        public bool DirectorEnabled { get; set; } = true;
        public List<string> Unique { get; set; } = new();
        public List<string> Cosmetics { get; set; } = new();
        public List<string> Loadout { get; set; } = new();
        public List<BottleMap> BottleMaps { get; set; } = new();
        public List<CoveHint> CoveHints { get; set; } = new();
        public bool Digging { get; set; }
        public bool DigWreck { get; set; }
        public double DigProgress { get; set; }
        public bool MonstersEnabled { get; set; } = true;
        public int[] Parts { get; set; } = new int[8];
        public int[] CustomStations { get; set; } = new int[4];
        public List<int[]> Officers { get; set; } = new();
        /// <summary>
        /// Written true since the cartographer rule (Nolan, 2026-09-27). An older voyage was charting without one, so it
        /// resumes with a green cartographer aboard rather than a chart it suddenly cannot open.
        /// </summary>
        public bool CartographerRule { get; set; }
        public List<string> HullsOwned { get; set; } = new();
        public double MarinesClock { get; set; }
        public List<string> Achievements { get; set; } = new();
        public int MonstersBeaten { get; set; }
        public double MonsterClock { get; set; }
        public double EruptionClock { get; set; }
        public double RudderJam { get; set; }
        public int MonsterType { get; set; } = -1;
        public double[] MonsterData { get; set; } = Array.Empty<double>();
        public List<double[]> MonsterTargets { get; set; } = new();
        public List<double[]> Eruptions { get; set; } = new();
        public bool TornSails { get; set; }
        public double StormClock { get; set; }
        public List<double[]> Storms { get; set; } = new();
        public bool DockedByAHair { get; set; }
        public List<int> RescuedAt { get; set; } = new();
        public bool RunOver { get; set; }
        public string CauseOfSinking { get; set; } = "";
        public double SirenPull { get; set; }
        public int LastMonsterHit { get; set; }
        public double MonsterHitTime { get; set; } = -1;
        public double WhirlpoolHitTime { get; set; } = -1;
        public double WhirlpoolCoreClock { get; set; }
        public double IceHitTime { get; set; } = -1;
        /// <summary>Per port, the prices remembered before its last visit (the market's trend arrows).</summary>
        public List<double?[]?> PricesLastVisit { get; set; } = new();
        /// <summary>The merchants' cached lanes: [from port, to port, x0, y0, x1, y1, …], in cache order.</summary>
        public List<double[]> Lanes { get; set; } = new();
        /// <summary>The harbour office: contracts in hand, board slots signed, bounties owed, patrols that searched her.</summary>
        public List<Contract> Contracts { get; set; } = new();
        public List<long> TakenOffers { get; set; } = new();
        public int BountyOwed { get; set; }
        public int BountyShips { get; set; }
        public List<int> SearchedBy { get; set; } = new();
        /// <summary>
        /// Older saves carried the whole input and command logs (MBs on a long voyage, rewritten on every dock); they
        /// are read past and no longer written: resuming needs the state, and a resumed voyage logs from its save.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<LogEntry>? Log { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<CommandEntry>? Commands { get; set; }
    }

    public sealed class ShipSave
    {
        public string Hull { get; set; } = "sloop";
        public double PosX { get; set; }
        public double PosY { get; set; }
        public double VelX { get; set; }
        public double VelY { get; set; }
        public double Heading { get; set; }
        public double HullHp { get; set; }
        public int Crew { get; set; }
        public int Cannons { get; set; }
        public int Faction { get; set; }
        public int Role { get; set; }
        public int HomePort { get; set; } = -1;
        public int DestPort { get; set; } = -1;
        public int CargoGood { get; set; }
        public int CargoUnits { get; set; }
        public double Wait { get; set; }
        public double Water { get; set; }
        public int Leaks { get; set; }
        public int Timber { get; set; }
        public int SailTarget { get; set; }
        public double Scale { get; set; } = 1;
        public double DamageMult { get; set; } = 1;
        public bool PlayerHostile { get; set; }
        public bool TornSails { get; set; }
        public double AngVel { get; set; }
        public double Rudder { get; set; }
        public double SailFraction { get; set; }
        public int Order { get; set; }
        public bool[] Loaded { get; set; } = { true, true };
        public double[] Reload { get; set; } = { 0, 0 };
        public double CarpenterWork { get; set; }
        public double PatchWork { get; set; }
        public double RamCooldown { get; set; }
        public int LastHitBy { get; set; } = -1;
        public bool Foundering { get; set; }
        public double Hourglass { get; set; }
        public bool WaterOnlyStand { get; set; }
        public List<double> Path { get; set; } = new();
        public int PathIndex { get; set; }
        public double Repath { get; set; }
        public int Tack { get; set; } = 1;
        public double TackHold { get; set; }
        public int TargetShip { get; set; } = -1;
        public double Lost { get; set; }
        public double WaypointX { get; set; }
        public double WaypointY { get; set; }
        public bool HasWaypoint { get; set; }
        public bool Fleeing { get; set; }
        public double FleeTime { get; set; }
        public int Id { get; set; }
        public double WindDir { get; set; }
        public double WindSpeed { get; set; }
        public double AngleOff { get; set; } = 180;
    }

    public sealed class FlotsamSave
    {
        public double X { get; set; }
        public double Y { get; set; }
        public int Good { get; set; } = -1;
        public int Units { get; set; }
        public int Gold { get; set; }
        public double Life { get; set; }
        public double Bob { get; set; }
    }

    public sealed class BallSave
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double VX { get; set; }
        public double VY { get; set; }
        public double Life { get; set; }
        public int Shooter { get; set; } = -1;
        public double Damage { get; set; }
        public double Delay { get; set; }
    }

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false, IncludeFields = true };

    public SaveData ToSave() => new()
    {
        Seed = Seed, MapVersion = Map.Version, Hull = Ship.Hull.Id, StartHull = HullId, Ticks = Ticks, Rng = Rng.State,
        WindDirection = Wind.BaseDirection, WindSpeed = Wind.BaseSpeed, WindRate = Wind.DirRate, WindFixed = Wind.Fixed,
        PosX = Ship.Pos.X, PosY = Ship.Pos.Y, VelX = Ship.Vel.X, VelY = Ship.Vel.Y,
        Heading = Ship.Heading, AngVel = Ship.AngVel, SailTarget = Ship.SailTarget, SailFraction = Ship.SailFraction, Rudder = Ship.Rudder,
        HullHp = Ship.HullHp, Cannons = Ship.Cannons, CannonGrade = Ship.CannonGrade, Order = (int)Ship.Order,
        Water = Ship.Water, Leaks = Ship.Leaks, Loaded = (bool[])Ship.Loaded.Clone(), Reload = (double[])Ship.Reload.Clone(),
        Foundering = Ship.Foundering, Hourglass = Ship.Hourglass, WaterOnlyStand = Ship.WaterOnlyStand,
        PlayerCarpenterWork = Ship.CarpenterWork, PlayerPatchWork = Ship.PatchWork, PlayerRamCooldown = Ship.RamCooldown,
        PlayerWindDir = Ship.LocalWind.Direction, PlayerWindSpeed = Ship.LocalWind.Speed, PlayerAngleOff = Ship.AngleOffWindDeg,
        LastRudderInput = lastInput.Rudder, LastSailDelta = lastInput.SailDelta,
        LastPaint = new[] { lastPaint.X, lastPaint.Y }, LastPos = new[] { lastPos.X, lastPos.Y }, VisionRadius = VisionRadius, PlayerSpeedMult = Ship.SpeedMult,
        PlayerLastHitBy = Ship.LastHitBy?.Id ?? -1,
        Reveal = PackReveal(Reveal.Bytes),
        DiscoveredPorts = Map.Ports.Where(p => p.Discovered).Select(p => p.Id).ToList(),
        DugTreasures = Map.Treasures.Where(t => t.Dug).Select(t => t.Id).ToList(),
        SalvagedWrecks = Map.Wrecks.Where(w => w.Salvaged).Select(w => w.Id).ToList(),
        Pins = Pins.Select(p => new Pin { Pos = p.Pos, Note = p.Note }).ToList(),
        Gold = Player.Gold, Cargo = (int[])Player.Cargo.Clone(), CostBasis = (double[])Player.CostBasis.Clone(),
        LotPort = (int[])Player.LotPort.Clone(), LotUnits = (int[])Player.LotUnits.Clone(), LotGold = (double[])Player.LotGold.Clone(),
        Crew = Ship.Crew, Unpaid = Player.Unpaid, Reputation = (double[])Player.Reputation.Clone(),
        Ledger = Player.Ledger.Select(e => new LedgerEntry { Port = e.Port, Good = e.Good, Price = e.Price, Day = e.Day, Rumor = e.Rumor }).ToList(),
        PortsVisited = Player.PortsVisited.ToList(), ShipName = Player.ShipName,
        Docked = Docked?.Id ?? -1, TradedThisVisit = tradedThisVisit, LastUpkeepDay = lastUpkeepDay, LastDockPort = lastDockPort, LastDockDay = lastDockDay,
        Stocks = Map.Ports.Select(p => (double[])p.Market.Stock.Clone()).ToArray(),
        GoldEarned = Stats.GoldEarned, ShipsSunk = Stats.ShipsSunk, LeaguesSailed = Stats.LeaguesSailed,
        BestTrade = Stats.BestTrade, BestTradeGood = Stats.BestTradeGood, TreasuresDug = Stats.TreasuresDug, CovesFound = Stats.CovesFound,
        RegionsEntered = Stats.RegionsEntered.Select(r => (int)r).ToList(),
        Preset = (int)Preset, DirectorCredits = Director.Credits, DirectorClock = Director.Clock, DirectorSpawned = Director.Spawned, DirectorQuiet = Director.QuietFor, LastHunters = lastHunters,
        MerchantArrivals = MerchantArrivals,
        Ships = Others.Where(o => !o.Sunk && o.Ai != null).Select(o => new ShipSave
        {
            Hull = o.Hull.Id, PosX = o.Pos.X, PosY = o.Pos.Y, VelX = o.Vel.X, VelY = o.Vel.Y, Heading = o.Heading, HullHp = o.HullHp,
            Crew = o.Crew, Cannons = o.Cannons, Faction = (int)o.Faction, Role = (int)o.Ai!.Role, HomePort = o.Ai.HomePort, DestPort = o.Ai.DestPort,
            CargoGood = (int)o.Ai.CargoGood, CargoUnits = o.Ai.CargoUnits, Wait = o.Ai.Wait, Water = o.Water, Leaks = o.Leaks, Timber = o.Timber,
            SailTarget = o.SailTarget, Scale = o.Ai.Scale, DamageMult = o.DamageMult, PlayerHostile = o.PlayerHostile,
            AngVel = o.AngVel, Rudder = o.Rudder, SailFraction = o.SailFraction, Order = (int)o.Order,
            Loaded = (bool[])o.Loaded.Clone(), Reload = (double[])o.Reload.Clone(), CarpenterWork = o.CarpenterWork, PatchWork = o.PatchWork,
            RamCooldown = o.RamCooldown, LastHitBy = o.LastHitBy?.Id ?? -1, Foundering = o.Foundering, Hourglass = o.Hourglass, WaterOnlyStand = o.WaterOnlyStand,
            Path = o.Ai.Path.SelectMany(p => new[] { p.X, p.Y }).ToList(), PathIndex = o.Ai.PathIndex, Repath = o.Ai.Repath, Tack = o.Ai.Tack, TackHold = o.Ai.TackHold,
            TargetShip = o.Ai.TargetShip, Lost = o.Ai.Lost, WaypointX = o.Ai.Waypoint.X, WaypointY = o.Ai.Waypoint.Y, HasWaypoint = o.Ai.HasWaypoint,
            Fleeing = o.Ai.Fleeing, FleeTime = o.Ai.FleeTime, Id = o.Id,
            WindDir = o.LocalWind.Direction, WindSpeed = o.LocalWind.Speed, AngleOff = o.AngleOffWindDeg, TornSails = o.TornSails,
        }).ToList(),
        Flotsam = Flotsam.Select(f => new FlotsamSave { X = f.Pos.X, Y = f.Pos.Y, Good = f.Good.HasValue ? (int)f.Good.Value : -1, Units = f.Units, Gold = f.Gold, Life = f.Life, Bob = f.Bob }).ToList(),
        Balls = Balls.Select(b => new BallSave { X = b.Pos.X, Y = b.Pos.Y, VX = b.Vel.X, VY = b.Vel.Y, Life = b.Life, Shooter = b.Shooter?.Id ?? -1, Damage = b.Damage, Delay = b.Delay }).ToList(),
        FortClocks = new Dictionary<int, double>(fortClocks), NextShipId = nextShipId,
        Lantern = Lantern, TornSails = Ship.TornSails, StormClock = Weather.SpawnClock, DirectorEnabled = DirectorEnabled, MonstersEnabled = MonstersEnabled,
        Unique = Player.Unique.ToList(), Cosmetics = Player.Cosmetics.ToList(), Loadout = Player.Loadout.ToList(),
        BottleMaps = Player.BottleMaps.Select(m => new BottleMap { Treasure = m.Treasure, Solved = m.Solved, Rotation = m.Rotation }).ToList(),
        CoveHints = Player.CoveHints.Select(c => new CoveHint { Port = c.Port, X = c.X, Y = c.Y, Radius = c.Radius }).ToList(),
        Digging = Digging, DigWreck = digWreck, DigProgress = DigProgress,
        Parts = (int[])Ship.Parts.Clone(), CustomStations = (int[])Ship.CustomStations.Clone(),
        Officers = Player.Officers.Select(o => new[] { (int)o.Type, o.Tier }).ToList(), CartographerRule = true, HullsOwned = Stats.HullsOwned.ToList(), MarinesClock = marinesClock,
        Achievements = Player.Achievements.ToList(), MonstersBeaten = Stats.MonstersBeaten, MonsterClock = MonsterClock, EruptionClock = EruptionClock, RudderJam = RudderJam,
        MonsterType = Monster == null ? -1 : (int)Monster.Type,
        MonsterData = Monster == null ? Array.Empty<double>() : new[] { Monster.Pos.X, Monster.Pos.Y, Monster.Heading, (int)Monster.State, Monster.Timer, Monster.Hp, Monster.Surfaced ? 1 : 0, Monster.FlareTimer, Monster.FlareClock, Monster.Age, Monster.Bites, Monster.Perch.X, Monster.Perch.Y, Monster.GunClock, Monster.Side },
        MonsterTargets = Monster == null ? new List<double[]>() : Monster.Targets.Select(t => new[] { t.Pos.X, t.Pos.Y, t.Radius, t.Hp }).ToList(),
        Eruptions = Eruptions.Select(e => new[] { e.Pos.X, e.Pos.Y, e.Radius, e.Warning, e.Landed ? 1 : 0, e.Age }).ToList(),
        Storms = Weather.Storms.Select(c => new[] { c.Pos.X, c.Pos.Y, c.Radius, c.Strength, c.Life, c.Age }).ToList(), DockedByAHair = DockedByAHair, RescuedAt = RescuedAt.ToList(), RunOver = RunOver, CauseOfSinking = CauseOfSinking,
        SirenPull = SirenPull, LastMonsterHit = (int)LastMonsterHit, MonsterHitTime = MonsterHitTime,
        WhirlpoolHitTime = WhirlpoolHitTime, WhirlpoolCoreClock = coreClock, IceHitTime = IceHitTime,
        PricesLastVisit = Map.Ports.Select(p => p.PricesLastVisit == null ? null : (double?[])p.PricesLastVisit.Clone()).ToList(),
        Lanes = routeCache.Select(kv => new double[] { kv.Key.Item1, kv.Key.Item2 }.Concat(kv.Value.SelectMany(p => new[] { p.X, p.Y })).ToArray()).ToList(),
        Contracts = Player.Contracts.Select(c => c.Clone()).ToList(), TakenOffers = Player.TakenOffers.ToList(),
        BountyOwed = Player.BountyOwed, BountyShips = Player.BountyShips, SearchedBy = searchedBy.ToList(),
    };

    public string SaveJson() => JsonSerializer.Serialize(ToSave(), JsonOptions);

    public static World FromSave(SaveData s)
    {
        Check(s);
        string startHull = s.StartHull.Length > 0 ? s.StartHull : s.Hull;
        var w = new World(s.Seed, startHull, (Preset)s.Preset, populate: false, mapVersion: s.MapVersion);
        CheckAgainst(s, w.Map);
        if (s.Hull != startHull)
            w.Ship = new Ship(Hulls.Get(s.Hull), w.Ship.Pos, w.Ship.Heading) { Id = 0, IsPlayer = true, Faction = Faction.FreeTraders, Crew = 4 };
        w.Ticks = s.Ticks;
        w.Rng.State = s.Rng;
        w.Wind.BaseDirection = s.WindDirection;
        w.Wind.BaseSpeed = s.WindSpeed;
        w.Wind.DirRate = s.WindRate;
        w.Wind.Fixed = s.WindFixed;
        w.Ship.Pos = new Vec2(s.PosX, s.PosY);
        w.Ship.Vel = new Vec2(s.VelX, s.VelY);
        w.Ship.Heading = s.Heading;
        w.Ship.AngVel = s.AngVel;
        w.Ship.SailTarget = s.SailTarget;
        w.Ship.SailFraction = s.SailFraction;
        w.Ship.Rudder = s.Rudder;
        w.Ship.HullHp = s.HullHp;
        w.Ship.Cannons = s.Cannons;
        w.Ship.CannonGrade = s.CannonGrade;
        w.Ship.Order = (CrewOrder)s.Order;
        w.Ship.Water = s.Water;
        w.Ship.Leaks = s.Leaks;
        if (s.Loaded.Length == 2) s.Loaded.CopyTo(w.Ship.Loaded, 0);
        if (s.Reload.Length == 2) s.Reload.CopyTo(w.Ship.Reload, 0);
        w.Ship.Foundering = s.Foundering;
        w.Ship.Hourglass = s.Hourglass;
        w.Ship.WaterOnlyStand = s.WaterOnlyStand;
        w.Ship.SetCoarseWind(new Wind(s.PlayerWindDir, s.PlayerWindSpeed), s.PlayerAngleOff);
        w.Ship.CarpenterWork = s.PlayerCarpenterWork;
        w.Ship.PatchWork = s.PlayerPatchWork;
        w.Ship.RamCooldown = s.PlayerRamCooldown;
        w.lastInput = new ShipInput(s.LastRudderInput, s.LastSailDelta);
        w.logNext = true;   // the resumed log opens with the first input after the save
        w.lastPos = s.LastPos is { Length: 2 } lpos ? new Vec2(lpos[0], lpos[1]) : w.Ship.Pos;
        if (s.Reveal.Length > 0) w.Reveal.Load(UnpackReveal(s.Reveal));
        w.lastPaint = s.LastPaint is { Length: 2 } lp ? new Vec2(lp[0], lp[1]) : w.Ship.Pos;
        w.Ship.SpeedMult = s.PlayerSpeedMult;
        foreach (var p in w.Map.Ports) p.Discovered = false;
        foreach (int id in s.DiscoveredPorts) w.Map.Ports[id].Discovered = true;
        foreach (int id in s.DugTreasures) w.Map.Treasures[id].Dug = true;
        foreach (int id in s.SalvagedWrecks) w.Map.Wrecks[id].Salvaged = true;
        w.Pins.AddRange(s.Pins);
        w.Player.Gold = s.Gold;
        if (s.Cargo.Length == Goods.Count) s.Cargo.CopyTo(w.Player.Cargo, 0);
        if (s.CostBasis.Length == Goods.Count) s.CostBasis.CopyTo(w.Player.CostBasis, 0);
        if (s.LotPort.Length == Goods.Count && s.LotUnits.Length == Goods.Count && s.LotGold.Length == Goods.Count)
        {
            s.LotPort.CopyTo(w.Player.LotPort, 0);
            s.LotUnits.CopyTo(w.Player.LotUnits, 0);
            s.LotGold.CopyTo(w.Player.LotGold, 0);
        }
        w.Ship.Crew = s.Crew;
        w.Player.Unpaid = s.Unpaid;
        if (s.Reputation.Length == 3) s.Reputation.CopyTo(w.Player.Reputation, 0);
        w.Player.Ledger.AddRange(s.Ledger);
        foreach (var id in s.PortsVisited) w.Player.PortsVisited.Add(id);
        if (s.ShipName.Length > 0) w.Player.ShipName = s.ShipName;
        w.Docked = s.Docked >= 0 ? w.Map.Ports[s.Docked] : null;
        w.tradedThisVisit = s.TradedThisVisit;
        // Saves from before the visit rule: a save made alongside counts as that visit.
        w.lastDockPort = s.LastDockPort >= 0 ? s.LastDockPort : w.Docked?.Id ?? -1;
        w.lastDockDay = s.LastDockPort >= 0 ? s.LastDockDay : w.Day;
        w.lastUpkeepDay = s.LastUpkeepDay;
        for (int i = 0; i < Math.Min(s.Stocks.Length, w.Map.Ports.Count); i++)
            if (s.Stocks[i].Length == Goods.Count) s.Stocks[i].CopyTo(w.Map.Ports[i].Market.Stock, 0);
        w.Stats.GoldEarned = s.GoldEarned;
        w.Stats.ShipsSunk = s.ShipsSunk;
        w.Stats.LeaguesSailed = s.LeaguesSailed;
        w.Stats.BestTrade = s.BestTrade;
        w.Stats.BestTradeGood = s.BestTradeGood;
        w.Stats.TreasuresDug = s.TreasuresDug;
        w.Stats.CovesFound = s.CovesFound;
        w.Stats.RegionsEntered.Clear();
        foreach (var r in s.RegionsEntered) w.Stats.RegionsEntered.Add((RegionType)r);
        w.Director.Credits = s.DirectorCredits;
        w.Director.QuietFor = s.DirectorQuiet;
        w.lastHunters = s.LastHunters;
        w.Director.Clock = s.DirectorClock;
        w.Director.Spawned = s.DirectorSpawned;
        w.MerchantArrivals = s.MerchantArrivals;
        var byId = new Dictionary<int, Ship> { [0] = w.Ship };
        foreach (var o in s.Ships)
        {
            var role = (Role)o.Role;
            // Spawn without a captain: the temporary spawn id can equal a saved id already loaded (ids have gaps
            // once ships sink or slip away), and registering or removing under it took that ship's captain.
            var ship = w.Spawn(o.Hull, new Vec2(o.PosX, o.PosY), o.Heading, (Faction)o.Faction, null, crew: o.Crew, cannons: o.Cannons);
            ship.Id = o.Id;
            w.Captains[ship.Id] = CaptainFor(role);
            ship.Vel = new Vec2(o.VelX, o.VelY);
            ship.AngVel = o.AngVel;
            ship.Rudder = o.Rudder;
            ship.HullHp = o.HullHp;
            ship.Water = o.Water;
            ship.Leaks = o.Leaks;
            ship.Timber = o.Timber;
            ship.SailTarget = o.SailTarget;
            ship.SailFraction = o.SailFraction;
            ship.DamageMult = o.DamageMult;
            ship.PlayerHostile = o.PlayerHostile;
            ship.TornSails = o.TornSails;
            ship.Order = (CrewOrder)o.Order;
            if (o.Loaded.Length == 2) o.Loaded.CopyTo(ship.Loaded, 0);
            if (o.Reload.Length == 2) o.Reload.CopyTo(ship.Reload, 0);
            ship.CarpenterWork = o.CarpenterWork;
            ship.PatchWork = o.PatchWork;
            ship.RamCooldown = o.RamCooldown;
            ship.Foundering = o.Foundering;
            ship.Hourglass = o.Hourglass;
            ship.WaterOnlyStand = o.WaterOnlyStand;
            ship.SetCoarseWind(new Wind(o.WindDir, o.WindSpeed), o.AngleOff);
            var path = new List<Vec2>();
            for (int i = 0; i + 1 < o.Path.Count; i += 2) path.Add(new Vec2(o.Path[i], o.Path[i + 1]));
            ship.Ai = new AiState
            {
                Role = role, HomePort = o.HomePort, DestPort = o.DestPort, CargoGood = (Good)o.CargoGood, CargoUnits = o.CargoUnits, Wait = o.Wait, Scale = o.Scale,
                Path = path, PathIndex = o.PathIndex, Repath = o.Repath, Tack = o.Tack, TackHold = o.TackHold, TargetShip = o.TargetShip, Lost = o.Lost,
                Waypoint = new Vec2(o.WaypointX, o.WaypointY), HasWaypoint = o.HasWaypoint, Fleeing = o.Fleeing, FleeTime = o.FleeTime,
            };
            byId[ship.Id] = ship;
        }
        foreach (var o in s.Ships)
            if (o.LastHitBy >= 0 && byId.TryGetValue(o.LastHitBy, out var by)) byId[o.Id].LastHitBy = by;
        // The logbook only asks whether a ship's guns hurt her; one that has since sunk or sailed off stands in as a hulk.
        if (s.PlayerLastHitBy > 0)
            w.Ship.LastHitBy = byId.TryGetValue(s.PlayerLastHitBy, out var hitBy) ? hitBy : new Ship(Hulls.Sloop, w.Ship.Pos, 0) { Id = s.PlayerLastHitBy, Sunk = true };
        w.nextShipId = Math.Max(s.NextShipId, w.nextShipId);
        foreach (var f in s.Flotsam)
            w.Flotsam.Add(new Flotsam { Pos = new Vec2(f.X, f.Y), Good = f.Good >= 0 ? (Good)f.Good : null, Units = f.Units, Gold = f.Gold, Life = f.Life, Bob = f.Bob });
        foreach (var b in s.Balls)
            w.Balls.Add(new Cannonball { Pos = new Vec2(b.X, b.Y), Vel = new Vec2(b.VX, b.VY), Life = b.Life, Shooter = b.Shooter >= 0 && byId.TryGetValue(b.Shooter, out var sh) ? sh : null, Damage = b.Damage, Delay = b.Delay });
        foreach (var (k, v) in s.FortClocks) w.fortClocks[k] = v;
        w.Lantern = s.Lantern;
        w.DirectorEnabled = s.DirectorEnabled;
        w.MonstersEnabled = s.MonstersEnabled;
        foreach (var u in s.Unique) if (BlackMarketDef.All.Any(d => d.Key == u)) w.Player.Unique.Add(u);   // the bilge engine is gone
        foreach (var c in s.Cosmetics) w.Player.Cosmetics.Add(c);
        for (int i = 0; i < w.Player.Loadout.Length && i < s.Loadout.Count; i++) w.Player.Loadout[i] = s.Loadout[i] ?? "";
        w.Player.BottleMaps.AddRange(s.BottleMaps);
        w.Player.CoveHints.AddRange(s.CoveHints);
        w.Digging = s.Digging;
        w.digWreck = s.DigWreck;
        w.DigProgress = s.DigProgress;
        if (s.Parts.Length == 8) s.Parts.CopyTo(w.Ship.Parts, 0);
        else if (s.Parts.Length == 9) s.Parts.Where((_, i) => i != 6).ToArray().CopyTo(w.Ship.Parts, 0);   // older saves carried the pumps at 6
        if (s.CustomStations.Length == 4) s.CustomStations.CopyTo(w.Ship.CustomStations, 0);
        foreach (var o in s.Officers) if (o.Length == 2) w.Player.Officers.Add(new Officer { Type = (OfficerType)o[0], Tier = o[1] });
        if (!s.CartographerRule && !w.HasCartographer) w.Player.Officers.Add(new Officer { Type = OfficerType.Cartographer, Tier = 0 });
        foreach (var h in s.HullsOwned) w.Stats.HullsOwned.Add(h);
        w.marinesClock = s.MarinesClock;
        w.ApplyUnique();
        w.Ship.HullHp = s.HullHp;
        w.ApplyOfficers();
        foreach (var a in s.Achievements) w.Player.Achievements.Add(a);
        w.Stats.MonstersBeaten = s.MonstersBeaten;
        w.MonsterClock = s.MonsterClock;
        w.EruptionClock = s.EruptionClock;
        w.RudderJam = s.RudderJam;
        if (s.MonsterType >= 0 && s.MonsterData.Length >= 14)
        {
            var d = s.MonsterData;
            var m = new Monster
            {
                Type = (MonsterType)s.MonsterType, Pos = new Vec2(d[0], d[1]), Heading = d[2], State = (MonsterState)(int)d[3], Timer = d[4], Hp = d[5],
                Surfaced = d[6] > 0, FlareTimer = d[7], FlareClock = d[8], Age = d[9], Bites = (int)d[10], Perch = new Vec2(d[11], d[12]), GunClock = d[13],
                Side = d.Length > 14 ? (int)d[14] : 1,
            };
            foreach (var t in s.MonsterTargets)
                if (t.Length >= 4) m.Targets.Add(new MonsterTarget { Pos = new Vec2(t[0], t[1]), Radius = t[2], Hp = t[3] });
            w.Monster = m;
        }
        foreach (var e in s.Eruptions)
            if (e.Length >= 6) w.Eruptions.Add(new Eruption { Pos = new Vec2(e[0], e[1]), Radius = e[2], Warning = e[3], Landed = e[4] > 0, Age = e[5] });
        w.Ship.TornSails = s.TornSails;
        w.Weather.SpawnClock = s.StormClock;
        foreach (var c in s.Storms)
            if (c.Length >= 6) w.Weather.Storms.Add(new StormCell { Pos = new Vec2(c[0], c[1]), Radius = c[2], Strength = c[3], Life = c[4], Age = c[5] });
        w.DockedByAHair = s.DockedByAHair;
        foreach (var id in s.RescuedAt) w.RescuedAt.Add(id);
        w.RunOver = s.RunOver;
        w.CauseOfSinking = s.CauseOfSinking;
        w.SirenPull = s.SirenPull;
        w.LastMonsterHit = (MonsterType)s.LastMonsterHit;
        w.MonsterHitTime = s.MonsterHitTime;
        w.WhirlpoolHitTime = s.WhirlpoolHitTime;
        w.coreClock = s.WhirlpoolCoreClock;
        w.IceHitTime = s.IceHitTime;
        for (int i = 0; i < Math.Min(s.PricesLastVisit.Count, w.Map.Ports.Count); i++)
            if (s.PricesLastVisit[i] is { } prices && prices.Length == Goods.Count) w.Map.Ports[i].PricesLastVisit = prices;
        foreach (var lane in s.Lanes)
        {
            if (lane.Length < 2 || lane.Length % 2 != 0) continue;
            var path = new List<Vec2>();
            for (int i = 2; i + 1 < lane.Length; i += 2) path.Add(new Vec2(lane[i], lane[i + 1]));
            w.routeCache[((int)lane[0], (int)lane[1])] = path;
        }
        w.Player.Contracts.AddRange(s.Contracts.Select(c => c.Clone()));
        foreach (var k in s.TakenOffers) w.Player.TakenOffers.Add(k);
        w.Player.BountyOwed = s.BountyOwed;
        w.Player.BountyShips = s.BountyShips;
        foreach (var id in s.SearchedBy) w.searchedBy.Add(id);
        w.VisionRadius = s.VisionRadius > 0 ? s.VisionRadius : w.VisionAt(w.Ship.Pos);
        return w;
    }

    /// <summary>
    /// A save that parses but names things that cannot exist (a port, map, good, officer or beast beyond the tables)
    /// is refused here, while the caller can still keep the file and the title — not on the first tick after the resume.
    /// </summary>
    static void Check(SaveData s)
    {
        static void Need(bool ok, string what) { if (!ok) throw new InvalidDataException($"corrupt save: {what}"); }
        static bool In(int v, int count) => v >= 0 && v < count;
        Need(Enum.IsDefined((Preset)s.Preset), $"preset {s.Preset}");
        Need(s.Rng.Length == 4, "random state");
        Need(Hulls.Exists(s.Hull) && (s.StartHull.Length == 0 || Hulls.Exists(s.StartHull)), $"hull {s.Hull}");
        Need(s.Officers.All(o => o.Length == 2 && Enum.IsDefined((OfficerType)o[0]) && In(o[1], Officers.Price.Length)), "officer");
        Need(s.MonsterType < 0 || MonsterDefs.All.Any(m => (int)m.Type == s.MonsterType), $"monster {s.MonsterType}");
        Need(s.Flotsam.All(f => f.Good < 0 || In(f.Good, Goods.Count)), "flotsam good");
        Need(s.Contracts.All(c => Enum.IsDefined(c.Kind) && c.Slots >= 0), "contract");
        foreach (var o in s.Ships)
        {
            Need(Hulls.Exists(o.Hull) && Enum.IsDefined((Role)o.Role) && Enum.IsDefined((Faction)o.Faction) && In(o.CargoGood, Goods.Count), $"ship {o.Id}");
            Need(o.HomePort >= -1 && o.DestPort >= -1, $"ship {o.Id} ports");
            Need(o.HomePort >= 0 || (Role)o.Role is not (Role.Merchant or Role.Patrol or Role.Raider), $"ship {o.Id} has no home port");
        }
    }

    /// <summary>The save's references into the regenerated map, checked once the map exists.</summary>
    static void CheckAgainst(SaveData s, Map map)
    {
        static void Need(bool ok, string what) { if (!ok) throw new InvalidDataException($"corrupt save: {what}"); }
        static bool In(int v, int count) => v >= 0 && v < count;
        int ports = map.Ports.Count;
        Need(s.Docked < ports && s.DiscoveredPorts.All(i => In(i, ports)) && s.PortsVisited.All(i => In(i, ports)), "port index");
        Need(s.DugTreasures.All(i => In(i, map.Treasures.Count)) && s.BottleMaps.All(m => In(m.Treasure, map.Treasures.Count)), "treasure index");
        Need(s.SalvagedWrecks.All(i => In(i, map.Wrecks.Count)), "wreck index");
        Need(s.CoveHints.All(h => In(h.Port, ports)) && s.Ledger.All(e => In(e.Port, ports) && In((int)e.Good, Goods.Count)), "ledger or rumour port");
        Need(s.Ships.All(o => o.HomePort < ports && o.DestPort < ports), "ship port");
        Need(s.Contracts.All(c => In(c.From, ports) && In(c.To, ports)), "contract port");
    }

    /// <summary>
    /// The chart's ink, deflated ("z:" + base64): on the 18 × 13.5 km chart the raw mask is 190 KB, mostly blank, and it
    /// packs to a few KB. Saves from before carry plain base64, which still loads.
    /// </summary>
    static string PackReveal(byte[] bits)
    {
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            z.Write(bits, 0, bits.Length);
        return "z:" + Convert.ToBase64String(ms.ToArray());
    }

    static byte[] UnpackReveal(string s)
    {
        if (!s.StartsWith("z:")) return Convert.FromBase64String(s);
        using var z = new System.IO.Compression.DeflateStream(new MemoryStream(Convert.FromBase64String(s[2..])), System.IO.Compression.CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    public static World LoadJson(string json) =>
        FromSave(JsonSerializer.Deserialize<SaveData>(json, JsonOptions) ?? throw new InvalidDataException("empty save"));
}
