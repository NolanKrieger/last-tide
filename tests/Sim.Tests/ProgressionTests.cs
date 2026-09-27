using LastTide.Sim;

namespace Sim.Tests;

public class ProgressionTests
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

    [Fact]
    public void PartsHaveFiveGradesPricedByHullAndTheirEffectsShow()
    {
        var w = Docked();
        Assert.Equal(PartDef.Price(Part.Sails, 1, Hulls.Sloop), w.PartPrice(Part.Sails));
        Assert.True(PartDef.Price(Part.Sails, 1, Hulls.Get("man_o_war")) > PartDef.Price(Part.Sails, 1, Hulls.Sloop) * 3);
        double speed = w.Ship.TopSpeed, point = w.Ship.PointDeg, hp = w.Ship.MaxHp;
        int cargo = w.Ship.CargoCapacity;
        for (int g = 1; g <= 5; g++) Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Sails)));
        Assert.Equal(PortResult.Nothing, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Sails)));
        Assert.Equal(5, w.Ship.Grade(Part.Sails));
        Assert.Equal(speed * 1.15, w.Ship.TopSpeed, 6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Rigging)));
        Assert.Equal(point - 3, w.Ship.PointDeg, 6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Planking)));
        Assert.Equal(hp * 1.12, w.Ship.MaxHp, 6);
        Assert.Equal(w.Ship.MaxHp, w.Ship.HullHp, 6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Hold)));
        Assert.Equal((int)Math.Round(cargo * 1.1), w.Ship.CargoCapacity);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Cannons)));
        Assert.Equal(1, w.Ship.CannonGrade);
        Assert.Equal(180 + 28, w.Ship.Range, 6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Copper)));
        Assert.Equal(0.12, w.Ship.LeakSaveChance, 6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Pumps)));
        Assert.Equal(1.3, w.Ship.PumpMult, 6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Ram)));
        Assert.Equal(1.25, w.Ship.RamDamageMult, 6);
        w.Player.Gold = 0;
        Assert.Equal(PortResult.NoGold, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Lantern)));
    }

    [Fact]
    public void BuyingAHullCarriesOverCannonsLanternAndPumpsAndTradesInTheRest()
    {
        var w = Docked();
        foreach (var p in new[] { Part.Sails, Part.Cannons, Part.Lantern, Part.Pumps, Part.Planking })
            Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)p)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyCannon)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyCannon)));
        Assert.Equal(4, w.Ship.Cannons);
        int tradeIn = w.TradeInValue();
        Assert.True(tradeIn > 0 && tradeIn < 1000, $"trade-in {tradeIn}");
        int gold = w.Player.Gold;
        var brig = Hulls.Get("brig");
        int price = w.HullPrice(brig);
        Assert.Equal(brig.Cost - tradeIn, price);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "brig")));
        Assert.Equal("brig", w.Ship.Hull.Id);
        Assert.Equal(gold - price, w.Player.Gold);
        Assert.Equal(4, w.Ship.Cannons);
        Assert.Equal(1, w.Ship.Grade(Part.Cannons));
        Assert.Equal(1, w.Ship.Grade(Part.Lantern));
        Assert.Equal(1, w.Ship.Grade(Part.Pumps));
        Assert.Equal(0, w.Ship.Grade(Part.Sails));
        Assert.Equal(0, w.Ship.Grade(Part.Planking));
        Assert.Equal(w.Ship.MaxHp, w.Ship.HullHp, 6);
        Assert.Equal(4, w.Ship.Crew);
        Assert.True(w.IsDocked);
        // Down to a cutter: extra guns are sold, crew paid off to the berths.
        w.Ship.Crew = 36;
        w.Ship.Cannons = 14;
        int before = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "cutter")));
        Assert.Equal(4, w.Ship.Cannons);
        Assert.Equal(12, w.Ship.Crew);
        Assert.True(w.Player.Gold > before - Hulls.Get("cutter").Cost, "the trade-in and the sold guns count");
        // Cargo must fit.
        w.Player.Cargo[(int)Good.Sugar] = 24;
        Assert.Equal(PortResult.NoRoom, w.Apply(new PortCommand(PortAction.BuyHull, Text: "sloop")));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "galleon")));
        Assert.Contains("galleon_captain", w.Player.Achievements);
        Assert.Contains("galleon", w.Stats.HullsOwned);
    }

    [Fact]
    public void OfficersHireIntoSlotsAndEarnTheirKeep()
    {
        var w = Docked();
        var offer = w.TavernOfficers(w.Docked!);
        Assert.Equal(3, offer.Count);
        var look = offer.First(o => o.Type == OfficerType.Lookout);
        int gold = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)OfficerType.Lookout * 10 + look.Tier)));
        Assert.Equal(gold - Officers.Price[look.Tier], w.Player.Gold);
        Assert.Single(w.Player.Officers);
        Assert.Equal(1 + Officers.LookoutVision[look.Tier], w.VisionMult, 6);
        Assert.Equal(Officers.LookoutWarning[look.Tier], w.WarningRange);
        // A sloop has one slot.
        var q = offer.First(o => o.Type == OfficerType.Quartermaster);
        Assert.Equal(PortResult.CrewFull, w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)OfficerType.Quartermaster * 10 + q.Tier)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.DismissOfficer, Amount: (int)OfficerType.Lookout)));
        Assert.Equal(1.0, w.VisionMult, 6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)OfficerType.Quartermaster * 10 + q.Tier)));
        int wages = w.DailyWages();
        Assert.Equal((int)Math.Round(10 * (1 - Officers.QuartermasterWages[q.Tier])) + Officers.Wage[q.Tier], wages);
        // Better prices with a quartermaster.
        var good = w.Docked!.Produces[0];
        int quote = w.Docked.Market.QuoteBuy(good, 1);
        gold = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Buy, good, 1)));
        Assert.Equal(gold - (int)Math.Round(quote * (1 - Officers.QuartermasterPrices[q.Tier])), w.Player.Gold);
        // Friendly standing knocks 10% off too.
        w.Player.Reputation[(int)w.Docked.Faction] = 30;
        Assert.InRange(w.BuyPriceMult(w.Docked), 0.8, 0.88);
        Assert.InRange(w.SellPriceMult(w.Docked), 1.12, 1.22);
        // Unpaid: officers walk at the next dock.
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Player.Unpaid = true;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.Empty(w.Player.Officers);
        Assert.Contains("NOTICE_OFFICERS_LEFT", w.Notices);
    }

    [Fact]
    public void MarinesVolleyKillsEnemyCrewInRange()
    {
        var w = Sea.Fixed(fromCompass: 0, seed: 4);
        w.Player.Officers.Add(new Officer { Type = OfficerType.Marines, Tier = 2 });
        var foe = w.Spawn("sloop", w.Ship.Pos + new Vec2(0, 80), 0, Faction.Brethren, null, crew: 9, cannons: 2);
        foe.Ai = new AiState { Role = Role.Raider };
        Sea.Run(w, 7);
        Assert.Equal(6, foe.Crew);   // one volley of three
        Sea.Run(w, 6);
        Assert.Equal(3, foe.Crew);
        Sea.Run(w, 30);
        Assert.Equal(1, foe.Crew);   // never the last hand
        Assert.True(foe.PlayerHostile);
    }

    [Fact]
    public void CustomStationsAndShortRiggersSlowTheShip()
    {
        var w = Sea.Fixed(fromCompass: 90);
        Sea.Point(w, 0);
        w.Ship.Cannons = 2;
        w.Ship.Crew = 6;
        w.SetStations(guns: 2, sails: 1, repair: 1);
        Assert.Equal(CrewOrder.Custom, w.Ship.Order);
        Assert.Equal(new[] { 2, 1, 1, 2 }, w.Ship.Stations());
        Assert.Equal(0.5, w.Ship.RiggerFactor, 6);
        Sea.SetSail(w, 3);
        Sea.Run(w, 90);
        double shortHanded = w.Ship.ForwardSpeed;
        w.SetStations(guns: 2, sails: 2, repair: 1);
        Sea.Run(w, 90);
        Assert.True(w.Ship.ForwardSpeed > shortHanded * 1.05, $"{shortHanded:F2} → {w.Ship.ForwardSpeed:F2}");
        Assert.Equal(Tuning.SloopTopSpeed, w.Ship.ForwardSpeed, 1);
        w.Tick(new ShipInput(0, 0, Order: 1));
        Assert.Equal(CrewOrder.Battle, w.Ship.Order);
    }

    [Fact]
    public void ProgressionSaves()
    {
        var w = Docked(seed: 6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "schooner")));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Rigging)));
        var offer = w.TavernOfficers(w.Docked!);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)OfficerType.Lookout * 10 + offer[0].Tier)));
        w.SetStations(1, 2, 1);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        for (int i = 0; i < 300; i++) w.Tick(new ShipInput(0.2, i == 0 ? 2 : 0));
        var loaded = World.LoadJson(w.SaveJson());
        Assert.Equal("schooner", loaded.Ship.Hull.Id);
        Assert.Equal(1, loaded.Ship.Grade(Part.Rigging));
        Assert.Single(loaded.Player.Officers);
        Assert.Equal(w.VisionMult, loaded.VisionMult, 9);
        Assert.Equal(CrewOrder.Custom, loaded.Ship.Order);
        Assert.Equal(w.Hash(), loaded.Hash());
        for (int i = 0; i < 120; i++)
        {
            w.Tick(new ShipInput(-0.3, 0));
            loaded.Tick(new ShipInput(-0.3, 0));
        }
        Assert.Equal(w.Hash(), loaded.Hash());
    }

    [Fact]
    public void ProgressionReplaysFromTheCommandLog()
    {
        // Only what 200 gold buys, so the replay can afford the same; the same populated sea on both sides.
        var w = World.NewRun(6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)Part.Rigging)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Hire, Amount: 1)));
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        for (int i = 0; i < 300; i++) w.Tick(new ShipInput(0.2, i == 0 ? 2 : 0));
        var replay = World.Replay(w.Seed, w.HullId, w.Log, w.Commands, w.Ticks);
        Assert.Equal(1, replay.Ship.Grade(Part.Rigging));
        Assert.Equal(5, replay.Ship.Crew);
        Assert.Equal(w.Hash(), replay.Hash());
    }
}
