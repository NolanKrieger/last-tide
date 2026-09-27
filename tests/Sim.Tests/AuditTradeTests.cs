using System.Text.Json.Nodes;
using LastTide.Sim;

namespace Sim.Tests;

/// <summary>Regressions from the sim-trade audit (economy, ports, progression, exploration): each test failed before its fix.</summary>
public class AuditTradeTests
{
    static World Docked(int seed = 4, int gold = 100000)
    {
        var w = World.NewRun(seed, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        w.Player.Gold = gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        return w;
    }

    /// <summary>Casts off, lets one tick pass, and docks again in the same harbour.</summary>
    static void Redock(World w)
    {
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Tick(new ShipInput(0, 0));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
    }

    // T-01: friendly standing and the quartermaster made a same-port buy→sell round trip pay (+16 % to +42 %),
    // an instant, risk-free gold press with the clock frozen in port.
    [Theory]
    [InlineData(25.0, -1)]
    [InlineData(0.0, 0)]
    [InlineData(0.0, 1)]
    [InlineData(0.0, 2)]
    [InlineData(25.0, 2)]
    public void ASamePortRoundTripNeverProfits(double standing, int quartermaster)
    {
        foreach (int seed in new[] { 2, 4 })
        {
            var w = Docked(seed);
            var port = w.Docked!;
            w.Player.Reputation[(int)port.Faction] = standing;
            if (quartermaster >= 0) w.Player.Officers.Add(new Officer { Type = OfficerType.Quartermaster, Tier = quartermaster });
            int tried = 0;
            foreach (var g in Goods.All.Where(g => !Goods.IsRare(g.Id)))
                foreach (int n in new[] { 1, 10 })
                {
                    int before = w.Player.Gold;
                    if (w.Apply(new PortCommand(PortAction.Buy, g.Id, n)) != PortResult.Ok) continue;
                    tried++;
                    if (n == 10) Redock(w);   // leaving and coming straight back changes nothing
                    Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Sell, g.Id, n)));
                    Assert.True(w.Player.Gold <= before, $"seed {seed} {g.Key} ×{n}: {before} → {w.Player.Gold}");
                }
            Assert.True(tried > 20);
        }
    }

    // T-01 (the other side): goods carried from another port still earn the friendly premium.
    [Fact]
    public void GoodsBoughtElsewhereStillSellAtTheFriendlyPrice()
    {
        var w = Docked(4);
        var start = w.Docked!;
        var good = start.Produces[0];
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, good, 5)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        var other = w.Map.Ports.First(p => p != start && !p.Secret && p.Faction != Faction.Brethren);
        w.Player.Reputation[(int)other.Faction] = 40;
        w.Ship.Pos = other.Harbor;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        int quote = other.Market.QuoteSell(good, 5);
        int before = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Sell, good, 5)));
        Assert.Equal(before + (int)Math.Round(quote * 1.1), w.Player.Gold);
    }

    // T-01: the port panel calls the quotes every refresh; they must not change the run (replays have no panel).
    [Fact]
    public void QuotesLeaveTheWorldAlone()
    {
        var w = Docked(4);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, Good.Sugar, 10)));
        w.Player.Cargo[(int)Good.Sugar] = 4;   // six spoiled or thrown to the sharks: the lot is now larger than the hold
        int[] lotPort = w.Player.LotPort.ToArray(), lotUnits = w.Player.LotUnits.ToArray();
        double[] lotGold = w.Player.LotGold.ToArray(), basis = w.Player.CostBasis.ToArray();
        ulong hash = w.Hash();
        int sell = w.SellQuote(Good.Sugar, 4);
        int buy = w.BuyQuote(Good.Sugar, 3);
        Assert.True(sell > 0 && buy > 0);
        Assert.Equal(sell, w.SellQuote(Good.Sugar, 4));
        Assert.Equal(lotPort, w.Player.LotPort);
        Assert.Equal(lotUnits, w.Player.LotUnits);
        Assert.Equal(lotGold, w.Player.LotGold);
        Assert.Equal(basis, w.Player.CostBasis);
        Assert.Equal(hash, w.Hash());
        int gold = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Sell, Good.Sugar, 4)));
        Assert.Equal(gold + sell, w.Player.Gold);   // the quote is what the sale pays
    }

    // T-01/T-02 state rides in the suspend save; a save from before this branch still loads.
    [Fact]
    public void LotsAndVisitsSurviveASaveAndOldSavesLoad()
    {
        var w = Docked(4);
        var home = w.Docked!;
        w.Player.Reputation[(int)home.Faction] = 30;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, Good.Rum, 6)));
        string json = w.SaveJson();
        var loaded = World.LoadJson(json);
        Assert.Equal(w.Player.LotPort, loaded.Player.LotPort);
        Assert.Equal(w.Player.LotUnits, loaded.Player.LotUnits);
        Assert.Equal(w.Player.LotGold, loaded.Player.LotGold);
        Assert.Equal(w.SellQuote(Good.Rum, 6), loaded.SellQuote(Good.Rum, 6));
        double rep = loaded.Player.Reputation[(int)home.Faction];
        Redock(loaded);
        Assert.Equal(PortResult.Ok, loaded.Apply(new PortCommand(PortAction.Buy, Good.Rum, 1)));
        Assert.Equal(rep, loaded.Player.Reputation[(int)home.Faction]);   // still the same visit

        // A save written before the lots and the visit rule: the fields are simply absent.
        var old = JsonNode.Parse(json)!.AsObject();
        foreach (var key in new[] { "LotPort", "LotUnits", "LotGold", "LastDockPort", "LastDockDay" }) Assert.True(old.Remove(key), key);
        var legacy = World.LoadJson(old.ToJsonString());
        Assert.Same(legacy.Map.Ports[home.Id], legacy.Docked);
        Assert.All(legacy.Player.LotPort, p => Assert.Equal(-1, p));
        Assert.Equal(w.Player.Gold, legacy.Player.Gold);
        rep = legacy.Player.Reputation[(int)home.Faction];
        Redock(legacy);
        Assert.Equal(PortResult.Ok, legacy.Apply(new PortCommand(PortAction.Buy, Good.Rum, 1)));
        Assert.Equal(rep, legacy.Player.Reputation[(int)home.Faction]);   // a save made alongside counts as that visit
    }

    // T-02: re-docking reset the per-visit standing cap, so Esc/F in the same harbour farmed +1 a cycle to friendly.
    [Fact]
    public void ReDockingIsTheSameVisitForStanding()
    {
        var w = Docked(4);
        var home = w.Docked!;
        int f = (int)home.Faction;
        double r0 = w.Player.Reputation[f];
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, Good.Provisions, 1)));
        Assert.Equal(r0 + 1, w.Player.Reputation[f]);
        for (int i = 0; i < 5; i++)
        {
            Redock(w);
            Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, Good.Provisions, 1)));
        }
        Assert.Equal(r0 + 1, w.Player.Reputation[f]);
        Assert.Equal(PortResult.Nothing, w.Apply(new PortCommand(PortAction.Dock)));   // already alongside
        // A visit elsewhere makes the next call a new visit.
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        var other = w.Map.Ports.First(p => p != home && !p.Secret && w.IsOpen(p));
        w.Ship.Pos = other.Harbor;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Ship.Pos = home.Harbor;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, Good.Provisions, 1)));
        Assert.Equal(r0 + 2, w.Player.Reputation[f]);
        // So does a new day in the same harbour.
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Islands.Clear();
        int day = w.Day;
        while (w.Day == day) w.Tick(new ShipInput(0, 0));
        double decayed = w.Player.Reputation[f];
        w.Ship.Pos = home.Harbor;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, Good.Provisions, 1)));
        Assert.Equal(decayed + 1, w.Player.Reputation[f]);
    }

    // T-15: at a harbour whose one rescue was spent, F still docked a foundering ship, and a hull bought there
    // (any hull, even a cheaper one with the refund) left the last stand behind with the old hull.
    [Fact]
    public void ASpentHarbourLetsHerSinkAtItsMouth()
    {
        var w = Docked(4);
        var home = w.Docked!;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.RescuedAt.Add(home.Id);
        w.Ship.HullHp = 0;
        w.Tick(new ShipInput(0, 0));
        Assert.True(w.Ship.Foundering);
        Assert.Same(home, w.HarborHere);
        Assert.Contains("NOTICE_RESCUE_SPENT", w.Notices);
        Assert.Equal(PortResult.PortClosed, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.False(w.IsDocked);
        // A fresh harbour still hauls her in, once.
        var other = w.Map.Ports.First(p => p != home && !p.Secret && w.IsOpen(p));
        w.Ship.Pos = other.Harbor;
        w.Tick(new ShipInput(0, 0));
        Assert.True(w.IsDocked && !w.Ship.Foundering && w.DockedByAHair);
    }

    // T-03: cannon grades were priced per mounted gun but new guns came at the battery's grade for the flat 80,
    // so selling the guns, upgrading one and buying them back cut a 24-pdr battery from 23,760 to 1,950 gold on a frigate.
    [Theory]
    [InlineData("sloop")]
    [InlineData("frigate")]
    public void NewGunsCostTheBatterysGrade(string hull)
    {
        World Fit()
        {
            var w = Docked(4, 1_000_000);
            if (hull != "sloop") Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: hull)));
            while (w.Apply(new PortCommand(PortAction.BuyCannon)) == PortResult.Ok) { }
            return w;
        }
        var honest = Fit();
        int guns = honest.Ship.Cannons;
        int gold = honest.Player.Gold;
        for (int g = 0; g < PartDef.MaxGrade; g++) Assert.Equal(PortResult.Ok, honest.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Cannons)));
        int inPlace = gold - honest.Player.Gold;

        var dodge = Fit();
        gold = dodge.Player.Gold;
        while (dodge.Apply(new PortCommand(PortAction.SellCannon)) == PortResult.Ok) { }
        for (int g = 0; g < PartDef.MaxGrade; g++) Assert.Equal(PortResult.Ok, dodge.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Cannons)));
        while (dodge.Apply(new PortCommand(PortAction.BuyCannon)) == PortResult.Ok) { }
        Assert.Equal(guns, dodge.Ship.Cannons);
        Assert.Equal(PartDef.MaxGrade, dodge.Ship.CannonGrade);
        int viaSale = gold - dodge.Player.Gold;
        Assert.True(viaSale >= inPlace, $"{hull}: upgrading {guns} guns in place costs {inPlace}, selling and rebuying them {viaSale}");
    }

    // T-04: a trade-in worth more than the new hull was silently kept by the shipwright (brig → cutter lost 4,600 gold).
    [Fact]
    public void TradingDownPaysOutTheTradeInSurplus()
    {
        var w = Docked(4, 20000);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "brig")));
        int tradeIn = w.TradeInValue();
        int gold = w.Player.Gold;
        var cutter = Hulls.Get("cutter");
        Assert.True(tradeIn > cutter.Cost);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "cutter")));
        Assert.Equal(gold + tradeIn - cutter.Cost, w.Player.Gold);   // two guns: none to sell
    }

    // T-05: officers beyond the new hull's berths were dropped after their effects were applied, so a lost lookout's sight stayed.
    [Fact]
    public void OfficersWithoutABerthTakeTheirEffectAshore()
    {
        var w = Docked(4);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "frigate")));
        w.Player.Officers.Add(new Officer { Type = OfficerType.Marines, Tier = 0 });
        w.Player.Officers.Add(new Officer { Type = OfficerType.Quartermaster, Tier = 0 });
        w.Player.Officers.Add(new Officer { Type = OfficerType.Lookout, Tier = 2 });
        w.ApplyOfficers();
        Assert.Equal(1.5, w.VisionMult, 6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "sloop")));
        Assert.Single(w.Player.Officers);
        Assert.Equal(OfficerType.Marines, w.Player.Officers[0].Type);
        Assert.Equal(1.0, w.VisionMult, 6);
        Assert.Equal(-1, w.Player.OfficerTier(OfficerType.Lookout));
    }

    // T-06: the "cargo must fit" check ignored the hidden hold, which moves with the captain (+15 %).
    [Fact]
    public void TheHiddenHoldCountsWhenCargoMustFit()
    {
        var w = Docked(4);
        w.Player.Unique.Add("hidden_hold");
        w.ApplyUnique();
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "schooner")));
        Array.Clear(w.Player.Cargo);
        w.Player.Cargo[(int)Good.Sugar] = 28;   // a cutter holds 25, or 29 with the hidden hold
        var cutter = Hulls.Get("cutter");
        Assert.True(w.HullFits(cutter));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "cutter")));
        Assert.Equal(29, w.Ship.CargoCapacity);
        Assert.False(w.HullFits(Hulls.Sloop));   // 23 with the hidden hold
        Assert.Equal(PortResult.NoRoom, w.Apply(new PortCommand(PortAction.BuyHull, Text: "sloop")));
    }

    // T-07: "one bottle map on offer per port" — buying it put the next one on the counter, so a tavern sold every map.
    [Fact]
    public void ATavernHasOneBottleMapADay()
    {
        var w = Docked(4);
        var home = w.Docked!;
        var site = w.TavernMap(home);
        Assert.NotNull(site);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyMap)));
        Assert.Contains(w.Player.BottleMaps, m => m.Treasure == site!.Id);
        Assert.Null(w.TavernMap(home));
        Assert.Equal(PortResult.NoStock, w.Apply(new PortCommand(PortAction.BuyMap)));
        Assert.Single(w.Player.BottleMaps);
        // Another day, another map may come in; never one already carried.
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        for (int day = 0; day < 6; day++)
        {
            int d = w.Day;
            while (w.Day == d) w.Tick(new ShipInput(0, 0));
            var offer = w.TavernMap(home);
            Assert.True(offer == null || w.Player.BottleMaps.All(m => m.Treasure != offer.Id));
        }
    }

    // T-08: malformed port commands: a part index outside the table threw instead of being refused.
    [Fact]
    public void AnUnknownPartIsRefusedNotAnException()
    {
        var w = Docked(4);
        Assert.Equal(PortResult.Nothing, w.Apply(new PortCommand(PortAction.BuyPart, Amount: 99)));
        Assert.Equal(PortResult.Nothing, w.Apply(new PortCommand(PortAction.BuyPart, Amount: -1)));
        Assert.Equal(100000, w.Player.Gold);
    }

    // T-09: a repair was charged for whole hit points: half a point missing cost two gold where the shipwright's quote said one.
    [Fact]
    public void RepairsChargeForTheHullTheyMend()
    {
        var w = Docked(4, 1000);
        w.Ship.HullHp = w.Ship.MaxHp - 0.5;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Repair)));
        Assert.Equal(1000 - (int)Math.Ceiling(0.5 * w.RepairCostPerHp), w.Player.Gold);
        Assert.Equal(w.Ship.MaxHp, w.Ship.HullHp, 9);
        w.Ship.HullHp = w.Ship.MaxHp - 10.2;
        int gold = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Repair)));
        Assert.Equal(gold - (int)Math.Ceiling(10.2 * w.RepairCostPerHp - 1e-9), w.Player.Gold);
    }

    // T-10: the cost basis kept the price of goods that were eaten, fired or burnt, so the logbook's best trade came out low.
    [Fact]
    public void TheCostBasisFollowsTheGoodsLeftAboard()
    {
        var w = Docked(4);
        w.Player.Cargo[(int)Good.Provisions] = 0;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, Good.Provisions, 8)));
        double paid = w.Player.CostBasis[(int)Good.Provisions];
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Islands.Clear();
        while (w.Day == 1) w.Tick(new ShipInput(0, 0));
        Assert.Equal(7, w.Player.Units(Good.Provisions));   // four hands eat one a day
        Assert.Equal(paid * 7 / 8, w.Player.CostBasis[(int)Good.Provisions], 6);
        // Everything fired, then a fresh load: the old powder's price is gone with it.
        w.Ship.Pos = w.Map.StartPort.Harbor;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, Good.Munitions, 10)));
        w.Player.Cargo[(int)Good.Munitions] = 0;   // as a broadside spends them
        int gold = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, Good.Munitions, 10)));
        Assert.Equal(gold - w.Player.Gold, w.Player.CostBasis[(int)Good.Munitions], 6);
    }

    // T-11: where two dig rings overlap, the first site in the list shadowed the other: its matched map could not be dug there.
    [Fact]
    public void OverlappingDigRingsWorkTheMatchedSite()
    {
        var w = Docked(4);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Islands.Clear();
        var a = w.Map.Treasures[0];
        var b = w.Map.Treasures[1];
        b.DigRing = a.DigRing + new Vec2(100, 0);
        w.GiveBottleMap(b.Id);
        w.Player.BottleMaps[0].Solved = true;
        var spot = a.DigRing + new Vec2(50, 0);   // inside both rings
        w.Ship.Pos = spot;
        Assert.Same(b, w.DigSiteHere);
        Assert.True(w.CanDigHere);
        w.Tick(new ShipInput(0, 0, Action: true));
        Assert.True(w.Digging);
        for (int i = 0; i < 30 * 16 && w.Digging; i++) { w.Ship.Pos = spot; w.Tick(new ShipInput(0, 0)); }
        Assert.True(b.Dug);
        Assert.False(a.Dug);
    }

    // T-14: a treasure's rare goods were drawn before checking the hold, so a nearly full hold usually got none
    // even when emeralds (five to a slot) would have fitted (GDD §19 M9: "a rare good that fits").
    [Fact]
    public void TreasureGivesARareGoodThatFits()
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            var w = World.NewRun(seed, populate: false);
            w.DirectorEnabled = false;
            w.MonstersEnabled = false;
            var site = w.Map.Treasures[0];
            w.GiveBottleMap(site.Id);
            w.Player.BottleMaps[0].Solved = true;
            Array.Clear(w.Player.Cargo);
            w.Player.Cargo[(int)Good.Sugar] = 19;
            w.Player.Cargo[(int)Good.Provisions] = 2;   // 19.5 of 20 slots: room for two emeralds, nothing bulkier
            w.Ship.Pos = site.DigRing;
            w.Ship.Vel = Vec2.Zero;
            w.Tick(new ShipInput(0, 0, Action: true));
            Assert.True(w.Digging, $"seed {seed}");
            for (int i = 0; i < 30 * 16 && w.Digging; i++) { w.Ship.Pos = site.DigRing; w.Tick(new ShipInput(0, 0)); }
            Assert.True(site.Dug, $"seed {seed}");
            Assert.InRange(w.Player.Units(Good.Emeralds), 1, 2);
            Assert.True(w.Player.SlotsUsed <= w.Ship.CargoCapacity + 1e-9);
        }
    }

    // T-12: a cove first seen through the spyglass was never counted: no Smuggler's Welcome, a short logbook.
    [Fact]
    public void ACoveSightedThroughTheSpyglassCounts()
    {
        var w = Docked(4);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Islands.Clear();
        foreach (var cove in w.Map.Ports.Where(p => p.Secret))
            for (int k = 0; k < 16; k++)
            {
                var spot = cove.Harbor + Vec2.FromAngle(k * Angles.Tau / 16) * 700;
                if (!Map.InBounds(spot) || w.Map.Ports.Any(p => p.Secret && p.Harbor.DistanceTo(spot) < 620)) continue;
                w.Ship.Pos = spot;
                // The glass rides in the logged input (sim-sailing S19), so it is raised through the tick's input.
                for (int t = 0; t < 31; t++) w.Tick(new ShipInput(0, 0, Spyglass: true, SpyglassDir: (cove.Harbor - spot).Angle));
                Assert.True(cove.Discovered, "the glass finds the cove");
                Assert.Equal(1, w.Stats.CovesFound);
                return;
            }
        Assert.Fail("no clear spot 700 m from a cove");
    }

    // The lean start (GDD §3): a profitable first trade within half a day's sail, with the real purse and hold.
    [Fact]
    public void TheLeanStartHasAProfitableFirstTradeWithinHalfADay()
    {
        var charted = new List<int>();
        var profits = new List<int>();
        for (int seed = 1; seed <= 150; seed++)
        {
            var w = World.NewRun(seed, populate: false);
            var start = w.Map.StartPort;
            var d = w.Map.Nav.Distances(start.Harbor);
            double free = w.Ship.CargoCapacity - w.Player.SlotsUsed;
            int best = 0, bestCharted = 0;
            foreach (var port in w.Map.Ports)
            {
                double dist = w.Map.Nav.DistanceAt(d, port.Harbor);
                if (port == start || dist < 0 || dist > MapGen.StartRouteRange || !w.IsOpen(port)) continue;
                foreach (var g in Goods.All.Where(g => !Goods.IsRare(g.Id)))
                {
                    int units = 0;
                    while ((units + 1) * g.SlotsPerUnit <= free + 1e-9 && units + 1 <= start.Market.Stock[(int)g.Id]
                           && start.Market.QuoteBuy(g.Id, units + 1) <= w.Player.Gold) units++;
                    if (units == 0) continue;
                    int profit = port.Market.QuoteSell(g.Id, units) - start.Market.QuoteBuy(g.Id, units);
                    best = Math.Max(best, profit);
                    if (!port.Secret) bestCharted = Math.Max(bestCharted, profit);
                }
            }
            Assert.True(best > 0, $"seed {seed}: no profitable trade within {MapGen.StartRouteRange} m");
            if (bestCharted <= 0) charted.Add(seed);
            else profits.Add(bestCharted);
        }
        profits.Sort();
        Assert.True(profits[profits.Count / 10] >= 40, $"p10 first-trade profit {profits[profits.Count / 10]}");
        // Generator v3+ (audit S-13 / S21): a hidden cove never counts as the start route, so every seed has a charted
        // market for the first trade (on v2 ~2 % of seeds had only an uncharted cove in range).
        Assert.True(charted.Count == 0, $"seeds with only a hidden cove in range: {string.Join(", ", charted)}");
    }
}
