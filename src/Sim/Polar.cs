namespace LastTide.Sim;

/// <summary>
/// Point-of-sail speed curves (GDD §4): fraction of hull top speed against the angle off the true wind.
/// Below the hull's pointing angle the boat is in irons and gets nothing; the last few degrees fade
/// so the edge is not a cliff. Values at 60° and below are the close-hauled plateau.
/// </summary>
public static class Polar
{
    static readonly (double Deg, double F)[] ForeAndAft = { (60, 0.50), (75, 0.80), (90, 1.00), (110, 1.00), (135, 0.90), (160, 0.82), (180, 0.75) };
    static readonly (double Deg, double F)[] Lateen     = { (60, 0.50), (75, 0.80), (90, 1.00), (110, 1.00), (135, 0.90), (160, 0.84), (180, 0.78) };
    static readonly (double Deg, double F)[] Mixed      = { (60, 0.50), (75, 0.78), (90, 1.00), (110, 1.00), (135, 0.92), (160, 0.87), (180, 0.82) };
    static readonly (double Deg, double F)[] Square     = { (60, 0.50), (75, 0.76), (90, 1.00), (110, 1.00), (135, 0.95), (160, 0.92), (180, 0.90) };

    public static (double Deg, double F)[] Table(Rig rig) => rig switch
    {
        Rig.Lateen => Lateen,
        Rig.Mixed => Mixed,
        Rig.Square => Square,
        _ => ForeAndAft,
    };

    /// <param name="offWindDeg">0 = head to wind, 180 = dead downwind.</param>
    /// <param name="pointDeg">The hull's closest sailable angle to the wind.</param>
    public static double Fraction(Rig rig, double offWindDeg, double pointDeg)
    {
        var table = Table(rig);
        double a = Math.Clamp(offWindDeg, 0, 180);
        double fadeStart = pointDeg - Tuning.IronsFadeDeg;
        if (a < fadeStart) return 0;
        double atPoint = Interp(table, pointDeg);
        if (a < pointDeg) return atPoint * (a - fadeStart) / Tuning.IronsFadeDeg;
        return Interp(table, a);
    }

    static double Interp((double Deg, double F)[] t, double a)
    {
        if (a <= t[0].Deg) return t[0].F;
        for (int i = 1; i < t.Length; i++)
        {
            if (a <= t[i].Deg)
            {
                double u = (a - t[i - 1].Deg) / (t[i].Deg - t[i - 1].Deg);
                return t[i - 1].F + (t[i].F - t[i - 1].F) * u;
            }
        }
        return t[^1].F;
    }
}
