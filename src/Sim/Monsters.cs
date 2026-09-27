namespace LastTide.Sim;

public enum MonsterState { Approach, Surfaced, Dive, Grip, Retreat, Perched, Faded }

/// <summary>One row of the monster table (GDD §12): where it lives, how tough it is, what beats it.</summary>
public sealed record MonsterDef(MonsterType Type, string Key, RegionType Region, double Hp, string Achievement, string EscapeAchievement);

public static class MonsterDefs
{
    public static readonly MonsterDef[] All =
    {
        new(MonsterType.ReefSerpent, "reef_serpent", RegionType.Shoals, 36, "serpent_slayer", ""),
        new(MonsterType.Kraken, "kraken", RegionType.Deep, 80, "", "unkrakened"),
        new(MonsterType.GhostShip, "ghost_ship", RegionType.FogBanks, 50, "lay_the_ghost", ""),
        new(MonsterType.Crocodile, "crocodile", RegionType.Mangrove, 40, "handbags", ""),
        new(MonsterType.WeedKraken, "weed_kraken", RegionType.Sargasso, 50, "", "weeded_out"),
        new(MonsterType.Siren, "siren", RegionType.SirenRuins, 50, "deaf_ears", ""),
    };

    public static MonsterDef Of(MonsterType t) => All.First(m => m.Type == t);
}

/// <summary>A shootable part of a monster: the beast itself, a tentacle, a weed mass, a rock perch.</summary>
public sealed class MonsterTarget
{
    public Vec2 Pos;
    public double Radius;
    public double Hp;
    public bool Alive => Hp > 0;
}

/// <summary>A sea monster in the act (GDD §12). Behaviour lives in <see cref="World"/>; this is its state.</summary>
public sealed class Monster
{
    public MonsterType Type;
    public MonsterDef Def => MonsterDefs.Of(Type);
    public Vec2 Pos;
    public double Heading;
    public MonsterState State = MonsterState.Approach;
    public double Timer;
    public double Hp;
    public bool Surfaced;         // visible and shootable
    public double FlareTimer;     // ghost ship: seconds left in a lantern flare (0 = none)
    public double FlareClock;     // seconds until the next flare
    public double Age;
    public int Bites;
    public int Side = 1;          // which side it works from
    public readonly List<MonsterTarget> Targets = new();
    public Vec2 Perch;            // siren's rock / crocodile's bank
    public double GunClock;       // ghost ship broadsides
    public bool Done;             // beaten or escaped; the world removes it
    public bool Beaten;

    public bool Visible(Conditions c) => Type == MonsterType.GhostShip ? (c.Fog > 0.05 || c.Night) && Surfaced : Surfaced;
}

/// <summary>A telegraphed rock shower in the Volcanic Isles (GDD §12): a marked zone, then the rocks land.</summary>
public sealed class Eruption
{
    public Vec2 Pos;
    public double Radius;
    public double Warning;   // seconds until impact
    public bool Landed;
    public double Age;
}
