using LastTide.Sim;

namespace Sim.Tests;

public class HullTests
{
    [Fact]
    public void FourteenHullsWithUniqueIds()
    {
        Assert.Equal(14, Hulls.All.Length);
        Assert.Equal(14, Hulls.All.Select(h => h.Id).Distinct().Count());
        Assert.Same(Hulls.Sloop, Hulls.Get("sloop"));
        Assert.Equal("man_o_war", Hulls.All[^1].Id);
    }

    [Fact]
    public void PricesRiseFromTheFreeSloop()
    {
        Assert.Equal(0, Hulls.Sloop.Cost);
        for (int i = 1; i < Hulls.All.Length; i++)
            Assert.True(Hulls.All[i].Cost > Hulls.All[i - 1].Cost, Hulls.All[i].Id);
        Assert.Equal(800, Hulls.Get("cutter").Cost);
        Assert.Equal(60000, Hulls.Get("man_o_war").Cost);
    }

    [Fact]
    public void EveryStatIsPositiveAndOfficerSlotsFollowSize()
    {
        foreach (var h in Hulls.All)
        {
            Assert.True(h.HullHp > 0 && h.Cargo > 0 && h.CrewMax > 0 && h.GunsPerSide > 0, h.Id);
            Assert.True(h.Speed > 0 && h.Turn > 0 && h.Length > 0 && h.Beam > 0 && h.Riggers > 0, h.Id);
            Assert.InRange(h.PointDeg, 38, 65);
            int expectedSlots = h.HullHp >= 400 ? 3 : h.HullHp >= 200 ? 2 : 1;
            Assert.Equal(expectedSlots, h.OfficerSlots);
        }
        Assert.Equal(2, Hulls.Sloop.Riggers);
        Assert.Equal(30, Hulls.Get("man_o_war").Riggers);
    }

    [Fact]
    public void XebecIsTheFastestHull()
    {
        var fastest = Hulls.All.MaxBy(h => h.Speed)!;
        Assert.Equal("xebec", fastest.Id);
        Assert.Equal(Tuning.SloopTopSpeed * 1.15, fastest.TopSpeed, 9);
    }
}
