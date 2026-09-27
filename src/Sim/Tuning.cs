namespace LastTide.Sim;

/// <summary>
/// Every v1 tuning value behind the GDD's (proposal) tags, in one place so a playtest change is one edit.
/// Units: metres, seconds, radians unless the name says otherwise. Decided rules are not here.
/// </summary>
public static class Tuning
{
    public const int TicksPerSecond = 30;
    public const double Dt = 1.0 / TicksPerSecond;
    public const double SecondsPerDay = 120;                  // GDD §3: one in-game day is two real minutes
    public const double SecondsPerHour = SecondsPerDay / 24;
    public const double DawnHour = 6;                         // runs start at dawn: 80 s of day, 40 s of night (§9)
    public const double DuskHour = 22;

    // Sails (§4)
    public static readonly double[] SailFraction = { 0.0, 0.4, 0.75, 1.0 };
    public static double SailChangeRate = 0.16;                // sail fraction per second: one step ≈ 2.5 s
    public static double TurnMultAtFullSail = 0.6;             // full sail turns slower; ×1.0 at ⅓ and below

    // Speed scale. A sloop at polar 1.0, full sail and standard wind makes 12 m/s ≈ 23 kn,
    // so the 6 km map crosses in ~4 in-game days at a good point of sail (§5).
    public static double SloopTopSpeed = 12.0;
    public static double SloopTurnRate = 40 * Math.PI / 180;   // rad/s at ⅓ sail, full rudder, full authority
    public static double StandardWind = 8.0;                   // m/s; target speed scales with sqrt(wind / standard)
    public static double WindMultMin = 0.45, WindMultMax = 1.3;

    // Hull dynamics: thrust equals drag at the polar target speed, so equilibrium speed is exactly the table.
    public static double QuadDrag = 0.018;                     // 1/m; 0→76% of top speed in ~4 s, 12→6 m/s coast in ~4 s
    public static double LinDrag = 0.03;                       // 1/s; kills the slow tail of a coast-down
    public static double TurnDrag = 0.25;                      // fraction of speed scrubbed per second per rad/s of turn
    public static double KeelDrag = 2.5, KeelQuadDrag = 0.3;   // sideways drag: the keel bites, the ship carves
    public static double Leeway = 0.8;                         // m/s² to leeward at standard wind, full sail, beam wind
    public static double IronsBackwash = 0.08;                 // m/s² astern when head to wind with sail set
    public static double IronsFadeDeg = 8;                     // polar fades to zero over this band below the pointing angle

    // Helm
    public static double RudderRate = 2.5, RudderCenterRate = 3.0;   // rudder units per second (0.4 s to hard over)
    public static double TurnLag = 0.4;                        // seconds for a turn to build or die
    public static double RudderFullSpeedFraction = 0.35;       // full authority from this fraction of hull top speed
    public static double MinAuthorityUnderSail = 0.5;          // the crew can back the sails to swing a slow ship (tack ≈ 7 s)

    // Collisions with land: bounce, and Coulomb friction μ against the normal impulse (a scrape slides, a blow stops)
    public static double Restitution = 0.15, CollisionFriction = 0.6;

    // Wind (§4, §9): a free wander with no prevailing direction, plus a slow spatial field and gusts.
    public static double WindMeanSpeed = 8, WindSpeedStd = 2.5, WindSpeedTau = 30, WindMin = 3, WindMax = 14;
    public static double WindDirRateStd = 0.035, WindDirTau = 10;
    public static double WindDirNoiseDeg = 25, WindDirNoiseScale = 1500, WindDirNoiseTime = 120;
    public static double WindSpeedNoise = 0.25, WindSpeedNoiseScale = 1200, WindSpeedNoiseTime = 90;
    public static double GustStrength = 0.45, GustScale = 250, GustTime = 12, WindMaxLocal = 16;

    // Camera (the view reads these; they are feel, not rules)
    public static double CameraLeadPerSpeed = 4.0, CameraLeadMax = 120;
}
