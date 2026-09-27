using LastTide.Sim;

namespace Sim.Tests;

public class ShipPhysicsTests
{
    [Fact]
    public void BeamReachSettlesAtExactlyTheHullTopSpeed()
    {
        // Wind from the east, heading north: 90° off the wind, wind on the starboard side.
        var w = Sea.Fixed(fromCompass: 90);
        Sea.Point(w, 0);
        Sea.SetSail(w, 3);
        Sea.Run(w, 90);
        Assert.Equal(90, w.Ship.AngleOffWindDeg, 6);
        Assert.Equal(1, w.Ship.WindSide);
        Assert.Equal(PointOfSail.BeamReach, w.Ship.PointOfSail);
        Assert.Equal(Tuning.SloopTopSpeed, w.Ship.ForwardSpeed, 1);
        Assert.InRange(w.Ship.ForwardSpeed / Tuning.SloopTopSpeed, 0.99, 1.01);
    }

    [Theory]
    [InlineData(1, 0.4)]
    [InlineData(2, 0.75)]
    [InlineData(3, 1.0)]
    public void SailLevelScalesSpeed(int level, double mult)
    {
        var w = Sea.Fixed(fromCompass: 90);
        Sea.Point(w, 0);
        Sea.SetSail(w, level);
        Sea.Run(w, 90);
        Assert.InRange(w.Ship.ForwardSpeed / (Tuning.SloopTopSpeed * mult), 0.99, 1.01);
    }

    [Fact]
    public void CloseHauledIsHalfSpeedAndRunningThreeQuarters()
    {
        var w = Sea.Fixed(fromCompass: 0);
        Sea.Point(w, 45);   // 45° off a north wind
        Sea.SetSail(w, 3);
        Sea.Run(w, 90);
        Assert.Equal(PointOfSail.CloseHauled, w.Ship.PointOfSail);
        Assert.InRange(w.Ship.ForwardSpeed / (Tuning.SloopTopSpeed * 0.5), 0.99, 1.01);

        Sea.Point(w, 180);
        Sea.Run(w, 90);
        Assert.Equal(PointOfSail.Running, w.Ship.PointOfSail);
        Assert.InRange(w.Ship.ForwardSpeed / (Tuning.SloopTopSpeed * 0.75), 0.99, 1.01);
    }

    [Fact]
    public void StrongerWindMeansMoreSpeedUpToTheCap()
    {
        var calm = Sea.Fixed(fromCompass: 90, speed: 4);
        var fresh = Sea.Fixed(fromCompass: 90, speed: 8);
        var gale = Sea.Fixed(fromCompass: 90, speed: 16);
        foreach (var w in new[] { calm, fresh, gale })
        {
            Sea.Point(w, 0);
            Sea.SetSail(w, 3);
            Sea.Run(w, 90);
        }
        Assert.InRange(calm.Ship.ForwardSpeed / Tuning.SloopTopSpeed, 0.70, 0.72);
        Assert.InRange(fresh.Ship.ForwardSpeed / Tuning.SloopTopSpeed, 0.99, 1.01);
        Assert.InRange(gale.Ship.ForwardSpeed / Tuning.SloopTopSpeed, Tuning.WindMultMax - 0.01, Tuning.WindMultMax + 0.01);
    }

    [Fact]
    public void HeadToWindStallsAndDriftsAstern()
    {
        var w = Sea.Fixed(fromCompass: 0);
        Sea.Point(w, 0);
        Sea.SetSail(w, 3);
        Sea.Run(w, 30);
        Assert.Equal(PointOfSail.InIrons, w.Ship.PointOfSail);
        Assert.True(w.Ship.InIrons);
        Assert.Equal(0, w.Ship.PolarFraction);
        Assert.True(w.Ship.ForwardSpeed < 0, $"expected sternway, got {w.Ship.ForwardSpeed}");
        Assert.InRange(w.Ship.ForwardSpeed, -3, 0);
    }

    [Fact]
    public void FullSailTurnsSlowerThanOneThird()
    {
        double Turned(int level)
        {
            var w = Sea.Fixed(fromCompass: 90);
            Sea.Point(w, 0);
            Sea.SetSail(w, level);
            w.Ship.Vel = w.Ship.Forward * 6;
            double before = w.Ship.Heading;
            Sea.Run(w, 3, rudder: 1);
            return Angles.Wrap(w.Ship.Heading - before);
        }
        double third = Turned(1), full = Turned(3);
        Assert.True(third > 0 && full > 0, "starboard rudder turns clockwise on screen");
        Assert.InRange(full / third, 0.5, 0.95);
        Assert.Equal(1.0, Ship.SailTurnMultiplier(0.4), 9);
        Assert.Equal(Tuning.TurnMultAtFullSail, Ship.SailTurnMultiplier(1.0), 9);
        Assert.InRange(Ship.SailTurnMultiplier(0.75), 0.75, 0.78);
    }

