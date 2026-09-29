namespace LastTide.Sim;

public sealed partial class World
{
    public const double NearRadius = 1500;       // ships closer than this get full physics
    public const double FortRange = 200, FortReload = 10, FortDamage = 8;
    /// <summary>Guns a merchantman carries (two a side): enough to answer an attacker as she runs, never to go looking for a fight.</summary>
    public const int MerchantGuns = 4;

    public Preset Preset { get; set; } = Preset.RoughSeas;
    public double ThreatNow => Threat.Of(Preset, DaysSurvived);
    public int ThreatTier => Threat.Tier(ThreatNow);
    public Director Director { get; } = new();
    /// <summary>Off for weather tests and quiet screenshots: no hunters are bought.</summary>
    public bool DirectorEnabled { get; set; } = true;
    readonly Dictionary<int, double> fortClocks = new();
    public int HuntersAlive => Others.Count(s => s.Ai?.Role is Role.Hunter or Role.Privateer);

    /// <summary>
    /// A ship's name: hers is the one the player gave; every other is drawn from the run's seed and the ship's id, so it
    /// holds across saves and replays and costs the run's random stream nothing.
    /// </summary>
    public string NameOf(Ship s) =>
        s.IsPlayer ? Player.ShipName : Names.Ship(new Rng((ulong)(uint)Seed * 0x9E3779B97F4A7C15UL + (ulong)(uint)s.Id * 0xBF58476D1CE4E5B9UL + 7));

    // ---- Hostility (GDD §8) ----
    public bool PlayerHostileTo(Faction f) => Player.Rep(f) <= -20;

    /// <summary>Would <paramref name="a"/> attack <paramref name="b"/> on sight?</summary>
    public bool Hostile(Ship a, Ship b)
    {
        if (a == b || a.Sunk || b.Sunk) return false;
        var ra = a.Ai?.Role ?? Role.Player;
        var rb = b.Ai?.Role ?? Role.Player;
        if (ra is Role.Hunter or Role.Privateer) return rb == Role.Player;
        if (rb is Role.Hunter or Role.Privateer) return ra == Role.Player || a.Faction != b.Faction && a.Faction == Faction.Crown;
        if (ra == Role.Player) return PlayerHostileTo(b.Faction) || b.PlayerHostile;
        if (rb == Role.Player) return PlayerHostileTo(a.Faction) || a.PlayerHostile;
        if (ra == Role.Merchant) return false;
        if (ra == Role.Raider) return b.Faction != Faction.Brethren;
        if (ra == Role.Patrol) return b.Faction == Faction.Brethren;
        return false;
    }

    /// <summary>
    /// The nearest ship hostile to <paramref name="ship"/> within <paramref name="radius"/>. The player is seen as far as
    /// she can be spotted instead (GDD §9: a lit lantern at night is a beacon at 650 m, doused a shadow at 200 m).
    /// </summary>
    public Ship? NearestThreatTo(Ship ship, double radius)
    {
        Ship? best = null;
        double bestD = double.MaxValue;
        double playerRange = -1;
        foreach (var other in AllShips)
        {
            if (!Hostile(other, ship)) continue;
            double d = other.Pos.DistanceTo(ship.Pos);
            double range = other.IsPlayer ? (playerRange = playerRange < 0 ? DetectRangeOfPlayer() : playerRange) : radius;
            if (d < range && d < bestD)
            {
                bestD = d;
                best = other;
            }
        }
        return best;
    }

    /// <summary>The nearest ship <paramref name="ship"/> would attack: others within <paramref name="radius"/>, the player as far as she can be spotted.</summary>
    public Ship? NearestEnemyOf(Ship ship, double radius)
    {
        Ship? best = null;
        double bestD = double.MaxValue;
        double playerRange = -1;
        foreach (var other in AllShips)
        {
            if (!Hostile(ship, other)) continue;
            double d = other.Pos.DistanceTo(ship.Pos);
            double range = other.IsPlayer ? (playerRange = playerRange < 0 ? DetectRangeOfPlayer() : playerRange) : radius;
            if (d < range && d < bestD)
            {
                bestD = d;
                best = other;
            }
        }
        return best;
    }

