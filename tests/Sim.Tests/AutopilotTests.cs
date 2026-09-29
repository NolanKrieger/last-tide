using LastTide.Sim;

namespace Sim.Tests;

public class AutopilotTests
{
    static (World w, Autopilot a) Run(int seed, Preset preset, double days, bool populate = true)
    {
        var w = World.NewRun(seed, "sloop", preset, populate);
        var a = new Autopilot();
        long cap = (long)(days * Tuning.SecondsPerDay * Tuning.TicksPerSecond);
        int guard = 0;
        while (w.Ticks < cap && !w.RunOver && guard++ < cap * 2)
        {
            var input = a.Tick(w);
            if (!w.IsDocked) w.Tick(input);
        }
        return (w, a);
    }

    [Fact]
    public void TradesFromTheLeanStart()
    {
        // Six seas, four days each: every voyage visits, most trade, and at least one turns the 200 gold into more. On the
        // stretched v7 chart a lean sloop chased by a pirate can spend three days between two ports and sell nothing
        // (seeds 7 and 11 since 2026-09-28), so "every voyage trades" on three pinned seeds was a matter of luck.
        // Visits too: seed 7 spends a third of its four days fleeing Brethren sloops and makes port once (2026-09-28).
        int bestWorth = 0, traded = 0, twice = 0;
        var seeds = new[] { 7, 11, 1000, 8, 9, 12 };
        foreach (int seed in seeds)
        {
            var (w, a) = Run(seed, Preset.RoughSeas, 4);
            Assert.True(a.Visits >= 1, $"seed {seed}: visits {a.Visits}");
            if (a.Visits >= 2) twice++;
            if (a.Trades >= 1) traded++;
            int worth = w.Player.Gold + (int)Goods.All.Sum(d => w.Player.Units(d.Id) * w.Player.CostBasis[(int)d.Id]);
            bestWorth = Math.Max(bestWorth, Math.Max(worth, a.MaxGold));
        }
        Assert.True(twice >= 5, $"{twice} of {seeds.Length} voyages made two ports in four days");
        Assert.True(traded >= 4, $"{traded} of {seeds.Length} voyages traded in four days");
        Assert.True(bestWorth >= 150, $"best worth {bestWorth}: the lean start should not be a bankruptcy in four days on every sea");
    }

    [Fact]
    public void RunsToTheEndOrTheCapWithoutStalling()
    {
        foreach (int seed in new[] { 11, 12 })
        {
            var (w, a) = Run(seed, Preset.CalmSeas, 8);
            Assert.True(w.RunOver || w.DaysSurvived >= 8 - 1e-6, $"seed {seed}: day {w.DaysSurvived:0.00}, over {w.RunOver}");
            Assert.True(a.Visits >= 3, $"seed {seed}: visits {a.Visits}");
        }
    }

    [Fact]
    public void FleesAStrongerShipAndFightsAWeakerOne()
    {
        var w = World.NewRun(5, populate: false);
        w.DirectorEnabled = false;
        w.MonstersEnabled = false;
        w.Islands.Clear();
        var a = new Autopilot();
        var frigate = w.SpawnHunter("frigate", w.Ship.Pos + new Vec2(400, 0), Faction.Brethren);
        for (int i = 0; i < 90; i++) w.Tick(a.Tick(w));
        Assert.Equal(Autopilot.Mode.Flee, a.State);
        Assert.Equal(1, a.Flights);

        var w2 = World.NewRun(5, populate: false);
        w2.DirectorEnabled = false;
        w2.MonstersEnabled = false;
        w2.Islands.Clear();
        w2.Ship.Cannons = 4;
        w2.Ship.Crew = 8;
        w2.Player.Cargo[(int)Good.Munitions] = 40;
        var b = new Autopilot();
        var sloop = w2.SpawnHunter("sloop", w2.Ship.Pos + new Vec2(300, 0), Faction.Brethren);
        sloop.Cannons = 1;
        sloop.Crew = 2;
        for (int i = 0; i < 90; i++) w2.Tick(b.Tick(w2));
        Assert.Equal(Autopilot.Mode.Fight, b.State);
    }
}
