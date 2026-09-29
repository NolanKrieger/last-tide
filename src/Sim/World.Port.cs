namespace LastTide.Sim;

public enum PortAction { Dock, CastOff, Buy, Sell, Repair, Hire, BuyCannon, SellCannon, BuyHull, BuyPart, HireOfficer, DismissOfficer, BuyUnique, BuyMap,
    CrewStations,               // the crew panel's hands per station: at sea or in port, logged so replays see it
    SignContract, AbandonContract }   // the harbour office: Amount is the board slot / the contract's index

/// <summary>An action taken in port (GDD §13). Logged with its tick so a run replays. <see cref="Text"/> carries a hull id.</summary>
public readonly record struct PortCommand(PortAction Action, Good Good = Good.Provisions, int Amount = 0, string Text = "");

public readonly record struct CommandEntry(long Tick, PortCommand Command);

public enum PortResult { Ok, NotInHarbor, PortClosed, NotDocked, NoGold, NoRoom, NoStock, NoCargo, CrewFull, Nothing, NotSold, Busy }

public sealed partial class World
{
    public const int SigningFee = 10;
    public const int CannonPrice = 80;
    public const int TornSailRepair = 25;
    public static readonly int[] StationWage = { 3, 2, 4, 2 };   // guns, sails, repair, spare hands (GDD §6)

    public Port? Docked { get; private set; }
    public bool IsDocked => Docked != null;
    public List<CommandEntry> Commands { get; } = new();
    bool tradedThisVisit;
    /// <summary>Where and on which day she last docked: coming straight back to the same harbour is the same visit (audit T-02).</summary>
    int lastDockPort = -1, lastDockDay;
    int lastUpkeepDay = 1;
    public event Action<Port>? OnDocked;

    /// <summary>The port whose harbour ring the ship is inside, if any.</summary>
    public Port? HarborHere => Map.PortAt(Ship.Pos);

    public bool IsOpen(Port port) => port.Secret || Player.Rep(port.Faction) > -50;

    public double RepairCostPerHp => 1.5 * Math.Sqrt(Ship.Hull.HullHp / 100.0);

    /// <summary>Crew per station under the current order: guns, sails, repair, spare hands (GDD §7).</summary>
    public int[] Stations() => Ship.Stations();

    public int DailyWages()
    {
        var st = Stations();
        double w = 0;
        for (int i = 0; i < 4; i++) w += st[i] * StationWage[i];
        int q = Player.OfficerTier(OfficerType.Quartermaster);
        if (q >= 0) w *= 1 - Officers.QuartermasterWages[q];
        return (int)Math.Round(w) + OfficerWages();
    }

    public int DailyProvisions
    {
        get
        {
            double need = Ship.Crew / 4.0;
            int q = Player.OfficerTier(OfficerType.Quartermaster);
            if (q >= 0) need /= 1 + Officers.QuartermasterProvisions[q];
            return (int)Math.Ceiling(need - 1e-9);
        }
    }

    public PortResult Apply(PortCommand cmd)
    {
        var result = Execute(cmd);
        if (result == PortResult.Ok)
            Commands.Add(new CommandEntry(Ticks, cmd));
        return result;
    }

