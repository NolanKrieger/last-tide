using System.Text.Json.Nodes;
using LastTide.Sim;

namespace Sim.Tests;

/// <summary>
/// The cartographer (Nolan, 2026-09-27): hired at taverns in three tiers with his own berth; without one nothing new
/// is charted (no ink, no ports marked) and the chart cannot be opened, but what was charted before is kept.
/// </summary>
public class CartographerTests
{
    static World Quiet(int seed = 4)
    {
        var w = World.NewRun(seed, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        return w;
    }

    /// <summary>Hires the tavern's cartographer through the logged port command (so a replay hires him too).</summary>
    static Officer HireCartographer(World w)
    {
        var offer = w.TavernOfficers(w.Docked!).First(o => o.Type == OfficerType.Cartographer);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)OfficerType.Cartographer * 10 + offer.Tier)));
        return offer;
    }

    /// <summary>The region farthest from home: nothing there is charted at the start.</summary>
    static Vec2 FarSea(World w) => w.Map.Regions.OrderByDescending(r => r.Seed.DistanceTo(w.Map.StartPort.Harbor)).First().Seed;

    [Fact]
    public void TheVoyageStartsWithTheHarbourMastersChartButNoCartographer()
    {
        var w = Quiet();
        Assert.False(w.HasCartographer);
        Assert.Equal(0, w.ChartRadius);
        Assert.True(w.Reveal.IsRevealed(w.Map.StartPort.Harbor), "the harbour master's chart of the Trade Isles is aboard");
        Assert.True(w.Map.StartPort.Discovered);
        // The home tavern always has a green one on offer, whatever the roll.
        foreach (int seed in new[] { 4, 9, 21 })
        {
            var v = seed == 4 ? w : World.NewRun(seed, populate: false);
            var offer = v.TavernOfficers(v.Map.StartPort).Single(o => o.Type == OfficerType.Cartographer);
            Assert.Equal(0, offer.Tier);
            Assert.True(Officers.PriceOf(OfficerType.Cartographer, 0) < v.Player.Gold, "the lean start can afford him");
        }
        // Every tavern offers one (one of each type).
        Assert.All(w.Map.Ports.Take(12), p => Assert.Contains(w.TavernOfficers(p), o => o.Type == OfficerType.Cartographer));
    }

    [Fact]
    public void WithoutACartographerSailingInksNothingAndMarksNoPort()
    {
        var w = Quiet();
        w.Islands.Clear();
        w.Map.Whirlpools.Clear();
        int cells = w.Reveal.RevealedCells;
        var known = w.Map.Ports.Where(p => p.Discovered).Select(p => p.Id).ToList();
        // Sail right past every port on the way to the far sea, glass up, then tie up somewhere new.
        var target = FarSea(w);
        foreach (var port in w.Map.Ports.Where(p => !p.Discovered).OrderBy(p => p.Harbor.DistanceTo(target)).Take(6))
        {
            w.Ship.Pos = port.Harbor + new Vec2(20, 0);
            for (int t = 0; t < 30; t++) w.Tick(new ShipInput(0, 0, Spyglass: true, SpyglassDir: t * 0.2));
        }
        var strange = w.Map.Ports.First(p => !p.Discovered && !p.Secret && w.IsOpen(p));
        w.Ship.Pos = strange.Harbor;
        w.Ship.Vel = Vec2.Zero;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.Equal(cells, w.Reveal.RevealedCells);
        Assert.Equal(known, w.Map.Ports.Where(p => p.Discovered).Select(p => p.Id).ToList());
        Assert.False(strange.Discovered, "tying up without a cartographer marks nothing");
        Assert.Contains(strange.Id, w.Player.PortsVisited);
        Assert.NotEmpty(w.Player.Ledger.Where(e => e.Port == strange.Id));   // the purser's ledger is kept either way
        Assert.Equal(0, w.Stats.CovesFound);
    }

    [Fact]
    public void ACartographerInksOutToHisReachAndMarksPortsInside()
    {
        for (int tier = 0; tier < 3; tier++)
        {
            var w = Quiet();
            w.Islands.Clear();
            w.Player.Officers.Add(new Officer { Type = OfficerType.Cartographer, Tier = tier });
            var at = FarSea(w);
            Assert.False(w.Reveal.IsRevealed(at));
            w.Ship.Pos = at;
            w.Ship.Vel = Vec2.Zero;
            w.Tick(new ShipInput(0, 0));
            double reach = w.ChartRadius;
            Assert.Equal(w.VisionRadius * Officers.CartographerReach[tier], reach, 9);
            for (int k = 0; k < 8; k++)
            {
                var dir = Vec2.FromAngle(k * Angles.Tau / 8);
                var inside = w.Ship.Pos + dir * (reach - 2 * RevealMask.Cell);
                var outside = w.Ship.Pos + dir * (reach + 2 * RevealMask.Cell);
                if (!Map.InBounds(inside) || !Map.InBounds(outside)) continue;
                Assert.True(w.Reveal.IsRevealed(inside), $"tier {tier}: {reach - 2 * RevealMask.Cell:F0} m out is inked");
                Assert.False(w.Reveal.IsRevealed(outside), $"tier {tier}: {reach + 2 * RevealMask.Cell:F0} m out is blank");
            }
            foreach (var port in w.Map.Ports)
                if (port.Harbor.DistanceTo(w.Ship.Pos) <= reach) Assert.True(port.Discovered, $"tier {tier}: {port.Name} inside his reach");
        }
        Assert.True(Officers.CartographerReach[0] >= 1 && Officers.CartographerReach[1] > Officers.CartographerReach[0] && Officers.CartographerReach[2] > Officers.CartographerReach[1]);
    }

    [Fact]
    public void TheChartIsKeptWhileNoCartographerIsAboard()
    {
        var w = Quiet();
        w.Islands.Clear();
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        var hired = HireCartographer(w);
        Assert.Equal(0, hired.Tier);
        Assert.True(w.HasCartographer);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        var first = FarSea(w);
        w.Ship.Pos = first;
        w.Tick(new ShipInput(0, 0));
        Assert.True(w.Reveal.IsRevealed(first));
        int charted = w.Reveal.RevealedCells;
        // He goes ashore: sailing on inks nothing, but the far sea stays charted.
        w.Player.Officers.RemoveAll(o => o.Type == OfficerType.Cartographer);
        var second = first + new Vec2(first.X > 0 ? -1500 : 1500, 0);
        w.Ship.Pos = second;
        w.Tick(new ShipInput(0, 0));
        Assert.Equal(charted, w.Reveal.RevealedCells);
        Assert.False(w.Reveal.IsRevealed(second));
        Assert.True(w.Reveal.IsRevealed(first));
    }

    [Fact]
    public void TheCartographerTakesNoOfficerSlot()
    {
        var w = Quiet();
        w.Player.Gold = 100000;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.Equal(1, w.Ship.Hull.OfficerSlots);
        var offer = w.TavernOfficers(w.Docked!);
        Assert.Equal(Enum.GetValues<OfficerType>().Length, offer.Count);
        var look = offer.First(o => o.Type == OfficerType.Lookout);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)OfficerType.Lookout * 10 + look.Tier)));
        int gold = w.Player.Gold;
        var cart = HireCartographer(w);
        Assert.Equal(gold - Officers.PriceOf(OfficerType.Cartographer, cart.Tier), w.Player.Gold);
        Assert.Equal(2, w.Player.Officers.Count);
        Assert.Equal(1, w.Player.SlottedOfficers);
        // The one slot is still full for a third.
        var q = offer.First(o => o.Type == OfficerType.Quartermaster);
        Assert.Equal(PortResult.CrewFull, w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)OfficerType.Quartermaster * 10 + q.Tier)));
        // A bigger hull, a full wardroom, then back down to one slot: the extra officers go ashore, he stays.
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "brigantine")));
        Assert.Equal(2, w.Ship.Hull.OfficerSlots);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)OfficerType.Quartermaster * 10 + q.Tier)));
        Assert.Equal(3, w.Player.Officers.Count);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "sloop")));
        Assert.Equal(1, w.Player.SlottedOfficers);
        Assert.NotNull(w.Player.OfficerOf(OfficerType.Lookout));   // the first slotted officer keeps her berth
        Assert.True(w.HasCartographer);
        Assert.Contains("NOTICE_OFFICERS_ASHORE", w.Notices);
        // Dismissed, he goes like any officer.
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.DismissOfficer, Amount: (int)OfficerType.Cartographer)));
        Assert.False(w.HasCartographer);
    }

    [Fact]
    public void HeDrawsWagesAndLeavesWhenUnpaid()
    {
        var w = Quiet();
        w.Wind.SetFixed(0, 6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        int before = w.DailyWages();
        var cart = HireCartographer(w);
        Assert.Equal(before + Officers.WageOf(OfficerType.Cartographer, cart.Tier), w.DailyWages());
        Assert.Equal(Officers.CartographerWage[cart.Tier], w.OfficerWages());
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        // An empty purse at midnight: the hands and the officers go unpaid.
        w.Player.Gold = 0;
        var harbour = w.Ship.Pos;
        long midnight = (long)((24 - Tuning.DawnHour) * Tuning.SecondsPerHour * Tuning.TicksPerSecond) + 5;
        for (long t = 0; t < midnight; t++) w.Tick(new ShipInput(0, 0));
        Assert.True(w.Player.Unpaid);
        Assert.True(w.HasCartographer, "he stays aboard until the next harbour");
        w.Ship.Pos = harbour;
        w.Ship.Vel = Vec2.Zero;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.False(w.HasCartographer);
        Assert.Contains("NOTICE_OFFICERS_LEFT", w.Notices);
        Assert.True(w.Reveal.IsRevealed(harbour), "the chart stays aboard when he walks off");
    }

    [Fact]
    public void HeSurvivesASaveAndAnOlderVoyageResumesWithOne()
    {
        var w = Quiet(6);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        var cart = HireCartographer(w);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        for (int i = 0; i < 300; i++) w.Tick(new ShipInput(0.2, i == 0 ? 2 : 0));
        var loaded = World.LoadJson(w.SaveJson());
        Assert.True(loaded.HasCartographer);
        Assert.Equal(cart.Tier, loaded.Player.OfficerTier(OfficerType.Cartographer));
        Assert.Equal(w.ChartRadius, loaded.ChartRadius, 9);
        Assert.Equal(w.Hash(), loaded.Hash());
        for (int i = 0; i < 120; i++)
        {
            w.Tick(new ShipInput(-0.3, 0));
            loaded.Tick(new ShipInput(-0.3, 0));
        }
        Assert.Equal(w.Reveal.RevealedCells, loaded.Reveal.RevealedCells);
        Assert.Equal(w.Hash(), loaded.Hash());

        // A voyage saved today without one resumes without one.
        var bare = Quiet(6);
        Assert.False(World.LoadJson(bare.SaveJson()).HasCartographer);
        // A voyage saved before the rule was charting without one: it resumes with a green cartographer aboard.
        var old = JsonNode.Parse(bare.SaveJson())!.AsObject();
        old.Remove("CartographerRule");
        var resumed = World.LoadJson(old.ToJsonString());
        Assert.True(resumed.HasCartographer);
        Assert.Equal(0, resumed.Player.OfficerTier(OfficerType.Cartographer));
    }

    [Fact]
    public void HiringAndDismissingHimReplays()
    {
        // A voyage resumed at a far harbour nothing has charted yet (a resumed voyage replays from its save), with a
        // cartographer signed on at home: arriving inks the water round her; after he is dismissed there, the glass
        // inks nothing more; a replay from the save agrees.
        var a = World.NewRun(5);
        Assert.Equal(PortResult.Ok, a.Apply(new PortCommand(PortAction.Dock)));
        HireCartographer(a);
        Assert.Equal(PortResult.Ok, a.Apply(new PortCommand(PortAction.CastOff)));
        var far = FarSea(a);
        var port = a.Map.Ports.Where(p => !p.Secret && !p.Discovered && !a.Reveal.IsRevealed(p.Harbor) && a.IsOpen(p)).OrderBy(p => p.Harbor.DistanceTo(far)).First();
        a.Ship.Pos = port.Harbor;
        a.Ship.Vel = Vec2.Zero;
        string save = a.SaveJson();
        var b = World.LoadJson(save);
        int before = b.Reveal.RevealedCells;
        for (int t = 0; t < 30; t++) b.Tick(new ShipInput(0, 0));
        Assert.True(b.Reveal.RevealedCells > before && b.Map.Ports[port.Id].Discovered, "arriving with a cartographer inks the water and marks the harbour");
        Assert.Equal(PortResult.Ok, b.Apply(new PortCommand(PortAction.Dock)));
        Assert.Equal(PortResult.Ok, b.Apply(new PortCommand(PortAction.DismissOfficer, Amount: (int)OfficerType.Cartographer)));
        Assert.Equal(PortResult.Ok, b.Apply(new PortCommand(PortAction.CastOff)));
        int kept = b.Reveal.RevealedCells;
        for (int t = 0; t < 300; t++) b.Tick(new ShipInput(0, 0, Spyglass: true, SpyglassDir: 1.3));
        Assert.Equal(kept, b.Reveal.RevealedCells);
        var r = World.Replay(save, b.Log, b.Commands, b.Ticks);
        Assert.False(r.HasCartographer);
        Assert.Equal(b.Reveal.RevealedCells, r.Reveal.RevealedCells);
        Assert.Equal(b.Hash(), r.Hash());
    }

    [Fact]
    public void TheAutopilotHiresOneAtItsFirstPort()
    {
        var w = Quiet(7);
        var a = new Autopilot();
        int guard = 0;
        while (a.Visits == 0 && guard++ < 30 * 600)
        {
            var input = a.Tick(w);
            if (!w.IsDocked) w.Tick(input);
        }
        Assert.True(a.Visits >= 1, "the autopilot docked");
        Assert.True(w.HasCartographer, "and signed a cartographer on");
    }
}
