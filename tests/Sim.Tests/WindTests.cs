using LastTide.Sim;

namespace Sim.Tests;

public class WindTests
{
    [Fact]
    public void SameSeedGivesTheSameWander()
    {
        var a = new WindField(7, 1.0, 8);
        var b = new WindField(7, 1.0, 8);
        var ra = new Rng(99);
        var rb = new Rng(99);
        for (int i = 0; i < 3000; i++)
        {
            a.Tick(Tuning.Dt, ra);
            b.Tick(Tuning.Dt, rb);
        }
        Assert.Equal(a.BaseDirection, b.BaseDirection);
        Assert.Equal(a.BaseSpeed, b.BaseSpeed);
        Assert.Equal(a.Sample(new Vec2(300, -200), 100).Speed, b.Sample(new Vec2(300, -200), 100).Speed);
    }

    [Fact]
    public void SpeedStaysInRangeAndDirectionWanders()
    {
        var w = new WindField(3, 0.5, 8);
        var rng = new Rng(5);
        double minDir = double.MaxValue, maxDir = double.MinValue;
        for (int i = 0; i < 30 * 600; i++)
        {
            w.Tick(Tuning.Dt, rng);
            Assert.InRange(w.BaseSpeed, Tuning.WindMin, Tuning.WindMax);
            minDir = Math.Min(minDir, w.BaseDirection);
            maxDir = Math.Max(maxDir, w.BaseDirection);
            var s = w.Sample(new Vec2(i, -i), i * Tuning.Dt);
            Assert.InRange(s.Speed, 0.5, Tuning.WindMaxLocal);
        }
        Assert.True(maxDir - minDir > 0.3, "the wind should have wandered over ten minutes");
    }

    [Fact]
    public void FixedWindIsUniformAndStill()
    {
        var w = new WindField(1, 0, 8);
        w.SetFixed(1.5, 6);
        var rng = new Rng(1);
        for (int i = 0; i < 100; i++) w.Tick(Tuning.Dt, rng);
        Assert.Equal(new Wind(1.5, 6), w.Sample(new Vec2(1000, 1000), 50));
        Assert.Equal(0, w.Gust(new Vec2(5, 5), 3));
    }

    [Fact]
    public void SpatialFieldVariesAcrossTheMapWithinBounds()
    {
        var w = new WindField(11, 0, 8);
        var a = w.Sample(Vec2.Zero, 0);
        var b = w.Sample(new Vec2(2500, -1800), 0);
        Assert.NotEqual(a.Direction, b.Direction);
        Assert.InRange(Math.Abs(Angles.Wrap(a.Direction - b.Direction)), 0, Angles.Rad(2 * Tuning.WindDirNoiseDeg) + 1e-9);
        for (int i = 0; i < 500; i++)
            Assert.InRange(w.Gust(new Vec2(i * 13.7, i * -7.1), i * 0.3), 0, 1);
    }

    [Fact]
    public void FromIsOppositeToDirectionAndKnotsConvert()
    {
        var w = new Wind(Angles.FromCompassDeg(225), 8);
        Assert.Equal(45, Angles.CompassDeg(w.From), 6);
        Assert.Equal(15.55, w.Knots, 2);
        Assert.Equal(2, Angles.PointIndex(w.From));
    }
}
