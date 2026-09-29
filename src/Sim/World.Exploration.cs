namespace LastTide.Sim;

public sealed partial class World
{
    public const double DigSeconds = 15, SalvageSeconds = 10;
    public const int BottleMapPrice = 60;
    public const double MatchRange = 150;   // beyond the island's bounding circle

    public double DigProgress { get; private set; }
    public bool Digging { get; private set; }
    /// <summary>The undug site whose ring she is in; where two rings overlap, the one she holds a matched map for (audit T-11).</summary>
    public TreasureSite? DigSiteHere
    {
        get
        {
            TreasureSite? first = null;
            foreach (var t in Map.Treasures)
            {
                if (t.Dug || t.DigRing.DistanceTo(Ship.Pos) > t.RingRadius) continue;
                if (Player.BottleMaps.Any(m => m.Treasure == t.Id && m.Solved)) return t;
                first ??= t;
            }
            return first;
        }
    }
    public Wreck? WreckHere => Map.Wrecks.FirstOrDefault(w => !w.Salvaged && w.Pos.DistanceTo(Ship.Pos) <= w.RingRadius);
    /// <summary>The dig site here is workable only once its map is matched.</summary>
    public bool CanDigHere => DigSiteHere is { } t && Player.BottleMaps.Any(m => m.Treasure == t.Id && m.Solved);
    public bool CanSalvageHere => WreckHere != null;
    /// <summary>The action in progress is a wreck salvage (10 s), not a dig (15 s): read-only, for the HUD's prompt.</summary>
    public bool Salvaging => Digging && digWreck;

    public List<BlackMarketDef> BlackMarketHere => Docked is { Secret: true } p ? BlackMarketDef.Stock(Seed, p.Id) : new List<BlackMarketDef>();
    public bool HasUnique(string key) => Player.Unique.Contains(key);

    /// <summary>Applies the unique parts and officers to the derived stats. Called after any change.</summary>
    public void ApplyUnique()
    {
        StealthBonus = HasUnique("ghost_sails") ? 0.3 : 0;
        Ship.PointMult = HasUnique("smugglers_keel") ? 0.95 : 1;
        Ship.RangeBonus = HasUnique("long_nines") ? 40 : 0;
        Ship.HoldMult = HasUnique("hidden_hold") ? 1.15 : 1;
        Ship.RefreshParts();
    }

    PortResult BuyUnique(string key)
    {
        if (Docked is not { Secret: true }) return PortResult.Nothing;
        if (!BlackMarketHere.Any(d => d.Key == key)) return PortResult.Nothing;
        if (HasUnique(key)) return PortResult.Nothing;
        var def = BlackMarketDef.Of(key);
        if (def.Price > Player.Gold) return PortResult.NoGold;
        Player.Gold -= def.Price;
        Player.Unique.Add(key);
        ApplyUnique();
        return PortResult.Ok;
    }

    /// <summary>
    /// The one map a tavern has today (GDD §19: one on offer per port), drawn from the undug sites by port and day, or
    /// null once she holds it. Drawing only from maps she lacked put the next map on the counter after every sale.
    /// </summary>
    public TreasureSite? TavernMap(Port port)
    {
        var candidates = Map.Treasures.Where(t => !t.Dug).ToList();
        if (candidates.Count == 0) return null;
        var rng = new Rng((ulong)(port.Id * 131 + Day * 17 + Seed * 7));
        var site = candidates[rng.Next(candidates.Count)];
        return Player.BottleMaps.Any(m => m.Treasure == site.Id) ? null : site;
    }

    PortResult BuyBottleMap()
    {
        if (Docked == null) return PortResult.NotDocked;
        var site = TavernMap(Docked);
        if (site == null) return PortResult.NoStock;
        if (Player.Gold < BottleMapPrice) return PortResult.NoGold;
        Player.Gold -= BottleMapPrice;
        GiveBottleMap(site.Id);
        return PortResult.Ok;
    }

    public void GiveBottleMap(int treasureId)
    {
        if (Player.BottleMaps.Any(m => m.Treasure == treasureId)) return;
        Player.BottleMaps.Add(new BottleMap { Treasure = treasureId, Rotation = Rng.Range(0, Angles.Tau) });
        Notices.Enqueue("NOTICE_BOTTLE_MAP");
    }

    /// <summary>Sailing within range of the sketched coast pins the X (GDD §10).</summary>
    void MatchMaps()
    {
        foreach (var map in Player.BottleMaps)
        {
            if (map.Solved) continue;
            var site = Map.Treasures[map.Treasure];
            if (site.IslandId >= Map.Islands.Count) continue;   // a test's sea with the islands cleared
            var island = Map.Islands[site.IslandId];
            if (Ship.Pos.DistanceTo(island.Centre) <= island.BoundRadius + MatchRange)
            {
                map.Solved = true;
                if (HasCartographer) Reveal.Paint(site.Pos, 40);   // the X goes on the chart only if someone keeps one
                Notices.Enqueue("NOTICE_MAP_MATCHED");
            }
        }
    }

