namespace LastTide.Sim;

/// <summary>Invented port names in a golden-age register. Nothing here is a real place or a borrowed one.</summary>
public static class Names
{
    static readonly string[] Onsets = { "Al", "Bar", "Cal", "Dor", "El", "Far", "Gal", "Har", "Is", "Jor", "Kel", "Lor", "Mar", "Nor", "Or", "Pel", "Quin", "Ral", "Sal", "Tor", "Ul", "Val", "Wen", "Yar", "Bre", "Cor", "Sel", "Tam" };
    static readonly string[] Middles = { "an", "en", "in", "on", "ar", "or", "ith", "oth", "el", "am", "um", "is", "ev", "ol" };
    static readonly string[] Ends = { "a", "o", "ia", "ey", "wick", "ford", "mouth", "ton", "by", "hold", "more", "ry", "ance", "ver", "den" };

    static readonly string[] ShipFirst = { "Fair", "Grey", "Black", "Restless", "Salt", "Silver", "Wandering", "Bold", "Quiet", "Golden", "Northern", "Last", "Little", "Proud", "Merry", "Iron", "Green", "Red", "Lucky", "Patient" };
    static readonly string[] ShipSecond = { "Margaret", "Gull", "Kestrel", "Tern", "Heron", "Fortune", "Venture", "Swallow", "Petrel", "Wren", "Anne", "Constance", "Promise", "Hope", "Lantern", "Compass", "Mercy", "Harriet", "Dolphin", "Plover" };
    static readonly string[] ShipAlone = { "Kestrel", "Second Chance", "Wayfarer", "Sea Wren", "Tide Runner", "Morning Star", "Providence", "Endeavour", "Resolution", "Halcyon", "Vixen", "Brisk", "Dart", "Swift", "Pelican" };

    /// <summary>A ship name for the naming screen; the player can type over it.</summary>
    public static string Ship(Rng rng) =>
        rng.NextDouble() < 0.35 ? ShipAlone[rng.Next(ShipAlone.Length)] : ShipFirst[rng.Next(ShipFirst.Length)] + " " + ShipSecond[rng.Next(ShipSecond.Length)];

    public static string Stem(Rng rng)
    {
        string s = Onsets[rng.Next(Onsets.Length)];
        if (rng.NextDouble() < 0.6) s += Middles[rng.Next(Middles.Length)];
        s += Ends[rng.Next(Ends.Length)];
        return s;
    }

    public static string Port(Rng rng, Faction faction, bool secret, HashSet<string> taken)
    {
        for (int tries = 0; tries < 50; tries++)
        {
            string stem = Stem(rng);
            string name;
            if (secret)
                name = rng.NextDouble() < 0.5 ? $"{stem} Cove" : $"Smugglers' {stem}";
            else
                name = faction switch
                {
                    Faction.Crown => rng.NextDouble() switch
                    {
                        < 0.35 => $"Port {stem}",
                        < 0.6 => $"Fort {stem}",
                        < 0.8 => $"Saint {stem}",
                        _ => $"{stem} Colony",
                    },
                    Faction.Brethren => rng.NextDouble() switch
                    {
                        < 0.4 => $"{stem} Haven",
                        < 0.7 => $"Black {stem}",
                        _ => $"{stem}'s Landing",
                    },
                    _ => rng.NextDouble() switch
                    {
                        < 0.35 => $"{stem} Bay",
                        < 0.6 => $"Cape {stem}",
                        < 0.8 => $"{stem} Harbour",
                        _ => stem,
                    },
                };
            if (taken.Add(name)) return name;
        }
        string fallback = $"{Stem(rng)} {taken.Count}";
        taken.Add(fallback);
        return fallback;
    }
}
