namespace LastTide.Sim;

public enum RegionType { TradeIsles, Shoals, Deep, FogBanks, StormReach, Mangrove, Volcanic, Sargasso, SirenRuins, IceReach, Maelstrom, CorsairKeys }

public enum MonsterType { None, ReefSerpent, Kraken, GhostShip, Crocodile, WeedKraken, Siren }

/// <summary>
/// One row of the region table (GDD §5): its islands' size and roughness, its ports' count and size, what they
/// produce and consume, which hazard lives there, its faction mix (<see cref="CrownShare"/> and
/// <see cref="FreeShare"/>; the rest are Brethren) and how hard the director's raiders press there
/// (<see cref="Raiders"/>, a multiplier on spawn credits). Island layout lives in <see cref="RegionLayout"/>; weather
/// with the weather (M6).
/// </summary>
public sealed record RegionDef(
    RegionType Id, string Key,
    double IslandSpacing, double IslandMinR, double IslandMaxR, double Channel, double Roughness,
    int PortsMin, int PortsMax, int PortSizeBias,
    Good[] Produces, Good[] Consumes, MonsterType Monster, double CoveWeight, double TreasureWeight,
    double CrownShare = 0.44, double FreeShare = 0.39, double Raiders = 1)
{
    public static readonly RegionDef[] All =
    {
        new(RegionType.TradeIsles, "trade_isles", 420, 90, 220, 170, 0.28, 20, 24, 1,
            new[] { Good.Provisions, Good.Rum, Good.Sugar, Good.Molasses, Good.Cloth, Good.Tools, Good.HempRope },
            new[] { Good.Timber, Good.Sailcloth, Good.Iron, Good.Tobacco, Good.Coffee, Good.Wine, Good.Silk, Good.Porcelain, Good.Spices, Good.Muskets },
            MonsterType.None, 1, 1),
        new(RegionType.Shoals, "shoals", 190, 40, 120, 80, 0.22, 12, 15, 0,
            new[] { Good.Provisions, Good.Pearls, Good.HempRope, Good.Spices },
            new[] { Good.Tools, Good.Cloth, Good.Rum, Good.Timber, Good.Muskets },
            MonsterType.ReefSerpent, 1, 1),
        new(RegionType.Deep, "deep", 720, 120, 260, 300, 0.30, 6, 8, 1,
            new[] { Good.Sailcloth, Good.PitchTar, Good.HempRope, Good.Silk },
            new[] { Good.Provisions, Good.Rum, Good.Tobacco, Good.Sugar, Good.Wine },
            MonsterType.Kraken, 1, 0.5),
        new(RegionType.FogBanks, "fog_banks", 430, 80, 200, 150, 0.34, 10, 13, 0,
            new[] { Good.Timber, Good.PitchTar, Good.Porcelain, Good.Cotton },
            new[] { Good.Muskets, Good.Cloth, Good.Tools, Good.Provisions, Good.Coffee },
            MonsterType.GhostShip, 3, 1),
        new(RegionType.StormReach, "storm_reach", 520, 100, 240, 180, 0.30, 10, 13, 2,
            new[] { Good.Wine, Good.Muskets, Good.Coffee, Good.Iron },
            new[] { Good.Silk, Good.Porcelain, Good.Spices, Good.Pearls, Good.Mahogany, Good.Indigo, Good.Ambergris, Good.Emeralds, Good.Relics, Good.Cocoa },
            MonsterType.None, 1, 1),
        new(RegionType.Mangrove, "mangrove", 160, 45, 110, 55, 0.45, 9, 11, 0,
            new[] { Good.Timber, Good.Mahogany, Good.Indigo, Good.Cocoa },
            new[] { Good.Tools, Good.Muskets, Good.Provisions, Good.Rum, Good.Cloth },
            MonsterType.Crocodile, 3, 1),
        new(RegionType.Volcanic, "volcanic", 600, 150, 330, 220, 0.40, 6, 8, 1,
            new[] { Good.Iron, Good.Munitions, Good.PitchTar },
            new[] { Good.Provisions, Good.Timber, Good.Sailcloth, Good.Rum, Good.Cotton },
            MonsterType.None, 0.5, 0.5),
        new(RegionType.Sargasso, "sargasso", 620, 60, 140, 260, 0.26, 5, 7, 0,
            new[] { Good.HempRope, Good.Sailcloth, Good.Molasses },
            new[] { Good.Provisions, Good.Rum, Good.Tools, Good.Timber, Good.Iron },
            MonsterType.WeedKraken, 1, 0.5),
        new(RegionType.SirenRuins, "siren_ruins", 300, 30, 130, 110, 0.36, 9, 11, 0,
            new[] { Good.Cotton, Good.Tobacco, Good.Sugar, Good.Coffee },
            new[] { Good.Tools, Good.Wine, Good.Cloth, Good.Muskets, Good.Provisions },
            MonsterType.Siren, 1, 4),
        new(RegionType.IceReach, "ice_reach", 460, 90, 260, 160, 0.38, 7, 9, 1,
            new[] { Good.Timber, Good.Iron, Good.PitchTar, Good.Provisions },
            new[] { Good.Rum, Good.Cloth, Good.Coffee, Good.Wine, Good.Tobacco, Good.Sugar },
            MonsterType.None, 1, 0.8),
        new(RegionType.Maelstrom, "maelstrom", 380, 80, 220, 130, 0.36, 8, 10, 1,
            new[] { Good.Wine, Good.Porcelain, Good.Munitions, Good.HempRope },
            new[] { Good.Provisions, Good.Timber, Good.Sailcloth, Good.Tools, Good.Sugar },
            MonsterType.None, 1, 1.2),
        new(RegionType.CorsairKeys, "corsair_keys", 220, 40, 150, 90, 0.26, 12, 15, 0,
            new[] { Good.Rum, Good.Muskets, Good.Cocoa, Good.Sugar },
            new[] { Good.Munitions, Good.Provisions, Good.Tools, Good.Sailcloth, Good.Silk, Good.Wine },
            MonsterType.None, 2.5, 1.5, CrownShare: 0.08, FreeShare: 0.30, Raiders: 1.8),
    };

    public static RegionDef Of(RegionType t) => All[(int)t];

    /// <summary>Hulls that fit the Mangrove Maze's channels (GDD §5).</summary>
    public static readonly HashSet<string> MangroveHulls = new() { "sloop", "cutter", "schooner", "xebec", "brigantine" };
}
