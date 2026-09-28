namespace LastTide.Sim;

public enum PointOfSail { InIrons, CloseHauled, CloseReach, BeamReach, BroadReach, Running }
public enum CrewOrder { Battle, MakeSail, Repair, Balanced, Custom }
public enum Side { Port = 0, Starboard = 1 }

/// <summary>What the player (or an AI captain) asks of a ship this tick.</summary>
/// <param name="Rudder">−1 hard to port … +1 hard to starboard.</param>
/// <param name="SailDelta">Steps to raise (+) or lower (−) the sail this tick.</param>
/// <param name="FirePort">Fire the port broadside this tick.</param>
/// <param name="FireStarboard">Fire the starboard broadside this tick.</param>
/// <param name="Order">1–4 sets a crew order (Battle, Make sail, Repair, Balanced); 0 keeps it.</param>
/// <param name="ToggleLantern">Douse or light the lantern (M6).</param>
/// <param name="Action">The context action (dig, salvage) (M9).</param>
/// <param name="Spyglass">The spyglass is raised (it inks the chart and spots ports, so it is logged like the helm).</param>
/// <param name="SpyglassDir">Where the raised glass points, radians.</param>
public readonly record struct ShipInput(double Rudder, int SailDelta, bool FirePort = false, bool FireStarboard = false, int Order = 0, bool ToggleLantern = false, bool Action = false,
    bool Spyglass = false, double SpyglassDir = 0);

/// <summary>
/// A ship as a custom 2D rigid body (GDD §4, §17). Forward thrust is set so that at equilibrium the
/// speed equals the polar table exactly; drag is quadratic plus a small linear tail; the keel resists
/// sideways motion; rudder torque scales with speed; turning scrubs speed. No Godot types.
/// </summary>
public sealed class Ship
{
    public HullDef Hull { get; }
    public Vec2 Pos;
    public Vec2 Vel;
    public double Heading;       // radians, screen convention (0 = east, +clockwise); −π/2 = north
    public double AngVel;        // rad/s, + = turning to starboard
    public int SailTarget;       // 0 furled … 3 full
    public double SailFraction;  // effective sail area 0–1, follows SailTarget over time
    public double Rudder;        // −1 port … +1 starboard, follows the input over time
    public double HullHp;        // structure left
    public int Cannons;          // guns aboard: the odd one goes to starboard

    // ---- Parts (GDD §7) ----
    public int Grade(Part p) => Parts[(int)p];
    /// <summary>Full hull: the hull table, planking grades, and a director spawn's Threat scale (its leaks and patch cap
    /// count from this, not from the unscaled hull: audit R-03).</summary>
    public double MaxHp => Hull.HullHp * (1 + 0.12 * Grade(Part.Planking)) * DamageMult;
    public int CargoCapacity => (int)Math.Round(Hull.Cargo * (1 + 0.10 * Grade(Part.Hold)) * HoldMult);
    public double PointDeg => Math.Max(25, (Hull.PointDeg - 3 * Grade(Part.Rigging)) * PointMult);
    public double TopSpeed => Hull.TopSpeed * (1 + 0.03 * Grade(Part.Sails) + 0.02 * Grade(Part.Copper) - 0.015 * Grade(Part.Planking));
    public double SailChangeMult => 1 + 0.15 * Grade(Part.Sails);
    public double LanternMult => 1 + 0.10 * Grade(Part.Lantern);
    public void RefreshParts()
    {
        CannonGrade = Grade(Part.Cannons);
        LeakSaveChance = 0.12 * Grade(Part.Copper);
        RamDamageMult = 1 + 0.25 * Grade(Part.Ram);
        RamSelfMult = 1 - 0.12 * Grade(Part.Ram);
        HullHp = Math.Min(HullHp, MaxHp);
    }