    /// <summary>
    /// How far an AI lookout sees at a point: 500 m by day, 250 m at night (their lanterns burn), cut by fog, rain and
    /// ash as the player's is. Not the player's own vision, which her officers, parts and lantern change (audit C-08).
    /// </summary>
    public double SightAt(Vec2 p)
    {
        var c = ConditionsAt(p);
        double v = c.Night ? 250 : 500;
        if (c.Dusk > 0 && !c.Night) v = 500 - 250 * c.Dusk;
        if (c.Fog > 0) v = Math.Min(v, 500 - 320 * c.Fog);
        if (c.Rain > 0) v *= 1 - 0.4 * c.Rain;
        if (c.Ash > 0) v *= 1 - 0.4 * c.Ash;
        return Math.Max(90, v);
    }

    /// <summary>A raider's prey: the nearest ship it would attack — the player when hostile and spotted, a merchant or a patrol.</summary>
    public Ship? NearestPreyOf(Ship raider, double radius) => NearestEnemyOf(raider, radius);

    // ---- Population ----
    void PopulateFleet()
    {
        foreach (var port in Map.Ports)
        {
            if (port.Secret) continue;
            // Merchants sail from free ports and colonies; havens breed raiders; colonies keep a patrol.
            if (port.Faction != Faction.Brethren && Rng.NextDouble() < 0.7)
                SpawnAt(port, Role.Merchant, Faction.FreeTraders, MerchantHull(Rng));
            if (port.Faction == Faction.Crown && Rng.NextDouble() < 0.6)
                SpawnAt(port, Role.Patrol, Faction.Crown, Rng.NextDouble() < 0.7 ? "cutter" : "corvette");
            if (port.Faction == Faction.Brethren)
                SpawnAt(port, Role.Raider, Faction.Brethren, Rng.NextDouble() < 0.6 ? "sloop" : Rng.NextDouble() < 0.5 ? "brig" : "xebec");
        }
    }

    static string MerchantHull(Rng rng) => rng.NextDouble() switch { < 0.35 => "cutter", < 0.7 => "schooner", < 0.9 => "fluyt", _ => "barque" };

    Ship SpawnAt(Port port, Role role, Faction faction, string hullId)
    {
        var pos = SeaPointNear(port.Harbor, 40, port.RingRadius + 80);
        var ship = Spawn(hullId, pos, Rng.Range(-Math.PI, Math.PI), faction, CaptainFor(role));
        ship.Ai = new AiState { Role = role, HomePort = port.Id, Wait = Rng.Range(2, 30) };
        if (role == Role.Merchant)
        {
            ship.Cannons = Math.Min(ship.Cannons, MerchantGuns);
            ship.Order = CrewOrder.MakeSail;
        }
        return ship;
    }

    /// <summary>
    /// A ship of ordinary traffic placed at <paramref name="pos"/> (the `--fleet=N` crowd, audit R-08): a merchant, patrol
    /// or raider with its own captain and a home port of its kind (the nearest one), as <see cref="PopulateFleet"/> makes.
    /// </summary>
    public Ship SpawnTraffic(Role role, Vec2 pos)
    {
        Faction faction = role switch { Role.Patrol => Faction.Crown, Role.Raider => Faction.Brethren, _ => Faction.FreeTraders };
        bool Fits(Port p) => !p.Secret && (role == Role.Merchant ? p.Faction != Faction.Brethren : p.Faction == faction);
        var home = Map.Ports.Where(Fits).OrderBy(p => p.Harbor.DistanceTo(pos)).FirstOrDefault() ?? Map.StartPort;
        string hull = role switch
        {
            Role.Merchant => MerchantHull(Rng),
            Role.Patrol => Rng.NextDouble() < 0.7 ? "cutter" : "corvette",
            _ => Rng.NextDouble() < 0.6 ? "sloop" : Rng.NextDouble() < 0.5 ? "brig" : "xebec",
        };
        var ship = Spawn(hull, pos, Rng.Range(-Math.PI, Math.PI), faction, CaptainFor(role));
        ship.Ai = new AiState { Role = role, HomePort = home.Id, Wait = Rng.Range(2, 30) };
        if (role == Role.Merchant)
        {
            ship.Cannons = Math.Min(ship.Cannons, MerchantGuns);
            ship.Order = CrewOrder.MakeSail;
        }
        return ship;
    }

    public static Captain CaptainFor(Role role) => role switch
    {
        Role.Merchant => new MerchantCaptain(),
        Role.Patrol => new PatrolCaptain(),
        Role.Raider => new RaiderCaptain(),
        Role.Hunter or Role.Privateer => new HunterCaptain(),
        _ => new SparringCaptain(),
    };

