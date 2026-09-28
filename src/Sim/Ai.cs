namespace LastTide.Sim;

public enum Role { Player, Merchant, Patrol, Raider, Hunter, Privateer, Sparring }

/// <summary>Everything an AI captain remembers between ticks. Lives on the ship so it saves with it.</summary>
public sealed class AiState
{
    public Role Role;
    public int HomePort = -1;
    public int DestPort = -1;
    public Good CargoGood;
    public int CargoUnits;
    public List<Vec2> Path = new();
    public int PathIndex;
    public double Wait;          // seconds left in port / idling
    public double Repath;        // seconds until the route is recomputed
    public int Tack = 1;
    public double TackHold;      // seconds to keep the current tack before reconsidering
    public int TargetShip = -1;
    public double Lost;          // seconds the target has been out of sight / out of range
    public Vec2 Waypoint;
    public bool HasWaypoint;
    public bool Fleeing;
    public double FleeTime;
    public double Scale = 1;     // Threat scaling applied at spawn (for the recap/debug)
}

/// <summary>Shared seamanship: getting to a point through the wind and around the land, and fighting.</summary>
public static class Seamanship
{
    public const double ArriveRadius = 45;

    /// <summary>
    /// Rudder and sail to reach a point: tacks when the direct course is in the no-go zone, dodges land ahead.
    /// <paramref name="easeIn"/> shortens sail to ⅓ in the last 90 m (a lane's corners are sailed through at speed).
    /// </summary>
    public static ShipInput SteerTo(World world, Ship ship, Vec2 target, bool full = true, bool easeIn = true)
    {
        var ai = ship.Ai!;
        double dt = Tuning.Dt;
        var to = target - ship.Pos;
        double want = to.Angle;
        double from = ship.LocalWind.From;
        double noGo = Angles.Rad(ship.Hull.PointDeg + 4);
        double offWind = Angles.Wrap(want - from);
        ai.TackHold -= dt;
        if (Math.Abs(offWind) < noGo)
        {
            // Beat toward it: hold the tack for a while, then take the tack that points closer to the target —
            // but a slow ship first gathers way enough to carry her through the eye of the wind (audit C-02).
            if (ai.TackHold <= 0)
            {
                int tack = offWind >= 0 ? 1 : -1;
                if (tack != ai.Tack && ship.ForwardSpeed < TackSpeed * ship.Hull.TopSpeed)
                    ai.TackHold = 3;
                else
                {
                    ai.Tack = tack;
                    ai.TackHold = 14;
                }
            }
            want = Angles.Wrap(from + ai.Tack * noGo);
        }
        want = AvoidLand(world, ship, want);
        double rudder = Helm(ship, want);
        int sailWant = !full ? 2 : 3;
        if (easeIn && to.Length < 90) sailWant = 1;
        int sail = Math.Sign(sailWant - ship.SailTarget);
        return new ShipInput(rudder, sail);
    }

