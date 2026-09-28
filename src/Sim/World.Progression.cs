namespace LastTide.Sim;

public sealed partial class World
{
    public const double TradeInRate = 0.6;

    // ---- Prices with standing and the quartermaster (GDD §7, §8) ----
    /// <summary>Multiplier on what the player pays: friendly standing and a quartermaster both help.</summary>
    public double BuyPriceMult(Port port)
    {
        double m = 1;
        if (!port.Secret && Player.Rep(port.Faction) >= 20) m *= 0.9;
        int q = Player.OfficerTier(OfficerType.Quartermaster);
        if (q >= 0) m *= 1 - Officers.QuartermasterPrices[q];
        return m;
    }

    public double SellPriceMult(Port port)
    {
        double m = 1;
        if (!port.Secret && Player.Rep(port.Faction) >= 20) m *= 1.1;
        int q = Player.OfficerTier(OfficerType.Quartermaster);
        if (q >= 0) m *= 1 + Officers.QuartermasterPrices[q];
        return m;
    }

    /// <summary>What buying <paramref name="units"/> here costs the player: the market's unit-by-unit quote with standing and the quartermaster.</summary>
    public int BuyQuote(Good g, int units) =>
        Docked is { } port && units > 0 ? (int)Math.Round(port.Market.QuoteBuy(g, units) * BuyPriceMult(port)) : 0;

    /// <summary>
    /// What selling <paramref name="units"/> here pays: the market's quote with standing and the quartermaster, except that
    /// goods bought at this very port go back at no more than was paid for them. With friendly standing (10%) and a
    /// quartermaster (3–10%) on both sides of a 5% spread, a buy→sell round trip in one port otherwise paid up to 42%.
    /// </summary>
    public int SellQuote(Good g, int units)
    {
        if (Docked is not { } port || units <= 0) return 0;
        units = Math.Min(units, Player.Units(g));
        if (units <= 0) return 0;
        int revenue = (int)Math.Round(port.Market.QuoteSell(g, units) * SellPriceMult(port));
        var (own, paid) = Player.LotAt(port.Id, g, units);
        if (own > 0)
        {
            double share = revenue * (double)own / units;
            if (share > paid) revenue -= (int)Math.Ceiling(share - paid - 1e-9);
        }
        return revenue;
    }

    public int PartPrice(Part part) => PartDef.Price(part, Ship.Grade(part) + 1, Ship.Hull, Ship.Cannons);

    /// <summary>
    /// A new gun is cast at the battery's grade, so it costs the base price plus that grade's price for one gun on this hull.
    /// At a flat 80 gold, selling the guns, upgrading one and buying them back skipped the per-gun grade price (audit T-03).
    /// </summary>
    public int CannonCost
    {
        get
        {
            int cost = CannonPrice;
            for (int g = 1; g <= Ship.Grade(Part.Cannons); g++) cost += PartDef.Price(Part.Cannons, g, Ship.Hull, 1);
            return cost;
        }
    }

    /// <summary>What the shipwright gives for the current hull with the parts that stay aboard her (GDD §7).</summary>
    public int TradeInValue()
    {
        double parts = 0;
        foreach (var def in PartDef.All)
        {
            if (def.MovesWithCaptain) continue;
            for (int g = 1; g <= Ship.Grade(def.Id); g++) parts += PartDef.Price(def.Id, g, Ship.Hull, Ship.Cannons);
        }
        return (int)Math.Round((Ship.Hull.Cost + parts) * TradeInRate);
    }

    public int HullPrice(HullDef hull) => Math.Max(0, hull.Cost - TradeInValue());

    /// <summary>Gold the shipwright pays out when the trade-in is worth more than the new hull (trading down).</summary>
    public int HullRefund(HullDef hull) => Math.Max(0, TradeInValue() - hull.Cost);

    /// <summary>The new hull's hold: its own cargo (the hold grade stays with the old hull) with the hidden hold, which moves with the captain.</summary>
    public int HullCapacity(HullDef hull) => (int)Math.Round(hull.Cargo * Ship.HoldMult);

    /// <summary>Cargo must fit the new hold; crew beyond the new berths are paid off.</summary>
    public bool HullFits(HullDef hull) => Player.SlotsUsed <= HullCapacity(hull) + 1e-9;

