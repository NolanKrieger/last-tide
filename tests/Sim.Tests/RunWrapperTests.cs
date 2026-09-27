using LastTide.Sim;

namespace Sim.Tests;

public class RunWrapperTests
{
    [Fact]
    public void CosmeticTablesAreConsistent()
    {
        var all = Cosmetics.Slots.SelectMany(s => Cosmetics.Options(s).Skip(1)).ToList();
        Assert.Equal(15, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(World.CosmeticKeys.OrderBy(k => k), all.OrderBy(k => k));
        foreach (var key in all) Assert.NotNull(Cosmetics.SlotOf(key));
        Assert.Null(Cosmetics.SlotOf("nope"));
        // Every achievement reward is a real cosmetic and no two achievements share one; the 15 rewards cover the table.
        var rewards = Achievements.All.Select(Achievements.Reward).Where(r => r != null).ToList();
        Assert.Equal(15, rewards.Count);
        Assert.Equal(rewards.Count, rewards.Distinct().Count());
        Assert.All(rewards, r => Assert.Contains(r!, all));
        Assert.Equal(17, Achievements.All.Length);
    }

    [Fact]
    public void ShipNamesAndLoadoutSurviveASave()
    {
        var rng = new Rng(5);
        var names = Enumerable.Range(0, 40).Select(_ => Names.Ship(rng)).ToList();
        Assert.All(names, n => Assert.True(n.Length is >= 4 and <= 24, n));
        Assert.True(names.Distinct().Count() > 20);
        var w = World.NewRun(3, populate: false);
        w.Player.ShipName = "Grey Gull";
        w.Player.Loadout[0] = "flag_black";
        w.Player.Loadout[4] = "wake_gold";
        Assert.Equal("flag_black", w.Player.Cosmetic("flag"));
        var loaded = World.LoadJson(w.SaveJson());
        Assert.Equal("Grey Gull", loaded.Player.ShipName);
        Assert.Equal(w.Player.Loadout, loaded.Player.Loadout);
        Assert.Equal(w.Hash(), loaded.Hash());
        loaded.Player.Loadout[1] = "sails_ochre";
        Assert.NotEqual(w.Hash(), loaded.Hash());
    }

    [Fact]
    public void DaysSurvivedIsTheScore()
    {
        var w = World.NewRun(3, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        for (int i = 0; i < 30 * 30; i++) w.Tick(new ShipInput(0, 0));
        Assert.Equal(0.25, w.DaysSurvived, 6);
        Assert.Equal(1, w.Day);
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        for (int i = 0; i < 300; i++) w.Tick(new ShipInput(0, 0));
        Assert.Equal(0.25, w.DaysSurvived, 6);   // the clock freezes in port
    }
}
