using LastTide.Sim;

namespace Sim.Tests;

/// <summary>No wall at the chart's edge: the sea goes on, and whirlpools and beasts crowd in the further out she sails.</summary>
public class EdgeTests
{
    [Fact]
    public void SheSailsPastTheChartsEdge()
    {
        var w = Sea.Fixed(fromCompass: 270, speed: 9);   // wind from the west: a run to the east
        Sea.SetSail(w, 3);
        Sea.Point(w, 90);
        w.Ship.Pos = new Vec2(Map.HalfW - 120, 0);
        w.Ship.Vel = new Vec2(5, 0);
        Sea.Run(w, 60);
        Assert.True(w.Ship.Pos.X > Map.HalfW + 100, $"she stopped at x = {w.Ship.Pos.X:F0} (edge {Map.HalfW})");
        Assert.True(Map.BeyondEdge(w.Ship.Pos) > 100);
    }

    [Fact]
    public void WhirlpoolsCrowdInTheFurtherOutSheSails()
    {
        var map = MapGen.Generate(4);
        int Count(double from, double to)
        {
            int n = 0;
            var seen = new HashSet<Vec2>();
            for (double y = -Map.HalfH; y <= Map.HalfH; y += 300)
            {
                var list = new List<Whirlpool>();
                Whirlpools.Near(map, new Vec2(Map.HalfW + (from + to) / 2, y), (to - from) / 2, list);
                foreach (var wp in list)
                {
                    double b = Map.BeyondEdge(wp.Pos);
                    if (b >= from && b < to && seen.Add(wp.Pos)) n++;
                }
            }
            return n;
        }
        Assert.Equal(0, Count(0, Whirlpools.OuterStart - 1));
        int near = Count(300, 1300), far = Count(2300, 3300);
        Assert.True(far > near * 2 && near > 0, $"near {near}, far {far}");
        // Pure function of seed and cell: the same sea every time.
        Assert.Equal(Whirlpools.Outer(4, 60, 3), Whirlpools.Outer(4, 60, 3));
    }

    [Fact]
    public void AWhirlpoolDrawsAHullInTurnsHerAndHolesHer()
    {
        var w = Sea.Fixed(speed: 0.1);
        Sea.SetSail(w, 0);
        var pool = new Whirlpool(new Vec2(0, 0), 150, 5);
        w.Map.Whirlpools.Add(pool);
        w.Ship.Pos = new Vec2(100, 0);
        double heading = w.Ship.Heading, hp = w.Ship.HullHp;
        Sea.Run(w, 10);
        Assert.True(w.Ship.Pos.DistanceTo(pool.Pos) < 90, $"still {w.Ship.Pos.DistanceTo(pool.Pos):F0} m out");
        Assert.NotEqual(heading, w.Ship.Heading);
        Assert.Equal(pool, w.InWhirlpool);
        Sea.Run(w, 60);   // an idle hull is drawn all the way into the eye
        Assert.True(w.Ship.HullHp < hp, "the core should grind the hull");
        Assert.True(w.WhirlpoolHitTime > 0);
    }

    [Fact]
    public void MonstersRiseFasterTheFurtherBeyondTheChart()
    {
        // At the edge of the chart a quiet spell; far out beyond it, a beast within seconds.
        var w = Sea.Fixed(speed: 6);
        w.MonstersEnabled = true;
        w.MonsterClock = 0;
        w.Ship.Pos = new Vec2(Map.HalfW + 1800, 0);   // a whirlpool may take her, but not in twenty seconds
        bool rose = false;
        for (int i = 0; i < 30 * 20 && !rose; i++)
        {
            w.Tick(new ShipInput(0, 0));
            rose = w.Monster != null;
        }
        Assert.True(rose, "no beast rose 1.8 km beyond the chart in 20 s");
        Assert.True(World.EdgePressure(1800) > World.EdgePressure(600) && World.EdgePressure(-50) == 0);
    }

    [Fact]
    public void TheMaelstromStraitsTurnInTheirNarrows()
    {
        var map = MapGen.Generate(6);
        Assert.InRange(map.Whirlpools.Count, 3, 12);
        Assert.All(map.Whirlpools, wp => Assert.Equal(RegionType.Maelstrom, map.RegionAt(wp.Pos).Type));
        Assert.All(map.Ports, p => Assert.All(map.Whirlpools, wp => Assert.True(wp.Pos.DistanceTo(p.Harbor) >= wp.Radius + p.RingRadius, $"{p.Name} harbours in a whirlpool")));
        // Their cores are closed to the lanes.
        Assert.All(map.Whirlpools, wp => Assert.True(map.Nav.IsLand(wp.Pos)));
    }
}
