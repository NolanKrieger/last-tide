namespace LastTide.Sim;

public enum Part { Sails, Rigging, Planking, Copper, Cannons, Ram, Hold, Lantern }

/// <summary>One row of the parts table (GDD §7): eight parts, five grades, price scaling with hull size.</summary>
public sealed record PartDef(Part Id, string Key, int BasePrice, bool MovesWithCaptain, bool PerGun)
{
    public static readonly PartDef[] All =
    {
        new(Part.Sails, "sails", 120, false, false),
        new(Part.Rigging, "rigging", 120, false, false),
        new(Part.Planking, "planking", 150, false, false),
        new(Part.Copper, "copper", 180, false, false),
        new(Part.Cannons, "cannons", 60, true, true),
        new(Part.Ram, "ram", 100, false, false),
        new(Part.Hold, "hold", 120, false, false),
        new(Part.Lantern, "lantern", 90, true, false),
    };

    public const int MaxGrade = 5;
    public static PartDef Of(Part p) => All[(int)p];

    /// <summary>Gold for the next grade on this hull: base × grade × a hull factor (0.6 for the sloop, 2.1 for the man-o'-war).</summary>
    public static int Price(Part part, int nextGrade, HullDef hull, int cannons = 1)
    {
        double hullFactor = 0.6 + hull.Cost / 40000.0;
        var def = Of(part);
        double price = def.BasePrice * nextGrade * hullFactor;
        if (def.PerGun) price *= Math.Max(1, cannons);
        return (int)Math.Round(price / 5) * 5;
    }

    public static readonly int[] PounderByGrade = { 4, 6, 9, 12, 18, 24 };
}

public enum OfficerType { Lookout, Marines, Quartermaster, Cartographer }

/// <summary>A hired officer (GDD §7): one slot per type, three tiers. The cartographer has a berth of his own (no slot).</summary>
public sealed class Officer
{
    public OfficerType Type { get; set; }
    public int Tier { get; set; }   // 0 green, 1 seasoned, 2 legendary
}

public static class Officers
{
    public static readonly int[] Price = { 60, 150, 400 };
    public static readonly int[] Wage = { 4, 8, 15 };
    public static readonly string[] TierKey = { "green", "seasoned", "legendary" };
    public static readonly double[] LookoutVision = { 0.15, 0.30, 0.50 };
    public static readonly double[] LookoutWarning = { 100, 200, 300 };
    public static readonly int[] MarinesKills = { 1, 2, 3 };
    public static readonly double[] QuartermasterPrices = { 0.03, 0.06, 0.10 };
    public static readonly double[] QuartermasterWages = { 0.10, 0.20, 0.30 };
    public static readonly double[] QuartermasterProvisions = { 0.15, 0.30, 0.50 };
    public const double MarinesRange = 120, MarinesPeriod = 6;

    // ---- The cartographer (Nolan, 2026-09-27; GDD §7, §19): without one nothing is charted and the chart stays shut ----
    /// <summary>How far he inks, as a multiple of the vision radius: green 1.0, seasoned 1.25, legendary 1.5.</summary>
    public static readonly double[] CartographerReach = { 1.0, 1.25, 1.5 };
    public static readonly int[] CartographerPrice = { 40, 120, 320 };
    public static readonly int[] CartographerWage = { 3, 6, 12 };

    /// <summary>Gold to hire, per type (in <see cref="OfficerType"/> order) and tier.</summary>
    public static readonly int[][] PriceByType = { Price, Price, Price, CartographerPrice };
    /// <summary>Wages a day, per type and tier.</summary>
    public static readonly int[][] WageByType = { Wage, Wage, Wage, CartographerWage };
    /// <summary>Whether the type fills one of the hull's officer slots; the cartographer berths apart (Nolan).</summary>
    public static readonly bool[] TakesSlot = { true, true, true, false };

    public static int PriceOf(OfficerType type, int tier) => PriceByType[(int)type][tier];
    public static int WageOf(OfficerType type, int tier) => WageByType[(int)type][tier];
    public static bool UsesSlot(OfficerType type) => TakesSlot[(int)type];
}
