namespace LastTide.Sim;

/// <summary>
/// A headless captain for the player's own ship, used by the balance runs (GDD §11 targets) and tests.
/// It trades on remembered prices, keeps the ship fed and paid, repairs, upgrades when rich, digs matched
/// treasure, fights what it can beat and runs from what it cannot. Deterministic: it reads only the world
/// and its own state. Port business runs as one scripted visit (the clock is frozen there anyway).
/// </summary>
public sealed class Autopilot
{
    public enum Mode { Idle, Voyage, Explore, Flee, Fight, Dig, Rescue, Shelter }

    public Mode State { get; private set; } = Mode.Idle;
    public int Dest { get; private set; } = -1;
    public Good PlanGood { get; private set; } = Good.Provisions;
    public int PlanUnits { get; private set; }
    public int Trades, Fights, Flights, Digs, Visits, Upgrades, HitsTaken, Contracts;
    public int MaxGold;
    public readonly Dictionary<string, int> Routes = new();   // "good:from>to" → count
    /// <summary>Diagnostics: one line per decision worth reading (balance harness, tests).</summary>
    public Action<string>? Log;

    const double PlanSpeed = 6.0;       // m/s assumed for voyage planning
    const int Reserve = 30;             // gold kept for wages
    double fleeTime, repath;
    int lastOrder = -1, fightTarget = -1;
    Mode resume = Mode.Idle;              // what to go back to after a flee or a fight
    int lastThreatId = -1, sameThreatFlights;
    double threatDist = -1, threatDistAgo = -1, distClock;
    int leftPort = -1;                    // the port just cast off from, and when
    double leftAt = -1e9, shelterTime;
    int shelterPort = -1;
    public int Shelters;
    Vec2 fleeFrom, lastPos;
    double lastProgressTime;
    int digSite = -1;

