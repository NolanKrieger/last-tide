using LastTide.Sim;

namespace Sim.Tests;

public class DeterminismTests
{
    static ShipInput Scripted(Rng rng, int tick)
    {
        // A wandering helm and the odd sail change, from an RNG that is not the world's.
        double rudder = tick % 90 < 45 ? Math.Round(rng.Range(-1, 1), 2) : 0;
        int sail = tick % 300 == 0 ? (rng.Next(2) == 0 ? 1 : -1) : 0;
        return new ShipInput(rudder, sail);
    }

    static World Play(int seed, int ticks, out List<ShipInput> inputs)
    {
        var w = World.NewRun(seed);
        var script = new Rng(2024);
        inputs = new List<ShipInput>(ticks);
        for (int t = 0; t < ticks; t++)
        {
            var i = Scripted(script, t);
            inputs.Add(i);
            w.Tick(i);
        }
        return w;
    }

    [Fact]
    public void SameSeedAndInputsGiveTheSameHash()
    {
        var a = Play(7, 3000, out _);
        var b = Play(7, 3000, out _);
        Assert.Equal(a.Hash(), b.Hash());
        Assert.Equal(a.Ship.Pos, b.Ship.Pos);
        var c = Play(8, 3000, out _);
        Assert.NotEqual(a.Hash(), c.Hash());
    }

    [Fact]
    public void ReplayingTheCommandLogRebuildsTheSameState()
    {
        var a = Play(11, 4000, out _);
        Assert.True(a.Log.Count > 10 && a.Log.Count < 4000, $"log has {a.Log.Count} entries");
        var b = World.Replay(a.Seed, a.HullId, a.Log, a.Commands, a.Ticks);
        Assert.Equal(a.Hash(), b.Hash());
        Assert.Equal(a.Ship.Heading, b.Ship.Heading);
        Assert.Equal(a.Wind.BaseDirection, b.Wind.BaseDirection);
    }

    [Fact]
    public void SaveAndLoadContinueIdentically()
    {
        var a = Play(5, 1500, out var inputs);
        string json = a.SaveJson();
        var b = World.LoadJson(json);
        Assert.Equal(a.Hash(), b.Hash());
        var script = new Rng(77);
        for (int t = 0; t < 1500; t++)
        {
            var i = Scripted(script, t);
            a.Tick(i);
            b.Tick(i);
        }
        Assert.Equal(a.Hash(), b.Hash());
        // The suspend save carries the state, not the history: the resumed voyage logs from the save, and its save
        // plus that log replay it exactly.
        Assert.Empty(World.LoadJson(json).Log);
        Assert.DoesNotContain("\"Log\"", json);
        Assert.Equal(b.Hash(), World.Replay(json, b.Log, b.Commands, b.Ticks).Hash());
    }

    [Fact]
    public void RngIsReproducibleFromItsState()
    {
        var r = new Rng(3);
        for (int i = 0; i < 50; i++) r.NextDouble();
        var state = r.State;
        double next = r.NextDouble();
        var s = new Rng(0) { State = state };
        Assert.Equal(next, s.NextDouble());
        double mean = 0;
        for (int i = 0; i < 20000; i++) mean += r.NextGaussian();
        Assert.InRange(mean / 20000, -0.05, 0.05);
    }

    [Fact]
    public void NoiseIsSeededSmoothAndBounded()
    {
        double a = Noise.Value3(1, 3.3, 4.4, 5.5), b = Noise.Value3(1, 3.3, 4.4, 5.5), c = Noise.Value3(2, 3.3, 4.4, 5.5);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        double prev = Noise.Value3(9, 0, 0.3, 0.7);
        for (double x = 0.01; x < 20; x += 0.01)
        {
            double v = Noise.Value3(9, x, 0.3, 0.7);
            Assert.InRange(v, -1, 1);
            Assert.True(Math.Abs(v - prev) < 0.05, $"jump at x={x}: {prev} → {v}");
            prev = v;
        }
    }
}
