namespace LastTide.Sim;

public enum Preset { CalmSeas, RoughSeas, Tempest }

/// <summary>Difficulty (GDD §11): presets set the daily Threat growth; tiers name the pressure.</summary>
public static class Threat
{
    public static readonly double[] GrowthPerDay = { 0.10, 0.15, 0.22 };
    public static readonly double[] TierStart = { 1.0, 1.5, 2.0, 2.75, 3.5, 4.5, 6.0, 8.0, 10.0 };
    public static readonly string[] TierKey = { "flat_calm", "light_airs", "freshening", "choppy", "squall", "gale", "storm", "maelstrom", "last_tide" };

    public static double Of(Preset preset, double days) => 1 + GrowthPerDay[(int)preset] * days;

    public static int Tier(double threat)
    {
        int t = 0;
        for (int i = 0; i < TierStart.Length; i++)
            if (threat >= TierStart[i]) t = i;
        return t;
    }

    /// <summary>+10% enemy hull and damage per +1.0 of Threat.</summary>
    public static double EnemyScale(double threat) => 1 + 0.1 * (threat - 1);
}

/// <summary>A spawn card (GDD §11): what the director can buy, at what price, from which Threat.</summary>
public sealed record SpawnCard(string Key, int Cost, double Unlock, bool NeedsCrownHostile, string[] Hulls, Faction Faction, Role Role);

/// <summary>
/// The director (GDD §11): earns credits per second times Threat and spends them on spawn cards
/// placed just outside the player's vision. Monsters have their own clock (M7).
/// </summary>
public sealed class Director
{
    public const double CreditsPerSecond = 0.06;
    public const double CheckEvery = 10;
    public const int MaxHunters = 6;

    public static readonly SpawnCard[] Cards =
    {
        new("brethren_sloop", 10, 1.0, false, new[] { "sloop" }, Faction.Brethren, Role.Hunter),
        new("brethren_brig", 30, 2.0, false, new[] { "brig" }, Faction.Brethren, Role.Hunter),
        new("crown_cutter", 15, 1.0, true, new[] { "cutter" }, Faction.Crown, Role.Hunter),
        new("crown_frigate", 60, 2.75, true, new[] { "frigate" }, Faction.Crown, Role.Hunter),
        new("hunter_pack", 80, 3.5, false, new[] { "cutter", "cutter", "brig" }, Faction.Brethren, Role.Privateer),
        new("brethren_galleon", 120, 4.5, false, new[] { "galleon" }, Faction.Brethren, Role.Hunter),
    };

    public double Credits;
    public double Clock;
    public int Spawned;
    public string LastCard = "";

    public IEnumerable<SpawnCard> Affordable(double threat, bool crownHostile) =>
        Cards.Where(c => threat >= c.Unlock && (!c.NeedsCrownHostile || crownHostile) && c.Cost <= Credits);

    /// <summary>Accrues credits; returns a card to spawn when the clock and the purse allow.</summary>
    /// <summary>Seconds of calm after a hunter gives up or goes down: the next card waits (GDD §19 tuning).</summary>
    public const double QuietAfterHunter = 150;
    public double QuietFor;

    /// <param name="creditMult">Night presses harder (GDD §19: credits ×1.5); it earns faster but unlocks no card early
    /// and raises no cap, which the true <paramref name="threat"/> decides (audit C-05).</param>
    public SpawnCard? Tick(double dt, double threat, bool crownHostile, int huntersAlive, Rng rng, double creditMult = 1)
    {
        Credits = Math.Min(Credits + CreditsPerSecond * threat * creditMult * dt, 300);
        Clock -= dt;
        QuietFor = Math.Max(0, QuietFor - dt);
        if (Clock > 0 || QuietFor > 0) return null;
        Clock = CheckEvery;
        int cap = Math.Min(MaxHunters, 1 + (int)Math.Floor(threat));   // 2 at Threat 1, 6 from Threat 5
        if (huntersAlive >= cap) return null;
        // A card must fit under the cap whole: a three-ship pack is not dealt with one berth left (audit C-11).
        var options = Affordable(threat, crownHostile).Where(c => huntersAlive + c.Hulls.Length <= cap).ToList();
        if (options.Count == 0) return null;
        // Prefer the dearer cards as the Threat climbs: weight = cost^threat-ish, kept gentle.
        double total = 0;
        var weights = options.Select(c => Math.Pow(c.Cost, Math.Min(2, threat * 0.5))).ToList();
        foreach (var w in weights) total += w;
        double roll = rng.NextDouble() * total;
        for (int i = 0; i < options.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0)
            {
                Credits -= options[i].Cost;
                Spawned++;
                LastCard = options[i].Key;
                return options[i];
            }
        }
        return null;
    }
}