    /// <summary>
    /// Looks ahead on bearings 22° apart around the wish: a probe just off the bow, then 60/120/180 m out, and steers
    /// toward the clearest. It keeps within ±66° while that fan is clear and turns further, all the way round if it
    /// must, when land is close ahead: a ship pressed against a coast, or a distant ship whose next step is a land
    /// cell, turns away instead of holding a course it cannot sail (audit C-01).
    /// </summary>
    public static double AvoidLand(World world, Ship ship, double want)
    {
        var nav = world.Map.Nav;
        double best = want;
        double bestScore = double.MinValue;
        double bow = ship.Hull.Length * 0.5 + 15;
        // Inside the grid's coastal margin every probe in her own cell reads land: look past it for the way out.
        double first = nav.IsLand(ship.Pos) ? 20 : 2;
        double from = ship.LocalWind.From, noGo = Angles.Rad(ship.PointDeg);
        // Aground (or barely moving) against a coast the 25 m grid cannot see — a spit between cell centres: the way off
        // is away from the rocks she is touching, whatever the grid says (a fluyt sat 2 min on one, `fleet 7 roam`).
        Vec2 offTheRocks = Vec2.Zero;
        if (ship.Aground || ship.Speed < 1.5)
        {
            double nearest = ship.Hull.Length * 0.5 + 6;
            foreach (var island in world.Map.IslandsNear(ship.Pos, ship.Hull.Length))
            {
                var (q, dist, inside) = island.Closest(ship.Pos);
                if (!inside && dist >= nearest) continue;
                nearest = inside ? 0 : dist;
                offTheRocks = (inside ? q - ship.Pos : ship.Pos - q).Normalized;
            }
        }
        // Nearest bearings first; stop once no wider bearing could beat the best so far (open water: one look).
        for (int i = 0; i <= 16; i++)
        {
            int a = (i + 1) / 2, k = i % 2 == 1 ? -a : a;
            double score = a <= 3 ? -a * 0.4 : -1.2 - (a - 3) * 2.5;
            if (score <= bestScore) break;
            double h = Angles.Wrap(want + k * Angles.Rad(22));
            var d = Vec2.FromAngle(h);
            bool blocked = offTheRocks != Vec2.Zero && d.Dot(offTheRocks) < 0.2;
            for (double r = first; r <= Math.Max(bow, first) && !blocked; r += 6)
            {
                var close = ship.Pos + d * r;
                blocked = !Map.InBounds(close) || nav.IsLand(close);
            }
            if (blocked)
                score -= 20;
            else
                for (int step = 1; step <= 3; step++)
                {
                    var p = ship.Pos + d * (60 * step + ship.Hull.Length);
                    if (!Map.InBounds(p) || nav.IsLand(p))
                    {
                        score -= 10.0 / step;
                        break;
                    }
                }
            // A course inside the no-go zone stops her dead (and a distant ship cannot move at all): never dodge into irons.
            if (Math.Abs(Angles.Wrap(h - from)) < noGo) score -= 6;
            if (score > bestScore)
            {
                bestScore = score;
                best = h;
            }
        }
        return best;
    }

    /// <summary>Way needed to tack through the eye of the wind, as a fraction of the hull's top speed.</summary>
    public const double TackSpeed = 0.15;

    /// <summary>
    /// The helm toward a heading. Too slow to carry a tack, she wears round (turns away through downwind) rather than
    /// stall head to wind; with sternway the rudder bites the other way, so the order is reversed (audit C-02).
    /// </summary>
    public static double Helm(Ship ship, double heading)
    {
        double err = Angles.Wrap(heading - ship.Heading);
        double eye = Angles.Wrap(ship.LocalWind.From - ship.Heading);
        bool throughTheEye = err > 0 ? eye > 0 && eye < err : eye < 0 && eye > err;
        double rudder = throughTheEye && Math.Abs(err) > Angles.Rad(20) && ship.ForwardSpeed < TackSpeed * ship.Hull.TopSpeed
            ? -Math.Sign(err)
            : Pilot.Helm(ship, heading);
        return ship.ForwardSpeed < -0.2 ? -rudder : rudder;
    }

    /// <summary>Follows the AI's path; returns true when the last waypoint is reached.</summary>
    public static bool FollowPath(World world, Ship ship, out ShipInput input)
    {
        var ai = ship.Ai!;
        if (ai.Path.Count == 0)
        {
            input = new ShipInput(0, ship.SailTarget > 0 ? -1 : 0);
            return true;
        }
        while (ai.PathIndex < ai.Path.Count - 1 && ship.Pos.DistanceTo(ai.Path[ai.PathIndex]) < ArriveRadius)
            ai.PathIndex++;
        var target = ai.Path[ai.PathIndex];
        bool last = ai.PathIndex >= ai.Path.Count - 1;
        if (last && ship.Pos.DistanceTo(target) < ArriveRadius)
        {
            input = new ShipInput(0, ship.SailTarget > 0 ? -1 : 0);
            return true;
        }
        // Off the lane (a long tack, a flight, a chase), land may now lie between her and the next waypoint:
        // every few seconds check the line and find the way back round the coast (audit C-01).
        ai.Repath -= Tuning.Dt;
        if (ai.Repath <= 0)
        {
            ai.Repath = RejoinCheck;
            var nav = world.Map.Nav;
            if (!Pathing.Clear(nav, ship.Pos, target))
            {
                var detour = Pathing.Find(nav, ship.Pos, target, ship.LocalWind.From, ship.Hull.PointDeg);
                if (detour.Count == 0) ai.Repath = 30;   // no way through from here: do not search the whole grid every 3 s
                if (detour.Count > 1)
                {
                    detour.AddRange(ai.Path.Skip(ai.PathIndex + 1));
                    ai.Path = detour;
                    ai.PathIndex = 0;
                    while (ai.PathIndex < ai.Path.Count - 1 && ship.Pos.DistanceTo(ai.Path[ai.PathIndex]) < ArriveRadius)
                        ai.PathIndex++;
                    target = ai.Path[ai.PathIndex];
                }
            }
        }
        input = SteerTo(world, ship, target, full: true, easeIn: last);
        return false;
    }