    PortResult Execute(PortCommand cmd)
    {
        switch (cmd.Action)
        {
            case PortAction.Dock:
            {
                if (Docked != null) return PortResult.Nothing;   // already alongside
                var port = HarborHere;
                if (port == null) return PortResult.NotInHarbor;
                if (!IsOpen(port)) return PortResult.PortClosed;
                // A foundering ship is taken in only by the harbour's own rescue, once per port (GDD §8, §19); docking by
                // hand at a port whose mercy is spent let her buy a new hull (audit T-15) or repair to full and sink with a
                // sound hull (audit C-21). An unspent open harbour rescues her on its own before a hand dock could matter.
                if (Ship.Foundering) return PortResult.PortClosed;
                Docked = port;
                // Casting off and coming straight back is still the same visit: the standing cap and the trend arrows hold.
                bool sameVisit = port.Id == lastDockPort && Day == lastDockDay;
                lastDockPort = port.Id;
                lastDockDay = Day;
                if (!sameVisit)
                {
                    tradedThisVisit = false;
                    port.PricesLastVisit = Goods.All.Select(g => Player.Remembered(port.Id, g.Id)?.Price).ToArray();
                }
                // Tying up marks the port on the chart only with a cartographer aboard (nothing new is inked without
                // one); the purser's ledger of prices below is kept either way.
                if (HasCartographer) port.Discovered = true;
                bool firstVisit = Player.PortsVisited.Add(port.Id);
                foreach (var g in Goods.All)
                    Player.Remember(port.Id, g.Id, port.Market.Price(g.Id), DaysSurvived);
                if (Player.Unpaid)
                {
                    int leaving = (int)Math.Ceiling(Ship.Crew * 0.3);
                    leaving = Math.Min(leaving, Ship.Crew - 1);
                    if (leaving > 0)
                    {
                        Ship.Crew -= leaving;
                        Notices.Enqueue("NOTICE_DESERTED");
                    }
                    if (Player.Officers.Count > 0)
                    {
                        Player.Officers.Clear();
                        ApplyOfficers();
                        Notices.Enqueue("NOTICE_OFFICERS_LEFT");
                    }
                    Player.Unpaid = false;
                }
                Arrive(port, firstVisit);
                Ship.Vel = Vec2.Zero;
                Ship.AngVel = 0;
                OnDocked?.Invoke(port);
                return PortResult.Ok;
            }
            case PortAction.CastOff:
                if (Docked == null) return PortResult.NotDocked;
                Docked = null;
                return PortResult.Ok;
            case PortAction.CrewStations:
                SetStations(cmd.Amount & 1023, (cmd.Amount >> 10) & 1023, (cmd.Amount >> 20) & 1023);
                return PortResult.Ok;
        }
        if (Docked == null) return PortResult.NotDocked;
        var market = Docked.Market;
        switch (cmd.Action)
        {
            case PortAction.Buy:
            {
                int units = cmd.Amount;
                if (units <= 0) return PortResult.Nothing;
                if (Goods.IsRare(cmd.Good) && !Docked.Secret) return PortResult.NoStock;   // rare goods come from coves and treasure only
                if (!Goods.IsTraded(cmd.Good)) return PortResult.NoStock;   // ports buy fish; they don't sell it
                if (market.Stock[(int)cmd.Good] < units) return PortResult.NoStock;
                if (Player.SlotsUsed + units * Goods.Of(cmd.Good).SlotsPerUnit > Ship.CargoCapacity + 1e-9) return PortResult.NoRoom;
                int cost = BuyQuote(cmd.Good, units);
                if (cost > Player.Gold) return PortResult.NoGold;
                Player.Gold -= cost;
                Player.Bought(Docked.Id, cmd.Good, units, cost);
                market.TakeStock(cmd.Good, units);
                Trade();
                Player.Remember(Docked.Id, cmd.Good, market.Price(cmd.Good), DaysSurvived);
                return PortResult.Ok;
            }
            case PortAction.Sell:
            {
                int units = cmd.Amount;
                if (units <= 0) return PortResult.Nothing;
                if (Player.Cargo[(int)cmd.Good] < units) return PortResult.NoCargo;
                int revenue = SellQuote(cmd.Good, units);
                double basis = Player.CostBasis[(int)cmd.Good] * units / Player.Cargo[(int)cmd.Good];
                Player.Gold += revenue;
                Player.Remove(cmd.Good, units, soldAtPort: Docked.Id);
                market.AddStock(cmd.Good, units);
                Stats.GoldEarned += revenue;
                int profit = revenue - (int)Math.Round(basis);
                if (profit > Stats.BestTrade)
                {
                    Stats.BestTrade = profit;
                    Stats.BestTradeGood = Goods.Of(cmd.Good).Key;
                }
                Trade();
                Player.Remember(Docked.Id, cmd.Good, market.Price(cmd.Good), DaysSurvived);
                return PortResult.Ok;
            }
            case PortAction.Repair:
            {
                double missing = Ship.MaxHp - Ship.HullHp;
                if (Ship.TornSails && missing <= 0)
                {
                    if (Player.Gold < TornSailRepair) return PortResult.NoGold;
                    Player.Gold -= TornSailRepair;
                    Ship.TornSails = false;
                    return PortResult.Ok;
                }
                if (missing <= 0) return PortResult.Nothing;
                // Charge for the hull actually mended: a fraction of a point costs a fraction (the quote the shipwright shows).
                double hp = cmd.Amount <= 0 ? missing : Math.Min(cmd.Amount, missing);
                int cost = (int)Math.Ceiling(hp * RepairCostPerHp - 1e-9);
                if (cost > Player.Gold)
                {
                    hp = Math.Floor(Player.Gold / RepairCostPerHp);
                    cost = (int)Math.Ceiling(hp * RepairCostPerHp - 1e-9);
                    if (hp <= 0) return PortResult.NoGold;
                }
                Player.Gold -= cost;
                Ship.HullHp = Math.Min(Ship.MaxHp, Ship.HullHp + hp);
                if (Ship.TornSails && Player.Gold >= TornSailRepair)
                {
                    Player.Gold -= TornSailRepair;
                    Ship.TornSails = false;
                }
                return PortResult.Ok;
            }
            case PortAction.Hire:
            {
                int n = Math.Max(1, cmd.Amount);
                if (Ship.Crew >= Ship.Hull.CrewMax) return PortResult.CrewFull;
                n = Math.Min(n, Ship.Hull.CrewMax - Ship.Crew);
                int cost = n * SigningFee;
                if (cost > Player.Gold) return PortResult.NoGold;
                Player.Gold -= cost;
                Ship.Crew += n;
                ManNewHands();
                return PortResult.Ok;
            }
            case PortAction.BuyCannon:
            {
                if (Ship.Cannons >= Ship.Hull.GunsPerSide * 2) return PortResult.CrewFull;
                int price = CannonCost;
                if (Player.Gold < price) return PortResult.NoGold;
                Player.Gold -= price;
                Ship.Cannons++;
                return PortResult.Ok;
            }
            case PortAction.SellCannon:
            {
                if (Ship.Cannons <= 0) return PortResult.Nothing;
                Ship.Cannons--;
                Player.Gold += CannonPrice / 2;
                return PortResult.Ok;
            }
            case PortAction.BuyHull: return BuyHull(cmd.Text);
            case PortAction.BuyUnique: return BuyUnique(cmd.Text);
            case PortAction.BuyMap: return BuyBottleMap();
            case PortAction.SignContract: return SignContract(cmd.Amount);
            case PortAction.AbandonContract: return AbandonContract(cmd.Amount);
            case PortAction.BuyPart: return cmd.Amount >= 0 && cmd.Amount < PartDef.All.Length ? BuyPart((Part)cmd.Amount) : PortResult.Nothing;
            case PortAction.HireOfficer: return HireOfficer(cmd.Amount / 10, cmd.Amount % 10);
            case PortAction.DismissOfficer: return DismissOfficer(cmd.Amount);
        }
        return PortResult.Nothing;
    }