    public ShipInput Tick(World w)
    {
        if (w.RunOver) return default;
        var ship = w.Ship;
        ship.Ai ??= new AiState { Role = Role.Player };
        MaxGold = Math.Max(MaxGold, w.Player.Gold);
        if (w.IsDocked)
        {
            InPort(w);
            return default;
        }
        double dt = Tuning.Dt;
        var ai = ship.Ai;
        ShipInput input;
        int order = 0;
        bool lantern = false;

        foreach (var e in w.Events) if (e.Type == CombatEventType.Hit && e.ShipId == ship.Id) HitsTaken++;
        // ---- danger first: a hostile that is close, closing, or a hunter (they always chase) ----
        var hostile = w.NearestThreatTo(ship, 650);
        Ship? threat = null;
        if (hostile != null)
        {
            double d = hostile.Pos.DistanceTo(ship.Pos);
            if (hostile.Id != lastThreatId) { threatDist = threatDistAgo = d; distClock = 0; lastThreatId = hostile.Id; sameThreatFlights = 0; }
            distClock += dt;
            if (distClock >= 2) { threatDistAgo = threatDist; threatDist = d; distClock = 0; }
            bool closing = threatDistAgo - d > 12;
            bool hunter = hostile.Ai?.Role is Role.Hunter or Role.Privateer;
            if (d < 330 || closing || hunter || State is Mode.Fight or Mode.Flee or Mode.Shelter) threat = hostile;
        }
        else lastThreatId = -1;
        if (ship.Foundering)
        {
            var harbour = NearestOpenPort(w, 1200, rescue: true);
            if (harbour != null)
            {
                State = Mode.Rescue;
                input = Seamanship.SteerTo(w, ship, harbour.Harbor, full: true);
                return Orders(w, input with { Order = Order(4) });
            }
            if (State == Mode.Rescue) State = Mode.Idle;
        }
        // ---- a beast: carpenters first, guns when it shows itself, and away from it ----
        var beast = w.Monster;
        if (beast != null && beast.State != MonsterState.Faded && State != Mode.Fight && State != Mode.Flee)
        {
            // Shoot at what can be hit: the nearest live tentacle or weed mass in a grip, else the beast itself (audit C-13).
            var aim = beast.Pos;
            double best = double.MaxValue;
            foreach (var t in beast.Targets)
                if (t.Alive && t.Pos.DistanceTo(ship.Pos) < best) { best = t.Pos.DistanceTo(ship.Pos); aim = t.Pos; }
            double db = aim.DistanceTo(ship.Pos);
            ShipInput bi = Seamanship.Flee(w, ship, beast.Pos);
            bool fireP = false, fireS = false;
            if (beast.Surfaced && ship.Cannons > 0 && db < ship.Range && w.Player.Units(Good.Munitions) > 0)
            {
                double rel = Angles.Wrap((aim - ship.Pos).Angle - ship.Heading);
                if (Math.Abs(rel - Math.PI / 2) < Angles.Rad(24)) fireS = ship.CanFire(Side.Starboard);
                if (Math.Abs(rel + Math.PI / 2) < Angles.Rad(24)) fireP = ship.CanFire(Side.Port);
            }
            // Carpenters first against a beast — but held by the Kraken the guns must be manned to cut her free:
            // under "repair" nobody serves them and a side takes 120 s to reload (audit C-13).
            int beastOrder = w.Pinned && beast.Type == MonsterType.Kraken ? 1 : 3;
            return Orders(w, bi with { FirePort = fireP, FireStarboard = fireS, Order = Order(beastOrder) });
        }
        if (threat != null && State is not (Mode.Fight or Mode.Flee or Mode.Shelter))
        {
            resume = State is Mode.Voyage or Mode.Explore ? State : Mode.Idle;
            if (CanFight(w, ship, threat)) { State = Mode.Fight; fightTarget = threat.Id; Fights++; }
            else
            {
                State = Mode.Flee; fleeTime = 18; fleeFrom = threat.Pos; Flights++;
                // Chased off the same road twice: it is his road; go somewhere else.
                if (++sameThreatFlights >= 2 && Dest >= 0 && w.Time - lastPlanTime > 20) { Plan(w, avoid: Dest); resume = State; State = Mode.Flee; }
            }
            Log?.Invoke($"day {w.DaysSurvived:0.00} {State}: {threat.Hull.Id} {threat.Faction} guns {threat.Cannons} crew {threat.Crew} hull {threat.HullHp:0} at {threat.Pos.DistanceTo(ship.Pos):0} m · me guns {ship.Cannons} crew {ship.Crew} hull {ship.HullHp:0} shot {w.Player.Units(Good.Munitions)}");
        }
        if (State == Mode.Fight)
        {
            var target = threat != null && threat.Id == fightTarget ? threat : threat;
            if (target == null || !CanFight(w, ship, target) && target.Pos.DistanceTo(ship.Pos) > 200)
            {
                if (target != null) { State = Mode.Flee; fleeTime = 18; fleeFrom = target.Pos; Flights++; }
                else { State = Dest >= 0 ? resume : Mode.Idle; repath = 0; }
            }
            else
            {
                input = Seamanship.Engage(w, ship, target);
                return Orders(w, input with { Order = Order(1) });
            }
        }
        if (State == Mode.Flee)
        {
            fleeTime -= dt;
            if (threat != null) { fleeFrom = threat.Pos; fleeTime = Math.Max(fleeTime, 6); }
            if (fleeTime <= 0) { State = Dest >= 0 && resume != Mode.Idle ? resume : Mode.Idle; repath = 0; }
            else
            {
                input = Seamanship.Flee(w, ship, fleeFrom);
                var fort = NearestSanctuary(w, threat, 1600);
                if (fort != null)
                {
                    input = Seamanship.SteerTo(w, ship, fort.Harbor, full: true);
                    if (fort.InHarbor(ship.Pos)) { State = Mode.Shelter; shelterPort = fort.Id; shelterTime = 0; Shelters++; }
                }
                bool night = w.IsNight;
                int fleeOrder = ship.Leaks == 0 && ship.Water < 5 ? 2 : ship.Water > 25 ? 3 : 4;   // speed while dry; carpenters first once she is taking water
                return Orders(w, input with { Order = Order(fleeOrder), ToggleLantern = night && w.Lantern });
            }
        }

        if (State == Mode.Shelter)
        {
            // Lie furled inside the fort's ring: its guns and patrols see hunters off (they give up after 90 s here).
            shelterTime += dt;
            var fort = w.Map.Ports[shelterPort];
            bool clear = hostile == null || hostile.Pos.DistanceTo(ship.Pos) > 900;
            if (clear || shelterTime > 140)
            {
                State = Dest >= 0 && resume != Mode.Idle ? resume : Mode.Idle;
                repath = 0;
            }
            else
            {
                double dRing = ship.Pos.DistanceTo(fort.Harbor);
                if (dRing > fort.RingRadius * 0.6) input = Seamanship.SteerTo(w, ship, fort.Harbor, full: false);
                else input = new ShipInput(0, ship.SailTarget > 0 ? -1 : 0);
                // Sell and mend while sheltering if the port is worth a stop; the clock stands still inside.
                if (fort.InHarbor(ship.Pos) && w.IsOpen(fort) && WorthDocking(w, fort) && w.Apply(new PortCommand(PortAction.Dock)) == PortResult.Ok) return default;
                return Orders(w, input with { Order = Order(4) });
            }
        }

        // ---- housekeeping ----
        order = ship.Leaks > 0 || ship.Water > 8 ? 3 : 4;
        if (!w.Lantern && !w.IsNight) lantern = true;     // relight by day (it toggles)
        if (!w.Lantern && w.IsNight && threat == null) lantern = true;

        // ---- treasure ----
        if (State == Mode.Dig)
        {
            var site = w.Map.Treasures[digSite];
            if (site.Dug) { Digs++; State = Mode.Idle; repath = 0; }
            else
            {
                double d = ship.Pos.DistanceTo(site.DigRing);
                if (d > site.RingRadius * 0.6)
                    input = Seamanship.SteerTo(w, ship, site.DigRing, full: false);
                else
                    input = new ShipInput(0, ship.SailTarget > 0 ? -1 : 0, Action: ship.SailTarget == 0 && ship.SailFraction < 0.05);
                return Orders(w, input with { Order = Order(order), ToggleLantern = lantern });
            }
        }
        else if (threat == null && ship.HullHp > ship.MaxHp * 0.5)
        {
            foreach (var map in w.Player.BottleMaps)
            {
                if (!map.Solved) continue;
                var site = w.Map.Treasures[map.Treasure];
                if (site.Dug || site.DigRing.DistanceTo(ship.Pos) > 700) continue;
                State = Mode.Dig;
                digSite = site.Id;
                break;
            }
        }

        // ---- the voyage ----
        if (State == Mode.Idle && (w.Time - lastPlanTime > 5 || Dest < 0)) Plan(w);
        var dest = Dest >= 0 ? w.Map.Ports[Dest] : null;
        if (dest == null) return Orders(w, new ShipInput(0, 0, Order: Order(order), ToggleLantern: lantern));
        // Starving: the nearest open port wins over the plan.
        int need = (int)Math.Ceiling(ship.Crew / 4.0);
        if (w.Player.Units(Good.Provisions) < need)
        {
            var near = NearestOpenPort(w, 5000);
            if (near != null && near.Id != Dest) SetDest(w, near.Id, Mode.Explore);
            dest = w.Map.Ports[Dest];
        }
        var here = w.HarborHere;
        if (here != null && w.IsOpen(here) && WorthDocking(w, here))
        {
            if (w.Apply(new PortCommand(PortAction.Dock)) == PortResult.Ok) return default;
        }
        repath -= dt;
        if (repath <= 0 || ai.Path.Count == 0) SetDest(w, Dest, State);
        if (Seamanship.FollowPath(w, ship, out input))
            input = Seamanship.SteerTo(w, ship, dest.Harbor, full: false);
        // Storms: shorten sail.
        if (w.ConditionsAt(ship.Pos).Storm > 0.2 && ship.SailTarget >= 3) input = input with { SailDelta = -1 };
        // Stuck watch: no progress for a while → replan.
        if (ship.Pos.DistanceTo(lastPos) > 40) { lastPos = ship.Pos; lastProgressTime = w.Time; }
        else if (w.Time - lastProgressTime > 90) { lastProgressTime = w.Time; Plan(w, avoid: Dest); }
        return Orders(w, input with { Order = Order(order), ToggleLantern = lantern });
    }

