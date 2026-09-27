namespace LastTide.Sim;

/// <summary>An AI at the helm of one ship. Returns the ship's input for this tick.</summary>
public abstract class Captain
{
    public abstract ShipInput Tick(World world, Ship ship);
}

/// <summary>
/// A practice opponent: holds a beam reach, tacks when it strays too far from the player, and
/// fires whichever broadside bears when the player is in range. The real captains arrive with M5.
/// </summary>
public sealed class SparringCaptain : Captain
{
    double heading = double.NaN;
    double retarget;

    public override ShipInput Tick(World world, Ship ship)
    {
        var wind = ship.LocalWind;
        var player = world.Ship;
        retarget -= Tuning.Dt;
        if (double.IsNaN(heading) || retarget <= 0)
        {
            var toPlayer = player.Pos - ship.Pos;
            double from = wind.From;
            if (toPlayer.Length > ship.Range * 0.8)
            {
                // Close the gap: straight at the player unless that points into the wind, then the nearer close-hauled course.
                double want = toPlayer.Angle;
                double off = Math.Abs(Angles.Wrap(want - from));
                double limit = Angles.Rad(ship.Hull.PointDeg + 5);
                heading = off >= limit ? want : Angles.Wrap(from + Math.Sign(Angles.Wrap(want - from)) * limit);
            }
            else
            {
                // In range: sail across the wind so a broadside bears, on the tack that keeps the player abeam.
                double a = Angles.Wrap(from + Math.PI / 2), b = Angles.Wrap(from - Math.PI / 2);
                double bearing = toPlayer.Angle;
                heading = Math.Abs(Angles.Wrap(a - bearing)) < Math.Abs(Angles.Wrap(b - bearing)) ? a : b;
            }
            retarget = 6;
        }
        double rudder = Pilot.Helm(ship, heading);
        int sail = ship.SailTarget < 2 ? 1 : 0;
        // Fire the side whose ripple would cross her hull (as every captain does since the sim-combat audit, C-09).
        var side = Seamanship.ClearShot(world, ship, player);
        return new ShipInput(rudder, sail, side == Side.Port && ship.CanFire(Side.Port), side == Side.Starboard && ship.CanFire(Side.Starboard));
    }
}