    /// <summary>A random sea point in a ring around a centre; falls back to the centre.</summary>
    public Vec2 SeaPointNear(Vec2 centre, double minR, double maxR)
    {
        for (int i = 0; i < 40; i++)
        {
            var p = centre + Vec2.FromAngle(Rng.Range(0, Angles.Tau)) * Rng.Range(minR, maxR);
            if (Map.InBounds(p) && Math.Abs(p.X) < Map.HalfW - 60 && Math.Abs(p.Y) < Map.HalfH - 60 && Map.Nav.IsSea(p) && !Map.IslandsNear(p, 25).Any(i => i.Closest(p).Dist < 25))
                return p;
        }
        return centre;
    }

    /// <summary>A point on the lanes: another port's harbour within reach, or open sea near home.</summary>
    public Vec2 LaneWaypointNear(Port home, double radius)
    {
        var ports = Map.Ports.Where(p => p != home && !p.Secret && p.Harbor.DistanceTo(home.Harbor) < radius).ToList();
        if (ports.Count > 0 && Rng.NextDouble() < 0.7)
        {
            var target = ports[Rng.Next(ports.Count)];
            return SeaPointNear(target.Harbor, target.RingRadius + 60, target.RingRadius + 300);
        }
        return SeaPointNear(home.Harbor, 300, radius * 0.6);
    }

    /// <summary>A merchant picks a good its port makes and a port that wants it, loads up and lays a course.</summary>
    readonly Dictionary<(int, int), List<Vec2>> routeCache = new();
    int lastHunters;

    public void PlanVoyage(Ship ship)
    {
        var ai = ship.Ai!;
        var home = Map.Ports[ai.HomePort];
        var candidates = new List<(Port port, Good good)>();
        foreach (var good in home.Produces)
            foreach (var port in Map.Ports)
            {
                if (port == home || port.Secret || port.Produces.Contains(good)) continue;
                double d = port.Harbor.DistanceTo(home.Harbor);
                if (d < MerchantRun && (port.Consumes.Contains(good) || Rng.NextDouble() < 0.25))
                    candidates.Add((port, good));
            }
        if (candidates.Count == 0)
        {
            // Nothing to sell: sail to a neighbour anyway and try there.
            var ports = Map.Ports.Where(p => p != home && !p.Secret && p.Harbor.DistanceTo(home.Harbor) < MerchantRun).ToList();
            if (ports.Count == 0) return;
            candidates.Add((ports[Rng.Next(ports.Count)], home.Produces.Count > 0 ? home.Produces[0] : Good.Provisions));
        }
        var pick = candidates[Rng.Next(candidates.Count)];
        var path = new List<Vec2>(Lane(home, pick.port));
        if (path.Count == 0) return;
        int units = Math.Min((int)Math.Floor(home.Market.Stock[(int)pick.good] * 0.3), Math.Min(30, ship.Hull.Cargo));
        units = Math.Max(0, units);
        home.Market.TakeStock(pick.good, units);
        RecordNews(ship, home);   // she knows the prices she loaded at
        ai.CargoGood = pick.good;
        ai.CargoUnits = units;
        ai.DestPort = pick.port.Id;
        ai.Path = path;
        ai.PathIndex = 0;
        ai.Repath = 45;
    }

    // ---- Lanes (audit R-09) ----
    // A* per port pair ran inside a tick the first time a merchant took each lane (3 ms on average, 24 ms at worst, some
    // 500 lanes). Now each harbour gets one breadth-first sea-distance field over the water round it (LaneReach), warmed
    // one a tick from the start of a voyage with traffic, and a lane is the walk down that field from the other harbour,
    // string-pulled. Lanes depend on the map alone, so when they are computed changes nothing (save/load and replays agree).
    /// <summary>The longest merchant run, harbour to harbour as the crow flies (2.2 km on v6's chart, stretched with the sea).</summary>
    public const double MerchantRun = 2200 * Map.Stretch;
    /// <summary>How far round a destination its sea-distance field reaches: past the longest merchant run with room for detours.</summary>
    public const double LaneReach = 3600 * Map.Stretch;
    int fieldsWarmed;

    NavGrid.LocalField HarbourField(Port port) => Map.Nav.LocalDistances(port.Harbor, LaneReach);