    /// <summary>The destination, a port never visited, a good sale for the hold, a battered hull or an empty larder.</summary>
    bool WorthDocking(World w, Port here)
    {
        if (here.Id == leftPort && w.Time - leftAt < 90) return false;   // just cast off from it
        if (here.Id == Dest || !w.Player.PortsVisited.Contains(here.Id)) return true;
        foreach (var c in w.Player.Contracts) if (c.To == here.Id) return true;   // a delivery due here, on the way elsewhere
        var ship = w.Ship;
        if (w.Player.Gold < 25) return false;
        if (ship.HullHp < ship.MaxHp * 0.5 || ship.TornSails) return true;
        if (w.Player.Units(Good.Provisions) < Math.Ceiling(ship.Crew / 4.0) * 2 && w.Player.Gold > 40) return true;
        foreach (var def in Goods.All)
        {
            var g = def.Id;
            if (g is Good.Provisions or Good.Munitions or Good.Timber || w.Player.Units(g) <= 0) continue;
            if (here.Market.SellPrice(g) >= w.Player.CostBasis[(int)g] * 1.15) return true;
        }
        return false;
    }

    int Order(int want) { if (want == lastOrder) return 0; lastOrder = want; return want; }
    /// <summary>Final say on the helm: never plough into another hull (a ram on a merchant makes an enemy and costs hull).</summary>
    ShipInput Orders(World w, ShipInput input)
    {
        var ship = w.Ship;
        if (ship.Speed < 0.5) return input;
        Ship? near = null;
        double nearD = 75 + ship.Hull.Length;
        foreach (var o in w.Others)
        {
            if (o.Sunk) continue;
            double d = o.Pos.DistanceTo(ship.Pos);
            if (d < nearD) { nearD = d; near = o; }
        }
        if (near == null) return input;
        if (State == Mode.Fight && near.Id == fightTarget) return input;
        double bearing = Angles.Wrap((near.Pos - ship.Pos).Angle - ship.Heading);
        if (Math.Abs(bearing) > Angles.Rad(50)) return input;
        double dodge = bearing >= 0 ? -1 : 1;   // it is to starboard: helm to port, and vice versa
        return input with { Rudder = dodge };
    }

