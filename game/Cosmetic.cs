using Godot;

namespace LastTide;

/// <summary>Colours for the cosmetic keys (GDD §14). Plain ("") is the ink defaults.</summary>
public static class Cosmetic
{
    public static readonly Color Gold = new(0.80f, 0.64f, 0.22f);

    public static Color Hull(string key) => key switch
    {
        "hull_black" => new Color(0.24f, 0.21f, 0.18f),
        "hull_green" => new Color(0.31f, 0.43f, 0.31f),
        "hull_red" => new Color(0.58f, 0.25f, 0.18f),
        _ => Ink.Hull,
    };

    public static Color Sail(string key) => key switch
    {
        "sails_ochre" => new Color(0.87f, 0.72f, 0.42f),
        "sails_indigo" => new Color(0.40f, 0.46f, 0.64f),
        _ => Ink.Sail,
    };

    public static bool Striped(string key) => key == "sails_striped";

    public static Color Flag(string key) => key switch
    {
        "flag_crimson" => Ink.Red,
        "flag_black" => Ink.Black,
        "flag_gold" => Gold,
        _ => Ink.Paper,
    };

    public static Color Wake(string key) => key switch
    {
        "wake_gold" => Gold,
        "wake_crimson" => Ink.Red,
        "wake_indigo" => Ink.Wind,
        _ => Ink.Black,
    };
}