    /// <summary>Seconds between checks that the next waypoint is still in clear water ahead.</summary>
    public const double RejoinCheck = 3;

    /// <summary>
    /// A waypoint for prowling and patrolling that she can reach in a straight line (a few tries), so a captain does not
    /// spend the day pressed against the far side of an island; the last try when none is clear.
    /// </summary>
    public static Vec2 ClearWaypoint(World world, Ship ship, Func<Vec2> pick)
    {
        var p = pick();
        for (int i = 0; i < 5 && !Pathing.Clear(world.Map.Nav, ship.Pos, p); i++)
            p = pick();
        return p;
    }

    /// <summary>
    /// True when the waypoint should be dropped: reached, or (checked every few seconds) land now lies across the
    /// straight line to it. Uses <see cref="AiState.Repath"/> as its clock.
    /// </summary>
    public static bool WaypointSpent(World world, Ship ship)
    {
        var ai = ship.Ai!;
        if (!ai.HasWaypoint || ship.Pos.DistanceTo(ai.Waypoint) < ArriveRadius) return true;
        ai.Repath -= Tuning.Dt;
        if (ai.Repath > 0) return false;
        ai.Repath = RejoinCheck;
        return !Pathing.Clear(world.Map.Nav, ship.Pos, ai.Waypoint);
    }

    /// <summary>Brings a broadside to bear on the target and fires it (GDD §8 AI).</summary>
    public static ShipInput Engage(World world, Ship ship, Ship target)
    {
        var rel = target.Pos - ship.Pos;
        double d = rel.Length;
        double range = ship.Range;
        ShipInput input;
        if (d > range * 0.7)
        {
            // Close: aim a little ahead of the target.
            input = SteerTo(world, ship, target.Pos + target.Vel * 3, full: true);
        }
        else
        {
            // Put the target abeam on whichever side needs the smaller turn, unless that course lies in the no-go
            // zone: then the other beam (a ship holding a course into the wind sits in irons, audit C-02).
            double bearing = rel.Angle;
            double hs = Angles.Wrap(bearing - Math.PI / 2), hp = Angles.Wrap(bearing + Math.PI / 2);
            double noGo = Angles.Rad(ship.PointDeg + 4), from = ship.LocalWind.From;
            bool sOk = Math.Abs(Angles.Wrap(hs - from)) >= noGo, pOk = Math.Abs(Angles.Wrap(hp - from)) >= noGo;
            double want = sOk != pOk ? (sOk ? hs : hp)
                : Math.Abs(Angles.Wrap(hs - ship.Heading)) < Math.Abs(Angles.Wrap(hp - ship.Heading)) ? hs : hp;
            want = AvoidLand(world, ship, want);
            input = new ShipInput(Helm(ship, want), Math.Sign(2 - ship.SailTarget));
        }
        var side = ClearShot(world, ship, target);
        return input with { FirePort = side == Side.Port && ship.CanFire(Side.Port), FireStarboard = side == Side.Starboard && ship.CanFire(Side.Starboard) };
    }

    /// <summary><see cref="Bears"/>, and no island in the way (shot stops at land: P-01). The land test runs only when a
    /// loaded side bears, so it costs nothing between broadsides.</summary>
    public static Side? ClearShot(World world, Ship ship, Ship target)
    {
        var side = Bears(ship, target);
        if (side is not { } s || !ship.CanFire(s)) return side;
        return world.LandAlong(ship.Pos, target.Pos) == null ? side : null;
    }

