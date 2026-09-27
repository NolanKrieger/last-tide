namespace LastTide.Sim;

/// <summary>A remembered price: what a good cost at a port the day the player saw it (GDD §5 ledger).</summary>
public sealed class LedgerEntry
{
    public int Port { get; set; }
    public Good Good { get; set; }
    public double Price { get; set; }
    public double Day { get; set; }
    public bool Rumor { get; set; }
}

/// <summary>Everything the player owns and remembers that is not the hull itself.</summary>
public sealed class Player
{
    public int Gold = 200;
    public readonly int[] Cargo = new int[Goods.Count];
    /// <summary>Gold paid for what is in the hold, per good, for the best-trade statistic.</summary>
    public readonly double[] CostBasis = new double[Goods.Count];
    public bool Unpaid;
    public readonly double[] Reputation = { 0, 0, -25 };   // Crown, Free Traders, Brethren (GDD §8)
    public readonly List<LedgerEntry> Ledger = new();
    public readonly HashSet<int> PortsVisited = new();
    public string ShipName = "Last Tide";
    public readonly HashSet<string> Achievements = new();
    public readonly HashSet<string> Unique = new();
    public readonly HashSet<string> Cosmetics = new();
    /// <summary>The loadout chosen at run start, one key per <see cref="Sim.Cosmetics.Slots"/> ("" = plain).</summary>
    public string[] Loadout = { "", "", "", "", "" };
    public string Cosmetic(string slot) => Loadout[Sim.Cosmetics.SlotIndex(slot)];
    public readonly List<BottleMap> BottleMaps = new();
    public readonly List<CoveHint> CoveHints = new();
    public readonly List<Officer> Officers = new();
    public Officer? OfficerOf(OfficerType t) => Officers.FirstOrDefault(o => o.Type == t);
    public int OfficerTier(OfficerType t) => OfficerOf(t)?.Tier ?? -1;

    public int Units(Good g) => Cargo[(int)g];

    /// <summary>
    /// The port each good was last bought at, with how many of those units are still aboard and what they cost:
    /// a port buys its own goods back at no more than it was paid, so standing and the quartermaster can never
    /// turn a same-port round trip into profit (audit T-01). −1 = no lot.
    /// </summary>
    public readonly int[] LotPort = Enumerable.Repeat(-1, Goods.Count).ToArray();
    public readonly int[] LotUnits = new int[Goods.Count];
    public readonly double[] LotGold = new double[Goods.Count];

    /// <summary>Goods bought: cargo, cost basis and the port's lot. An empty hold forgets the price of goods eaten or fired.</summary>
    public void Bought(int port, Good g, int units, int gold)
    {
        int i = (int)g;
        if (Cargo[i] == 0) CostBasis[i] = 0;
        if (Cargo[i] == 0 || LotPort[i] != port) { LotPort[i] = port; LotUnits[i] = 0; LotGold[i] = 0; }
        else ClampLot(i);
        Cargo[i] += units;
        CostBasis[i] += gold;
        LotUnits[i] += units;
        LotGold[i] += gold;
    }

    /// <summary>
    /// How many of <paramref name="units"/> sold at <paramref name="port"/> come out of its own lot, and what they cost there.
    /// A pure query (the port panel calls it for its quotes): the lot is read as clamped to the hold, never changed.
    /// </summary>
    public (int Units, double Gold) LotAt(int port, Good g, int units)
    {
        int i = (int)g;
        if (LotPort[i] != port || LotUnits[i] <= 0) return (0, 0);
        int lotUnits = Math.Min(LotUnits[i], Cargo[i]);
        double lotGold = LotGold[i] * lotUnits / LotUnits[i];
        int own = Math.Min(units, lotUnits);
        return own <= 0 ? (0, 0) : (own, lotGold * own / lotUnits);
    }

    /// <summary>Goods leave the hold (sold, eaten, fired, burnt): cargo, cost basis and lot shrink in proportion.</summary>
    public void Remove(Good g, int units, int soldAtPort = -1)
    {
        int i = (int)g;
        int held = Cargo[i];
        units = Math.Min(units, held);
        if (units <= 0) return;
        ClampLot(i);
        CostBasis[i] -= CostBasis[i] * units / held;
        if (soldAtPort >= 0 && LotPort[i] == soldAtPort)
        {
            var (own, gold) = LotAt(soldAtPort, g, units);
            LotUnits[i] -= own;
            LotGold[i] -= gold;
        }
        Cargo[i] = held - units;
        if (Cargo[i] == 0) CostBasis[i] = 0;
        ClampLot(i);
    }

    /// <summary>A lot never covers more units than the hold carries (the rest were eaten, fired or sold elsewhere).</summary>
    void ClampLot(int i)
    {
        if (LotUnits[i] <= Cargo[i]) return;
        LotGold[i] = LotUnits[i] > 0 ? LotGold[i] * Cargo[i] / LotUnits[i] : 0;
        LotUnits[i] = Cargo[i];
        if (LotUnits[i] == 0) { LotPort[i] = -1; LotGold[i] = 0; }
    }

    public double SlotsUsed
    {
        get
        {
            double s = 0;
            for (int i = 0; i < Goods.Count; i++)
                s += Cargo[i] * Goods.All[i].SlotsPerUnit;
            return s;
        }
    }

    public double Rep(Faction f) => Reputation[(int)f];

    public LedgerEntry? Remembered(int port, Good g)
    {
        LedgerEntry? best = null;
        foreach (var e in Ledger)
            if (e.Port == port && e.Good == g && (best == null || e.Day > best.Day)) best = e;
        return best;
    }

    public void Remember(int port, Good g, double price, double day, bool rumor)
    {
        Ledger.RemoveAll(e => e.Port == port && e.Good == g);
        Ledger.Add(new LedgerEntry { Port = port, Good = g, Price = price, Day = day, Rumor = rumor });
    }
}

/// <summary>Numbers the logbook page shows at the end of a run (GDD §3).</summary>
public sealed class RunStats
{
    public int GoldEarned;
    public int ShipsSunk;
    public double LeaguesSailed;      // 1 league = 5556 m
    public int BestTrade;
    public string BestTradeGood = "";
    public int TreasuresDug;
    public int CovesFound;
    public int MonstersBeaten;
    public HashSet<string> HullsOwned = new() { "sloop" };
    public HashSet<RegionType> RegionsEntered = new();
}