    /// <summary>
    /// One harbour a tick: its field is traced once for every lane into it from a port a merchant could sail from, then
    /// dropped. A field per harbour over the whole chart would be ~1.5 MB each.
    /// </summary>
    void WarmLanes()
    {
        while (fieldsWarmed < Map.Ports.Count && Map.Ports[fieldsWarmed].Secret) fieldsWarmed++;
        if (fieldsWarmed >= Map.Ports.Count) return;
        var to = Map.Ports[fieldsWarmed++];
        NavGrid.LocalField? field = null;
        foreach (var from in Map.Ports)
        {
            if (from == to || from.Secret || routeCache.ContainsKey((from.Id, to.Id)) || from.Harbor.DistanceTo(to.Harbor) > MerchantRun) continue;
            field ??= HarbourField(to);
            routeCache[(from.Id, to.Id)] = Trace(from, to, field.Value);
        }
    }

    /// <summary>The lane from one harbour to another: down the destination's field, string-pulled; empty if no sea route.</summary>
    public List<Vec2> Lane(Port from, Port to)
    {
        if (!routeCache.TryGetValue((from.Id, to.Id), out var lane))
            routeCache[(from.Id, to.Id)] = lane = Trace(from, to, HarbourField(to));
        return lane;
    }

    List<Vec2> Trace(Port from, Port to, NavGrid.LocalField field)
    {
        var nav = Map.Nav;
        int x = NavGrid.ToX(from.Harbor.X), y = NavGrid.ToY(from.Harbor.Y);
        if (nav.IsLand(x, y)) (x, y) = nav.NearestSea(x, y);
        var lane = new List<Vec2>();
        if (field.At(x, y) >= 0)
        {
            var pts = new List<Vec2> { from.Harbor };
            for (int guard = 0; guard < field.W * field.H && field.At(x, y) > 0; guard++)
            {
                int bx = -1, by = -1;
                float bd = field.At(x, y);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx == 0 && dy == 0) || nav.IsLand(nx, ny)) continue;
                        if (dx != 0 && dy != 0 && nav.IsLand(x + dx, y) && nav.IsLand(x, y + dy)) continue;
                        float d = field.At(nx, ny);
                        if (d >= 0 && d < bd) { bd = d; bx = nx; by = ny; }
                    }
                if (bx < 0) break;
                // A diagonal past one land cell would graze its corner: go round through the sea cell beside it.
                if (bx != x && by != y)
                {
                    if (nav.IsLand(bx, y)) pts.Add(NavGrid.Centre(x, by));
                    else if (nav.IsLand(x, by)) pts.Add(NavGrid.Centre(bx, y));
                }
                x = bx;
                y = by;
                pts.Add(NavGrid.Centre(x, y));
            }
            pts.Add(to.Harbor);
            lane = Pull(nav, pts);
        }
        return lane;
    }

    /// <summary>
    /// String-pulls a walk of cell centres: from each corner, the farthest point still in a clear line, found by doubling
    /// the reach and then halving back (a few line checks a corner, where <see cref="Pathing.Smooth"/> tries every point).
    /// </summary>
    static List<Vec2> Pull(NavGrid nav, List<Vec2> pts)
    {
        var result = new List<Vec2> { pts[0] };
        int i = 0, last = pts.Count - 1;
        while (i < last)
        {
            int good = i + 1, bad = -1, reach = 2;
            while (good < last)
            {
                int j = Math.Min(last, i + reach);
                if (Pathing.Clear(nav, pts[i], pts[j])) { good = j; reach *= 2; }
                else { bad = j; break; }
            }
            while (bad > good + 1)
            {
                int mid = (good + bad) / 2;
                if (Pathing.Clear(nav, pts[i], pts[mid])) good = mid; else bad = mid;
            }
            result.Add(pts[good]);
            i = good;
        }
        return result;
    }

    public int MerchantArrivals { get; private set; }

    public void ArriveMerchant(Ship ship)
    {
        var ai = ship.Ai!;
        if (ai.DestPort < 0) return;
        var dest = Map.Ports[ai.DestPort];
        MerchantArrivals++;
        if (ai.CargoUnits > 0) dest.Market.AddStock(ai.CargoGood, ai.CargoUnits);
        ai.CargoUnits = 0;
        RecordNews(ship, dest);   // and the prices she sold at
        ai.HomePort = dest.Id;
        ai.DestPort = -1;
        ai.Path.Clear();
        ai.Wait = Rng.Range(20, 45);
        Recover(ship);
    }

    /// <summary>In port an AI crew mends and reloads; the sea is not so kind to the player.</summary>
    public void Recover(Ship ship)
    {
        ship.HullHp = Math.Min(ship.MaxHp, ship.HullHp + 20 * Dt);
        ship.Water = Math.Max(0, ship.Water - 5 * Dt);
        ship.Leaks = 0;
        ship.Timber = 4;
    }

    // ---- The fleet's tick: near ships get physics, far ships move along their lanes ----
    void FleetTick()
    {
        if (fieldsWarmed < Map.Ports.Count && Others.Count > 0) WarmLanes();
        // The director.
        int hunters = HuntersAlive;
        if (hunters < lastHunters) Director.QuietFor = Director.QuietAfterHunter;   // one gave up or went down: a breather
        lastHunters = hunters;
        // Raiders press harder at night and in their own waters (the Corsair Keys).
        double press = (IsNight ? 1.5 : 1) * (Map.InBounds(Ship.Pos) ? RegionDef.Of(Map.RegionAt(Ship.Pos).Type).Raiders : 1);
        // Beyond the chart the sea is the only hunter: no pirate follows her out there (other ships keep to the chart).
        bool beyond = Map.BeyondEdge(Ship.Pos) > 0;
        var card = Docked == null && DirectorEnabled && !beyond ? Director.Tick(Dt, ThreatNow, PlayerHostileTo(Faction.Crown), hunters, Rng, press) : null;
        if (card != null) SpawnCard(card);

        for (int i = 0; i < Others.Count; i++)
        {
            var ship = Others[i];
            if (ship.Sunk) continue;
            var captain = Captains.TryGetValue(ship.Id, out var c) ? c : null;
            var input = captain?.Tick(this, ship) ?? new ShipInput(0, 0);
            double d = ship.Pos.DistanceTo(Ship.Pos);
            if (d <= NearRadius)
            {
                var w = WindAt(ship.Pos);
                Swells(ship);
                ship.Step(Dt, w, input, Map.IslandsAround(ship.Pos));
                KeepShipOnTheChart(ship);
                if (input.FirePort) Fire(ship, Side.Port);
                if (input.FireStarboard) Fire(ship, Side.Starboard);
            }
            else
                CoarseStep(ship, input);
        }
        FortsFire();
    }

    /// <summary>Distant traffic: slide along the ordered course at the polar speed, no physics.</summary>
    void CoarseStep(Ship ship, ShipInput input)
    {
        var w = WindAt(ship.Pos);
        ship.SailTarget = Math.Clamp(ship.SailTarget + input.SailDelta, 0, 3);
        ship.SailFraction = Tuning.SailFraction[ship.SailTarget];
        // Turn toward the helm's wish at a coarse rate and move.
        double turn = input.Rudder * ship.Hull.TurnRate * 0.6 * Dt;
        ship.Heading = Angles.Wrap(ship.Heading + turn);
        double off = Angles.Deg(Math.Abs(Angles.Wrap(w.From - ship.Heading)));
        // SpeedMult: the Sargasso weed slows far traffic as it slows near ships (audit X8).
        double speed = ship.Hull.TopSpeed * Polar.Fraction(ship.Hull.Rig, off, ship.Hull.PointDeg) * ship.SailFraction * Math.Clamp(Math.Sqrt(w.Speed / Tuning.StandardWind), 0.45, 1.3) * ship.SpeedMult;
        // Land cells of the nav grid stop her; at a cell's corner she slides along its edge instead of sticking,
        // and one already on a land cell may sail off it (audit C-01: distant traffic froze on the first corner).
        var step = ship.Forward * (speed * Dt);
        bool onLand = Map.Nav.IsLand(ship.Pos);
        bool Free(Vec2 p) => Map.InBounds(p) && (onLand || Map.Nav.IsSea(p));
        var next = ship.Pos + step;
        if (!Free(next))
        {
            var alongX = ship.Pos + new Vec2(step.X, 0);
            var alongY = ship.Pos + new Vec2(0, step.Y);
            bool xFirst = Math.Abs(step.X) >= Math.Abs(step.Y);
            next = Free(xFirst ? alongX : alongY) ? (xFirst ? alongX : alongY) : Free(xFirst ? alongY : alongX) ? (xFirst ? alongY : alongX) : ship.Pos;
        }
        ship.Vel = (next - ship.Pos) / Dt;
        ship.Pos = next;
        ship.SetCoarseWind(w, off);
        ship.Reload[0] = Math.Max(0, ship.Reload[0] - Dt);
        ship.Reload[1] = Math.Max(0, ship.Reload[1] - Dt);
        if (ship.Reload[0] <= 0) ship.Loaded[0] = true;
        if (ship.Reload[1] <= 0) ship.Loaded[1] = true;
    }

    void KeepShipOnTheChart(Ship ship)
    {
        double wall = 30;
        double x = Math.Clamp(ship.Pos.X, -Map.HalfW + wall, Map.HalfW - wall);
        double y = Math.Clamp(ship.Pos.Y, -Map.HalfH + wall, Map.HalfH - wall);
        if (x != ship.Pos.X || y != ship.Pos.Y)
        {
            ship.Pos = new Vec2(x, y);
            ship.Vel = Vec2.Zero;
        }
    }

    /// <summary>Harbour forts fire on any hostile hull inside their ring (GDD §8).</summary>
    void FortsFire()
    {
        foreach (var port in Map.Ports)
        {
            if (!port.Fort) continue;
            fortClocks.TryGetValue(port.Id, out double clock);
            clock -= Dt;
            if (clock > 0)
            {
                fortClocks[port.Id] = clock;
                continue;
            }
            Ship? target = null;
            foreach (var ship in AllShips)
            {
                if (ship.Sunk || !port.InHarbor(ship.Pos)) continue;
                bool hostile = ship.IsPlayer ? PlayerHostileTo(port.Faction) || Player.Rep(port.Faction) <= -50 : ship.Faction != port.Faction && (ship.Ai?.Role is Role.Raider or Role.Hunter or Role.Privateer);
                if (hostile) { target = ship; break; }
            }
            if (target == null) { fortClocks[port.Id] = 0; continue; }
            fortClocks[port.Id] = FortReload;
            // The battery stands on the harbour side of the coast: the balls leave from just offshore, toward the ring,
            // so they do not burst on the fort's own island now that land stops shot (P-01).
            var muzzle = port.Pos + (port.Harbor - port.Pos).Normalized * 15;
            var dir = (target.Pos - muzzle).Normalized;
            for (int k = 0; k < 3; k++)
            {
                double spread = Angles.Rad(Rng.Range(-4, 4));
                Balls.Add(new Cannonball { Pos = muzzle, Vel = dir.Rotated(spread) * BallSpeed, Life = FortRange / BallSpeed, Shooter = null, Damage = FortDamage * Rng.Range(0.8, 1.2), Delay = k * 0.05 });
            }
            Events.Add(new CombatEvent(CombatEventType.Fire, port.Pos, -2, 3));
        }
    }

    /// <summary>Spends a director card: ships appear just beyond the player's sight and come for her.</summary>
    void SpawnCard(SpawnCard card)
    {
        var centre = SeaPointNear(Ship.Pos, VisionRadius + 150, VisionRadius + 400);
        if (centre == Ship.Pos)
        {
            // Nowhere beyond her sight to put them: the card goes back in the deck (audit C-11).
            Director.Credits += card.Cost;
            Director.Spawned--;
            return;
        }
        foreach (var hullId in card.Hulls)
            SpawnHunter(hullId, SeaPointNear(centre, 0, 60), card.Faction, card.Role);
        Notices.Enqueue(card.Role == Role.Privateer ? "NOTICE_HUNTERS" : "NOTICE_SAIL_SIGHTED");
    }

    /// <summary>A hunter scaled to the current Threat, already under full sail and pointed at the player.</summary>
    public Ship SpawnHunter(string hullId, Vec2 pos, Faction faction, Role role = Role.Hunter)
    {
        double scale = Threat.EnemyScale(ThreatNow);
        var ship = Spawn(hullId, pos, (Ship.Pos - pos).Angle, faction, new HunterCaptain());
        ship.Ai = new AiState { Role = role, Scale = scale };
        ship.DamageMult = scale;   // hull and damage (GDD §11): MaxHp scales with it, so leaks count from the scaled hull
        ship.HullHp = ship.MaxHp;
        // Early hunters are lone pirates, not full crews: half guns and hands at Threat 1, full strength by 2.5.
        double strength = Math.Clamp((ThreatNow - 1) / 1.5, 0.5, 1);
        ship.Cannons = Math.Max(2, (int)Math.Round(ship.Cannons * strength));
        ship.Crew = Math.Max(4, (int)Math.Round(ship.Crew * strength));
        ship.SailTarget = 3;
        ship.SailFraction = 1;
        return ship;
    }
}
