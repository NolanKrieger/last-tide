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
    /// <summary>A floe meeting <paramref name="want"/> at the world's time, well out in the Ice Reach, clear of land 150 m round.</summary>
    static Floe FindFloe(World w, Func<Floe, bool> want)
    {
        var region = w.Map.RegionOf(RegionType.IceReach);
        var list = new List<Floe>();
        for (double r = 0; r < 4000; r += 60)
            for (int k = 0; k < 8; k++)
            {
                list.Clear();
                Ice.Near(w.Map, region.Seed + Vec2.FromAngle(k * Math.PI / 4) * r, 150, w.Time, list);
                foreach (var f in list.OrderBy(f => f.Id))
                    if (want(f) && w.LandAlong(f.Pos - new Vec2(150, 0), f.Pos + new Vec2(150, 0)) == null
                        && w.LandAlong(f.Pos - new Vec2(0, 150), f.Pos + new Vec2(0, 150)) == null) return f;
            }
        throw new Xunit.Sdk.XunitException("no such floe");
    }

    [Fact]
    public void ABigHullCannotSailOverASmallFloe()
    {
        // Nolan, 2026-09-28: "why can i drive over ... icebergs". The hull was three circles along the keel, and a small floe
        // slipped between them: a man-o'-war closed right over one. Her whole capsule meets the ice now.
        var w = Sea.Fixed(speed: 0.1, hull: "man_o_war");
        var floe = FindFloe(w, f => f.Radius < 7);
        double r = w.Ship.Hull.Beam * 0.5, half = w.Ship.Hull.Length * 0.5 - r;
        // Keel east-west, the floe abreast of her between midships and the bow and already a metre inside her side: no
        // sample circle reached it there, so she stayed on it. She drifts south, onto it.
        w.Ship.Heading = 0;
        w.Ship.Pos = floe.Pos + new Vec2(-half * 0.5, -(floe.Radius + r - 1));
        w.Ship.Vel = new Vec2(0, 2);
        double worst = double.MinValue;
        var list = new List<Floe>();
        for (int t = 0; t < 90; t++)
        {
            w.Tick(new ShipInput(0, 0));
            list.Clear();
            Ice.Near(w.Map, w.Ship.Pos, 80, w.Time, list);
            var now = list.First(f => f.Id == floe.Id);
            double keel = Geometry.SegmentDistance(w.Ship.Pos + w.Ship.Forward * half, w.Ship.Pos - w.Ship.Forward * half, now.Pos, now.Pos);
            worst = Math.Max(worst, now.Radius + r - keel);
        }
        Assert.True(worst < 0.3, $"her hull stayed {worst:F1} m into a floe of r {floe.Radius:F1}");
    }

    [Fact]
    public void ShotStopsAtDriftIceAndCaptainsHoldFireThroughIt()
    {
        var w = Sea.Fixed(speed: 0.1);
        var floe = FindFloe(w, f => f.Radius >= 8);
        var dir = new Vec2(1, 0);
        var target = w.Spawn("sloop", floe.Pos + dir * (floe.Radius + 20), Angles.FromCompassDeg(0), Faction.Brethren, null, crew: 4, cannons: 0);
        w.Ship.Pos = floe.Pos - dir * (floe.Radius + 60);
        w.Ship.Heading = Angles.FromCompassDeg(0);   // north: her starboard battery bears east, across the floe
        double hp = target.HullHp;
        Assert.NotNull(w.IceAlong(w.Ship.Pos, target.Pos));
        Assert.Null(Seamanship.ClearShot(w, w.Ship, target));   // a captain holds his fire
        w.Balls.Add(new Cannonball { Pos = w.Ship.Pos + dir * 12, Vel = dir * World.BallSpeed, Life = 3, Shooter = w.Ship, Damage = 10 });
        bool burst = false;
        for (int t = 0; t < 90 && w.Balls.Count > 0; t++)
        {
            w.Tick(new ShipInput(0, 0));
            burst |= w.Events.Any(e => e.Type == CombatEventType.Splash && e.Pos.X < floe.Pos.X && e.Pos.DistanceTo(floe.Pos) < floe.Radius + 5);
        }
        Assert.Empty(w.Balls);
        Assert.True(burst, "the ball bursts on the floe's near rim");
        Assert.Equal(hp, target.HullHp);
    }
}
