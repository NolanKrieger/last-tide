namespace LastTide.Sim;

public enum Good
{
    Provisions, Munitions, Timber,
    HempRope, Sailcloth, PitchTar, Iron,
    Sugar, Molasses, Tobacco, Cotton, Coffee, Cocoa, Indigo, Mahogany,
    Rum, Cloth, Tools, Muskets, Wine,
    Spices, Silk, Porcelain, Pearls,
    Ambergris, Emeralds, Relics,
}

public enum GoodGroup { Consumable, ShipStores, Produce, Manufactured, Luxury, Rare }

/// <summary>One row of the goods table (GDD §6). <see cref="SlotsPerUnit"/> encodes bulk: timber 2, pearls 0.2.</summary>
public sealed record GoodDef(Good Id, string Key, GoodGroup Group, int BasePrice, double SlotsPerUnit);

/// <summary>The 27 goods, decided in GDD §6 with base prices as drafted.</summary>
public static class Goods
{
    public static readonly GoodDef[] All =
    {
        new(Good.Provisions, "provisions", GoodGroup.Consumable, 8, 0.25),   // ship's stores pack tight: a barrel a slot
        new(Good.Munitions, "munitions", GoodGroup.Consumable, 12, 0.1),
        new(Good.Timber, "timber", GoodGroup.Consumable, 12, 2),
        new(Good.HempRope, "hemp_rope", GoodGroup.ShipStores, 15, 1),
        new(Good.Sailcloth, "sailcloth", GoodGroup.ShipStores, 20, 1),
        new(Good.PitchTar, "pitch_tar", GoodGroup.ShipStores, 14, 1),
        new(Good.Iron, "iron", GoodGroup.ShipStores, 25, 1),
        new(Good.Sugar, "sugar", GoodGroup.Produce, 18, 1),
        new(Good.Molasses, "molasses", GoodGroup.Produce, 14, 1),
        new(Good.Tobacco, "tobacco", GoodGroup.Produce, 30, 1),
        new(Good.Cotton, "cotton", GoodGroup.Produce, 22, 1),
        new(Good.Coffee, "coffee", GoodGroup.Produce, 35, 1),
        new(Good.Cocoa, "cocoa", GoodGroup.Produce, 40, 1),
        new(Good.Indigo, "indigo", GoodGroup.Produce, 55, 1),
        new(Good.Mahogany, "mahogany", GoodGroup.Produce, 45, 2),
        new(Good.Rum, "rum", GoodGroup.Manufactured, 28, 1),
        new(Good.Cloth, "cloth", GoodGroup.Manufactured, 32, 1),
        new(Good.Tools, "tools", GoodGroup.Manufactured, 40, 1),
        new(Good.Muskets, "muskets", GoodGroup.Manufactured, 70, 1),
        new(Good.Wine, "wine", GoodGroup.Manufactured, 36, 1),
        new(Good.Spices, "spices", GoodGroup.Luxury, 80, 0.2),
        new(Good.Silk, "silk", GoodGroup.Luxury, 110, 1),
        new(Good.Porcelain, "porcelain", GoodGroup.Luxury, 95, 1),
        new(Good.Pearls, "pearls", GoodGroup.Luxury, 150, 0.2),
        new(Good.Ambergris, "ambergris", GoodGroup.Rare, 220, 1),
        new(Good.Emeralds, "emeralds", GoodGroup.Rare, 260, 0.2),
        new(Good.Relics, "relics", GoodGroup.Rare, 300, 1),
    };

    public const int Count = 27;

    public static GoodDef Of(Good g) => All[(int)g];

    public static bool IsRare(Good g) => Of(g).Group == GoodGroup.Rare;
    public static bool IsLuxury(Good g) => Of(g).Group == GoodGroup.Luxury;

    /// <summary>Hold slots taken by <paramref name="units"/> of a good, rounded up.</summary>
    public static int Slots(Good g, int units) => (int)Math.Ceiling(units * Of(g).SlotsPerUnit - 1e-9);
}