    /// <summary>
    /// A Mangrove Maze yard builds only the hulls its channels take (§5): a big hull bought there would roam a maze the
    /// rule closes to her (audit X6).
    /// </summary>
    public bool HullSoldHere(HullDef hull) => Docked is not { Region: RegionType.Mangrove } || RegionDef.MangroveHulls.Contains(hull.Id);

    PortResult BuyHull(string hullId)
    {
        if (!Hulls.Exists(hullId)) return PortResult.Nothing;
        var hull = Hulls.Get(hullId);
        if (hull.Id == Ship.Hull.Id) return PortResult.Nothing;
        if (!HullSoldHere(hull)) return PortResult.NotSold;
        if (!HullFits(hull)) return PortResult.NoRoom;
        int price = HullPrice(hull);
        if (price > Player.Gold) return PortResult.NoGold;
        Player.Gold += HullRefund(hull) - price;   // trading down, the shipwright pays the difference
        var old = Ship;
        var fresh = new Ship(hull, old.Pos, old.Heading) { Id = old.Id, IsPlayer = true, Faction = old.Faction, Order = old.Order, Crew = Math.Min(old.Crew, hull.CrewMax) };
        // Cannons move (grade and as many guns as the new hull has slots; extras are sold), with the lantern.
        int slots = hull.GunsPerSide * 2;
        int extras = Math.Max(0, old.Cannons - slots);
        Player.Gold += extras * CannonPrice / 2;
        fresh.Cannons = Math.Min(old.Cannons, slots);
        fresh.Parts[(int)Part.Cannons] = old.Grade(Part.Cannons);
        fresh.Parts[(int)Part.Lantern] = old.Grade(Part.Lantern);
        fresh.RefreshParts();
        fresh.HullHp = fresh.MaxHp;
        old.CustomStations.CopyTo(fresh.CustomStations, 0);
        Ship = fresh;
        // Officers beyond the new berths go ashore first, so their effects go with them. The cartographer has a berth
        // of his own on every hull and always stays.
        if (Player.SlottedOfficers > hull.OfficerSlots)
        {
            int kept = 0;
            for (int i = 0; i < Player.Officers.Count; i++)
            {
                if (!Officers.UsesSlot(Player.Officers[i].Type) || ++kept <= hull.OfficerSlots) continue;
                Player.Officers.RemoveAt(i--);
            }
            Notices.Enqueue("NOTICE_OFFICERS_ASHORE");
        }
        ApplyUnique();
        ApplyOfficers();
        Stats.HullsOwned.Add(hull.Id);
        if (hull.Id == "galleon") Unlock("galleon_captain");
        if (hull.Id == "man_o_war") Unlock("ship_of_the_line");
        return PortResult.Ok;
    }

    PortResult BuyPart(Part part)
    {
        int grade = Ship.Grade(part);
        if (grade >= PartDef.MaxGrade) return PortResult.Nothing;
        int price = PartPrice(part);
        if (price > Player.Gold) return PortResult.NoGold;
        Player.Gold -= price;
        Ship.Parts[(int)part] = grade + 1;
        if (part == Part.Planking) Ship.HullHp += Ship.Hull.HullHp * 0.12;   // new timber comes sound
        Ship.RefreshParts();
        return PortResult.Ok;
    }

    // ---- Officers (GDD §7) ----
    /// <summary>
    /// The officers a tavern has on offer this visit: one of each type at a random tier. The home port's tavern always
    /// has a green cartographer, so the voyage's first purchase can be the chart (the run starts without one).
    /// </summary>
    public List<Officer> TavernOfficers(Port port)
    {
        var rng = new Rng((ulong)(port.Id * 7919 + Day * 104729 + Seed));
        var list = new List<Officer>();
        foreach (var t in Enum.GetValues<OfficerType>())
        {
            double roll = rng.NextDouble();
            int tier = roll < 0.55 ? 0 : roll < 0.9 ? 1 : 2;
            if (port.Size == 0 && tier == 2) tier = 1;
            if (t == OfficerType.Cartographer && port.Id == Map.StartPort.Id) tier = 0;
            list.Add(new Officer { Type = t, Tier = tier });
        }
        return list;
    }