    /// <summary>Damage per second of one broadside side, as the guns are manned.</summary>
    static double Dps(Ship s) => Math.Max(1, s.Cannons / 2.0) * s.BallDamage * 0.7 / Math.Max(4, s.ReloadTime);

    /// <summary>Fight when she can sink him well before he sinks her, with shot enough to do it.</summary>
    bool CanFight(World w, Ship ship, Ship threat)
    {
        if (ship.Cannons == 0 || ship.Foundering || ship.HullHp < ship.MaxHp * 0.45) return false;
        double myTtk = threat.HullHp / Dps(ship);
        double hisTtk = ship.HullHp / Math.Max(0.01, Dps(threat));
        int shotNeeded = (int)Math.Ceiling(myTtk / Math.Max(4, ship.ReloadTime) * Math.Max(1, ship.Cannons / 2.0));
        double edge = ship.HullHp >= ship.MaxHp * 0.6 ? 1.1 : 0.8;   // a sound ship takes an even fight; a hurt one wants the odds
        return myTtk < edge * hisTtk && w.Player.Units(Good.Munitions) >= shotNeeded;
    }

    /// <summary>A fort of another faction than the hunter's: its guns and patrols make hunters give up (HunterCaptain).</summary>
    Port? NearestSanctuary(World w, Ship? threat, double radius)
    {
        Port? best = null;
        double bestD = radius;
        foreach (var p in w.Map.Ports)
        {
            // Not a fort whose guns are turned on her (audit C-13).
            if (!p.Discovered || !p.Fort || !w.IsOpen(p) || w.PlayerHostileTo(p.Faction) || (threat != null && p.Faction == threat.Faction)) continue;
            double d = p.Harbor.DistanceTo(w.Ship.Pos);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    /// <param name="rescue">Only a port that has not already taken her in once (GDD §19; the notice tells a player so).</param>
    Port? NearestOpenPort(World w, double radius, bool rescue = false)
    {
        Port? best = null;
        double bestD = radius;
        foreach (var p in w.Map.Ports)
        {
            if (!p.Discovered || !w.IsOpen(p) || rescue && w.RescuedAt.Contains(p.Id)) continue;
            double d = p.Harbor.DistanceTo(w.Ship.Pos);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    void SetDest(World w, int portId, Mode mode)
    {
        Dest = portId;
        State = mode;
        var ai = w.Ship.Ai ??= new AiState { Role = Role.Player };   // a new hull is a new ship object
        var port = w.Map.Ports[portId];
        ai.Path = Pathing.Smooth(w.Map.Nav, Pathing.Find(w.Map.Nav, w.Ship.Pos, port.Harbor, w.Ship.LocalWind.From, w.Ship.Hull.PointDeg));
        ai.PathIndex = 0;
        repath = 25;
        lastPos = w.Ship.Pos;
        lastProgressTime = w.Time;
    }

    /// <summary>Picks where to go next: a contract's port when one is due, else the best remembered trade, else somewhere unknown.</summary>
    double lastPlanTime = -1e9;

    void Plan(World w, int avoid = -1)
    {
        PlanTrade(w, avoid);
        // Contracts in hand come first: the earliest deadline, with the trade plan kept only if it goes there too.
        Contract? due = null;
        foreach (var c in w.Player.Contracts)
            if (c.To != avoid && (w.Docked == null || c.To != w.Docked.Id) && (due == null || c.Deadline < due.Deadline)) due = c;
        if (due == null || Dest == due.To) return;
        PlanUnits = 0;
        SetDest(w, due.To, Mode.Voyage);
        Log?.Invoke($"day {w.DaysSurvived:0.00} contract: → {w.Map.Ports[due.To].Name} due {due.Deadline:0.00} pays {due.Pay}");
    }

    /// <summary>Days the autopilot expects a passage from the port she is in to take.</summary>
    static double PassageDays(World w, Contract c) => w.SeaDistance(c.From, c.To) / (PlanSpeed * Tuning.SecondsPerDay);

    static bool InTime(World w, Contract c) => PassageDays(w, c) * 1.3 + 0.2 < c.Deadline - w.DaysSurvived;

    /// <summary>
    /// The harbour office. With no trade worth making she takes the best-paying freight or dispatch on the board and
    /// goes there; bound somewhere anyway, she signs whatever is bound there too. She does not smuggle.
    /// </summary>
    void OfficeWork(World w, Port port, bool chooseLeg)
    {
        var offers = w.ContractOffers(port);
        if (chooseLeg && State == Mode.Explore && Dest >= 0)
        {
            // Only another market she has yet to learn, no farther than the one she was going to learn anyway: the
            // yardstick's seamanship is the limit, and a paid passage it cannot make stalls it (seeds 11 and 12 sailed
            // in circles for days round a harbour across a headland).
            double reach = w.SeaDistance(port.Id, Dest);
            int best = -1;
            double bestRate = 0;
            for (int i = 0; i < offers.Length; i++)
            {
                if (offers[i] is not { Kind: not ContractKind.Contraband } c || !InTime(w, c)) continue;
                if (c.To != Dest && (w.Player.PortsVisited.Contains(c.To) || w.SeaDistance(c.From, c.To) > reach)) continue;
                if (w.Player.SlotsUsed + c.Slots > w.Ship.CargoCapacity + 1e-9) continue;
                double rate = c.Pay / (PassageDays(w, c) + 0.3);
                if (rate > bestRate) { bestRate = rate; best = i; }
            }
            if (best >= 0) SetDest(w, offers[best]!.To, Mode.Explore);
            return;
        }
        for (int i = 0; i < offers.Length; i++)
            if (offers[i] is { Kind: not ContractKind.Contraband } c && c.To == Dest && InTime(w, c)
                && w.Apply(new PortCommand(PortAction.SignContract, Amount: i)) == PortResult.Ok)
            {
                Contracts++;
                Log?.Invoke($"day {w.DaysSurvived:0.00} signed {c.Kind} → {w.Map.Ports[c.To].Name} pays {c.Pay} ({c.Advance} now) due {c.Deadline:0.00}");
            }
    }

    void PlanTrade(World w, int avoid)
    {
        lastPlanTime = w.Time;
        var ship = w.Ship;
        var player = w.Player;
        var here = w.Docked;
        var nav = w.Map.Nav;
        var dist = nav.Distances(ship.Pos);
        double D(Port p) => dist[NavGrid.ToY(p.Harbor.Y) * nav.W + NavGrid.ToX(p.Harbor.X)];
        double bestScore = 0;
        int bestPort = -1;
        Good bestGood = Good.Provisions;
        int bestUnits = 0;
        string nearMiss = "";
        double nearMissValue = double.MinValue;
        if (here != null)
        {
            var m = here.Market;
            int freeSlots = (int)Math.Floor(ship.CargoCapacity - player.SlotsUsed + 1e-9);
            foreach (var def in Goods.All)
            {
                var g = def.Id;
                if (def.Group == GoodGroup.Rare && !here.Secret) continue;
                if (g is Good.Provisions or Good.Munitions or Good.Timber) continue;
                if (m.Stock[(int)g] <= 0) continue;
                double unitCost = m.Price(g);
                int maxUnits = Math.Min((int)Math.Floor(m.Stock[(int)g]), (int)Math.Floor(freeSlots / def.SlotsPerUnit));
                maxUnits = Math.Min(maxUnits, (int)Math.Floor((player.Gold - Reserve) / Math.Max(1, unitCost)));
                int slotsNow = freeSlots;
                if (maxUnits <= 0) continue;
                foreach (var p in w.Map.Ports)
                {
                    if (p.Id == here.Id || p.Id == avoid || !p.Discovered || !w.IsOpen(p)) continue;
                    var r = player.Remembered(p.Id, g);
                    if (r == null) continue;
                    double d = D(p);
                    if (d < 0) continue;
                    double age = w.DaysSurvived - r.Day;
                    double sale = r.Price * 0.95 * (age > 3 ? 0.85 : 1.0);
                    int units = Math.Min(maxUnits, 40);
                    int cost = m.QuoteBuy(g, units);
                    while (units > 1 && cost > player.Gold - Reserve) { units = (int)(units * 0.7); cost = m.QuoteBuy(g, units); }
                    double profit = sale * units * 0.92 - cost;   // the buyer's price also softens as we sell
                    double days = d / (PlanSpeed * Tuning.SecondsPerDay);
                    double upkeep = days * (w.DailyWages() + need(ship) * 4);
                    if (profit - upkeep > nearMissValue) { nearMissValue = profit - upkeep; nearMiss = $"{units} {def.Key} → {p.Name} buy {cost} sell {sale * units * 0.92:0} upkeep {upkeep:0} days {days:0.00}"; }
                    if (profit <= 0 || units < 2) continue;
                    if (profit - upkeep < 12) continue;
                    double score = (profit - upkeep) / (days + 0.15);
                    if (score > bestScore) { bestScore = score; bestPort = p.Id; bestGood = g; bestUnits = units; }
                }
            }
        }
        // What is already in the hold: the best known buyer for it counts as a voyage too (no purchase needed).
        int carryPort = -1;
        double carryScore = 0;
        foreach (var def in Goods.All)
        {
            var g = def.Id;
            int units = player.Units(g);
            if (units <= 0 || g is Good.Provisions or Good.Munitions or Good.Timber) continue;
            foreach (var p in w.Map.Ports)
            {
                if ((here != null && p.Id == here.Id) || p.Id == avoid || !p.Discovered || !w.IsOpen(p)) continue;
                var r = player.Remembered(p.Id, g);
                if (r == null) continue;
                double d = D(p);
                if (d < 0) continue;
                double sale = r.Price * 0.95 * (w.DaysSurvived - r.Day > 3 ? 0.85 : 1.0) * units * 0.92;
                double gain = sale - player.CostBasis[(int)g] * units;
                double days = d / (PlanSpeed * Tuning.SecondsPerDay);
                double score = (gain - days * w.DailyWages()) / (days + 0.15);
                if (score > carryScore) { carryScore = score; carryPort = p.Id; }
            }
        }
        if (carryPort >= 0 && carryScore > bestScore * 0.8)
        {
            PlanUnits = 0;
            SetDest(w, carryPort, Mode.Voyage);
            Log?.Invoke($"day {w.DaysSurvived:0.00} carry: hold → {w.Map.Ports[carryPort].Name} score {carryScore:0.0} gold {player.Gold}");
            return;
        }
        if (bestPort >= 0)
        {
            PlanGood = bestGood;
            PlanUnits = bestUnits;
            SetDest(w, bestPort, Mode.Voyage);
            Log?.Invoke($"day {w.DaysSurvived:0.00} plan: {bestUnits} {Goods.Of(bestGood).Key} {(here != null ? here.Name : "sea")} → {w.Map.Ports[bestPort].Name} score {bestScore:0.0} gold {player.Gold}");
            return;
        }
        // Nothing known to be worth carrying: go and learn a market, and speculate on whatever is cheap here
        // against its base price (a cheap good at a producer is dear somewhere). Nearest open port never visited,
        // else the one with the stalest ledger.
        PlanUnits = 0;
        if (here != null)
        {
            var m = here.Market;
            int freeSlots = (int)Math.Floor(ship.CargoCapacity - player.SlotsUsed + 1e-9);
            double bestRatio = 0.66;
            foreach (var def in Goods.All)
            {
                var g = def.Id;
                if (g is Good.Provisions or Good.Timber || def.Group == GoodGroup.Rare || m.Stock[(int)g] < 3) continue;
                double ratio = m.Price(g) / def.BasePrice;
                if (ratio >= bestRatio) continue;
                // A known buyer anywhere (dearer than here by a third) makes it a stake worth taking; blind, a small one.
                bool buyerKnown = w.Map.Ports.Any(p => p.Discovered && p.Id != here.Id && (player.Remembered(p.Id, g)?.Price ?? 0) > m.Price(g) * 1.35);
                double stake = buyerKnown ? 0.85 : 0.6;
                int units = Math.Min((int)Math.Floor(m.Stock[(int)g]), (int)Math.Floor(freeSlots / def.SlotsPerUnit));
                units = Math.Min(units, 40);
                while (units > 0 && m.QuoteBuy(g, units) > (player.Gold - Reserve) * stake) units = units * 3 / 4;
                if (units < 2) continue;
                bestRatio = ratio;
                bestGood = g;
                bestUnits = units;
            }
            if (bestUnits >= 2) { PlanGood = bestGood; PlanUnits = bestUnits; }
        }
        Port? pick = null;
        double pickD = double.MaxValue;
        foreach (var p in w.Map.Ports)
        {
            if ((here != null && p.Id == here.Id) || p.Id == avoid || !p.Discovered || !w.IsOpen(p)) continue;
            double d = D(p);
            if (d < 0) continue;
            double score = player.PortsVisited.Contains(p.Id) ? d + 3000 - 200 * Freshness(w, p) : d;
            if (score < pickD) { pickD = score; pick = p; }
        }
        if (pick == null)
        {
            foreach (var p in w.Map.Ports)
                if (w.IsOpen(p) && (here == null || p.Id != here.Id) && D(p) >= 0 && D(p) < pickD) { pickD = D(p); pick = p; }
        }
        if (pick != null) SetDest(w, pick.Id, Mode.Explore);
        else { Dest = -1; State = Mode.Idle; }
        Log?.Invoke($"day {w.DaysSurvived:0.00} explore: {(here != null ? here.Name : "sea")} → {(pick != null ? pick.Name : "nowhere")} gold {player.Gold} ledger {player.Ledger.Count} known {w.Map.Ports.Count(q => q.Discovered)} spec {PlanUnits} {Goods.Of(PlanGood).Key} · best rejected: {nearMiss}");

        static int need(Ship s) => (int)Math.Ceiling(s.Crew / 4.0);
    }

    static double Freshness(World w, Port p)
    {
        double newest = -10;
        foreach (var e in w.Player.Ledger) if (e.Port == p.Id) newest = Math.Max(newest, e.Day);
        return Math.Max(0, w.DaysSurvived - newest);
    }

    /// <summary>The port visit: sell, repair, provision, hire, upgrade, buy the next cargo, cast off.</summary>
    void InPort(World w)
    {
        var ship = w.Ship;
        var player = w.Player;
        var port = w.Docked!;
        var m = port.Market;
        Visits++;
        if (Log != null)
        {
            var ratios = Goods.All.Where(d => d.Group != GoodGroup.Rare).Select(d => (d.Key, r: m.Price(d.Id) / d.BasePrice, p: m.Price(d.Id), st: m.Stock[(int)d.Id])).OrderBy(x => x.r).ToList();
            Log($"day {w.DaysSurvived:0.00} dock {port.Name} ({port.Faction}, produces {string.Join("/", port.Produces.Select(g => Goods.Of(g).Key))}; consumes {string.Join("/", port.Consumes.Select(g => Goods.Of(g).Key))}): gold {player.Gold} hull {ship.HullHp:0}/{ship.MaxHp:0} crew {ship.Crew} cargo {string.Join(",", Goods.All.Where(d => player.Units(d.Id) > 0).Select(d => $"{d.Key}×{player.Units(d.Id)}"))} · cheapest {string.Join(", ", ratios.Take(3).Select(x => $"{x.Key} {x.p:0.0} ({x.r:0.00}, stock {x.st:0})"))} · dearest {string.Join(", ", ratios.TakeLast(3).Select(x => $"{x.Key} {x.p:0.0} ({x.r:0.00})"))}");
        }
        // Sell what pays.
        foreach (var def in Goods.All)
        {
            var g = def.Id;
            int units = player.Units(g);
            if (units <= 0 || g is Good.Provisions or Good.Munitions or Good.Timber) continue;
            bool planned = port.Id == Dest && g == PlanGood;
            double basis = player.CostBasis[(int)g];
            bool stale = w.DaysSurvived - boughtDay.GetValueOrDefault(g, w.DaysSurvived) > 2.5;   // cut losses rather than sail a dead cargo about
            bool destination = port.Id == Dest;
            if (planned || m.SellPrice(g) >= basis * 1.05 || basis <= 0 || (destination && m.SellPrice(g) >= basis) || (stale && m.SellPrice(g) >= basis * 0.9))
            {
                int before = player.Gold;
                if (w.Apply(new PortCommand(PortAction.Sell, g, units)) == PortResult.Ok)
                {
                    Trades++;
                    if (planned) { string key = $"{def.Key}:{PlanFrom}>{port.Id}"; Routes[key] = Routes.GetValueOrDefault(key) + 1; }
                }
            }
        }
        // Repairs, within a budget: never spend the trading capital on the last few points of hull —
        // but never put to sea a wreck either: below 35% she sells guns and shot before she sails unrepaired.
        if (ship.TornSails) w.Apply(new PortCommand(PortAction.Repair, Amount: 0));
        RepairWithin(w, ship.HullHp < ship.MaxHp * 0.4 ? 10 : Reserve + 70);
        if (ship.HullHp < ship.MaxHp * 0.35)
        {
            while (ship.HullHp < ship.MaxHp * 0.5 && ship.Cannons > 0 && w.Apply(new PortCommand(PortAction.SellCannon)) == PortResult.Ok) RepairWithin(w, 10);
            if (ship.HullHp < ship.MaxHp * 0.35 && player.Units(Good.Munitions) > 0)
            {
                w.Apply(new PortCommand(PortAction.Sell, Good.Munitions, player.Units(Good.Munitions)));
                RepairWithin(w, 10);
            }
        }
        // Stores: provisions for four days, powder for the guns, a plank or two.
        int need = (int)Math.Ceiling(ship.Crew / 4.0);
        Buy(w, Good.Provisions, need * 3 - player.Units(Good.Provisions), keep: Reserve);
        if (ship.Cannons > 0) Buy(w, Good.Munitions, 12 - player.Units(Good.Munitions), keep: Reserve);
        Buy(w, Good.Timber, 2 - player.Units(Good.Timber), keep: Reserve);
        // Hands: enough for the rigging plus a gun crew, when it can be paid.
        int wantCrew = Math.Min(ship.Hull.CrewMax, Math.Max(4, ship.Hull.Riggers + 2 + ship.Cannons / 2));
        int mustCrew = Math.Min(ship.Hull.CrewMax, Math.Max(2, ship.Hull.Riggers));
        while (ship.Crew < mustCrew && player.Gold > World.SigningFee + 20 && w.Apply(new PortCommand(PortAction.Hire, Amount: 1)) == PortResult.Ok) { }
        while (ship.Crew < wantCrew && player.Gold > World.SigningFee + Reserve + 300 && w.Apply(new PortCommand(PortAction.Hire, Amount: 1)) == PortResult.Ok) { }
        // A cartographer as soon as she can pay for one: without him nothing new goes on the chart (no new markets).
        if (!w.HasCartographer)
            foreach (var o in w.TavernOfficers(port))
                if (o.Type == OfficerType.Cartographer && player.Gold >= Officers.PriceOf(o.Type, o.Tier) + Reserve
                    && w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)o.Type * 10 + o.Tier)) == PortResult.Ok) break;
        // Upgrades when rich.
        while (ship.Cannons < ship.Hull.GunsPerSide * 2 && player.Gold > World.CannonPrice + 260 && w.Apply(new PortCommand(PortAction.BuyCannon)) == PortResult.Ok) Upgrades++;
        foreach (var part in new[] { Part.Sails, Part.Rigging, Part.Hold, Part.Planking, Part.Copper, Part.Cannons, Part.Lantern })
        {
            if (ship.Grade(part) >= 5) continue;
            int price = w.PartPrice(part);
            if (player.Gold > price * 3 + 200 && w.Apply(new PortCommand(PortAction.BuyPart, Amount: (int)part)) == PortResult.Ok) Upgrades++;
        }
        HullDef? better = null;
        foreach (var h in Hulls.All)
        {
            if (h.Cargo <= ship.Hull.Cargo || h.Cost <= ship.Hull.Cost) continue;
            int price = w.HullPrice(h);
            if (price + 350 > player.Gold) continue;
            if (better == null || h.Cargo > better.Cargo) better = h;
        }
        if (better != null && w.Apply(new PortCommand(PortAction.BuyHull, Text: better.Id)) == PortResult.Ok) Upgrades++;
        if (player.OfficerOf(OfficerType.Quartermaster) == null && player.Gold > 420)
            foreach (var o in w.TavernOfficers(port))
                if (o.Type == OfficerType.Quartermaster && w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)o.Type * 10 + o.Tier)) == PortResult.Ok) { Upgrades++; break; }
        if (player.OfficerOf(OfficerType.Lookout) == null && player.Gold > 600 && w.Ship.Hull.OfficerSlots > 1)   // she may have a new hull by now
            foreach (var o in w.TavernOfficers(port))
                if (o.Type == OfficerType.Lookout && w.Apply(new PortCommand(PortAction.HireOfficer, Amount: (int)o.Type * 10 + o.Tier)) == PortResult.Ok) { Upgrades++; break; }
        foreach (var u in w.BlackMarketHere)
            if (!w.HasUnique(u.Key) && player.Gold > u.Price + 300 && w.Apply(new PortCommand(PortAction.BuyUnique, Text: u.Key)) == PortResult.Ok) Upgrades++;
        if (w.TavernMap(port) != null && player.BottleMaps.Count < 2 && player.Gold > World.BottleMapPrice + 200)
            w.Apply(new PortCommand(PortAction.BuyMap));
        if (player.Gold > 90) w.Apply(new PortCommand(PortAction.Rumor));
        // The next leg.
        PlanFrom = port.Id;
        Plan(w);
        OfficeWork(w, port, chooseLeg: true);
        if (PlanUnits > 0)
        {
            int units = PlanUnits;
            while (units > 0 && w.Apply(new PortCommand(PortAction.Buy, PlanGood, units)) != PortResult.Ok) units = units * 3 / 4;
            PlanUnits = units;
            if (units > 0) boughtDay[PlanGood] = w.DaysSurvived;
        }
        OfficeWork(w, port, chooseLeg: false);
        w.Apply(new PortCommand(PortAction.CastOff));
        leftPort = port.Id;
        leftAt = w.Time;
    }

    int PlanFrom = -1;
    readonly Dictionary<Good, double> boughtDay = new();

    static void RepairWithin(World w, int keep)
    {
        var ship = w.Ship;
        double missing = ship.MaxHp - ship.HullHp;
        if (missing < 5) return;
        int hp = (int)Math.Floor(Math.Max(0, w.Player.Gold - keep) / w.RepairCostPerHp);
        hp = Math.Min(hp, (int)Math.Ceiling(missing));
        if (hp >= 5) w.Apply(new PortCommand(PortAction.Repair, Amount: hp));
    }

    static void Buy(World w, Good g, int units, int keep)
    {
        if (units <= 0) return;
        var m = w.Docked!.Market;
        while (units > 0 && m.QuoteBuy(g, units) > w.Player.Gold - keep) units--;
        if (units > 0) w.Apply(new PortCommand(PortAction.Buy, g, units));
    }
}
