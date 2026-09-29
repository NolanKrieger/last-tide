using LastTide.Sim;

namespace Sim.Tests;

/// <summary>
/// Nolan, 2026-09-28: the tavern's rumours are gone; prices are only learned in port, but a merchant hailed at sea tells
/// the prices at the ports she traded in, and the harbour office posts which nearby ports want what (no prices).
/// </summary>
public class NewsTests
{
    static World Quiet(int seed = 4)
    {
        var w = World.NewRun(seed, populate: true);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        w.DriftEnabled = false;
        return w;
    }

    static Ship AMerchant(World w) => w.Others.First(o => o.Ai is { Role: Role.Merchant } && o.Ai.HomePort >= 0);

    [Fact]
    public void AMerchantRemembersThePricesWhereSheTrades()
    {
        var w = Quiet();
        var m = AMerchant(w);
        var home = w.Map.Ports[m.Ai!.HomePort];
        w.PlanVoyage(m);
        Assert.True(m.Ai.DestPort >= 0, "she lays a course");
        var loaded = m.Ai.News.Last();
        Assert.Equal(home.Id, loaded.Port);
        Assert.Equal(w.DaysSurvived, loaded.Day);
        for (int i = 0; i < loaded.Goods.Count; i++)
        {
            Assert.True(home.Produces.Contains(loaded.Goods[i]) || home.Consumes.Contains(loaded.Goods[i]));
            Assert.Equal(home.Market.Price(loaded.Goods[i]), loaded.Prices[i], 9);
        }
        int dest = m.Ai.DestPort;
        w.ArriveMerchant(m);
        Assert.Equal(dest, m.Ai.News.Last().Port);   // and the prices she sold at
        for (int k = 0; k < 6; k++)
        {
            w.PlanVoyage(m);
            if (m.Ai.DestPort >= 0) w.ArriveMerchant(m);
        }
        Assert.InRange(m.Ai.News.Count, 1, World.MerchantNewsKept);
        Assert.Equal(m.Ai.News.Count, m.Ai.News.Select(n => n.Port).Distinct().Count());
    }

    [Fact]
    public void HailingAMerchantEntersHerPricesInTheLedgerDatedHerVisit()
    {
        var w = Quiet();
        var m = AMerchant(w);
        w.PlanVoyage(m);
        w.ArriveMerchant(m);
        var news = m.Ai!.News.ToList();
        // Alongside her in her own waters, lying still.
        w.Ship.Pos = m.Pos + m.Right * 60;
        w.Ship.Vel = Vec2.Zero;
        m.Vel = Vec2.Zero;
        foreach (var p in w.Map.Ports) if (news.Any(n => n.Port == p.Id)) p.Discovered = false;
        // A fresher price she saw herself is kept over the merchant's older one.
        var n0 = news[0];
        w.Player.Remember(n0.Port, n0.Goods[0], 1, n0.Day + 1);
        Assert.Same(m, w.HailableMerchant);
        w.Tick(new ShipInput(0, 0, Action: true));
        string name = w.NameOf(m);
        foreach (var n in news)
        {
            Assert.True(w.Map.Ports[n.Port].Discovered, "the port she names goes on the chart");
            for (int i = 0; i < n.Goods.Count; i++)
            {
                var e = w.Player.Remembered(n.Port, n.Goods[i])!;
                if (n == n0 && i == 0) { Assert.Equal(1, e.Price); Assert.False(e.Heard); continue; }
                Assert.Equal(n.Prices[i], e.Price, 9);
                Assert.Equal(n.Day, e.Day);
                Assert.Equal(name, e.Teller);
            }
        }
        Assert.Contains(w.Notices, s => s.StartsWith("NOTICE_HAIL|" + name + "|"));
        Assert.Contains(w.Events, e => e.Type == CombatEventType.Ring && e.ShipId == m.Id);
        int ledger = w.Player.Ledger.Count;
        w.Tick(new ShipInput(0, 0, Action: true));   // she has nothing newer to tell
        Assert.Equal(ledger, w.Player.Ledger.Count);
        Assert.Contains(w.Notices, s => s == "NOTICE_HAIL_STALE|" + name);
    }

    [Fact]
    public void AMerchantSheStruckOrOneOutOfHailDoesNotAnswer()
    {
        var w = Quiet();
        var m = AMerchant(w);
        w.PlanVoyage(m);
        w.Ship.Pos = m.Pos + m.Right * (World.HailRange + 60);
        Assert.Null(w.HailableMerchant);
        w.Ship.Pos = m.Pos + m.Right * 60;
        m.PlayerHostile = true;   // she has been fired on: she runs rather than talks
        Assert.Null(w.HailableMerchant);
        int ledger = w.Player.Ledger.Count;
        w.Tick(new ShipInput(0, 0, Action: true));
        Assert.Equal(ledger, w.Player.Ledger.Count);
        Assert.DoesNotContain(w.Notices, s => s.StartsWith("NOTICE_HAIL"));
    }

    [Fact]
    public void MerchantsNewsAndHeardPricesSaveAndReplay()
    {
        var w = Quiet();
        var m = AMerchant(w);
        w.PlanVoyage(m);
        w.ArriveMerchant(m);
        w.Ship.Pos = m.Pos + m.Right * 60;
        w.Tick(new ShipInput(0, 0, Action: true));
        Assert.Contains(w.Player.Ledger, e => e.Heard);
        var loaded = World.LoadJson(w.SaveJson());
        Assert.Equal(w.Hash(), loaded.Hash());
        var lm = loaded.Others.First(o => o.Id == m.Id);
        Assert.Equal(m.Ai!.News.Select(n => (n.Port, n.Day, n.Goods.Count)), lm.Ai!.News.Select(n => (n.Port, n.Day, n.Goods.Count)));
        Assert.Equal(w.Player.Ledger.Count(e => e.Heard), loaded.Player.Ledger.Count(e => e.Heard));
        for (int i = 0; i < 90; i++) { w.Tick(new ShipInput(0, 0)); loaded.Tick(new ShipInput(0, 0)); }
        Assert.Equal(w.Hash(), loaded.Hash());
    }

    [Fact]
    public void TheOfficePostsWhatNearbyPortsWantInWordsOnly()
    {
        var w = Quiet();
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        var here = w.Docked!;
        var near = w.Map.Ports.First(p => p != here && !p.Secret && w.IsOpen(p) && p.Consumes.Any(g => !Goods.IsRare(g))
            && w.SeaDistance(here.Id, p.Id) is var d && d >= World.OfferMinDistance && d <= World.OfferMaxDistance);
        var good = near.Consumes.First(g => !Goods.IsRare(g));
        near.Market.Stock[(int)good] = near.Market.Target[(int)good] * 0.05;   // nearly none left: badly wanted
        var board = w.WantedNotices(here);
        Assert.InRange(board.Count, 1, World.WantedShown);
        Assert.Equal(new World.WantedNotice(near.Id, good, true, w.SeaDistance(here.Id, near.Id) / World.PassageMetresPerDay), board[0]);
        foreach (var n in board)
        {
            var p = w.Map.Ports[n.Port];
            Assert.True(p.Consumes.Contains(n.Good) && !p.Secret && p != here);
            Assert.True(p.Market.Stock[(int)n.Good] < p.Market.Target[(int)n.Good] * 0.85);
        }
        Assert.DoesNotContain(board, n => n.Port == here.Id);
        Assert.DoesNotContain(board, n => Goods.IsRare(n.Good));   // she carries none: a cove's rarity is not her news
    }
}
