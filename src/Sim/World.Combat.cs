namespace LastTide.Sim;

public sealed partial class World
{
    public const double BallSpeed = 110;
    public const double CollectRadius = 12;

    public List<Ship> Others { get; } = new();
    public Dictionary<int, Captain> Captains { get; } = new();
    public List<Cannonball> Balls { get; } = new();
    public List<Flotsam> Flotsam { get; } = new();
    /// <summary>What happened this tick, for the view's effects; cleared at the start of every tick.</summary>
    public List<CombatEvent> Events { get; } = new();
    public bool RunOver { get; private set; }
    public string CauseOfSinking { get; private set; } = "";
    public bool DockedByAHair { get; private set; }
    /// <summary>Ports that have already rescued a foundering ship this voyage (one mercy each).</summary>
    public HashSet<int> RescuedAt { get; } = new();
    bool rescueRefusedNoticed;
    double shotAt = -1;   // when a ball last struck her (forts and beasts leave no LastHitBy); not saved: a hint for the logbook
    double rammedAt = -1; // when a hull last struck her (P-07); not saved, likewise
    int nextShipId = 1;
    internal int NextShipId => nextShipId;

    public IEnumerable<Ship> AllShips
    {
        get
        {
            yield return Ship;
            foreach (var s in Others) yield return s;
        }
    }

    public Ship Spawn(string hullId, Vec2 pos, double heading, Faction faction, Captain? captain, int crew = -1, int cannons = -1)
    {
        var hull = Hulls.Get(hullId);
        var ship = new Ship(hull, pos, heading)
        {
            Id = nextShipId++, Faction = faction,
            Crew = crew >= 0 ? crew : hull.CrewMax * 2 / 3,
            Cannons = cannons >= 0 ? cannons : hull.GunsPerSide * 2,
            Order = CrewOrder.Battle,
        };
        Others.Add(ship);
        if (captain != null) Captains[ship.Id] = captain;
        return ship;
    }

    /// <summary>A practice sloop abeam of the player (`--sparring`).</summary>
    public Ship SpawnSparring()
    {
        var pos = Ship.Pos + Ship.Right * 150 + Ship.Forward * 40;
        var s = Spawn("sloop", pos, Ship.Heading, Faction.Brethren, new SparringCaptain(), crew: 8, cannons: 4);
        s.Ai = new AiState { Role = Role.Sparring };
        return s;
    }

    /// <summary>Fires every loaded gun on one side; the player pays a munition a gun (GDD §8).</summary>
    public bool Fire(Ship ship, Side side)
    {
        if (!ship.CanFire(side)) return false;
        int guns = ship.GunsOn(side);
        if (ship.IsPlayer)
        {
            int have = Player.Cargo[(int)Good.Munitions];
            guns = Math.Min(guns, have);
            if (guns <= 0) return false;   // no shot (and never a negative broadside: audit C-24)
            Player.Remove(Good.Munitions, guns);   // cost basis shrinks with the shot fired (audit R-01)
        }
        var dir = side == Side.Starboard ? ship.Right : -ship.Right;
        double along = ship.Hull.Length * 0.6;
        for (int i = 0; i < guns; i++)
        {
            double spread = Angles.Rad(Rng.Range(-3, 3));
            double x = guns == 1 ? 0 : -along / 2 + along * i / (guns - 1);
            var origin = ship.Pos + ship.Forward * x + dir * (ship.Hull.Beam * 0.5);
            Balls.Add(new Cannonball
            {
                Pos = origin, Vel = dir.Rotated(spread) * BallSpeed + ship.Vel * 0.25,
                Life = ship.Range / BallSpeed, Shooter = ship, Damage = ship.BallDamage * Rng.Range(0.8, 1.2), Delay = i * 0.05,
            });
        }
        ship.Loaded[(int)side] = false;
        ship.Reload[(int)side] = ship.BaseReload;   // drained at the manning of each moment (Ship.DamageTick, audit R-01)
        Events.Add(new CombatEvent(CombatEventType.Fire, ship.Pos + dir * ship.Hull.Beam * 0.5, ship.Id, guns));
        return true;
    }

