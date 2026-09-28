using LastTide.Sim;

namespace Sim.Tests;

/// <summary>The Ice Reach's drift ice: only there, always moving, the same on every replay, and it holes a hull met fast.</summary>
public class IceTests
{
    static Floe FirstFloe(World w, double time)
    {
        var seed = w.Map.RegionOf(RegionType.IceReach).Seed;
        var list = new List<Floe>();
        for (double r = 0; r < 3000 && list.Count == 0; r += 150)
            for (int k = 0; k < 8 && list.Count == 0; k++)
                Ice.Near(w.Map, seed + Vec2.FromAngle(k * Math.PI / 4) * r, 120, time, list);
        return list.OrderBy(f => f.Id).First();
    }

    [Fact]
    public void DriftIceLiesOnlyInTheIceReachAndOffEveryShore()
    {
        var map = MapGen.Generate(8);
        var floes = new List<Floe>();
        for (double y = -Map.HalfH; y < Map.HalfH; y += 900)
            for (double x = -Map.HalfW; x < Map.HalfW; x += 900)
                Ice.Near(map, new Vec2(x, y), 450, 0, floes);
        Assert.True(floes.Count > 50, $"only {floes.Count} floes");
        // A floe circles its home cell in the Reach, so near the border it may drift a little over it, never further.
        Assert.All(floes, f => Assert.Contains(new[] { f.Pos, f.Pos + new Vec2(160, 0), f.Pos - new Vec2(160, 0), f.Pos + new Vec2(0, 160), f.Pos - new Vec2(0, 160) },
            p => map.RegionAt(p).Type == RegionType.IceReach));
        Assert.All(floes, f => Assert.False(map.OnLand(f.Pos), $"floe {f.Id} aground"));
    }

    [Fact]
    public void TheIceDriftsAndIsTheSameEveryTime()
    {
        var w = Sea.Fixed();
        var a = FirstFloe(w, 0);
        var later = new List<Floe>();
        Ice.Near(w.Map, a.Pos, 200, 200, later);
        var moved = later.First(f => f.Id == a.Id);
        Assert.True(moved.Pos.DistanceTo(a.Pos) > 5, "the floe never moved");
        var again = new List<Floe>();
        Ice.Near(w.Map, a.Pos, 200, 200, again);
        Assert.Equal(moved, again.First(f => f.Id == a.Id));
    }

    [Fact]
    public void RammingAFloeFastStovesInPlanks()
    {
        foreach (var (speed, hurt) in new[] { (5.0, true), (0.8, false) })
        {
            var w = Sea.Fixed(speed: 0.1);
            var region = w.Map.RegionOf(RegionType.IceReach);
            // Put the ship right at a big floe, aimed at it, with way on.
            Floe floe = default;
            var list = new List<Floe>();
            for (double r = 0; r < 4000 && floe.Radius < 8; r += 100)
            {
                list.Clear();
                Ice.Near(w.Map, region.Seed + new Vec2(r, r * 0.3), 150, w.Time, list);
                floe = list.Where(f => f.Radius >= 8).OrderBy(f => f.Id).FirstOrDefault();
            }
            Assert.True(floe.Radius >= 8, "no big floe found");
            var dir = new Vec2(1, 0);
            w.Ship.Pos = floe.Pos - dir * (floe.Radius + w.Ship.Hull.Length * 0.5 + 3);
            w.Ship.Heading = 0;
            w.Ship.Vel = dir * speed;
            double hp = w.Ship.HullHp;
            Sea.Run(w, 3);
            Assert.Equal(hurt, w.Ship.HullHp < hp);
            Assert.Equal(hurt, w.IceHitTime >= 0);
        }
    }
}
