namespace LastTide.Sim;

/// <summary>
/// Cosmetics (GDD §14): five slots, chosen at run start, no stats. Keys match <see cref="World.CosmeticKeys"/>;
/// an empty choice is the plain ship. Unlocks live in the player's profile (game layer) and mirror achievements.
/// </summary>
public static class Cosmetics
{
    public static readonly string[] Slots = { "flag", "sails", "figurehead", "hull", "wake" };

    /// <summary>Every key of a slot, in unlock-table order. The first entry of each slot is the plain default.</summary>
    public static string[] Options(string slot) => slot switch
    {
        "flag" => new[] { "", "flag_crimson", "flag_black", "flag_gold" },
        "sails" => new[] { "", "sails_ochre", "sails_indigo", "sails_striped" },
        "figurehead" => new[] { "", "figurehead_serpent", "figurehead_siren", "figurehead_kraken" },
        "hull" => new[] { "", "hull_black", "hull_green", "hull_red" },
        "wake" => new[] { "", "wake_gold", "wake_crimson", "wake_indigo" },
        _ => new[] { "" },
    };

    public static int SlotIndex(string slot) => Array.IndexOf(Slots, slot);

    /// <summary>Which slot a cosmetic key belongs to, or null for an unknown key.</summary>
    public static string? SlotOf(string key)
    {
        foreach (var s in Slots)
            if (Array.IndexOf(Options(s), key) > 0) return s;
        return null;
    }
}