    void CombatTick(ShipInput playerInput)
    {
        if (playerInput.FirePort) Fire(Ship, Side.Port);
        if (playerInput.FireStarboard) Fire(Ship, Side.Starboard);

        FleetTick();

        // Balls fly and strike.
        for (int i = Balls.Count - 1; i >= 0; i--)
        {
            var b = Balls[i];
            if (b.Delay > 0)
            {
                b.Delay -= Dt;
                continue;
            }
            var next = b.Pos + b.Vel * Dt;
            if (StrikeMonster(b.Pos, next, b.Damage, b.Shooter))
            {
                Balls.RemoveAt(i);
                continue;
            }
            Ship? struck = null;
            foreach (var target in AllShips)
            {
                if (target == b.Shooter || target.Sunk) continue;
                double half = Math.Max(0, target.Hull.Length * 0.5 - target.Hull.Beam * 0.5);
                var bow = target.Pos + target.Forward * half;
                var stern = target.Pos - target.Forward * half;
                if (Geometry.SegmentDistance(b.Pos, next, bow, stern) <= target.Hull.Beam * 0.5 + 1)
                {
                    struck = target;
                    break;
                }
            }
            if (struck != null)
            {
                struck.Hit(b.Damage, b.Shooter, Rng);
                if (struck.IsPlayer)
                {
                    // Forts and beasts fire balls with no ship behind them: remember who, for the logbook (audit C-10).
                    shotAt = Time;
                    if (b.From != MonsterType.None) { LastMonsterHit = b.From; MonsterHitTime = Time; }
                }
                Events.Add(new CombatEvent(CombatEventType.Hit, next, struck.Id, b.Damage, By: b.Shooter?.Id ?? -1));
                if (b.Shooter?.IsPlayer == true) Draw(struck);
                Balls.RemoveAt(i);
                continue;
            }
            // Shot does not pass through islands: it bursts where it meets the shore, so land gives cover (P-01).
            if (LandAlong(b.Pos, next) is { } shore)
            {
                Events.Add(new CombatEvent(CombatEventType.Splash, shore, -1, 0));
                Balls.RemoveAt(i);
                continue;
            }
            // Nor through drift ice: a floe stops a ball as a shore does, so the Ice Reach's floes give cover too.
            if (IceAlong(b.Pos, next) is { } ice)
            {
                Events.Add(new CombatEvent(CombatEventType.Splash, ice, -1, 0));
                Balls.RemoveAt(i);
                continue;
            }
            b.Pos = next;
            b.Life -= Dt;
            if (b.Life <= 0)
            {
                Events.Add(new CombatEvent(CombatEventType.Splash, b.Pos, -1, 0));
                Balls.RemoveAt(i);
            }
        }

        // Ramming: every pair of hulls.
        var ships = AllShips.Where(s => !s.Sunk && s.Pos.DistanceTo(Ship.Pos) <= NearRadius).ToList();
        for (int i = 0; i < ships.Count; i++)
            for (int j = i + 1; j < ships.Count; j++)
                if (ships[i].Pos.DistanceTo(ships[j].Pos) < ships[i].Hull.Length + ships[j].Hull.Length)
                    Ram(ships[i], ships[j]);

        // Damage control aboard every ship.
        foreach (var ship in AllShips)
        {
            if (ship.Sunk) { if (!ship.IsPlayer && Others.Contains(ship)) Sink(ship, quiet: true); continue; }
            ship.DamageTick(Dt, ship.IsPlayer ? TakePlayerTimber : () => TakeTimber(ship));
            if (ship.Sunk)
                Sink(ship);
        }

        // A foundering player who reaches an open harbour is saved (GDD §8) — once per harbour per voyage
        // (GDD §19): a port that has already hauled her in lets her sink at its mouth the second time.
        if (Ship.Foundering && !Ship.Sunk && HarborHere is { } port && IsOpen(port))
        {
            if (RescuedAt.Contains(port.Id))
            {
                if (!rescueRefusedNoticed) { Notices.Enqueue("NOTICE_RESCUE_SPENT"); rescueRefusedNoticed = true; }
            }
            else
            {
            RescuedAt.Add(port.Id);
            rescueRefusedNoticed = false;
            Ship.Foundering = false;
            Ship.HullHp = Math.Max(1, Ship.HullHp);
            Ship.Water = 0;
            Ship.Leaks = 0;
            DockedByAHair = true;
            Notices.Enqueue("NOTICE_BY_A_HAIR");
            Events.Add(new CombatEvent(CombatEventType.Rescued, Ship.Pos, Ship.Id, 0));
            Execute(new PortCommand(PortAction.Dock));
            }
        }

        // Flotsam drifts, sinks, and is picked up anywhere along her side (the hull's capsule, not its centre: audit C-19).
        var wind = Ship.LocalWind;
        double keel = Math.Max(0, Ship.Hull.Length * 0.5 - Ship.Hull.Beam * 0.5);
        Vec2 fore = Ship.Pos + Ship.Forward * keel, aft = Ship.Pos - Ship.Forward * keel;
        for (int i = Flotsam.Count - 1; i >= 0; i--)
        {
            var f = Flotsam[i];
            f.Pos += wind.Vector * 0.15 * Dt;
            f.Life -= Dt;
            f.Bob += Dt;
            if (f.Life <= 0)
            {
                Flotsam.RemoveAt(i);
                continue;
            }
            if (!Ship.Sunk && Geometry.SegmentDistance(f.Pos, f.Pos, fore, aft) <= CollectRadius + Ship.Hull.Beam * 0.5)
            {
                if (f.Gold > 0)
                {
                    Player.Gold += f.Gold;
                    Stats.GoldEarned += f.Gold;
                    Events.Add(new CombatEvent(CombatEventType.Collect, f.Pos, -1, f.Gold));
                }
                if (f.Good is { } g && f.Units > 0)
                {
                    double room = (Ship.CargoCapacity - Player.SlotsUsed) / Goods.Of(g).SlotsPerUnit;
                    int take = Math.Min(f.Units, (int)Math.Floor(room + 1e-9));
                    if (take > 0)
                    {
                        Player.Cargo[(int)g] += take;
                        f.Units -= take;
                        Events.Add(new CombatEvent(CombatEventType.Collect, f.Pos, -1, take, Good: (int)g));   // what came aboard, for the view
                    }
                }
                if (f.Gold > 0 || f.Units <= 0) Flotsam.RemoveAt(i);
            }
        }
        Others.RemoveAll(s => s.Sunk);
    }

