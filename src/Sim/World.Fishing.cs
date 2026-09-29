namespace LastTide.Sim;

/// <summary>
/// Fishing (Nolan, 2026-09-28). Anywhere at sea, under ⅓ sail or furled, the context key puts lines out: the gun crews
/// and the spare hands fish (the riggers keep her in hand, the carpenters at their work) and the catch comes in with
/// the water's richness (<see cref="Fishing"/>). The crew eats fish before provisions, and fish spoils, so it stretches
/// a voyage rather than stocking a hold. Now and then a line brings up something else: a pearl, ambergris, a bottle
/// with a map, or, in a beast's own waters, the beast.
/// </summary>
public sealed partial class World
{
    /// <summary>Fish one hand brings in a minute on an ordinary sea, lying still by day: one a hand a day (a day is two minutes).</summary>
    public const double FishPerHandMinute = 0.5;
    /// <summary>Lines over her rails: at most this many hands fish, whatever her crew.</summary>
    public const int MaxLines = 6;
    /// <summary>Above this speed (m/s) the lines trail astern and the catch halves.</summary>
    public const double FishStillSpeed = 2.5;
    /// <summary>At night a lit lantern draws the fish to the lines (and the hunters to her).</summary>
    public const double LanternNightCatch = 2;
    /// <summary>Chances a fish is something else: a pearl (Shoals, Siren's Ruins), ambergris (the Deep), a bottle map.</summary>
    public static double PearlChance = 0.012, AmbergrisChance = 0.008, BottleMapChance = 0.004;
    /// <summary>Chance a fish in a beast's waters is the beast taking the line instead.</summary>
    public static double BeastHookChance = 0.012;
    /// <summary>Share of the fish left in the hold that spoils each midnight, after the crew has eaten: about three days.</summary>
    public const double FishSpoilShare = 1.0 / 3;

    /// <summary>The lines are out.</summary>
    public bool LinesOut { get; private set; }
    /// <summary>The next fish, part caught (0–1).</summary>
    public double FishCatch { get; private set; }

    readonly List<Fishing.Ground> fishScratch = new();

    /// <summary>Hands that fish while the lines are out: the gun crews and the spare hands, up to <see cref="MaxLines"/>.</summary>
    public int Fishers
    {
        get
        {
            Ship.Split(out int guns, out _, out _, out int spare);
            return Math.Min(guns + spare, MaxLines);
        }
    }

    /// <summary>The water under her, as a multiple of an ordinary sea's run of fish.</summary>
    public double FishRichnessHere => Fishing.Richness(Map, Ship.Pos, MidnightsPassed, fishScratch);

    /// <summary>Fish a minute the lines would bring in here and now (whether or not they are out).</summary>
    public double CatchPerMinute
    {
        get
        {
            double rate = FishPerHandMinute * Fishers * FishRichnessHere;
            if (Ship.Speed > FishStillSpeed) rate *= 0.5;
            if (IsNight && Lantern) rate *= LanternNightCatch;
            return rate;
        }
    }

    /// <summary>She may put lines out: at sea, under ⅓ sail or furled, afloat, not digging.</summary>
    public bool CanFish => Docked == null && Ship.SailTarget <= 1 && !Ship.Foundering && !Digging;

    /// <summary>The context key away from a dig or a wreck: lines out, or hauled in again.</summary>
    void ToggleLines()
    {
        if (LinesOut)
        {
            HaulIn("NOTICE_LINES_IN");
            return;
        }
        if (Docked != null || Ship.Foundering) return;
        if (Ship.SailTarget > 1) { Notices.Enqueue("NOTICE_FISH_SLOW"); return; }
        if (Fishers == 0) { Notices.Enqueue("NOTICE_FISH_NO_HANDS"); return; }
        LinesOut = true;
        FishCatch = 0;
        Notices.Enqueue("NOTICE_LINES_OUT");
    }

    void HaulIn(string? notice)
    {
        LinesOut = false;
        FishCatch = 0;
        if (notice != null) Notices.Enqueue(notice);
    }

    void FishingTick()
    {
        if (!LinesOut) return;
        if (Docked != null || Ship.Foundering || Digging) { HaulIn(null); return; }
        if (Ship.SailTarget > 1) { HaulIn("NOTICE_LINES_IN_SAIL"); return; }
        if (Fishers == 0) { HaulIn("NOTICE_FISH_NO_HANDS"); return; }
        FishCatch += CatchPerMinute * Dt / 60;
        while (FishCatch >= 1 && LinesOut)
        {
            FishCatch -= 1;
            LandFish();
        }
    }

    /// <summary>One fish on the line: into the hold, unless it is something else.</summary>
    void LandFish()
    {
        var region = Map.InBounds(Ship.Pos) && Map.Regions.Length > 0 ? Map.RegionAt(Ship.Pos).Type : (RegionType?)null;
        // In a beast's own waters the beast may take the line: she hauls in and has a fight on her hands.
        if (MonstersEnabled && Monster == null && region is { } r && Hookable(RegionDef.Of(r).Monster) && Rng.NextDouble() < BeastHookChance)
        {
            HaulIn("NOTICE_FISH_BEAST");
            SpawnMonster(RegionDef.Of(r).Monster);
            return;
        }
        double luck = Rng.NextDouble();
        if (region is RegionType.Shoals or RegionType.SirenRuins && luck < PearlChance) { Haul(Good.Pearls, "NOTICE_FISH_PEARL"); return; }
        if (region is RegionType.Deep && luck < AmbergrisChance) { Haul(Good.Ambergris, "NOTICE_FISH_AMBERGRIS"); return; }
        if (luck > 1 - BottleMapChance)
        {
            var site = Map.Treasures.FirstOrDefault(t => !t.Dug && !Player.BottleMaps.Any(m => m.Treasure == t.Id));
            if (site != null) { GiveBottleMap(site.Id); return; }
        }
        Haul(Good.Fish, null);
    }

    static bool Hookable(MonsterType t) => t is MonsterType.ReefSerpent or MonsterType.Kraken or MonsterType.Crocodile or MonsterType.WeedKraken;

    void Haul(Good good, string? notice)
    {
        if (Player.SlotsUsed + Goods.Of(good).SlotsPerUnit > Ship.CargoCapacity + 1e-9)
        {
            HaulIn("NOTICE_FISH_HOLD_FULL");
            return;
        }
        Player.Cargo[(int)good]++;
        Events.Add(new CombatEvent(CombatEventType.Collect, Ship.Pos, Ship.Id, 1, Good: (int)good));
        if (notice != null) Notices.Enqueue(notice);
    }

    /// <summary>After the crew has eaten: a third of the fish left (rounded up) has turned.</summary>
    void SpoilFish()
    {
        int left = Player.Cargo[(int)Good.Fish];
        int spoiled = (int)Math.Ceiling(left * FishSpoilShare - 1e-9);
        if (spoiled <= 0) return;
        Player.Remove(Good.Fish, spoiled);
        Notices.Enqueue("NOTICE_FISH_SPOILED");
    }

    /// <summary>For a resumed voyage.</summary>
    void RestoreFishing(bool linesOut, double catchPart)
    {
        LinesOut = linesOut;
        FishCatch = Math.Clamp(catchPart, 0, 1);
    }
}
