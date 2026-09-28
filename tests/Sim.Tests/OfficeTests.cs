using LastTide.Sim;

namespace Sim.Tests;

/// <summary>The harbour office: contracts, survey fees, Crown bounties, customs and patrol searches.</summary>
public class OfficeTests
{
    static World Quiet(int seed)
    {
        var w = World.NewRun(seed, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        return w;
    }

    static void DockAt(World w, Port p)
    {
        if (w.IsDocked) Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        w.Ship.Pos = p.Harbor;
        w.Ship.Vel = Vec2.Zero;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
    }

    /// <summary>A world docked at its start port with the first freight offer on the board (and its slot).</summary>
    static (World w, int slot, Contract offer) FreightOnOffer(bool live = false)
    {
        for (int seed = 1; seed < 60; seed++)
        {
            var w = live ? World.NewRun(seed) : Quiet(seed);
            DockAt(w, w.Map.StartPort);
            var offers = w.ContractOffers(w.Map.StartPort);
            for (int i = 0; i < offers.Length; i++)
                if (offers[i] is { } c && c.Kind == ContractKind.Freight) return (w, i, c);
        }
        throw new Exception("no freight offer at any start port");
    }

    static int Sum(World w) => w.DockReceipts.Sum(r => r.Gold);

    [Fact]
    public void TheBoardIsFixedForTheDayAndSendsHerSomewhereReal()
    {
        int seen = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var w = Quiet(seed);
            foreach (var port in w.Map.Ports)
            {
                var a = w.ContractOffers(port);
                var b = w.ContractOffers(port);
                Assert.Equal(World.BoardSize(port), a.Length);
                for (int i = 0; i < a.Length; i++)
                {
                    Assert.Equal(a[i] == null, b[i] == null);
                    if (a[i] is not { } c) continue;
                    seen++;
                    Assert.Equal(b[i]!.To, c.To);
                    Assert.Equal(b[i]!.Pay, c.Pay);
                    var dest = w.Map.Ports[c.To];
                    Assert.Equal(port.Id, c.From);
                    Assert.NotEqual(port.Id, c.To);
                    Assert.False(dest.Secret, "never to a secret cove");
                    Assert.InRange(w.SeaDistance(port.Id, c.To), World.OfferMinDistance, World.OfferMaxDistance);
                    Assert.True(c.Pay > 0 && c.Deadline > w.DaysSurvived + 0.5);
                    var def = ContractDef.Of(c.Kind);
                    if (c.Kind == ContractKind.Dispatch) Assert.Equal(0, c.Slots);
                    else Assert.InRange(c.Slots, def.SlotsFloor, Math.Min(def.SlotsCap, w.Ship.CargoCapacity));
                    Assert.Equal((int)Math.Round(c.Pay * def.Advance), c.Advance);
                    // Colonies and free ports never ship to a haven or smuggle; contraband goes to the Crown and the free ports.
                    if (port.Secret) Assert.Equal(ContractKind.Contraband, c.Kind);
                    else if (port.Faction != Faction.Brethren) Assert.NotEqual(ContractKind.Contraband, c.Kind);
                    if (c.Kind == ContractKind.Contraband) Assert.NotEqual(Faction.Brethren, dest.Faction);
                    else if (port.Faction == Faction.Brethren) Assert.NotEqual(Faction.Crown, dest.Faction);
                    else Assert.NotEqual(Faction.Brethren, dest.Faction);
                    Assert.True(dest.Region != RegionType.Mangrove || RegionDef.MangroveHulls.Contains(w.Ship.Hull.Id));
                }
            }
        }
        Assert.True(seen > 300, $"only {seen} offers across twelve maps");
    }

    [Fact]
    public void ABigHullIsNeverSentIntoTheMangroves()
    {
        for (int seed = 1; seed <= 8; seed++)
        {
            var w = World.NewRun(seed, "frigate", populate: false);
            foreach (var port in w.Map.Ports)
                foreach (var c in w.ContractOffers(port))
                    if (c != null) Assert.NotEqual(RegionType.Mangrove, w.Map.Ports[c.To].Region);
        }
    }

    [Fact]
    public void ANewDayPostsANewBoard()
    {
        var w = Quiet(3);
        var port = w.Map.StartPort;
        string Board() => string.Join(";", w.ContractOffers(port).Select(c => c == null ? "-" : $"{c.Kind}{c.To}:{c.Pay}"));
        string day1 = Board();
        Sea.Run(w, Tuning.SecondsPerDay + 1);
        Assert.Equal(2, w.Day);
        Assert.NotEqual(day1, Board());
    }