    /// <summary>
    /// The first point where the straight run from <paramref name="a"/> to <paramref name="b"/> meets an island (a itself
    /// when it starts ashore), or null over open water. Balls stop there; captains hold fire through it (P-01).
    /// </summary>
    public Vec2? LandAlong(Vec2 a, Vec2 b)
    {
        var mid = (a + b) * 0.5;
        double half = a.DistanceTo(b) * 0.5, best = double.MaxValue;
        Vec2? hit = null;
        foreach (var island in Map.IslandsNear(mid, half))
        {
            if (island.Contains(a)) return a;
            var pts = island.Points;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            {
                double t = Geometry.Crossing(a, b, pts[j], pts[i]);
                if (t >= 0 && t < best)
                {
                    best = t;
                    hit = Vec2.Lerp(a, b, t);
                }
            }
        }
        return hit;
    }

    /// <summary>An AI crew's planks; none left, no patching (and no debt: audit C-07).</summary>
    static bool TakeTimber(Ship ship)
    {
        if (ship.Timber <= 0) return false;
        ship.Timber--;
        return true;
    }

    bool TakePlayerTimber()
    {
        if (Player.Cargo[(int)Good.Timber] <= 0) return false;
        Player.Remove(Good.Timber, 1);   // cost basis shrinks with the plank used (audit R-01)
        return true;
    }

    /// <summary>The player struck a ship: its faction remembers (−10 once per ship, GDD §8).</summary>
    void Draw(Ship target)
    {
        if (target.PlayerHostile) return;
        target.PlayerHostile = true;
        Player.Reputation[(int)target.Faction] = Math.Max(-100, Player.Reputation[(int)target.Faction] - 10);
    }