    // Crew and combat state (GDD §7, §8)
    public int Id;
    public bool IsPlayer;
    public Faction Faction = Faction.FreeTraders;
    public int Crew;
    public CrewOrder Order = CrewOrder.Balanced;
    public int CannonGrade;                         // 0 = 4-pdr … 5 = 24-pdr
    public readonly bool[] Loaded = { true, true };
    public readonly double[] Reload = { 0, 0 };
    public int Leaks;                               // holes the carpenters must plug before they can patch
    public double Water;                            // 0–100
    public double CarpenterWork;
    public double PatchWork;                        // HP patched since the last plank was used
    public int Timber = 4;                          // an AI crew's planks; the player's come from the hold
    public double LeakSaveChance = 0, RamDamageMult = 1, RamSelfMult = 1;   // parts (M8)
    public bool Foundering, Sunk, WaterOnlyStand;
    public double Hourglass;
    /// <summary>Set for a tick while her carpenters hack at a gripping Kraken's tentacle instead of patching (not saved:
    /// the grip sets it again before the damage tick).</summary>
    public bool Hacking;
    public Ship? LastHitBy;
    public bool PlayerHostile;                      // the player already drew blood: reputation was charged
    public double RamCooldown;
    public bool TornSails;                          // storms (M6)
    public AiState? Ai;                             // null for the player
    public double DamageMult = 1;                   // Threat scaling for director spawns: damage and hull (GDD §11)
    public double SpeedMult = 1;                    // the Sargasso weed (GDD §5)
    /// <summary>Grades 0–5 per part (GDD §7). CannonGrade mirrors Parts[Cannons].</summary>
    public readonly int[] Parts = new int[8];
    public readonly int[] CustomStations = new int[4];   // when Order is Custom
    public double PointMult = 1, RangeBonus = 0, HoldMult = 1;   // black-market parts (GDD §10)

    // Readouts from the last step, for the HUD, the view and tests.
    public double AngleOffWindDeg { get; private set; } = 180;
    /// <summary>+1 wind from starboard (boom to port), −1 wind from port.</summary>
    public int WindSide { get; private set; } = 1;
    public double PolarFraction { get; private set; }
    public double TargetSpeed { get; private set; }
    public Wind LocalWind { get; private set; }
    /// <summary>Closing speed of the hardest land impact this tick, m/s (0 = none).</summary>
    public double LastImpact { get; private set; }
    public bool Aground { get; private set; }

    public Ship(HullDef hull, Vec2 pos, double heading)
    {
        Hull = hull;
        Pos = pos;
        Heading = Angles.Wrap(heading);
        HullHp = hull.HullHp;
        Cannons = 2;   // the lean start ships one gun a side
    }

    public Vec2 Forward => Vec2.FromAngle(Heading);
    public Vec2 Right => Vec2.FromAngle(Heading + Math.PI / 2);
    public double ForwardSpeed => Vel.Dot(Forward);
    public double LateralSpeed => Vel.Dot(Right);
    public double Speed => Vel.Length;
    public double Knots => Speed * 1.943844;
    public bool InIrons => AngleOffWindDeg < PointDeg;
    /// <summary>How well the sails are manned: 1 with the riggers the hull wants, less when short (GDD §7).</summary>
    public double RiggerFactor
    {
        get
        {
            if (Hull.Riggers == 0) return 1;
            Split(out _, out int sails, out _, out _);
            return Math.Clamp(sails / (double)Hull.Riggers, 0.35, 1);
        }
    }
    public Vec2 ApparentWind => LocalWind.Vector - Vel;

    public int GunsOn(Side s) => s == Side.Starboard ? (Cannons + 1) / 2 : Cannons / 2;
    public double Range => 180 + 28 * CannonGrade + RangeBonus;
    public double BallDamage => (6 + 2 * CannonGrade) * DamageMult;
    public bool CanFire(Side s) => !Foundering && Loaded[(int)s] && GunsOn(s) > 0;
    /// <summary>Speed and turn penalty from water in the hold: up to half at 100% (GDD §8).</summary>
    public double WaterFactor => 1 - 0.5 * Water / 100;

    /// <summary>The flood line (GDD §8): below this share of her hull the water rises, above it she drains.</summary>
    public const double FloodLine = 0.6;
    /// <summary>Water, % a second, for the whole hull's distance from the flood line: 1%/s for every 10% of hull.</summary>
    public const double FloodRate = 10;
    /// <summary>Seconds of glass in a last stand.</summary>
    public const double StandGlass = 35;
    /// <summary>Water, % a second, at her present hull: + rising below the flood line, − draining above it.</summary>
    public double FloodPerSecond => FloodRate * (FloodLine - HullHp / MaxHp);