    /// <summary>
    /// The side whose broadside would cross the target's hull now, if either. The balls fly square off the side (±3°),
    /// so where she will be when they arrive must lie within the battery's spread along our keel plus her own half
    /// length; a bearing test alone (the old ±22° off the beam) wasted every broadside beyond ~50 m (audit C-09).
    /// </summary>
    public static Side? Bears(Ship ship, Ship target)
    {
        var rel = target.Pos - ship.Pos;
        double d = rel.Length;
        if (d >= ship.Range || d < 1e-6) return null;
        var lead = rel + (target.Vel - ship.Vel * 0.25) * (d / World.BallSpeed);
        double along = lead.Dot(ship.Forward), across = lead.Dot(ship.Right);
        double reach = ship.Hull.Length * 0.3 + target.Hull.Length * 0.5 + d * 0.03;
        if (Math.Abs(along) > reach || Math.Abs(across) >= ship.Range) return null;
        return across > 0 ? Side.Starboard : Side.Port;
    }

    /// <summary>Runs from a threat on the fastest point of sail that leads away.</summary>
    public static ShipInput Flee(World world, Ship ship, Vec2 threat) =>
        new(Helm(ship, FleeHeading(world, ship, threat)), Math.Sign(3 - ship.SailTarget));

    /// <summary>How far off her escape course a fleeing ship will yaw to bring a loaded broadside to bear.</summary>
    public const double YawLimitDeg = 50;

    /// <summary>
    /// A merchantman's defence: runs as <see cref="Flee"/> does, but while the attacker is inside her gun range and a
    /// loaded side can be brought abeam of it within <see cref="YawLimitDeg"/> of the escape course, she yaws to it and
    /// fires as it bears, then falls back on her course while the side reloads. One that comes alongside is answered at once.
    /// </summary>
    public static ShipInput FightingRetreat(World world, Ship ship, Ship threat)
    {
        double run = FleeHeading(world, ship, threat.Pos), want = run;
        var rel = threat.Pos - ship.Pos;
        if (rel.Length < ship.Range)
        {
            double bearing = rel.Angle, from = ship.LocalWind.From, noGo = Angles.Rad(ship.PointDeg + 4);
            double best = Angles.Rad(YawLimitDeg);
            foreach (var side in new[] { Side.Port, Side.Starboard })
            {
                if (!ship.CanFire(side)) continue;
                double h = Angles.Wrap(side == Side.Starboard ? bearing - Math.PI / 2 : bearing + Math.PI / 2);
                double off = Math.Abs(Angles.Wrap(h - run));
                if (off <= best && Math.Abs(Angles.Wrap(h - from)) >= noGo)
                {
                    best = off;
                    want = h;
                }
            }
            if (want != run) want = AvoidLand(world, ship, want);
        }
        var shot = ClearShot(world, ship, threat);
        return new ShipInput(Helm(ship, want), Math.Sign(3 - ship.SailTarget),
            FirePort: shot == Side.Port && ship.CanFire(Side.Port), FireStarboard: shot == Side.Starboard && ship.CanFire(Side.Starboard));
    }

    /// <summary>The fastest point of sail that leads away from a threat, clear of land.</summary>
    public static double FleeHeading(World world, Ship ship, Vec2 threat)
    {
        double away = (ship.Pos - threat).Angle;
        double from = ship.LocalWind.From;
        double best = away, bestSpeed = -1;
        for (int k = -3; k <= 3; k++)
        {
            double h = Angles.Wrap(away + k * Angles.Rad(20));
            double off = Angles.Deg(Math.Abs(Angles.Wrap(h - from)));
            double speed = Polar.Fraction(ship.Hull.Rig, off, ship.Hull.PointDeg) * (1 - Math.Abs(k) * 0.05);
            if (speed > bestSpeed)
            {
                bestSpeed = speed;
                best = h;
            }
        }
        return AvoidLand(world, ship, best);
    }
}
