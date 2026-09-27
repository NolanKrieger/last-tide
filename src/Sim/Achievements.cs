namespace LastTide.Sim;

/// <summary>The achievement list (GDD §14), in display order. Keys match the sim's <c>Unlock</c> calls and the string table.</summary>
public static class Achievements
{
    public static readonly string[] All =
    {
        "fair_winds", "heavy_weather", "eye_of_the_storm",
        "serpent_slayer", "unkrakened", "lay_the_ghost", "handbags", "weeded_out", "deaf_ears",
        "nine_seas", "cartographer", "smugglers_welcome", "x_marks",
        "galleon_captain", "ship_of_the_line", "by_a_hair", "last_tide",
    };

    /// <summary>The cosmetic each achievement unlocks (GDD §14: cosmetics come from achievements and digs).</summary>
    public static string? Reward(string key) => key switch
    {
        "fair_winds" => "flag_gold",
        "heavy_weather" => "sails_ochre",
        "eye_of_the_storm" => "sails_indigo",
        "serpent_slayer" => "figurehead_serpent",
        "unkrakened" => "figurehead_kraken",
        "lay_the_ghost" => "hull_black",
        "handbags" => "hull_green",
        "weeded_out" => "wake_indigo",
        "deaf_ears" => "figurehead_siren",
        "nine_seas" => "wake_gold",
        "cartographer" => "flag_black",
        "smugglers_welcome" => "hull_red",
        "x_marks" => "sails_striped",
        "galleon_captain" => "flag_crimson",
        "ship_of_the_line" => "wake_crimson",
        _ => null,
    };
}