    /// <summary>F at sea: start digging or salvaging; the work continues while she stays furled inside the ring.</summary>
    void ActionTick(bool pressed)
    {
        bool site = CanDigHere, wreck = CanSalvageHere;
        if (Digging)
        {
            bool still = Ship.SailTarget == 0 && Ship.SailFraction < 0.05 && (digWreck ? WreckHere != null : DigSiteHere != null);
            if (!still)
            {
                Digging = false;
                DigProgress = 0;
                Notices.Enqueue("NOTICE_DIG_INTERRUPTED");
                return;
            }
            DigProgress += Dt;
            if (DigProgress >= (digWreck ? SalvageSeconds : DigSeconds))
            {
                Digging = false;
                DigProgress = 0;
                if (digWreck) FinishSalvage(WreckHere!);
                else FinishDig(DigSiteHere!);
            }
            return;
        }
        if (!pressed) return;
        if (!site && !wreck && !LinesOut && HailableMerchant is { } merchant)
        {
            Hail(merchant);   // a merchant within hail answers before the lines go out (lines already out: F hauls them in)
            return;
        }
        if (!site && !wreck)
        {
            ToggleLines();   // anywhere else at sea the key puts the lines out (World.Fishing)
            return;
        }
        if (Ship.SailTarget != 0 || Ship.SailFraction >= 0.05)
        {
            Notices.Enqueue("NOTICE_FURL_FIRST");
            return;
        }
        if (site) { Digging = true; digWreck = false; DigProgress = 0; }
        else if (wreck) { Digging = true; digWreck = true; DigProgress = 0; }
    }

    bool digWreck;

    void FinishDig(TreasureSite t)
    {
        t.Dug = true;
        Stats.TreasuresDug++;
        int gold = (int)((150 + Rng.Next(250)) * (1 + 0.2 * (ThreatNow - 1)));
        Player.Gold += gold;
        Stats.GoldEarned += gold;
        // 1–3 units of a rare good that fits (GDD §19 M9): with a nearly full hold only emeralds (five to a slot) may.
        double room = Ship.CargoCapacity - Player.SlotsUsed;
        var rare = Goods.All.Where(g => g.Group == GoodGroup.Rare).ToList();
        var fitting = rare.Where(g => room + 1e-9 >= g.SlotsPerUnit).ToList();
        if (fitting.Count > 0) rare = fitting;
        int units = 1 + Rng.Next(3);
        var good = rare[Rng.Next(rare.Count)].Id;
        int fit = (int)Math.Floor(room / Goods.Of(good).SlotsPerUnit + 1e-9);
        int given = Math.Min(units, fit);
        if (given > 0) Player.Cargo[(int)good] += given;
        if (Rng.NextDouble() < 0.2) UnlockCosmetic();
        Notices.Enqueue("NOTICE_TREASURE");
        Events.Add(new CombatEvent(CombatEventType.Collect, Ship.Pos, -1, gold));
        Player.BottleMaps.RemoveAll(m => m.Treasure == t.Id);
        if (Stats.TreasuresDug >= 5) Unlock("x_marks");
    }

    void FinishSalvage(Wreck w)
    {
        w.Salvaged = true;
        var def = RegionDef.Of(RegionType.Sargasso);
        var good = def.Produces[Rng.Next(def.Produces.Length)];
        int units = 5 + Rng.Next(8);
        int fit = (int)Math.Floor((Ship.CargoCapacity - Player.SlotsUsed) / Goods.Of(good).SlotsPerUnit + 1e-9);
        int given = Math.Min(units, fit);
        if (given > 0) Player.Cargo[(int)good] += given;
        int gold = 30 + Rng.Next(50);
        Player.Gold += gold;
        Stats.GoldEarned += gold;
        if (Rng.NextDouble() < 0.3)
        {
            var site = Map.Treasures.FirstOrDefault(t => !t.Dug && !Player.BottleMaps.Any(m => m.Treasure == t.Id));
            if (site != null) GiveBottleMap(site.Id);
        }
        Notices.Enqueue("NOTICE_SALVAGE");
        Events.Add(new CombatEvent(CombatEventType.Collect, Ship.Pos, -1, gold));
    }

    /// <summary>Cosmetics (GDD §14) unlock from digs and achievements; the run wrapper shows them.</summary>
    public static readonly string[] CosmeticKeys =
    {
        "flag_crimson", "flag_black", "flag_gold", "sails_ochre", "sails_indigo", "sails_striped", "figurehead_serpent", "figurehead_siren",
        "figurehead_kraken", "hull_black", "hull_green", "hull_red", "wake_gold", "wake_crimson", "wake_indigo",
    };

    void UnlockCosmetic()
    {
        var locked = CosmeticKeys.Where(k => !Player.Cosmetics.Contains(k)).ToList();
        if (locked.Count == 0) return;
        var pick = locked[Rng.Next(locked.Count)];
        Player.Cosmetics.Add(pick);
        Notices.Enqueue("NOTICE_COSMETIC_" + pick);
    }


    // ---- Achievements that watch the run (GDD §14) ----
    void AchievementsTick()
    {
        if (Ticks % 30 != 0) return;
        if (Stats.RegionsEntered.Count >= 9) Unlock("nine_seas");
        if (Reveal.Fraction >= 0.8) Unlock("cartographer");
        // Coves found by any sighting count, the spyglass's too (it marks them discovered without the vision pass's tally).
        Stats.CovesFound = Math.Max(Stats.CovesFound, Map.Ports.Count(p => p.Secret && p.Discovered));
        if (Stats.CovesFound >= 3) Unlock("smugglers_welcome");
        if (DockedByAHair) Unlock("by_a_hair");
        if (ThreatTier >= 8) Unlock("last_tide");
        if (Preset == Preset.CalmSeas && Day >= 10) Unlock("fair_winds");
        if (Preset == Preset.RoughSeas && Day >= 15) Unlock("heavy_weather");
        if (Preset == Preset.Tempest && Day >= 12) Unlock("eye_of_the_storm");
    }
}
