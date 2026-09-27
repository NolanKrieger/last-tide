using LastTide.Sim;

namespace Sim.Tests;

public class PolarTests
{
    [Fact]
    public void BeamReachIsFullSpeed() => Assert.Equal(1.0, Polar.Fraction(Rig.ForeAndAft, 90, 40));

    [Fact]
    public void CloseHauledIsThePlateau()
    {
        Assert.Equal(0.5, Polar.Fraction(Rig.ForeAndAft, 40, 40), 6);
        Assert.Equal(0.5, Polar.Fraction(Rig.ForeAndAft, 55, 40), 6);
    }

    [Fact]
    public void InIronsGivesNothingAndFadesInBelowThePointingAngle()
    {
        Assert.Equal(0.0, Polar.Fraction(Rig.ForeAndAft, 10, 40));
        Assert.Equal(0.0, Polar.Fraction(Rig.ForeAndAft, 31.9, 40));
        Assert.Equal(0.25, Polar.Fraction(Rig.ForeAndAft, 36, 40), 6);
    }

    [Fact]
    public void RunningSpeedsMatchTheTable()
    {
        Assert.Equal(0.75, Polar.Fraction(Rig.ForeAndAft, 180, 40), 6);
        Assert.Equal(0.90, Polar.Fraction(Rig.Square, 180, 55), 6);
        Assert.True(Polar.Fraction(Rig.Square, 180, 55) > Polar.Fraction(Rig.ForeAndAft, 180, 40));
    }

    [Fact]
    public void HullsThatPointWorseStartHigherOnTheCurve()
    {
        // An indiaman points at 65°, above the plateau, so its first sailable angle already sits on the rising curve.
        Assert.Equal(0.5 + (0.76 - 0.5) * 5 / 15, Polar.Fraction(Rig.Square, 65, 65), 6);
        Assert.Equal(0.0, Polar.Fraction(Rig.Square, 56, 65));
    }

    [Theory]
    [InlineData(Rig.ForeAndAft)]
    [InlineData(Rig.Lateen)]
    [InlineData(Rig.Mixed)]
    [InlineData(Rig.Square)]
    public void EveryCurveStaysInRangeAndPeaksOnTheBeam(Rig rig)
    {
        double best = 0;
        for (double a = 0; a <= 180; a += 0.5)
        {
            double f = Polar.Fraction(rig, a, 40);
            Assert.InRange(f, 0, 1);
            best = Math.Max(best, f);
        }
        Assert.Equal(1.0, best);
        Assert.Equal(1.0, Polar.Fraction(rig, 100, 40));
    }

}
