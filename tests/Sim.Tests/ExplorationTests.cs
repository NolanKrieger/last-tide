using LastTide.Sim;

namespace Sim.Tests;

public class ExplorationTests
{
    static World Quiet(int seed = 4, int gold = 5000)
    {
        var w = World.NewRun(seed, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        w.Player.Gold = gold;
        return w;
    }

    [Fact]
    public void CovesSellUniquePartsThatMoveWithTheCaptain()
    {
        var w = Quiet();
        var cove = w.Map.Ports.First(p => p.Secret);
        w.Ship.Pos = cove.Harbor;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        var stock = w.BlackMarketHere;
        Assert.InRange(stock.Count, 1, 2);
        Assert.Equal(stock.Select(s => s.Key), BlackMarketDef.Stock(w.Seed, cove.Id).Select(s => s.Key));
        int gold = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyUnique, Text: stock[0].Key)));
        Assert.Equal(gold - stock[0].Price, w.Player.Gold);
        Assert.Equal(PortResult.Nothing, w.Apply(new PortCommand(PortAction.BuyUnique, Text: stock[0].Key)));
        Assert.Equal(PortResult.Nothing, w.Apply(new PortCommand(PortAction.BuyUnique, Text: "not_a_thing")));
        // Effects, each in turn.
        foreach (var key in BlackMarketDef.All.Select(d => d.Key)) w.Player.Unique.Add(key);
        w.ApplyUnique();
        Assert.Equal(0.3, w.StealthBonus, 6);
        Assert.Equal(Hulls.Sloop.PointDeg * 0.95, w.Ship.PointDeg, 6);
        Assert.Equal(220, w.Ship.Range, 6);
        Assert.Equal(23, w.Ship.CargoCapacity);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "cutter")));
        Assert.Equal(220, w.Ship.Range, 6);   // uniques move with the captain
        Assert.Equal(Hulls.Get("cutter").PointDeg * 0.95, w.Ship.PointDeg, 6);
        // A regular port has no black market.
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Ship.Pos = w.Map.StartPort.Harbor;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.Empty(w.BlackMarketHere);
    }

    [Fact]
    public void BottleMapsMatchWhenTheCoastIsSightedAndDigsTakeFifteenSecondsFurled()
    {
        var w = Quiet();
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        var site = w.TavernMap(w.Docked!);
        Assert.NotNull(site);
        int gold = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyMap)));
        Assert.Equal(gold - World.BottleMapPrice, w.Player.Gold);
        Assert.Single(w.Player.BottleMaps);
        Assert.False(w.Player.BottleMaps[0].Solved);
        Assert.Contains("NOTICE_BOTTLE_MAP", w.Notices);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));

        // Nothing to dig until the sketch is matched.
        w.Ship.Pos = site!.DigRing;
        Assert.False(w.CanDigHere);
        // Sight the coast.
        w.Tick(new ShipInput(0, 0));
        Assert.True(w.Player.BottleMaps[0].Solved, "the dig ring is beside the island, so the coast is in sight");
        Assert.Contains("NOTICE_MAP_MATCHED", w.Notices);
        Assert.True(w.CanDigHere);

        // Sails set: no digging.
        w.Ship.SailTarget = 1;
        w.Ship.SailFraction = 0.4;
        w.Tick(new ShipInput(0, 0, Action: true));
        Assert.False(w.Digging);
        Assert.Contains("NOTICE_FURL_FIRST", w.Notices);
        w.Ship.SailTarget = 0;
        w.Ship.SailFraction = 0;
        w.Tick(new ShipInput(0, 0, Action: true));
        Assert.True(w.Digging);
        for (int i = 0; i < 30 * 5; i++) { w.Ship.Pos = site.DigRing; w.Tick(new ShipInput(0, 0)); }
        Assert.InRange(w.DigProgress, 4.9, 5.2);
        // Making sail interrupts.
        w.Tick(new ShipInput(0, 1));
        w.Tick(new ShipInput(0, 0));
        Assert.False(w.Digging);
        Assert.Equal(0, w.DigProgress);
        Assert.Contains("NOTICE_DIG_INTERRUPTED", w.Notices);
        w.Tick(new ShipInput(0, -1));
        for (int i = 0; i < 30 * 4; i++) w.Tick(new ShipInput(0, 0));
        Assert.Equal(0, w.Ship.SailFraction, 6);
        gold = w.Player.Gold;
        w.Tick(new ShipInput(0, 0, Action: true));
        Assert.True(w.Digging);
        for (int i = 0; i < 30 * 16; i++) { w.Ship.Pos = site.DigRing; w.Tick(new ShipInput(0, 0)); }
        Assert.True(site.Dug);
        Assert.False(w.Digging);
        Assert.True(w.Player.Gold > gold + 100, $"gold {gold} → {w.Player.Gold}");
        Assert.Equal(1, w.Stats.TreasuresDug);
        Assert.Empty(w.Player.BottleMaps);
        Assert.Contains("NOTICE_TREASURE", w.Notices);
        Assert.True(w.Player.Cargo.Skip((int)Good.Ambergris).Sum() >= 1, "treasure carries a rare good");
    }

    [Fact]
    public void WrecksSalvageInTheSargasso()
    {
        var w = Quiet();
        w.Islands.Clear();
        var wreck = w.Map.Wrecks[0];
        w.Ship.Pos = wreck.Pos;
        Assert.True(w.CanSalvageHere);
        int gold = w.Player.Gold;
        w.Tick(new ShipInput(0, 0, Action: true));
        Assert.True(w.Digging);
        for (int i = 0; i < 30 * 11; i++) { w.Ship.Pos = wreck.Pos; w.Tick(new ShipInput(0, 0)); }
        Assert.True(wreck.Salvaged);
        Assert.True(w.Player.Gold > gold);
        Assert.Contains("NOTICE_SALVAGE", w.Notices);
        Assert.True(RegionDef.Of(RegionType.Sargasso).Produces.Any(g => w.Player.Units(g) > 0), "salvage is Sargasso produce");
        Assert.False(w.CanSalvageHere);
    }

    [Fact]
    public void AchievementsWatchTheRun()
    {
        var w = Quiet(seed: 5);
        w.Islands.Clear();
        foreach (var r in Enum.GetValues<RegionType>()) w.Stats.RegionsEntered.Add(r);
        w.Stats.CovesFound = 3;
        foreach (var r in w.Map.Regions) w.Reveal.PaintRegion(w.Map, r.Type);
        for (int i = 0; i < 31; i++) w.Tick(new ShipInput(0, 0));
        Assert.Contains("nine_seas", w.Player.Achievements);
        Assert.Contains("smugglers_welcome", w.Player.Achievements);
        Assert.Contains("cartographer", w.Player.Achievements);
        Assert.DoesNotContain("fair_winds", w.Player.Achievements);
        var t = Quiet(seed: 5);
        t.Preset = Preset.Tempest;
        t.Islands.Clear();
        t.Player.Cargo[(int)Good.Provisions] = 200;
        t.Player.Gold = 100000;
        for (int i = 0; i < 30 * 120 * 11 + 40; i++) t.Tick(new ShipInput(0, 0));
        Assert.Equal(12, t.Day);
        Assert.Contains("eye_of_the_storm", t.Player.Achievements);
        Assert.Equal(1, t.ThreatTier is >= 1 ? 1 : 0);
    }

    [Fact]
    public void ExplorationSaves()
    {
        var w = Quiet();
        w.Player.Unique.Add("long_nines");
        w.ApplyUnique();
        w.GiveBottleMap(w.Map.Treasures[0].Id);
        w.Player.Cosmetics.Add("flag_black");
        w.Ship.Pos = w.Map.Wrecks[0].Pos;
        w.Tick(new ShipInput(0, 0, Action: true));
        w.Tick(new ShipInput(0, 0));
        Assert.True(w.Digging);
        var loaded = World.LoadJson(w.SaveJson());
        Assert.Contains("long_nines", loaded.Player.Unique);
        Assert.Equal(220, loaded.Ship.Range, 6);
        Assert.Single(loaded.Player.BottleMaps);
        Assert.Contains("flag_black", loaded.Player.Cosmetics);
        Assert.True(loaded.Digging);
        Assert.Equal(w.Hash(), loaded.Hash());
        for (int i = 0; i < 60; i++)
        {
            w.Tick(new ShipInput(0, 0));
            loaded.Tick(new ShipInput(0, 0));
        }
        Assert.Equal(w.Hash(), loaded.Hash());
    }
}
