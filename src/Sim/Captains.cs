namespace LastTide.Sim;

/// <summary>
/// Merchantmen trade between ports and flee anything hostile, answering with their few guns as they run (GDD §8).
/// Their cargo moves real stock.
/// </summary>
public sealed class MerchantCaptain : Captain
{
    public override ShipInput Tick(World world, Ship ship)
    {
        var ai = ship.Ai!;
        var threat = world.NearestThreatTo(ship, 450);
        if (threat != null)
        {
            ai.Fleeing = true;
            ai.FleeTime = 12;
            ai.Waypoint = threat.Pos;   // keep running from here once it drops out of sight (audit C-03)
            return Seamanship.FightingRetreat(world, ship, threat);
        }
        if (ai.Fleeing)
        {
            ai.FleeTime -= Tuning.Dt;
            if (ai.FleeTime > 0) return Seamanship.Flee(world, ship, ai.Waypoint);
            ai.Fleeing = false;
            ai.Repath = 0;
        }
        if (ai.DestPort < 0)
        {
            ai.Wait -= Tuning.Dt;
            // Lying in harbour the crew mends (World.Recover ran only once, on arrival: audit C-12).
            if (ai.HomePort >= 0 && world.Map.Ports[ai.HomePort].InHarbor(ship.Pos)) world.Recover(ship);
            if (ai.Wait > 0) return new ShipInput(0, ship.SailTarget > 0 ? -1 : 0);
            world.PlanVoyage(ship);
            if (ai.DestPort < 0) { ai.Wait = 20; return new ShipInput(0, 0); }
        }
        if (Seamanship.FollowPath(world, ship, out var input))
        {
            world.ArriveMerchant(ship);
            return new ShipInput(0, ship.SailTarget > 0 ? -1 : 0);
        }
        return input;
    }
}

/// <summary>Crown patrols circle their colony and go for anything hostile they sight (GDD §8).</summary>
public sealed class PatrolCaptain : Captain
{
    public override ShipInput Tick(World world, Ship ship)
    {
        var ai = ship.Ai!;
        var home = world.Map.Ports[ai.HomePort];
        var enemy = world.NearestEnemyOf(ship, world.SightAt(ship.Pos));
        if (enemy != null && ship.Pos.DistanceTo(home.Harbor) < 1400)
        {
            ai.TargetShip = enemy.Id;
            return Seamanship.Engage(world, ship, enemy);
        }
        ai.TargetShip = -1;
        if (Seamanship.WaypointSpent(world, ship))
        {
            ai.Waypoint = Seamanship.ClearWaypoint(world, ship, () => world.SeaPointNear(home.Harbor, 250, 750));
            ai.HasWaypoint = true;
        }
        return Seamanship.SteerTo(world, ship, ai.Waypoint, full: false);
    }
}

/// <summary>Brethren raiders prowl the lanes near their haven, take merchants, and run home when badly hurt.</summary>
public sealed class RaiderCaptain : Captain
{
    public override ShipInput Tick(World world, Ship ship)
    {
        var ai = ship.Ai!;
        var home = world.Map.Ports[ai.HomePort];
        if (ship.HullHp < ship.MaxHp * 0.3 || ship.Water > 60)
        {
            ai.TargetShip = -1;
            var input = Seamanship.SteerTo(world, ship, home.Harbor, full: true);
            if (ship.Pos.DistanceTo(home.Harbor) < home.RingRadius) world.Recover(ship);
            return input;
        }
        var prey = world.NearestPreyOf(ship, world.SightAt(ship.Pos));
        if (prey != null)
        {
            ai.TargetShip = prey.Id;
            return Seamanship.Engage(world, ship, prey);
        }
        ai.TargetShip = -1;
        if (Seamanship.WaypointSpent(world, ship))
        {
            ai.Waypoint = Seamanship.ClearWaypoint(world, ship, () => world.LaneWaypointNear(home, 1500));
            ai.HasWaypoint = true;
        }
        return Seamanship.SteerTo(world, ship, ai.Waypoint, full: false);
    }
}

/// <summary>Director spawns hunt the player and give up only when she is long gone or inside a fort's ring.</summary>
public sealed class HunterCaptain : Captain
{
    public const double HunterPatience = 180;

    public override ShipInput Tick(World world, Ship ship)
    {
        var ai = ship.Ai!;
        var player = world.Ship;
        double d = ship.Pos.DistanceTo(player.Pos);
        var fort = world.Map.PortAt(player.Pos);
        // A friendly fort's ring is a sanctuary; one whose guns are turned on her is not (audit C-15).
        bool sanctuary = fort != null && fort.Fort && !world.PlayerHostileTo(fort.Faction) && fort.Faction != ship.Faction;
        // Patience: a stern chase that never brings her within range is given up after three minutes (Wait doubles as the chase clock).
        if (d > ship.Range) ai.Wait += Tuning.Dt; else ai.Wait = 0;
        if (ai.Wait > HunterPatience) ship.Sunk = true;
        if (d > 2500 || sanctuary)
        {
            ai.Lost += Tuning.Dt;
            if (ai.Lost > 90) ship.Sunk = true;   // slips away over the horizon (no flotsam: not a sinking)
            if (sanctuary)
            {
                // Wait outside the ring.
                var hold = fort!.Harbor + (ship.Pos - fort.Harbor).Normalized * (fort.RingRadius + 120);
                return Seamanship.SteerTo(world, ship, hold, full: false);
            }
        }
        else ai.Lost = 0;
        if (!world.CanSpotPlayer(ship))
        {
            // Doused lantern or fog: they close on where they last saw her (lost past 900 m), then cast about there —
            // not on where she has gone since, which they cannot know (audit C-18).
            if (!ai.HasWaypoint)
            {
                ai.Waypoint = d < 900 ? player.Pos : world.SeaPointNear(ship.Pos, 200, 500);
                ai.HasWaypoint = true;
            }
            else if (ship.Pos.DistanceTo(ai.Waypoint) < Seamanship.ArriveRadius)
                ai.Waypoint = world.SeaPointNear(ship.Pos, 200, 500);
            return Seamanship.SteerTo(world, ship, ai.Waypoint, full: true);
        }
        ai.HasWaypoint = false;
        return Seamanship.Engage(world, ship, player);
    }
}
