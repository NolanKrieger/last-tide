namespace LastTide.Sim;

public enum Part { Sails, Rigging, Planking, Copper, Cannons, Ram, Pumps, Hold, Lantern }

/// <summary>One row of the parts table (GDD §7): nine parts, five grades, price scaling with hull size.</summary>
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
        new(Part.Pumps, "pumps", 80, true, false),
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

public enum OfficerType { Lookout, Marines, Quartermaster }

/// <summary>A hired officer (GDD §7): one slot per type, three tiers.</summary>
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
}
