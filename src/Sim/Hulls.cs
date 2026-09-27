namespace LastTide.Sim;

public enum Rig { ForeAndAft, Lateen, Mixed, Square }

/// <summary>One row of the hull table (GDD §7). Speed and Turn are relative to the sloop.</summary>
public sealed record HullDef(
    string Id, int Cost, int HullHp, int Cargo, int CrewMax, int GunsPerSide,
    double Speed, double Turn, double PointDeg, Rig Rig, string Niche,
    double Length, double Beam, int Riggers, int OfficerSlots)
{
    /// <summary>m/s at polar 1.0, full sail, standard wind.</summary>
    public double TopSpeed => Tuning.SloopTopSpeed * Speed;
    /// <summary>rad/s at ⅓ sail, full rudder, full authority.</summary>
    public double TurnRate => Tuning.SloopTurnRate * Turn;
    /// <summary>Relative mass; heavier hulls gather and shed way more slowly.</summary>
    public double Inertia => HullHp / 100.0;
}

/// <summary>The 14 hulls, decided in GDD §7. Prices and per-slot values as drafted; stats are v1 tuning.</summary>
public static class Hulls
{
    public static readonly HullDef[] All =
    {
        new("sloop",         0, 100,  20,  10,  2, 1.00, 1.00, 40, Rig.ForeAndAft, "all-rounder",        16,  5.0,  2, 1),
        new("cutter",      800, 110,  25,  12,  2, 1.10, 1.10, 38, Rig.ForeAndAft, "courier, escapes",   18,  5.5,  2, 1),
        new("schooner",   1500, 140,  40,  16,  3, 1.05, 0.90, 40, Rig.ForeAndAft, "all-rounder",        24,  6.5,  3, 1),
        new("xebec",      3000, 150,  30,  30,  4, 1.15, 1.00, 45, Rig.Lateen,     "raider",             30,  7.0,  4, 1),
        new("brigantine", 4000, 200,  70,  24,  5, 0.95, 0.75, 50, Rig.Mixed,      "all-rounder",        30,  8.0,  5, 2),
        new("fluyt",      6000, 220, 150,  20,  2, 0.85, 0.60, 55, Rig.Square,     "hauler",             32,  9.0,  5, 2),
        new("brig",       9000, 280, 100,  36,  7, 0.90, 0.65, 55, Rig.Square,     "all-rounder",        34,  9.0,  7, 2),
        new("barque",    11000, 260, 180,  30,  5, 0.90, 0.60, 50, Rig.Mixed,      "long-haul trader",   40, 10.0,  8, 2),
        new("corvette",  14000, 300,  80,  45,  9, 1.10, 0.70, 55, Rig.Square,     "hunter",             40, 10.0,  9, 2),
        new("frigate",   20000, 400, 140,  60, 12, 1.00, 0.55, 60, Rig.Square,     "warship",            48, 12.0, 12, 3),
        new("indiaman",  28000, 450, 300,  70, 10, 0.80, 0.45, 65, Rig.Square,     "armed mega-hauler",  52, 14.0, 14, 3),
        new("heavy_frigate", 32000, 520, 160, 80, 15, 0.95, 0.50, 60, Rig.Square,  "fast heavy warship", 54, 13.0, 16, 3),
        new("galleon",   40000, 600, 260,  90, 16, 0.80, 0.40, 65, Rig.Square,     "treasure ship",      55, 15.0, 18, 3),
        new("man_o_war", 60000, 900, 120, 140, 24, 0.75, 0.35, 65, Rig.Square,     "floating fortress",  62, 16.0, 30, 3),
    };

    public static readonly HullDef Sloop = All[0];

    static readonly Dictionary<string, HullDef> byId = All.ToDictionary(h => h.Id);

    public static HullDef Get(string id) =>
        byId.TryGetValue(id, out var h) ? h : throw new KeyNotFoundException($"no hull '{id}'");

    public static bool Exists(string id) => byId.ContainsKey(id);
}
