using LastTide.Sim;

namespace Sim.Tests;

/// <summary>Regression tests from the game-core audit (AUDIT-game-core.md).</summary>
public class AuditGameCoreTests
{
    [Fact]
    public void AVoyageWithChartPinsSavesAndLoads()
    {
        // GC-19: a chart pin made every save throw (JsonException: object cycle through Vec2.Normalized), so the dock
        // autosave and Save and quit failed for the rest of the voyage.
        var w = World.NewRun(7, "sloop", Preset.RoughSeas, populate: false);
        for (int i = 0; i < 30; i++) w.Tick(new ShipInput(0, 1));
        w.AddPin(w.Ship.Pos + new Vec2(300, 200), "Reef here");
        w.AddPin(new Vec2(-1234.5, 678.25), "");
        var json = w.SaveJson();
        var back = World.LoadJson(json);
        Assert.Equal(2, back.Pins.Count);
        Assert.Equal(w.Pins[0].Pos, back.Pins[0].Pos);
        Assert.Equal("Reef here", back.Pins[0].Note);
        Assert.Equal(new Vec2(-1234.5, 678.25), back.Pins[1].Pos);
        Assert.Equal(w.Hash(), back.Hash());
        // A save written before the fix (always "Pins":[]) still loads.
        var old = World.LoadJson(World.NewRun(7, "sloop", Preset.RoughSeas, populate: false).SaveJson());
        Assert.Empty(old.Pins);
    }
}
