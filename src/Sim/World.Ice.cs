namespace LastTide.Sim;

public sealed partial class World
{
    /// <summary>Speed into a floe (m/s) above which it hurts, hull damage per m/s beyond that, and the speed that holes her.</summary>
    public const double IceHarmlessSpeed = 1.2, IceDamagePerSpeed = 5, IceLeakSpeed = 3.5;

    readonly List<Floe> nearIce = new();

    /// <summary>
    /// Hulls against the Ice Reach's floes: pushed clear like a coast, but a blow faster than a crawl stoves in planks.
    /// Every ship afloat in the Reach meets the same ice.
    /// </summary>
    void IceTick()
    {
        if (Map.InBounds(Ship.Pos)) Bump(Ship);
        foreach (var other in Others)
            if (!other.Sunk && Map.InBounds(other.Pos) && Map.RegionAt(other.Pos).Type == RegionType.IceReach) Bump(other);
    }

    void Bump(Ship ship)
    {
        nearIce.Clear();
        Ice.Near(Map, ship.Pos, ship.Hull.Length, Time, nearIce);
        if (nearIce.Count == 0) return;
        double r = ship.Hull.Beam * 0.5;
        double half = Math.Max(0, ship.Hull.Length * 0.5 - r);
        foreach (var floe in nearIce)
        {
            for (int k = -1; k <= 1; k++)
            {
                var p = ship.Pos + ship.Forward * (half * k);
                var d = p - floe.Pos;
                double dist = d.Length, pen = floe.Radius + r - dist;
                if (pen <= 0 || dist < 1e-6) continue;
                var n = d / dist;
                ship.Pos += n * pen;
                double vn = ship.Vel.Dot(n);
                if (vn >= 0) continue;
                double speed = -vn;
                ship.Vel -= n * (vn * (1 + Tuning.Restitution));
                if (speed <= IceHarmlessSpeed) continue;
                ship.Hit((speed - IceHarmlessSpeed) * IceDamagePerSpeed, null, Rng, casualties: false, thresholdLeaks: false);
                if (speed >= IceLeakSpeed) ship.Leaks++;
                Events.Add(new CombatEvent(CombatEventType.Ram, floe.Pos, ship.Id, speed));
                if (ship.IsPlayer) IceHitTime = Time;
            }
        }
    }

    /// <summary>When ice last holed her, for the logbook's cause of sinking.</summary>
    public double IceHitTime { get; private set; } = -1;
}