    /// <summary>False colours (black market): reputations mend twice as fast.</summary>
    double RepDecay => HasUnique("false_colours") ? 4 : 2;

    void Trade()
    {
        if (tradedThisVisit || Docked == null) return;
        tradedThisVisit = true;
        Player.Reputation[(int)Docked.Faction] = Math.Min(100, Player.Reputation[(int)Docked.Faction] + 1);
    }

    /// <summary>Upkeep at each in-game midnight: wages by station and provisions (GDD §6).</summary>
    void Midnight()
    {
        // lastUpkeepDay counts 1 + the upkeeps charged, as it did when upkeep fell at dawn, so older saves carry on.
        int due = (int)MidnightsPassed + 1;
        if (due <= lastUpkeepDay) return;
        lastUpkeepDay = due;
        int wages = DailyWages();
        if (Player.Gold >= wages)
            Player.Gold -= wages;
        else
        {
            Player.Gold = 0;
            Player.Unpaid = true;
            Notices.Enqueue("NOTICE_UNPAID");
        }
        int food = DailyProvisions;
        int fish = Math.Min(food, Player.Cargo[(int)Good.Fish]);   // fresh fish first, before it turns
        Player.Remove(Good.Fish, fish);
        food -= fish;
        int have = Player.Cargo[(int)Good.Provisions];
        Player.Remove(Good.Provisions, food);   // what is eaten takes its share of the cost basis with it
        SpoilFish();
        if (have < food)
        {
            if (Ship.Crew > 1)
            {
                Ship.Crew--;
                Notices.Enqueue("NOTICE_STARVED");
            }
        }
        // Reputations decay toward neutral by 2 a day (GDD §8).
        for (int i = 0; i < 3; i++)
            Player.Reputation[i] = Angles.MoveToward(Player.Reputation[i], 0, RepDecay);
    }
}