    static Faction? Rival(Faction f) => f switch { Faction.Crown => Faction.Brethren, Faction.Brethren => Faction.Crown, _ => null };

    void Sink(Ship ship, bool quiet = false)
    {
        if (quiet)
        {
            Captains.Remove(ship.Id);
            return;   // slipped away, not sunk
        }
        Events.Add(new CombatEvent(CombatEventType.Sink, ship.Pos, ship.Id, ship.Hull.Length));
        if (ship.IsPlayer)
        {
            RunOver = true;
            CauseOfSinking = WhirlpoolHitTime >= 0 && Time - WhirlpoolHitTime < 30 && WhirlpoolHitTime >= MonsterHitTime ? "SUNK_WHIRLPOOL"
                : IceHitTime >= 0 && Time - IceHitTime < 60 && IceHitTime >= MonsterHitTime ? "SUNK_ICE"
                : MonsterHitTime >= 0 && Time - MonsterHitTime < 60 ? "SUNK_" + (LastMonsterHit == MonsterType.None ? "ERUPTION" : LastMonsterHit.ToString().ToUpperInvariant())
                : ship.WaterOnlyStand ? "SUNK_WATER"
                : rammedAt >= 0 && rammedAt >= shotAt && Time - rammedAt < 60 ? "SUNK_RAM"
                : ship.LastHitBy == null && !(shotAt >= 0 && Time - shotAt < 60) ? "SUNK_SEA" : "SUNK_GUNS";
            return;
        }
        if (ship.LastHitBy?.IsPlayer == true)
        {
            Stats.ShipsSunk++;
            if (ship.Faction == Faction.Brethren) EarnBounty(ship);   // the Crown pays for pirates, raider or hunter
            if (ship.Ai?.Role is Role.Hunter or Role.Privateer)
                Player.Reputation[(int)Faction.Crown] = Math.Min(100, Player.Reputation[(int)Faction.Crown] + 3);   // a bounty hunter fewer
            else
            {
                Player.Reputation[(int)ship.Faction] = Math.Max(-100, Player.Reputation[(int)ship.Faction] - 30);
                if (Rival(ship.Faction) is { } rival)
                    Player.Reputation[(int)rival] = Math.Min(100, Player.Reputation[(int)rival] + 10);
                else
                    Player.Reputation[(int)Faction.Crown] = Math.Max(-100, Player.Reputation[(int)Faction.Crown] - 10);   // traders lean Crown
            }
        }
        // Spill the cargo and the purse.
        int chests = 1 + Rng.Next(2);
        int purse = (int)(ship.Hull.Cost * 0.02 + 40 + Rng.Next(60));
        for (int i = 0; i < chests; i++)
            Flotsam.Add(new Flotsam { Pos = ship.Pos + Vec2.FromAngle(Rng.Range(0, Angles.Tau)) * Rng.Range(5, 25), Gold = purse / chests, Bob = Rng.Range(0, 6) });
        var region = RegionDef.Of(Map.RegionAt(ship.Pos).Type);
        if (ship.Ai is { CargoUnits: > 0 } cargo)
        {
            int left = cargo.CargoUnits;
            while (left > 0)
            {
                int n = Math.Min(left, 5);
                Flotsam.Add(new Flotsam { Pos = ship.Pos + Vec2.FromAngle(Rng.Range(0, Angles.Tau)) * Rng.Range(5, 30), Good = cargo.CargoGood, Units = n, Bob = Rng.Range(0, 6) });
                left -= n;
            }
        }
        else
        {
            int barrels = 2 + Rng.Next(3);
            for (int i = 0; i < barrels; i++)
            {
                var good = region.Produces[Rng.Next(region.Produces.Length)];
                Flotsam.Add(new Flotsam { Pos = ship.Pos + Vec2.FromAngle(Rng.Range(0, Angles.Tau)) * Rng.Range(5, 30), Good = good, Units = 2 + Rng.Next(4), Bob = Rng.Range(0, 6) });
            }
        }
        Captains.Remove(ship.Id);
    }