    /// <summary>Seconds a side takes to reload with every gun manned (4-pdr 12 s, +2 s a grade).</summary>
    public double BaseReload => 12 + 2 * CannonGrade;

    /// <summary>How well the guns are served now: gunners per gun, 0.1–1 (GDD §7: a half-crewed side reloads at half speed).</summary>
    public double Manning
    {
        get
        {
            if (Cannons <= 0) return 1;
            Split(out int guns, out _, out _, out _);
            return Math.Clamp(guns / (double)Cannons, 0.1, 1);
        }
    }

    /// <summary>Seconds to reload a side at the present manning. The reload itself runs at the manning of each moment
    /// (audit R-01), so crewing the guns after a broadside speeds the one under way.</summary>
    public double ReloadTime => BaseReload / Manning;

    /// <summary>Hands per station under the current order: guns, sails, repair, and the spare hands (GDD §7).</summary>
    public int[] Stations()
    {
        Split(out int guns, out int sails, out int repair, out int spare);
        return new[] { guns, sails, repair, spare };
    }

    /// <summary><see cref="Stations"/> without an array (it runs several times a ship a tick: audit R-04).</summary>
    public void Split(out int guns, out int sails, out int repair, out int spare)
    {
        int left = Crew;
        int needGuns = Cannons, needSails = Hull.Riggers, needRepair = Math.Max(1, (int)(MaxHp / 100));
        guns = sails = repair = 0;
        static void Take(ref int station, ref int left, int need) { int n = Math.Min(left, need); station += n; left -= n; }
        if (Order == CrewOrder.Custom)
        {
            Take(ref guns, ref left, Math.Min(needGuns, CustomStations[0]));
            Take(ref sails, ref left, Math.Min(needSails, CustomStations[1]));
            Take(ref repair, ref left, Math.Min(needRepair, CustomStations[2]));
            spare = left;
            return;
        }
        switch (Order)
        {
            case CrewOrder.Battle: Take(ref guns, ref left, needGuns); Take(ref sails, ref left, needSails); Take(ref repair, ref left, needRepair); break;
            case CrewOrder.MakeSail: Take(ref sails, ref left, needSails); Take(ref guns, ref left, needGuns); Take(ref repair, ref left, needRepair); break;
            case CrewOrder.Repair: Take(ref repair, ref left, needRepair); Take(ref sails, ref left, Math.Max(1, needSails / 2)); break;   // carpenters first
            default: Take(ref sails, ref left, needSails); Take(ref guns, ref left, needGuns); Take(ref repair, ref left, needRepair); break;
        }
        spare = left;
    }

    /// <summary>A ball or a ram struck: hull damage, leaks at every 10% lost, a chance of a casualty.</summary>
    /// <param name="thresholdLeaks">False for a grinding grip (the Kraken): only its own timed smash opens leaks.</param>
    public void Hit(double damage, Ship? by, Rng rng, bool casualties = true, bool thresholdLeaks = true)
    {
        double max = MaxHp;
        double before = HullHp;
        HullHp = Math.Max(0, HullHp - damage);
        int leaksBefore = (int)Math.Floor((max - before) / (max * 0.1) + 1e-9);
        int leaksAfter = (int)Math.Floor((max - HullHp) / (max * 0.1) + 1e-9);
        if (thresholdLeaks)
            for (int i = leaksBefore; i < leaksAfter; i++)
                if (rng.NextDouble() >= LeakSaveChance) Leaks++;
        if (casualties && Crew > 1 && rng.NextDouble() < 0.25) Crew--;
        if (by != null) LastHitBy = by;
    }

    /// <summary>Hull gone or water at the gunwales starts the last stand (GDD §8).</summary>
    void CheckFoundering()
    {
        if (Foundering || Sunk || (HullHp > 0 && Water < 100)) return;
        Foundering = true;
        WaterOnlyStand = HullHp > 0;
        Hourglass = StandGlass;
        Loaded[0] = Loaded[1] = false;
    }

