using LastTide.Sim;

namespace Sim.Tests;

public class WeatherTests
{
    static Vec2 FindPoint(World w, RegionType region, Func<Vec2, bool> ok)
    {
        var seed = w.Map.RegionOf(region).Seed;
        for (int r = 0; r < 900; r += 60)
            for (double a = 0; a < Angles.Tau; a += 0.5)
            {
                var p = seed + Vec2.FromAngle(a) * r;
                if (Map.InBounds(p) && w.Map.RegionAt(p).Type == region && ok(p)) return p;
            }
        throw new Exception($"no such point in {region}");
    }

    [Fact]
    public void VisionFollowsDawnNightLanternAndFog()
    {
        var w = World.NewRun(2, populate: false);
        w.Islands.Clear();
        var open = FindPoint(w, RegionType.TradeIsles, p => w.ConditionsAt(p).Fog == 0 && w.Map.Nav.IsSea(p));
        w.Ship.Pos = open;
        w.Tick(new ShipInput(0, 0));
        Assert.InRange(w.VisionRadius, 240, 300);   // the run opens at first light
        for (int i = 0; i < 30 * 10; i++) w.Tick(new ShipInput(0, 0));   // 08:00
        Assert.False(w.IsNight);
        Assert.Equal(500, w.VisionAt(open), 6);
        for (int i = 0; i < 30 * 72; i++) w.Tick(new ShipInput(0, 0));   // 22:24
        Assert.True(w.IsNight);
        Assert.True(w.Lantern);
        Assert.Equal(250, w.VisionAt(open), 6);
        Assert.Equal(650, w.DetectRangeOfPlayer(), 6);
        w.Tick(new ShipInput(0, 0, ToggleLantern: true));
        Assert.False(w.Lantern);
        Assert.Equal(150, w.VisionAt(open), 6);
        Assert.Equal(200, w.DetectRangeOfPlayer(), 6);
        w.Tick(new ShipInput(0, 0, ToggleLantern: true));
        Assert.True(w.Lantern);

        var foggy = FindPoint(w, RegionType.FogBanks, p => w.ConditionsAt(p).Fog >= 0.8);
        Assert.True(w.VisionAt(foggy) <= 250 - 0.8 * 0 && w.VisionAt(foggy) < 250, $"fog at night: {w.VisionAt(foggy)}");
        for (int i = 0; i < 30 * 50; i++) w.Tick(new ShipInput(0, 0));   // back to day
        Assert.False(w.IsNight);
        var foggy2 = FindPoint(w, RegionType.FogBanks, p => w.ConditionsAt(p).Fog >= 0.8);
        Assert.InRange(w.VisionAt(foggy2), 170, 260);
    }

    [Fact]
    public void FogBanksAreFoggyMostOfTheTimeAndTheTradeIslesRarely()
    {
        var w = World.NewRun(9, populate: false);
        int foggy = 0, clear = 0, samples = 0;
        foreach (var region in new[] { RegionType.FogBanks, RegionType.TradeIsles })
        {
            int f = 0, n = 0;
            var seed = w.Map.RegionOf(region).Seed;
            for (double t = 0; t < 2400; t += 40)
                for (int k = 0; k < 24; k++)
                {
                    var p = seed + Vec2.FromAngle(k * 0.26) * (k * 30 % 700);
                    if (w.Map.RegionAt(p).Type != region) continue;
                    n++;
                    if (w.Weather.Fog(p, t, WeatherDef.Of(region), 12) > 0.3) f++;
                }
            if (region == RegionType.FogBanks) { foggy = f; samples = n; } else clear = f;
        }
        Assert.True(foggy > samples * 0.4, $"fog banks foggy {foggy}/{samples}");
        Assert.True(clear < samples * 0.1, $"trade isles foggy {clear}/{samples}");
    }

    [Fact]
    public void RegionsChangeTheWindAndDoldrumsKillIt()
    {
        var w = World.NewRun(4, populate: false);
        double Ratio(Vec2 p) => w.WindAt(p).Speed / w.Wind.Sample(p, w.Time).Speed;
        var deep = FindPoint(w, RegionType.Deep, p => w.ConditionsAt(p).Doldrums == 0);
        Assert.Equal(1.3, Ratio(deep), 6);
        var calm = FindPoint(w, RegionType.Sargasso, p => w.ConditionsAt(p).Doldrums >= 0.9);
        Assert.True(Ratio(calm) < 0.75 * 0.2, $"doldrums ratio {Ratio(calm)}");
        var trade = FindPoint(w, RegionType.TradeIsles, p => w.ConditionsAt(p).Doldrums == 0);
        Assert.Equal(1.0, Ratio(trade), 6);
        w.Wind.SetFixed(0.5, 8);
        Assert.Equal(8, w.WindAt(deep).Speed, 6);   // a pinned wind ignores the regions
    }