    /// <summary>Two hulls touching: push apart, trade momentum, and split the damage by mass, angle and speed (GDD §8).</summary>
    void Ram(Ship a, Ship b)
    {
        double halfA = Math.Max(0, a.Hull.Length * 0.5 - a.Hull.Beam * 0.5);
        double halfB = Math.Max(0, b.Hull.Length * 0.5 - b.Hull.Beam * 0.5);
        var (pa, pb) = Geometry.ClosestPoints(a.Pos + a.Forward * halfA, a.Pos - a.Forward * halfA, b.Pos + b.Forward * halfB, b.Pos - b.Forward * halfB);
        double dist = pa.DistanceTo(pb);
        double radius = (a.Hull.Beam + b.Hull.Beam) * 0.5;
        if (dist >= radius) return;
        var n = dist > 1e-6 ? (pa - pb) / dist : (a.Pos - b.Pos).Normalized;   // from b toward a
        // Two hulls on the very same spot (a pack spawned at one point) have no normal: part them across a's beam, or
        // they never separate (audit X5).
        if (n.LengthSq < 0.5) n = Vec2.FromAngle(a.Heading + Math.PI / 2);
        double pen = radius - dist;
        double ma = a.Hull.Inertia, mb = b.Hull.Inertia;
        a.Pos += n * (pen * mb / (ma + mb));
        b.Pos -= n * (pen * ma / (ma + mb));
        double vrel = (a.Vel - b.Vel).Dot(n);
        if (vrel >= 0) return;   // already separating
        double closing = -vrel;
        double aIn = a.Vel.Dot(-n), bIn = b.Vel.Dot(n);   // how hard each drove into the other, before the bounce
        double e = 0.2;
        double jImpulse = -(1 + e) * vrel / (1 / ma + 1 / mb);
        a.Vel += n * (jImpulse / ma);
        b.Vel -= n * (jImpulse / mb);
        double spin = Math.Min(0.6, closing / 20);
        // Each hull turns with the torque of the push it takes (+n on a at pa, −n on b at pb): a struck bow is shoved
        // away from the rammer (b's sign was flipped, swinging its bow into her: audit C-23).
        a.AngVel += Math.Sign((pa - a.Pos).Cross(n)) * spin * 0.5;
        b.AngVel += Math.Sign((pb - b.Pos).Cross(-n)) * spin * 0.5;
        if (a.RamCooldown > 0 || b.RamCooldown > 0 || closing < 0.5) return;
        a.RamCooldown = b.RamCooldown = 0.6;

        // Who hit whom with what: a bow driving along the normal is a ram; a flank taking it is worse off.
        bool bowA = (pa - a.Pos).Dot(a.Forward) > a.Hull.Length * 0.3 && a.Forward.Dot(-n) > 0.6;
        bool bowB = (pb - b.Pos).Dot(b.Forward) > b.Hull.Length * 0.3 && b.Forward.Dot(n) > 0.6;
        double k = 1.2;
        double dmgToA = closing * k * (mb / ma), dmgToB = closing * k * (ma / mb);
        if (bowA) { dmgToB *= 2 * a.RamDamageMult; dmgToA *= 0.5 * a.RamSelfMult; }
        if (bowB) { dmgToA *= 2 * b.RamDamageMult; dmgToB *= 0.5 * b.RamSelfMult; }
        if (!bowA && !bowB) { dmgToA *= 1.3; dmgToB *= 1.3; }   // sides grinding together
        a.Hit(dmgToA, b, Rng);
        b.Hit(dmgToB, a, Rng);
        if (a.IsPlayer || b.IsPlayer) rammedAt = Time;   // the logbook's "Rammed and sunk" (P-07)
        // The player draws blood only when she did the driving; being rammed is not an attack (audit C-04).
        if (a.IsPlayer && aIn >= bIn) Draw(b);
        if (b.IsPlayer && bIn >= aIn) Draw(a);
        Events.Add(new CombatEvent(CombatEventType.Ram, (pa + pb) * 0.5, a.Id, closing));
    }
}
