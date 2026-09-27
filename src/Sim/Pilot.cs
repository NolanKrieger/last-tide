namespace LastTide.Sim;

/// <summary>
/// Helm helpers shared by tests, the self-test and (later) AI captains. Plain functions on ship state.
/// </summary>
public static class Pilot
{
    /// <summary>Rudder order that turns toward a heading: proportional on the error, damped on the turn rate.</summary>
    public static double Helm(Ship s, double targetHeading)
    {
        double err = Angles.Wrap(targetHeading - s.Heading);
        return Math.Clamp(err * 2.0 - s.AngVel * 0.8, -1, 1);
    }

    /// <summary>Close-hauled heading on the given tack: +1 starboard tack (wind from starboard), −1 port tack.</summary>
    public static double CloseHauledHeading(Ship s, in Wind w, int tack, double marginDeg = 3) =>
        Angles.Wrap(w.From - tack * Angles.Rad(s.Hull.PointDeg + marginDeg));
}
