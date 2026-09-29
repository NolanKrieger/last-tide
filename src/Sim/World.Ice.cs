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
        // Her whole hull, the keel's capsule (as a ball or a ram tests it): three sample circles along the keel let a small
        // floe slip between them, and a frigate or a man-o'-war sailed straight over it (Nolan, 2026-09-28). A few passes
        // settle her between floes that crowd her (clear of one, pushed into the next); a blow counts once a floe a tick.
        ulong struck = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            for (int i = 0; i < nearIce.Count; i++)
            {
                var floe = nearIce[i];
                var fore = ship.Pos + ship.Forward * half;
                var aft = ship.Pos - ship.Forward * half;
                var (keel, _) = Geometry.ClosestPoints(fore, aft, floe.Pos, floe.Pos);
                var d = keel - floe.Pos;
                double dist = d.Length, pen = floe.Radius + r - dist;
                if (pen <= 0.01) continue;
                var n = dist > 1e-6 ? d / dist : ship.Right;
                ship.Pos += n * pen;
                moved = true;
                double vn = ship.Vel.Dot(n);
                if (vn >= 0) continue;
                double speed = -vn;
                ship.Vel -= n * (vn * (1 + Tuning.Restitution));
                ulong bit = 1UL << (i & 63);
                if (speed <= IceHarmlessSpeed || (struck & bit) != 0) continue;
                struck |= bit;
                ship.Hit((speed - IceHarmlessSpeed) * IceDamagePerSpeed, null, Rng, casualties: false, thresholdLeaks: false);
                if (speed >= IceLeakSpeed) ship.Leaks++;
                Events.Add(new CombatEvent(CombatEventType.Ram, keel - n * r, ship.Id, speed));
                if (ship.IsPlayer) IceHitTime = Time;
            }
            if (!moved) break;
        }
    }

    /// <summary>
    /// Where the straight run from <paramref name="a"/> to <paramref name="b"/> first meets a floe, or null. Shot stops at
    /// drift ice as it does at a shore, and captains hold fire through it (Nolan, 2026-09-28: balls flew through floes).
    /// </summary>
    public Vec2? IceAlong(Vec2 a, Vec2 b)
    {
        var mid = (a + b) * 0.5;
        double reach = a.DistanceTo(b) * 0.5;
        iceAlong.Clear();
        Ice.Near(Map, mid, reach, Time, iceAlong);
        Vec2? hit = null;
        double best = double.MaxValue;
        var ab = b - a;
        double len2 = ab.LengthSq;
        foreach (var floe in iceAlong)
        {
            // The first crossing of the floe's rim along a→b (a itself when a lies on the ice).
            var f = a - floe.Pos;
            double c = f.LengthSq - floe.Radius * floe.Radius;
            if (c <= 0) return a;
            if (len2 < 1e-12) continue;
            double bq = f.Dot(ab), disc = bq * bq - len2 * c;
            if (disc < 0) continue;
            double t = (-bq - Math.Sqrt(disc)) / len2;
            if (t < 0 || t > 1 || t >= best) continue;
            best = t;
            hit = a + ab * t;
        }
        return hit;
    }

    readonly List<Floe> iceAlong = new();

    /// <summary>When ice last holed her, for the logbook's cause of sinking.</summary>
    public double IceHitTime { get; private set; } = -1;
}
