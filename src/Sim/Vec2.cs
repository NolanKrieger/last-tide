namespace LastTide.Sim;

/// <summary>
/// 2D vector in metres. Screen convention throughout the sim: +X east, +Y south, angles in radians
/// clockwise from +X (so north is −π/2). The Godot layer needs no flips.
/// </summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 FromAngle(double a) => new(Math.Cos(a), Math.Sin(a));

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSq => X * X + Y * Y;
    public double Angle => Math.Atan2(Y, X);

    public Vec2 Normalized
    {
        get
        {
            double l = Length;
            return l > 1e-12 ? this / l : Zero;
        }
    }

    public double Dot(Vec2 o) => X * o.X + Y * o.Y;
    public double Cross(Vec2 o) => X * o.Y - Y * o.X;
    /// <summary>Rotated a quarter turn clockwise on screen (+90°).</summary>
    public Vec2 Perp => new(-Y, X);

    public Vec2 Rotated(double a)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public double DistanceTo(Vec2 o) => (this - o).Length;

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public override string ToString() => $"({X:F2}, {Y:F2})";
}
