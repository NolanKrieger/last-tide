using LastTide.Sim;

namespace Sim.Tests;

public class TackingTests
{
    [Fact]
    public void CloseHauledMakesProgressToWindward()
    {
        var w = Sea.Fixed(fromCompass: 0);   // wind from the north
        var wind = w.WindAtShip;
        w.Ship.Heading = Pilot.CloseHauledHeading(w.Ship, wind, tack: +1);
        Sea.SetSail(w, 3);
        Sea.Run(w, 40);
        Assert.Equal(PointOfSail.CloseHauled, w.Ship.PointOfSail);
        Assert.True(w.Ship.Pos.Y < -100, $"should have gained ground north, y = {w.Ship.Pos.Y}");
    }

    [Fact]
    public void TackingThroughTheWindEndsOnTheOtherTackWithWayOn()
    {
        var w = Sea.Fixed(fromCompass: 0);
        var wind = w.WindAtShip;
        w.Ship.Heading = Pilot.CloseHauledHeading(w.Ship, wind, tack: +1);
        Sea.SetSail(w, 3);
        Sea.Run(w, 30);
        double speedBefore = w.Ship.ForwardSpeed;
        double target = Pilot.CloseHauledHeading(w.Ship, wind, tack: -1);

        double slowest = double.MaxValue;
        int ticksToSettle = -1;
        for (int i = 0; i < 30 * 25; i++)
        {
            w.Tick(new ShipInput(Pilot.Helm(w.Ship, target), 0));
            slowest = Math.Min(slowest, w.Ship.ForwardSpeed);
            if (ticksToSettle < 0 && Math.Abs(Angles.Wrap(w.Ship.Heading - target)) < Angles.Rad(3) && Math.Abs(w.Ship.AngVel) < 0.05)
                ticksToSettle = i;
        }
        Assert.True(ticksToSettle > 0 && ticksToSettle < 30 * 10, $"settled after {ticksToSettle} ticks (want under 10 s)");
        Assert.Equal(-1, w.Ship.WindSide);
        Assert.Equal(PointOfSail.CloseHauled, w.Ship.PointOfSail);
        Assert.True(slowest < speedBefore * 0.8, "a tack should cost speed through the eye of the wind");
        Assert.True(slowest > 0.5, $"but never stall completely; slowest {slowest}");
        Assert.InRange(w.Ship.ForwardSpeed / speedBefore, 0.9, 1.1);
    }

    [Fact]
    public void BeatingUpwindOverSeveralTacksGainsGround()
    {
        var w = Sea.Fixed(fromCompass: 0);
        var wind = w.WindAtShip;
        int tack = +1;
        w.Ship.Heading = Pilot.CloseHauledHeading(w.Ship, wind, tack);
        Sea.SetSail(w, 3);
        for (int leg = 0; leg < 4; leg++)
        {
            double target = Pilot.CloseHauledHeading(w.Ship, wind, tack);
            for (int i = 0; i < 30 * 20; i++)
                w.Tick(new ShipInput(Pilot.Helm(w.Ship, target), 0));
            tack = -tack;
        }
        Assert.True(w.Ship.Pos.Y < -220, $"four legs of 20 s should beat well north, y = {w.Ship.Pos.Y}");
        Assert.InRange(w.Ship.Pos.X, -300, 300);
    }

    [Fact]
    public void HelmReachesAHeadingWithoutOvershootingWildly()
    {
        var w = Sea.Fixed(fromCompass: 90);
        Sea.Point(w, 0);
        Sea.SetSail(w, 3);
        Sea.Run(w, 20);
        double target = Angles.FromCompassDeg(300);
        double maxOvershoot = 0;
        bool reached = false;
        for (int i = 0; i < 30 * 20; i++)
        {
            w.Tick(new ShipInput(Pilot.Helm(w.Ship, target), 0));
            double err = Angles.Wrap(w.Ship.Heading - target);
            if (reached) maxOvershoot = Math.Max(maxOvershoot, Math.Abs(err));
            else if (Math.Abs(err) < Angles.Rad(2)) reached = true;
        }
        Assert.True(reached);
        Assert.True(maxOvershoot < Angles.Rad(8), $"overshoot {Angles.Deg(maxOvershoot)}°");
    }
}
