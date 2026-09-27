namespace LastTide.Sim;

public static class Angles
{
    public const double Tau = 2 * Math.PI;

    /// <summary>Wraps to (−π, π].</summary>
    public static double Wrap(double a)
    {
        a %= Tau;
        if (a <= -Math.PI) a += Tau;
        else if (a > Math.PI) a -= Tau;
        return a;
    }

    public static double Deg(double rad) => rad * 180.0 / Math.PI;
    public static double Rad(double deg) => deg * Math.PI / 180.0;

    /// <summary>Compass bearing 0–360 (0 = north, 90 = east) of a math angle.</summary>
    public static double CompassDeg(double mathAngle)
    {
        double d = (Deg(mathAngle) + 90.0) % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    public static double FromCompassDeg(double compass) => Wrap(Rad(compass - 90.0));

    /// <summary>Index 0–15 of the nearest of the 16 compass points (0 = N, 4 = E, 8 = S, 12 = W).</summary>
    public static int PointIndex(double mathAngle) => (int)Math.Round(CompassDeg(mathAngle) / 22.5) % 16;

    public static double LerpAngle(double a, double b, double t) => Wrap(a + Wrap(b - a) * t);

    public static double MoveToward(double from, double to, double maxDelta)
    {
        double d = to - from;
        return Math.Abs(d) <= maxDelta ? to : from + Math.Sign(d) * maxDelta;
    }
}