    [Fact]
    public void StormsFormUpwindDriftBlowHardAndTearSails()
    {
        var w = World.NewRun(11, preset: Preset.Tempest, populate: false);
        w.Islands.Clear();
        w.DirectorEnabled = false;   // no hunters: this is about the weather
        w.MonstersEnabled = false;
        var reach = FindPoint(w, RegionType.StormReach, p => w.Map.Nav.IsSea(p));
        Assert.Equal(RegionType.StormReach, w.Map.RegionAt(reach).Type);
        w.Ship.Pos = reach;
        int ticks = 0;
        while (w.Weather.Storms.Count == 0 && ticks++ < 30 * 1500)
        {
            w.Ship.Pos = reach;
            w.Tick(new ShipInput(0, 0));
        }
        Assert.NotEmpty(w.Weather.Storms);
        var cell = w.Weather.Storms[0];
        Assert.True(cell.Age < 1 && w.WindAt(cell.Pos).Speed < 1.2 * w.Wind.Sample(cell.Pos, w.Time).Speed * WeatherDef.Of(RegionType.StormReach).WindFactor + 1,
            "a new cell has not gathered yet");
        for (int i = 0; i < StormCell.GatherSeconds * 30; i++)   // let it gather (the swirl on the chart fades in as long)
        {
            w.Ship.Pos = reach;
            w.Tick(new ShipInput(0, 0));
        }
        Assert.InRange(cell.Radius, 250, 450);
        Assert.InRange(cell.Strength, 0.3, 1.0);
        var eye = w.WindAt(cell.Pos);
        w.Weather.Storms.Clear();
        double baseSpeed = w.WindAt(cell.Pos).Speed;   // the same spot without the cell
        w.Weather.Storms.Add(cell);
        Assert.True(eye.Speed > baseSpeed * 1.8, $"storm wind {eye.Speed:F1} vs {baseSpeed:F1}");
        var before = cell.Pos;
        for (int i = 0; i < 30 * 5; i++) w.Tick(new ShipInput(0, 0));
        Assert.True(cell.Pos.DistanceTo(before) > 3 || cell.Life <= 0, "cells drift");
        Assert.True(w.ConditionsAt(cell.Pos).Rain > 0.5);
        Assert.True(w.VisionAt(cell.Pos) < 500 * 0.8);

        // Full sail in the eye tears the canvas soon enough.
        w.Ship.Pos = cell.Pos;
        w.Ship.SailTarget = 3;
        w.Ship.SailFraction = 1;
        int t = 0;
        while (!w.Ship.TornSails && t++ < 30 * 240)
        {
            w.Ship.Pos = cell.Pos;   // stay in the eye
            w.Tick(new ShipInput(0, 0));
        }
        Assert.True(w.Ship.TornSails, "full sail in a storm should tear the sails within four minutes");
        Assert.Contains("NOTICE_TORN_SAILS", w.Notices);
        w.Weather.Storms.Clear();
        w.Wind.SetFixed(Angles.Wrap(Angles.FromCompassDeg(90) + Math.PI), 8);
        w.Ship.Heading = Angles.FromCompassDeg(0);
        w.Ship.Vel = Vec2.Zero;
        for (int i = 0; i < 30 * 60; i++) w.Tick(new ShipInput(0, 0));
        Assert.InRange(w.Ship.ForwardSpeed / (Tuning.SloopTopSpeed * 0.7), 0.97, 1.03);

        // The shipwright mends them for 25 gold.
        w.Ship.Pos = w.Map.StartPort.Harbor;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Dock)));
        int gold = w.Player.Gold;
        Assert.Equal(PortResult.Ok, w.Apply(new PortCommand(PortAction.Repair)));
        Assert.False(w.Ship.TornSails);
        Assert.Equal(gold - World.TornSailRepair, w.Player.Gold);
    }

    [Fact]
    public void HuntersPressHarderAtNightAndWeatherSaves()
    {
        var w = World.NewRun(5, populate: false);
        w.Islands.Clear();
        double c0 = w.Director.Credits;
        for (int i = 0; i < 30 * 40; i++) w.Tick(new ShipInput(0, 0));   // day
        double day = w.Director.Credits - c0;
        for (int i = 0; i < 30 * 45; i++) w.Tick(new ShipInput(0, 0));   // → night at 80 s
        Assert.True(w.IsNight);
        double c1 = w.Director.Credits;
        for (int i = 0; i < 30 * 40; i++) w.Tick(new ShipInput(0, 0));
        double night = w.Director.Credits - c1;
        Assert.True(night > day * 1.35, $"night {night:F2} vs day {day:F2}");

        w.Weather.Storms.Add(new StormCell { Pos = w.Ship.Pos + new Vec2(400, 0), Radius = 300, Strength = 0.7, Life = 100, Age = 5 });
        w.Tick(new ShipInput(0, 0, ToggleLantern: true));
        w.Ship.TornSails = true;
        var loaded = World.LoadJson(w.SaveJson());
        Assert.False(loaded.Lantern);
        Assert.True(loaded.Ship.TornSails);
        Assert.Single(loaded.Weather.Storms);
        Assert.Equal(0.7, loaded.Weather.Storms[0].Strength);
        Assert.Equal(w.Hash(), loaded.Hash());
        for (int i = 0; i < 60; i++)
        {
            w.Tick(new ShipInput(0.3, 0));
            loaded.Tick(new ShipInput(0.3, 0));
        }
        Assert.Equal(w.Hash(), loaded.Hash());
    }
}