    /// <summary>Reloads, water, carpenters and the last stand, once a tick (GDD §8).</summary>
    public void DamageTick(double dt, Func<bool> takeTimber)
    {
        Split(out int gunners, out _, out int carpenters, out _);
        double manning = Cannons > 0 ? Math.Clamp(gunners / (double)Cannons, 0.1, 1) : 1;
        for (int s = 0; s < 2; s++)
            if (!Loaded[s])
            {
                Reload[s] -= dt * manning;   // the reload runs at the manning of the moment (R-01)
                if (Reload[s] <= 0) Loaded[s] = true;
            }
        if (RamCooldown > 0) RamCooldown -= dt;
        CheckFoundering();
        // The water follows the hull: it rises below the flood line and drains above it, faster the farther she is from it.
        Water = Math.Clamp(Water + FloodPerSecond * dt, 0, 100);
        if (Leaks > 0)
        {
            CarpenterWork += carpenters * dt / 8.0;
            while (CarpenterWork >= 1 && Leaks > 0)
            {
                Leaks--;
                CarpenterWork -= 1;
            }
        }
        else
        {
            CarpenterWork = 0;
            double cap = MaxHp * 0.7;
            // A stand for water alone can still be patched out of: above the flood line she drains (GDD §8).
            if (carpenters > 0 && HullHp < cap && (!Foundering || (WaterOnlyStand && HullHp > 0)) && !Hacking)
            {
                double patch = Math.Min(1.0 * dt, cap - HullHp);
                if (PatchWork <= 0 && !takeTimber())
                    patch = 0;
                else if (PatchWork <= 0)
                    PatchWork = 10;
                HullHp += patch;
                PatchWork -= patch;
            }
        }
        Hacking = false;
        CheckFoundering();
        if (Foundering)
        {
            // Shot to pieces during a stand for water, she is foundering for her hull now: draining no longer saves
            // her and the logbook says what sank her (audit R-11).
            if (WaterOnlyStand && HullHp <= 0) WaterOnlyStand = false;
            Hourglass -= dt;
            if (WaterOnlyStand && Water < 80)
                Foundering = false;
            else if (Hourglass <= 0)
                Sunk = true;
        }
    }

    public PointOfSail PointOfSail =>
        AngleOffWindDeg < PointDeg ? PointOfSail.InIrons
        : AngleOffWindDeg < 60 ? PointOfSail.CloseHauled
        : AngleOffWindDeg < 80 ? PointOfSail.CloseReach
        : AngleOffWindDeg <= 100 ? PointOfSail.BeamReach
        : AngleOffWindDeg < 150 ? PointOfSail.BroadReach
        : PointOfSail.Running;

    /// <summary>Coarse-tick traffic sets its wind readouts without running the physics.</summary>
    internal void SetCoarseWind(in Wind w, double offDeg)
    {
        LocalWind = w;
        AngleOffWindDeg = offDeg;
    }

    /// <summary>Boom angle off the centreline toward the leeward side, for the sail drawing.</summary>
    public double BoomAngleDeg => Math.Clamp((AngleOffWindDeg - 25) * 0.55, 10, 85);

    /// <summary>Turn-rate multiplier for the current sail: 1.0 up to ⅓, falling to 0.6 at full (§4).</summary>
    public static double SailTurnMultiplier(double sailFraction) =>
        sailFraction <= 0.4 ? 1.0 : 1.0 - (sailFraction - 0.4) / 0.6 * (1.0 - Tuning.TurnMultAtFullSail);

