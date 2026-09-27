namespace LastTide.Sim;

/// <summary>A landmass as a simple polygon in metres. Collision and containment queries live here.</summary>
public sealed class Island
{
    public readonly Vec2[] Points;
    public readonly Vec2 Centre;
    public readonly double BoundRadius;
    public int Id;
    public RegionType Region;
    /// <summary>Nominal radius the blob was grown from.</summary>
    public double Radius;

    public Island(Vec2[] points)
    {
        Points = points;
        double sx = 0, sy = 0;
        foreach (var p in points) { sx += p.X; sy += p.Y; }
        Centre = new Vec2(sx / points.Length, sy / points.Length);
        double r = 0;
        foreach (var p in points) r = Math.Max(r, p.DistanceTo(Centre));
        BoundRadius = r;
    }

    /// <summary>Even-odd point-in-polygon.</summary>
    public bool Contains(Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = Points.Length - 1; i < Points.Length; j = i++)
        {
            var a = Points[i];
            var b = Points[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Closest point on the coastline, its distance, and whether <paramref name="p"/> is on land.</summary>
    public (Vec2 Point, double Dist, bool Inside) Closest(Vec2 p)
    {
        Vec2 best = Points[0];
        double bestSq = double.MaxValue;
        for (int i = 0, j = Points.Length - 1; i < Points.Length; j = i++)
        {
            var q = ClosestOnSegment(p, Points[j], Points[i]);
            double d = (q - p).LengthSq;
            if (d < bestSq)
            {
                bestSq = d;
                best = q;
            }
        }
        return (best, Math.Sqrt(bestSq), Contains(p));
    }

    static Vec2 ClosestOnSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len = ab.LengthSq;
        if (len < 1e-9) return a;
        double t = Math.Clamp((p - a).Dot(ab) / len, 0, 1);
        return a + ab * t;
    }

    /// <summary>A noise-shaped blob: two octaves of periodic noise around a circle.</summary>
    public static Island Blob(int seed, int index, Vec2 centre, double radius, double roughness = 0.32, int sides = 0)
    {
        if (sides <= 0) sides = Math.Clamp((int)(radius / 6), 16, 48);
        var pts = new Vec2[sides];
        for (int i = 0; i < sides; i++)
        {
            double a = i * Angles.Tau / sides;
            double n1 = Noise.Value3(seed + 100, 10 + 1.9 * Math.Cos(a), 10 + 1.9 * Math.Sin(a), index * 3.7 + 0.5);
            double n2 = Noise.Value3(seed + 101, 20 + 4.3 * Math.Cos(a), 20 + 4.3 * Math.Sin(a), index * 2.1 + 0.25);
            double r = radius * (1 + roughness * n1 + roughness * 0.45 * n2);
            pts[i] = centre + Vec2.FromAngle(a) * Math.Max(r, radius * 0.35);
        }
        return new Island(pts) { Radius = radius };
    }
}
