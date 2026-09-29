using LastTide.Sim;

namespace Sim.Tests;

/// <summary>Shared fixtures: a world with the wind pinned so physics tests are exact.</summary>
static class Sea
{
    public const int Seed = 42;

    /// <param name="fromCompass">Compass bearing the wind blows FROM.</param>
    public static World Fixed(double fromCompass = 45, double speed = 8, string hull = "sloop", int seed = Seed)
    {
        var w = World.NewRun(seed, hull, populate: false);
        w.Wind.SetFixed(Angles.Wrap(Angles.FromCompassDeg(fromCompass) + Math.PI), speed);
        w.MonstersEnabled = false;
        w.DirectorEnabled = false;
        w.DriftEnabled = false;
        w.Islands.Clear();
        w.Map.Whirlpools.Clear();
        w.Ship.Pos = Vec2.Zero;
        w.Ship.Vel = Vec2.Zero;
        w.Ship.Heading = Angles.FromCompassDeg(0);
        return w;
    }

    public static void Run(World w, double seconds, double rudder = 0, int sailDelta = 0)
    {
        int n = (int)Math.Round(seconds * Tuning.TicksPerSecond);
        for (int i = 0; i < n; i++)
            w.Tick(new ShipInput(rudder, i == 0 ? sailDelta : 0));
    }

    public static void SetSail(World w, int level)
    {
        w.Ship.SailTarget = level;
        w.Ship.SailFraction = Tuning.SailFraction[level];
    }

    public static void Point(World w, double compassHeading) => w.Ship.Heading = Angles.FromCompassDeg(compassHeading);
}
