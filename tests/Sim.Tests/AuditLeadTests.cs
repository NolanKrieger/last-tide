using System.Linq;
using LastTide.Sim;
using Xunit;

namespace Sim.Tests;

/// <summary>Regressions for the fixes the audit lead made while merging (see docs/audit/).</summary>
public class AuditLeadTests
{
    [Fact]
    public void AMangroveYardSellsOnlyHullsTheMazeTakes()
    {
        // Audit X6: a frigate bought at a Mangrove Maze port roamed the maze the rule closes to big hulls.
        var w = World.NewRun(3, populate: false);
        w.Player.Gold = 100_000;
        var port = w.Map.Ports.First(p => p.Region == RegionType.Mangrove);
        port.Discovered = true;
        w.Ship.Pos = port.Harbor;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.False(w.HullSoldHere(Hulls.Get("frigate")));
        Assert.Equal(PortResult.NotSold, w.Apply(new PortCommand(PortAction.BuyHull, Text: "frigate")));
        Assert.Equal("sloop", w.Ship.Hull.Id);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.BuyHull, Text: "cutter")));
        Assert.Equal("cutter", w.Ship.Hull.Id);

        // Any other yard sells every hull.
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.CastOff)));
        var other = w.Map.Ports.First(p => p.Region != RegionType.Mangrove && !p.Secret && p.Faction != Faction.Brethren);
        w.Ship.Pos = other.Harbor;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        Assert.True(w.HullSoldHere(Hulls.Get("frigate")));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(12)]
    [InlineData(21)]
    public void NoLaneLegGrazesLand(int seed)
    {
        // A lane stepped diagonally past a single land cell and grazed its corner (seed 12 under generator v5).
        var w = World.NewRun(seed, populate: false);
        var ports = w.Map.Ports.Where(p => !p.Secret).ToList();
        foreach (var from in ports)
            foreach (var to in ports.Where(p => p != from && p.Harbor.DistanceTo(from.Harbor) < 1800))
            {
                var lane = w.Lane(from, to);
                for (int k = 1; k < lane.Count; k++)
                    Assert.True(Pathing.Clear(w.Map.Nav, lane[k - 1], lane[k]), $"seed {seed}: leg {k} of {from.Name}→{to.Name} touches land");
            }
    }

    [Fact]
    public void TwoHullsOnOneSpotArePartedApart()
    {
        // Audit X5: two ships on the same point had a zero normal and stayed fused for ever.
        var w = World.NewRun(3, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        var pos = w.SeaPointNear(w.Map.StartPort.Harbor, 300, 600);
        var a = w.Spawn("cutter", pos, 0.3, Faction.FreeTraders, null);
        var b = w.Spawn("cutter", pos, 0.3, Faction.FreeTraders, null);
        for (int i = 0; i < 5; i++) w.Tick(new ShipInput(0, 0));
        Assert.True(a.Pos.DistanceTo(b.Pos) > 1, $"still fused: {a.Pos} {b.Pos}");
    }
}