    [Fact]
    public void SigningPaysTheAdvanceTakesTheHoldAndClearsTheSlot()
    {
        var (w, slot, offer) = FreightOnOffer();
        int gold = w.Player.Gold;
        double slots = w.Player.SlotsUsed;
        w.Map.Ports[offer.To].Discovered = false;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.SignContract, Amount: slot)));
        Assert.Equal(gold + offer.Advance, w.Player.Gold);
        Assert.True(offer.Advance > 0);
        Assert.Equal(slots + offer.Slots, w.Player.SlotsUsed, 6);
        Assert.Single(w.Player.Contracts);
        Assert.True(w.Map.Ports[offer.To].Discovered, "the office marks the destination on her chart");
        Assert.Null(w.ContractOffers(w.Docked!)[slot]);
        Assert.Equal(PortResult.Nothing, w.Apply(new PortCommand(PortAction.SignContract, Amount: slot)));
        // Casting off and coming straight back: the signed slot stays empty.
        DockAt(w, w.Map.StartPort);
        Assert.Null(w.ContractOffers(w.Docked!)[slot]);
    }

    [Fact]
    public void FreightNeedsRoomAndHerHandsAreFullAtThree()
    {
        var (w, slot, offer) = FreightOnOffer();
        w.Player.Cargo[(int)Good.Silk] = (int)Math.Floor(w.Ship.CargoCapacity - w.Player.SlotsUsed);   // under a slot free
        Assert.Equal(PortResult.NoRoom, w.Apply(new PortCommand(PortAction.SignContract, Amount: slot)));
        w.Player.Cargo[(int)Good.Silk] = 0;
        for (int i = 0; i < World.MaxContracts; i++)
            w.Player.Contracts.Add(new Contract { Kind = ContractKind.Dispatch, From = offer.From, To = offer.To, Pay = 10, Deadline = 99 });
        Assert.Equal(PortResult.Busy, w.Apply(new PortCommand(PortAction.SignContract, Amount: slot)));
    }

    [Fact]
    public void DeliveryPaysTheRestOnArrivalAndTheIssuerRemembers()
    {
        var (w, slot, offer) = FreightOnOffer();
        w.Apply(new PortCommand(PortAction.SignContract, Amount: slot));
        var issuer = w.Issuer(offer);
        double rep = w.Player.Rep(issuer);
        int gold = w.Player.Gold, earned = w.Stats.GoldEarned;
        DockAt(w, w.Map.Ports[offer.To]);
        Assert.Empty(w.Player.Contracts);
        var receipt = w.DockReceipts.Single(r => r.Key == "RECEIPT_DELIVERED");
        Assert.Equal(offer.Pay - offer.Advance, receipt.Gold);
        Assert.Equal(offer.From, receipt.Arg);
        Assert.Equal(gold + Sum(w), w.Player.Gold);
        Assert.Equal(earned + offer.Pay + (Sum(w) - receipt.Gold), w.Stats.GoldEarned);
        Assert.True(w.Player.Rep(issuer) > rep, "the house that hired her thinks better of her");
    }

    [Fact]
    public void AMissedDeadlineTakesBackTheAdvanceAndCostsStanding()
    {
        var (w, slot, offer) = FreightOnOffer();
        w.Apply(new PortCommand(PortAction.SignContract, Amount: slot));
        w.Apply(new PortCommand(PortAction.CastOff));
        var issuer = w.Issuer(offer);
        double rep = w.Player.Rep(issuer);
        int gold = w.Player.Gold;
        w.Player.Contracts[0].Deadline = w.DaysSurvived + Tuning.Dt / Tuning.SecondsPerDay * 0.5;
        w.Tick(new ShipInput(0, 0));
        w.Tick(new ShipInput(0, 0));
        Assert.Empty(w.Player.Contracts);
        Assert.Equal(gold - offer.Advance, w.Player.Gold);
        Assert.Equal(rep - World.ContractFailRep, w.Player.Rep(issuer), 1);
        Assert.Contains(w.Notices, n => n == "NOTICE_CONTRACT_LATE|" + w.Map.Ports[offer.To].Name);
        Assert.Equal(6.7, w.Player.SlotsUsed, 6);   // the crates went with the contract (the start kit is 6.7 slots)
    }

    [Fact]
    public void GivingUpTakesBackOnlyWhatThePurseHolds()
    {
        var (w, slot, offer) = FreightOnOffer();
        w.Apply(new PortCommand(PortAction.SignContract, Amount: slot));
        w.Player.Gold = 3;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.AbandonContract, Amount: 0)));
        Assert.Equal(0, w.Player.Gold);
        Assert.Empty(w.Player.Contracts);
        Assert.Equal(PortResult.Nothing, w.Apply(new PortCommand(PortAction.AbandonContract, Amount: 0)));
    }

    [Fact]
    public void ASurveyFeeIsPaidOnceAPortAndNeverAtHomeOrACove()
    {
        var w = Quiet(5);
        DockAt(w, w.Map.StartPort);
        Assert.DoesNotContain(w.DockReceipts, r => r.Key == "RECEIPT_SURVEY");
        var ports = w.Map.Ports.Where(p => !p.Secret && p.Id != w.Map.StartPortId && w.IsOpen(p))
            .OrderBy(p => p.Harbor.DistanceTo(w.Map.StartPort.Harbor)).ToList();
        var near = ports.First();
        var far = ports.Last();
        Assert.InRange(w.SurveyFee(near), World.SurveyBase, World.SurveyCap);
        Assert.True(w.SurveyFee(far) > w.SurveyFee(near), "farther from home pays more");
        int gold = w.Player.Gold;
        DockAt(w, near);
        Assert.Equal(w.SurveyFee(near), w.DockReceipts.Single(r => r.Key == "RECEIPT_SURVEY").Gold);
        Assert.Equal(gold + w.SurveyFee(near), w.Player.Gold);
        DockAt(w, w.Map.StartPort);
        DockAt(w, near);
        Assert.DoesNotContain(w.DockReceipts, r => r.Key == "RECEIPT_SURVEY");
        var cove = w.Map.Ports.First(p => p.Secret);
        Assert.Equal(0, w.SurveyFee(cove));
    }

    static (World w, Ship target) Arena(Faction faction)
    {
        var w = Sea.Fixed(fromCompass: 0, seed: 4);
        Sea.Point(w, 90);
        w.Ship.Pos = new Vec2(-500, 900);
        w.Ship.Cannons = 4;
        w.Ship.Crew = 8;
        w.Ship.Order = CrewOrder.Battle;
        w.Player.Cargo[(int)Good.Munitions] = 40;
        var target = w.Spawn("sloop", w.Ship.Pos + new Vec2(0, 120), Angles.FromCompassDeg(90), faction, null, crew: 6, cannons: 2);
        target.HullHp = 1;
        target.Crew = 1;
        return (w, target);
    }

    static void SinkIt(World w)
    {
        w.Tick(new ShipInput(0, 0, FireStarboard: true));
        for (int i = 0; i < 30 * 40 && w.Others.Count > 0; i++) w.Tick(new ShipInput(0, 0));   // the stand's glass is 35 s
        Assert.Empty(w.Others);
    }

    [Fact]
    public void TheCrownPaysForPiratesAtItsOwnPortsOnly()
    {
        var (w, _) = Arena(Faction.Brethren);
        SinkIt(w);
        int bounty = (int)Math.Round(World.BountyBase * Threat.EnemyScale(w.ThreatNow));   // a sloop's hull costs nothing
        Assert.Equal(bounty, w.Player.BountyOwed);
        Assert.Equal(1, w.Player.BountyShips);
        Assert.Contains(w.Notices, n => n == "NOTICE_BOUNTY|" + bounty);
        var free = w.Map.Ports.First(p => !p.Secret && p.Faction == Faction.FreeTraders && w.IsOpen(p));
        DockAt(w, free);
        Assert.Equal(bounty, w.Player.BountyOwed);
        Assert.DoesNotContain(w.DockReceipts, r => r.Key == "RECEIPT_BOUNTY");
        var crown = w.Map.Ports.First(p => !p.Secret && p.Faction == Faction.Crown && w.IsOpen(p));
        int gold = w.Player.Gold;
        DockAt(w, crown);
        var paid = w.DockReceipts.Single(r => r.Key == "RECEIPT_BOUNTY");
        Assert.Equal(bounty, paid.Gold);
        Assert.Equal(1, paid.Arg);
        Assert.Equal(gold + Sum(w), w.Player.Gold);
        Assert.Equal(0, w.Player.BountyOwed);
        Assert.Equal(0, w.Player.BountyShips);
    }

    [Fact]
    public void NoBountyForAMerchant()
    {
        var (w, _) = Arena(Faction.FreeTraders);
        SinkIt(w);
        Assert.Equal(0, w.Player.BountyOwed);
    }

    [Fact]
    public void BiggerPiratesAndAMeanerSeaPayMore()
    {
        var w = Quiet(2);
        var sloop = w.Spawn("sloop", Vec2.Zero, 0, Faction.Brethren, null);
        var brig = w.Spawn("brig", Vec2.Zero, 0, Faction.Brethren, null);
        Assert.Equal(World.BountyBase, w.Bounty(sloop));
        Assert.Equal((int)Math.Round(World.BountyBase + World.BountyHullShare * Hulls.Get("brig").Cost), w.Bounty(brig));
        Assert.True(w.DangerMoney == 1);
    }

    /// <summary>A contraband run in her hold, bound for <paramref name="to"/>.</summary>
    static Contract Smuggle(World w, Port to) =>
        new() { Kind = ContractKind.Contraband, From = w.Map.Ports.First(p => p.Secret).Id, To = to.Id, Slots = 2, Pay = 200, Deadline = 999 };

    [Fact]
    public void CustomsSearchSomeDocksAndSeizeWhatTheyFind()
    {
        var w = Quiet(6);
        var crown = w.Map.Ports.First(p => !p.Secret && p.Faction == Faction.Crown && w.IsOpen(p));
        var other = w.Map.Ports.First(p => !p.Secret && p.Faction == Faction.Crown && p != crown && w.IsOpen(p));
        int caught = 0, runs = 400;
        for (int i = 0; i < runs; i++)
        {
            w.Player.Reputation[(int)Faction.Crown] = 0;
            w.Player.Gold = 1000;
            w.Player.Contracts.Clear();
            w.Player.Contracts.Add(Smuggle(w, other));
            DockAt(w, i % 2 == 0 ? crown : other);
            Assert.Equal(1000 + Sum(w), w.Player.Gold);
            if (w.DockReceipts.Any(r => r.Key == "RECEIPT_CAUGHT"))
            {
                caught++;
                Assert.Empty(w.Player.Contracts);
                Assert.Equal(-100, w.DockReceipts.Single(r => r.Key == "RECEIPT_CAUGHT").Gold);   // half the 200 fee
                Assert.Equal(-World.CaughtRep, w.Player.Rep(Faction.Crown), 1);
            }
            else if (w.Docked == other)
            {
                Assert.Contains(w.DockReceipts, r => r.Key == "RECEIPT_SMUGGLED" && r.Gold == 200);
                Assert.Empty(w.Player.Contracts);
            }
            else Assert.Single(w.Player.Contracts);   // not searched, and not her buyer's port
        }
        Assert.InRange(caught / (double)runs, World.CustomsCrown - 0.07, World.CustomsCrown + 0.07);
        // No customs at a haven or a cove.
        var haven = w.Map.Ports.First(p => !p.Secret && p.Faction == Faction.Brethren && w.IsOpen(p));
        for (int i = 0; i < 40; i++)
        {
            w.Player.Contracts.Clear();
            w.Player.Contracts.Add(Smuggle(w, crown));
            DockAt(w, haven);
            Assert.Single(w.Player.Contracts);
        }
    }

    [Fact]
    public void ACrownPatrolAlongsideSearchesHerOnce()
    {
        var w = Sea.Fixed(seed: 4);
        var crown = w.Map.Ports.First(p => !p.Secret && p.Faction == Faction.Crown);
        int caught = 0, runs = 240;
        for (int k = 0; k < runs; k++)
        {
            w.Player.Gold = 500;
            w.Player.Reputation[(int)Faction.Crown] = 0;
            w.Player.Contracts.Clear();
            w.Player.Contracts.Add(Smuggle(w, crown));
            w.Notices.Clear();
            var patrol = w.Spawn("cutter", w.Ship.Pos + new Vec2(60, 0), 0, Faction.Crown, null);
            patrol.Ai = new AiState { Role = Role.Patrol, HomePort = crown.Id };
            long night = w.MidnightsPassed;
            for (int t = 0; t < 20; t++) w.Tick(new ShipInput(0, 0));
            bool paidWages = w.MidnightsPassed != night;   // midnight's wages fell in these ticks
            var searches = w.Notices.Where(n => n.StartsWith("NOTICE_SEARCH")).ToList();
            if (w.Player.Contracts.Count == 0)
            {
                caught++;
                Assert.Equal("NOTICE_SEARCH_CAUGHT|100", Assert.Single(searches));
                if (!paidWages)
                {
                    Assert.Equal(400, w.Player.Gold);
                    Assert.Equal(-World.CaughtRep, w.Player.Rep(Faction.Crown), 1);   // (midnight also eases standing)
                }
            }
            else
            {
                Assert.Contains(patrol.Id, w.SearchedBy);
                Assert.Equal("NOTICE_SEARCH_CLEAR", Assert.Single(searches));   // one search per ship
            }
            w.Others.Remove(patrol);
        }
        Assert.InRange(caught / (double)runs, World.PatrolSearch - 0.1, World.PatrolSearch + 0.1);
    }

    [Fact]
    public void AHostilePatrolFightsInsteadOfSearchingAndNoneSearchAnHonestHold()
    {
        var w = Sea.Fixed(seed: 4);
        var crown = w.Map.Ports.First(p => !p.Secret && p.Faction == Faction.Crown);
        var patrol = w.Spawn("cutter", w.Ship.Pos + new Vec2(60, 0), 0, Faction.Crown, null);
        patrol.Ai = new AiState { Role = Role.Patrol, HomePort = crown.Id };
        for (int t = 0; t < 10; t++) w.Tick(new ShipInput(0, 0));
        Assert.Empty(w.SearchedBy);   // nothing to find, nothing searched
        w.Player.Contracts.Add(Smuggle(w, crown));
        w.Player.Reputation[(int)Faction.Crown] = -30;
        for (int t = 0; t < 10; t++) w.Tick(new ShipInput(0, 0));
        Assert.Empty(w.SearchedBy);
        Assert.Single(w.Player.Contracts);
    }

    [Fact]
    public void TheHiddenHoldHalvesTheOdds()
    {
        var w = Quiet(6);
        var crown = w.Map.Ports.First(p => !p.Secret && p.Faction == Faction.Crown && w.IsOpen(p));
        w.Player.Unique.Add("hidden_hold");
        w.ApplyUnique();
        int caught = 0, runs = 600;
        for (int i = 0; i < runs; i++)
        {
            w.Player.Reputation[(int)Faction.Crown] = 0;
            w.Player.Contracts.Clear();
            w.Player.Contracts.Add(Smuggle(w, w.Map.Ports.First(p => p.Secret)));
            DockAt(w, crown);
            if (w.DockReceipts.Any(r => r.Key == "RECEIPT_CAUGHT")) caught++;
        }
        Assert.InRange(caught / (double)runs, World.CustomsCrown / 2 - 0.05, World.CustomsCrown / 2 + 0.05);
    }

    [Fact]
    public void TheOfficeReplaysAndSurvivesASave()
    {
        var (w, slot, offer) = FreightOnOffer(live: true);
        w.Apply(new PortCommand(PortAction.SignContract, Amount: slot));
        w.Apply(new PortCommand(PortAction.CastOff));
        Sea.Run(w, 3, sailDelta: 2);
        // The command log replays the signing.
        var replay = World.Replay(w.Seed, w.HullId, w.Log, w.Commands, w.Ticks, w.Preset, mapVersion: w.Map.Version);
        Assert.Equal(w.Hash(), replay.Hash());
        Assert.Equal(offer.Pay, replay.Player.Contracts.Single().Pay);
        w.Player.BountyOwed = 77;
        w.Player.BountyShips = 2;
        var copy = World.LoadJson(w.SaveJson());
        Assert.Equal(w.Hash(), copy.Hash());
        Assert.Equal(offer.Pay, copy.Player.Contracts.Single().Pay);
        Assert.Equal(offer.Deadline, copy.Player.Contracts.Single().Deadline);
        Assert.Contains(offer.Offer, copy.Player.TakenOffers);
        Assert.Equal(77, copy.Player.BountyOwed);
        Sea.Run(w, 2);
        Sea.Run(copy, 2);
        Assert.Equal(w.Hash(), copy.Hash());
    }

    [Fact]
    public void ASaveNamingAPortBeyondTheMapIsRefused()
    {
        var (w, slot, _) = FreightOnOffer();
        w.Apply(new PortCommand(PortAction.SignContract, Amount: slot));
        var json = w.SaveJson().Replace($"\"To\":{w.Player.Contracts[0].To},", "\"To\":999,");
        Assert.Throws<InvalidDataException>(() => World.LoadJson(json));
    }
}
