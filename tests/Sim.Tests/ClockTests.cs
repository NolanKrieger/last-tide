using LastTide.Sim;

namespace Sim.Tests;

public class ClockTests
{
    [Fact]
    public void ARunStartsAtDawnOnDayOne()
    {
        var w = Sea.Fixed();
        Assert.Equal(1, w.Day);
        Assert.Equal(6, w.HourOfDay, 6);
        Assert.Equal(1, w.WatchIndex);   // morning watch
        Assert.False(w.IsNight);
    }

    [Fact]
    public void TwoMinutesIsADayWithEightySecondsOfDaylight()
    {
        var w = Sea.Fixed();
        Sea.Run(w, 79.9);
        Assert.False(w.IsNight);
        Sea.Run(w, 0.2);
        Assert.True(w.IsNight);
        Assert.Equal(1, w.Day);
        Sea.Run(w, 40);
        Assert.Equal(2, w.Day);
        Assert.InRange(w.HourOfDay, 6, 6.1);
        Assert.Equal(1.0, w.DaysSurvived, 2);
    }

    [Fact]
    public void WatchesFollowTheHours()
    {
        var w = Sea.Fixed();
        var seen = new List<int>();
        for (int i = 0; i < 24; i++)
        {
            seen.Add(w.WatchIndex);
            Sea.Run(w, Tuning.SecondsPerHour);
        }
        Assert.Equal(new[] { 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0, 0, 0, 0, 1, 1 }, seen);
    }

    [Fact]
    public void CompassHelpersRoundTrip()
    {
        Assert.Equal(0, Angles.CompassDeg(Angles.FromCompassDeg(0)), 9);
        Assert.Equal(270, Angles.CompassDeg(Angles.FromCompassDeg(270)), 9);
        Assert.Equal(-Math.PI / 2, Angles.FromCompassDeg(0), 9);
        Assert.Equal(0, Angles.PointIndex(Angles.FromCompassDeg(5)));
        Assert.Equal(15, Angles.PointIndex(Angles.FromCompassDeg(340)));
        Assert.Equal(0, Angles.PointIndex(Angles.FromCompassDeg(350)));
        Assert.Equal(8, Angles.PointIndex(Angles.FromCompassDeg(180)));
    }
}
