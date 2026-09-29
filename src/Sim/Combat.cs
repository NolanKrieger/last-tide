namespace LastTide.Sim;

public sealed class Cannonball
{
    public Vec2 Pos, Vel;
    public double Life;
    public Ship? Shooter;   // null for a fort or a beast
    public MonsterType From;   // a beast's ball (the ghost ship), so the logbook knows who sank her
    public double Damage;
    public double Delay;   // seconds until this gun in the ripple fires (the view stages the smoke; the ball waits)
}

/// <summary>
/// Barrels of cargo and chests of gold spilled by a sinking (GDD §8), or a barrel adrift on the open sea
/// (<see cref="World.DriftTick"/>). They drift downwind and sink after a minute (a barrel adrift floats longer).
/// </summary>
public sealed class Flotsam
{
    public Vec2 Pos;
    public Good? Good;
    public int Units;
    public int Gold;
    public double Life = 60;
    public double Bob;   // phase for the view
}

public enum CombatEventType { Fire, Hit, Splash, Ram, Sink, Collect, Rescued, Ring }

/// <param name="By">For a hit: the ship that fired (−1 for a fort, a beast or the sea), so the view can tell her own hits.</param>
/// <param name="Good">For a pickup: the good hauled aboard (−1 for gold); <paramref name="Strength"/> is then the units or the gold.</param>
public readonly record struct CombatEvent(CombatEventType Type, Vec2 Pos, int ShipId, double Strength, int By = -1, int Good = -1);

public static class Geometry
{
    /// <summary>Shortest distance between two segments.</summary>
    public static double SegmentDistance(Vec2 p1, Vec2 q1, Vec2 p2, Vec2 q2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        double a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r);
        double s, t;
        if (a <= 1e-9 && e <= 1e-9) return r.Length;
        if (a <= 1e-9)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            double c = d1.Dot(r);
            if (e <= 1e-9)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                double b = d1.Dot(d2);
                double denom = a * e - b * b;
                s = denom != 0 ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0, 1);
                }
            }
        }
        var c1 = p1 + d1 * s;
        var c2 = p2 + d2 * t;
        return (c1 - c2).Length;
    }

    /// <summary>Where segment p→q crosses segment a–b, as the fraction along p→q; −1 when they do not cross.</summary>
    public static double Crossing(Vec2 p, Vec2 q, Vec2 a, Vec2 b)
    {
        var r = q - p;
        var s = b - a;
        double denom = r.Cross(s);
        if (Math.Abs(denom) < 1e-12) return -1;
        var ap = a - p;
        double t = ap.Cross(s) / denom, u = ap.Cross(r) / denom;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1 ? t : -1;
    }

    /// <summary>Closest points between two segments (parameters s on the first, t on the second).</summary>
    public static (Vec2 A, Vec2 B) ClosestPoints(Vec2 p1, Vec2 q1, Vec2 p2, Vec2 q2)
    {
        // Sample-based: robust and cheap enough for a few ships.
        Vec2 bestA = p1, bestB = p2;
        double best = double.MaxValue;
        const int n = 8;
        for (int i = 0; i <= n; i++)
        {
            var a = Vec2.Lerp(p1, q1, i / (double)n);
            for (int j = 0; j <= n; j++)
            {
                var b = Vec2.Lerp(p2, q2, j / (double)n);
                double d = (a - b).LengthSq;
                if (d < best)
                {
                    best = d;
                    bestA = a;
                    bestB = b;
                }
            }
        }
        return (bestA, bestB);
    }
}