    [Fact]
    public void AStoppedShipCannotTurnButASailingOneCan()
    {
        var w = Sea.Fixed(fromCompass: 90);
        Sea.Point(w, 0);
        Sea.SetSail(w, 0);
        double before = w.Ship.Heading;
        Sea.Run(w, 3, rudder: 1);
        Assert.InRange(Math.Abs(Angles.Wrap(w.Ship.Heading - before)), 0, 1e-9);

        Sea.SetSail(w, 1);
        Sea.Run(w, 3, rudder: 1);
        Assert.True(Angles.Wrap(w.Ship.Heading - before) > Angles.Rad(10), "under sail the crew can swing the bow");
    }

    [Fact]
    public void TheKeelKillsSidewaysMotionButNotHeadway()
    {
        var w = Sea.Fixed(fromCompass: 90);
        Sea.Point(w, 0);
        Sea.SetSail(w, 0);
        w.Ship.Vel = w.Ship.Right * 5;
        Sea.Run(w, 3);
        double sideLeft = Math.Abs(w.Ship.LateralSpeed) / 5;

        w.Ship.Vel = w.Ship.Forward * 5;
        Sea.Run(w, 3);
        double headwayLeft = w.Ship.ForwardSpeed / 5;
        Assert.InRange(sideLeft, 0, 0.05);
        Assert.InRange(headwayLeft, 0.55, 0.9);
        Assert.True(headwayLeft > sideLeft * 10, "the keel resists sideways motion far more than headway");
    }

    [Fact]
    public void TurningScrubsSpeed()
    {
        var straight = Sea.Fixed(fromCompass: 90);
        var turning = Sea.Fixed(fromCompass: 90);
        foreach (var w in new[] { straight, turning })
        {
            Sea.Point(w, 0);
            Sea.SetSail(w, 3);
            Sea.Run(w, 60);
        }
        Sea.Run(straight, 3);
        Sea.Run(turning, 3, rudder: 1);
        Assert.True(turning.Ship.Speed < straight.Ship.Speed * 0.9, $"{turning.Ship.Speed} vs {straight.Ship.Speed}");
    }

    [Fact]
    public void LeewaySlipsTheShipDownwindOnAReach()
    {
        var w = Sea.Fixed(fromCompass: 90);
        Sea.Point(w, 0);
        Sea.SetSail(w, 3);
        Sea.Run(w, 60);
        // Wind from the east pushes the hull west: lateral speed toward port (negative) but small.
        Assert.InRange(w.Ship.LateralSpeed, -1.0, -0.05);
    }

    [Fact]
    public void RudderFollowsTheOrderAndCentresItself()
    {
        var w = Sea.Fixed();
        Sea.Run(w, 1, rudder: 1);
        Assert.Equal(1, w.Ship.Rudder, 6);
        Sea.Run(w, 0.2, rudder: -1);
        Assert.InRange(w.Ship.Rudder, 0.4, 0.6);
        Sea.Run(w, 1, rudder: 0);
        Assert.Equal(0, w.Ship.Rudder, 6);
    }

    [Fact]
    public void SailChangesTakeTimeAndClampAtTheEnds()
    {
        var w = Sea.Fixed();
        w.Tick(new ShipInput(0, 1));
        Assert.Equal(1, w.Ship.SailTarget);
        Assert.InRange(w.Ship.SailFraction, 0.001, 0.02);
        Sea.Run(w, 3);
        Assert.Equal(0.4, w.Ship.SailFraction, 6);
        w.Tick(new ShipInput(0, 5));
        Assert.Equal(3, w.Ship.SailTarget);
        w.Tick(new ShipInput(0, -9));
        Assert.Equal(0, w.Ship.SailTarget);
    }

    [Fact]
    public void RunningAgroundStopsTheShipOutsideTheIsland()
    {
        var w = Sea.Fixed(fromCompass: 0);
        var island = Island.Blob(1, 0, new Vec2(300, 0), 120);
        w.Islands.Add(island);
        Sea.Point(w, 90);   // east, beam reach on a north wind
        Sea.SetSail(w, 3);
        bool hit = false;
        double impact = 0;
        for (int i = 0; i < 30 * 40; i++)
        {
            w.Tick(new ShipInput(0, 0));
            if (w.Ship.Aground) hit = true;
            impact = Math.Max(impact, w.Ship.LastImpact);
            Assert.False(island.Contains(w.Ship.Pos), $"ship centre inside land at tick {i}");
        }
        Assert.True(hit, "the ship should have struck the island");
        Assert.True(impact > 3, $"impact speed {impact}");
        Assert.True(w.Ship.Speed < 2, $"pinned against the coast, speed {w.Ship.Speed}");
        var (_, dist, inside) = island.Closest(w.Ship.Pos);
        Assert.False(inside);
        Assert.True(dist >= w.Ship.Hull.Beam * 0.5 - 0.5, $"hull clearance {dist}");
    }
}