    PortResult HireOfficer(int typeIndex, int tier)
    {
        if (Docked == null) return PortResult.NotDocked;
        var type = (OfficerType)typeIndex;
        var offer = TavernOfficers(Docked).FirstOrDefault(o => o.Type == type);
        if (offer == null || offer.Tier != tier) return PortResult.Nothing;
        var existing = Player.OfficerOf(type);
        if (existing == null && Officers.UsesSlot(type) && Player.SlottedOfficers >= Ship.Hull.OfficerSlots) return PortResult.CrewFull;
        int price = Officers.PriceOf(type, tier);
        if (price > Player.Gold) return PortResult.NoGold;
        Player.Gold -= price;
        if (existing != null) existing.Tier = tier;
        else Player.Officers.Add(new Officer { Type = type, Tier = tier });
        ApplyOfficers();
        if (type == OfficerType.Cartographer) ChartAround();   // he starts with the harbour he signed on in
        return PortResult.Ok;
    }

    PortResult DismissOfficer(int typeIndex)
    {
        int n = Player.Officers.RemoveAll(o => o.Type == (OfficerType)typeIndex);
        ApplyOfficers();
        return n > 0 ? PortResult.Ok : PortResult.Nothing;
    }

    public void ApplyOfficers()
    {
        int look = Player.OfficerTier(OfficerType.Lookout);
        VisionMult = look >= 0 ? 1 + Officers.LookoutVision[look] : 1;   // the lantern part widens only the lit night radius (VisionAt)
    }

    /// <summary>A cartographer is aboard: the chart can be opened and what she sees is kept (Nolan, 2026-09-27).</summary>
    public bool HasCartographer => Player.OfficerOf(OfficerType.Cartographer) != null;

    /// <summary>How far round her the chart is inked: the vision radius × the cartographer's reach; 0 with none aboard.</summary>
    public double ChartRadius => Player.OfficerTier(OfficerType.Cartographer) is >= 0 and var t ? VisionRadius * Officers.CartographerReach[t] : 0;

    /// <summary>The cartographer inks the chart round her out to <see cref="ChartRadius"/> and marks the ports inside it.</summary>
    void ChartAround()
    {
        double chart = ChartRadius;
        if (chart <= 0) return;
        Reveal.Paint(Ship.Pos, chart);
        foreach (var port in Map.Ports)
            if (!port.Discovered && port.Harbor.DistanceTo(Ship.Pos) <= chart + port.RingRadius)
                Discover(port);
    }

    public double WarningRange => Player.OfficerTier(OfficerType.Lookout) is >= 0 and var t ? Officers.LookoutWarning[t] : 0;

    public int OfficerWages()
    {
        int w = 0;
        foreach (var o in Player.Officers) w += Officers.WageOf(o.Type, o.Tier);
        return w;
    }

    double marinesClock = Officers.MarinesPeriod;

    /// <summary>Marines volley at enemy hulls within 120 m every 6 s, killing 1/2/3 hands (GDD §7).</summary>
    void MarinesTick()
    {
        int tier = Player.OfficerTier(OfficerType.Marines);
        if (tier < 0 || Docked != null) return;
        marinesClock -= Dt;
        if (marinesClock > 0) return;
        marinesClock = Officers.MarinesPeriod;
        foreach (var other in Others)
        {
            if (other.Sunk || !Hostile(other, Ship) || other.Pos.DistanceTo(Ship.Pos) > Officers.MarinesRange) continue;
            int kills = Math.Min(Officers.MarinesKills[tier], Math.Max(0, other.Crew - 1));
            other.Crew -= kills;
            Events.Add(new CombatEvent(CombatEventType.Fire, Ship.Pos, -5, kills));
            Draw(other);
            break;
        }
    }

    // ---- Crew stations by hand (GDD §7) ----
    public void SetStations(int guns, int sails, int repair)
    {
        Ship.CustomStations[0] = Math.Max(0, guns);
        Ship.CustomStations[1] = Math.Max(0, sails);
        Ship.CustomStations[2] = Math.Max(0, repair);
        Ship.Order = CrewOrder.Custom;
    }
}