    public void Step(double dt, in Wind wind, ShipInput input, IReadOnlyList<Island> islands)
    {
        LocalWind = wind;

        // Sail: the crew works the canvas at a fixed rate, so a change takes a couple of seconds.
        if (input.SailDelta != 0)
            SailTarget = Math.Clamp(SailTarget + input.SailDelta, 0, 3);
        SailFraction = Angles.MoveToward(SailFraction, Tuning.SailFraction[SailTarget], Tuning.SailChangeRate * SailChangeMult * RiggerFactor * dt);

        // Helm: the rudder swings toward the order and centres itself when released.
        double want = Math.Clamp(input.Rudder, -1, 1);
        double rudderRate = want != 0 ? Tuning.RudderRate : Tuning.RudderCenterRate;
        Rudder = Angles.MoveToward(Rudder, want, rudderRate * dt);

        // Wind geometry.
        double signed = Angles.Wrap(wind.From - Heading);
        double off = Math.Abs(signed);
        AngleOffWindDeg = Angles.Deg(off);
        WindSide = signed >= 0 ? 1 : -1;
        PolarFraction = Polar.Fraction(Hull.Rig, AngleOffWindDeg, PointDeg);
        double windMult = Math.Clamp(Math.Sqrt(wind.Speed / Tuning.StandardWind), Tuning.WindMultMin, Tuning.WindMultMax);
        TargetSpeed = TopSpeed * PolarFraction * SailFraction * windMult * WaterFactor * (TornSails ? 0.7 : 1) * SpeedMult * (0.8 + 0.2 * RiggerFactor);
        if (Foundering) TargetSpeed = Math.Min(TargetSpeed, 0.5 * Hull.TopSpeed);

        // Body-frame velocity.
        var fwd = Forward;
        var right = Right;
        double u = Vel.Dot(fwd), v = Vel.Dot(right);
        double massScale = Math.Sqrt(Hull.Inertia);
        double kf = Tuning.QuadDrag / massScale, kl = Tuning.LinDrag / massScale;

        double thrust = kf * TargetSpeed * TargetSpeed + kl * TargetSpeed;
        double drag = kf * u * Math.Abs(u) + kl * u;
        double au = thrust - drag - Tuning.TurnDrag * Math.Abs(AngVel) * u;
        double windRatio = wind.Speed / Tuning.StandardWind;
        if (PolarFraction <= 0)
            au -= Tuning.IronsBackwash * windRatio * SailFraction;

        double av = -WindSide * Tuning.Leeway * SailFraction * windRatio * Math.Sin(off)
                    - Tuning.KeelDrag * v - Tuning.KeelQuadDrag * v * Math.Abs(v);

        u += au * dt;
        v += av * dt;
        Vel = fwd * u + right * v;

        // Rudder authority grows with way on; under sail the crew can always swing the bow a little.
        double authority = Math.Clamp(Math.Abs(u) / (Tuning.RudderFullSpeedFraction * Hull.TopSpeed), 0, 1) * (0.7 + 0.3 * RiggerFactor);
        if (SailFraction > 0.05)
            authority = Math.Max(authority, Tuning.MinAuthorityUnderSail);
        double omegaTarget = Rudder * Hull.TurnRate * SailTurnMultiplier(SailFraction) * authority * WaterFactor * (u < -0.2 ? -1 : 1);
        AngVel += (omegaTarget - AngVel) * Math.Min(1, dt / Tuning.TurnLag);
        Heading = Angles.Wrap(Heading + AngVel * dt);
        Pos += Vel * dt;

        LastImpact = 0;
        Aground = false;
        foreach (var island in islands)
            Collide(island);
    }

    /// <summary>Capsule (bow, midships, stern circles) against the island polygon: push out, bounce a little, scrub speed.</summary>
    void Collide(Island island)
    {
        if (Pos.DistanceTo(island.Centre) > island.BoundRadius + Hull.Length) return;
        double r = Hull.Beam * 0.5;
        double half = Math.Max(0, Hull.Length * 0.5 - r);
        for (int pass = 0; pass < 2; pass++)
        {
            var fwd = Forward;
            for (int k = -1; k <= 1; k++)
            {
                var p = Pos + fwd * (half * k);
                var (q, dist, inside) = island.Closest(p);
                double pen = inside ? dist + r : r - dist;
                if (pen <= 0) continue;
                var n = inside ? (q - p).Normalized : (p - q).Normalized;
                if (n == Vec2.Zero) n = (p - island.Centre).Normalized;
                Pos += n * pen;
                double vn = Vel.Dot(n);
                if (vn < 0)
                {
                    LastImpact = Math.Max(LastImpact, -vn);
                    var tangential = Vel - n * vn;
                    // Coulomb friction: the scrape takes at most μ × the normal impulse off her way along the coast, so a
                    // hard blow stops her and a glancing one lets her slide on. (Taking 40% every tick she touched glued a
                    // grazing hull to the coast at a few cm/s, and halving her swing every tick held her there.)
                    double vt = tangential.Length;
                    double scrub = Tuning.CollisionFriction * (1 + Tuning.Restitution) * -vn;
                    Vel = n * (-vn * Tuning.Restitution) + (vt > scrub ? tangential * ((vt - scrub) / vt) : Vec2.Zero);
                    AngVel *= 1 - 0.5 * Math.Clamp(-vn / 2, 0, 1);   // a hard blow checks her swing; a scrape does not
                }
                Aground = true;
            }
        }
    }
}
