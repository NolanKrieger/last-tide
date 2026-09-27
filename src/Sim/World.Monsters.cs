namespace LastTide.Sim;

public sealed partial class World
{
    public const double MonsterBaseChancePerHour = 0.10;   // × Threat, ×2 at night, in a monster's region
    public const double MonsterRest = 60;                  // seconds after an encounter before the next can start
    public const double SargassoDrag = 0.6;                // weed slows every ship (GDD §5)

    public Monster? Monster { get; private set; }
    public List<Eruption> Eruptions { get; } = new();
    public double MonsterClock { get; set; } = MonsterRest;
    public double EruptionClock { get; set; } = 30;
    public double RudderJam { get; private set; }
    public double SirenPull { get; private set; }         // signed rudder bias toward the rocks
    public bool Pinned { get; private set; }               // held fast by a kraken or the weed
    /// <summary>Which beast last hurt her, for the logbook.</summary>
    public MonsterType LastMonsterHit { get; private set; }
    public double MonsterHitTime { get; private set; } = -1;
    public bool MonstersEnabled { get; set; } = true;

    public HashSet<string> Achievements => Player.Achievements;

    void Unlock(string key)
    {
        if (key.Length == 0 || !Player.Achievements.Add(key)) return;
        Notices.Enqueue("ACHIEVEMENT_" + key);
        AchievementUnlocked?.Invoke(key);
    }

    public event Action<string>? AchievementUnlocked;

    /// <summary>Damage to the player from a beast: no faction, but the logbook remembers.</summary>
    void MonsterHits(MonsterType type, double damage, int leaks = 0, bool thresholdLeaks = true)
    {
        bool blow = damage >= 1 || leaks > 0;   // a grinding grip is not a blow: no casualty roll, no burst
        Ship.Hit(damage, null, Rng, casualties: blow, thresholdLeaks: thresholdLeaks);
        for (int i = 0; i < leaks; i++) Ship.Leaks++;
        LastMonsterHit = type;
        MonsterHitTime = Time;
        if (blow) Events.Add(new CombatEvent(CombatEventType.Hit, Ship.Pos, 0, damage));
    }

    void MonsterTick()
    {
        RudderJam = Math.Max(0, RudderJam - Dt);
        SirenPull = 0;
        Pinned = false;
        var region = Map.RegionAt(Ship.Pos).Type;
        // A pinned wind is the laboratory: no regional drag there.
        Ship.SpeedMult = !Wind.Fixed && region == RegionType.Sargasso ? SargassoDrag : 1;
        foreach (var other in Others)
            other.SpeedMult = !Wind.Fixed && Map.RegionAt(other.Pos).Type == RegionType.Sargasso ? SargassoDrag : 1;

        EruptionTick(region);

        if (Monster == null)
        {
            if (!MonstersEnabled) return;   // the switch stops new beasts, not one already in play
            MonsterClock -= Dt;
            if (MonsterClock > 0) return;
            MonsterClock = Tuning.SecondsPerHour;   // roll once an in-game hour
            var def = RegionDef.Of(region);
            if (def.Monster == MonsterType.None || Docked != null) return;
            if (def.Monster == MonsterType.GhostShip && !GhostWeather(ConditionsAt(Ship.Pos))) return;   // it walks only in fog or dark (audit C-16)
            double chance = MonsterBaseChancePerHour * ThreatNow * (IsNight ? 2 : 1);
            if (Rng.NextDouble() < chance) SpawnMonster(def.Monster);
            return;
        }

        var m = Monster;
        m.Age += Dt;
        switch (m.Type)
        {
            case MonsterType.ReefSerpent: SerpentTick(m, region); break;
            case MonsterType.Kraken: KrakenTick(m, region); break;
            case MonsterType.GhostShip: GhostTick(m); break;
            case MonsterType.Crocodile: CrocodileTick(m, region); break;
            case MonsterType.WeedKraken: WeedTick(m, region); break;
            case MonsterType.Siren: SirenTick(m); break;
        }
        if (m.Done)
        {
            Notices.Enqueue(m.Beaten ? "NOTICE_MONSTER_BEATEN" : "NOTICE_MONSTER_ESCAPED");
            Monster = null;
            MonsterClock = MonsterRest;
        }
    }

    public Monster SpawnMonster(MonsterType type)
    {
        var def = MonsterDefs.Of(type);
        var m = new Monster { Type = type, Hp = def.Hp };
        switch (type)
        {
            case MonsterType.ReefSerpent:
            case MonsterType.Kraken:
            case MonsterType.WeedKraken:
                // They lie ahead: a ship under way sails into them.
                m.Pos = Ship.Pos + Ship.Forward * 200 + Ship.Right * Rng.Range(-60, 60);
                break;
            case MonsterType.GhostShip:
                m.Pos = Ship.Pos + Vec2.FromAngle(Rng.Range(0, Angles.Tau)) * 260;
                m.Surfaced = true;
                m.FlareClock = 5;
                m.State = MonsterState.Surfaced;
                m.Targets.Add(new MonsterTarget { Pos = m.Pos, Radius = 12, Hp = def.Hp });
                break;
            case MonsterType.Crocodile:
            case MonsterType.Siren:
            {
                // On the nearest coast.
                Vec2 best = Ship.Pos + Vec2.FromAngle(Rng.Range(0, Angles.Tau)) * 120;
                double bestD = double.MaxValue;
                foreach (var island in Map.IslandsNear(Ship.Pos, 400))
                {
                    var (q, d, _) = island.Closest(Ship.Pos);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = q;
                    }
                }
                m.Perch = best;
                m.Pos = best;
                m.State = MonsterState.Perched;
                m.Surfaced = true;
                m.Targets.Add(new MonsterTarget { Pos = best, Radius = type == MonsterType.Siren ? 12 : 8, Hp = def.Hp });
                break;
            }
        }
        Monster = m;
        Notices.Enqueue("NOTICE_MONSTER_" + def.Key);
        Events.Add(new CombatEvent(CombatEventType.Ring, m.Pos, -3, 1));
        return m;
    }

    /// <summary>Cannonballs strike the beast's targets; the ball loop calls this before checking ships.</summary>
    bool StrikeMonster(Vec2 from, Vec2 to, double damage, Ship? shooter)
    {
        if (Monster == null || shooter?.IsPlayer != true) return false;
        var m = Monster;
        foreach (var t in m.Targets)
        {
            if (!t.Alive) continue;
            if (Geometry.SegmentDistance(from, to, t.Pos, t.Pos) > t.Radius) continue;
            if (m.Type == MonsterType.GhostShip && m.FlareTimer <= 0) { Events.Add(new CombatEvent(CombatEventType.Splash, to, -3, 0)); return true; }   // passes through
            if (!m.Surfaced && m.Type != MonsterType.Kraken) continue;
            t.Hp -= damage;
            Events.Add(new CombatEvent(CombatEventType.Hit, to, -3, damage));
            if (m.Type is MonsterType.ReefSerpent or MonsterType.GhostShip or MonsterType.Crocodile or MonsterType.Siren)
                m.Hp = t.Hp;
            return true;
        }
        return false;
    }

    // ---- Reef Serpent: dives, surfaces alongside, bites at the waterline, dives again ----
    void SerpentTick(Monster m, RegionType region)
    {
        if (region != RegionType.Shoals && !m.Surfaced) { m.Done = true; return; }   // it will not follow into deep water
        if (m.Hp <= 0) { Beat(m); return; }
        switch (m.State)
        {
            case MonsterState.Approach:
                m.Surfaced = false;
                MoveToward(m, Ship.Pos + Ship.Right * (22 * m.Side), 9);
                if (m.Pos.DistanceTo(Ship.Pos + Ship.Right * (22 * m.Side)) < 6)
                {
                    m.State = MonsterState.Surfaced;
                    m.Surfaced = true;
                    m.Timer = 9;
                    m.Targets.Clear();
                    m.Targets.Add(new MonsterTarget { Pos = m.Pos, Radius = 14, Hp = m.Hp });
                }
                break;
            case MonsterState.Surfaced:
                m.Pos = Ship.Pos + Ship.Right * (22 * m.Side);
                m.Targets[0].Pos = m.Pos;
                m.Timer -= Dt;
                if (m.Timer <= 7 && m.Bites == 0) { m.Bites = 1; MonsterHits(MonsterType.ReefSerpent, 8, leaks: 1); }
                if (m.Timer <= 0) { m.State = MonsterState.Dive; m.Surfaced = false; m.Timer = 12 + Rng.Range(0, 5); m.Bites = 0; m.Side = -m.Side; }
                break;
            case MonsterState.Dive:
                m.Timer -= Dt;
                MoveToward(m, Ship.Pos - Ship.Forward * 120, 9);
                if (m.Timer <= 0) m.State = MonsterState.Approach;
                break;
        }
    }

    // ---- Kraken: tentacles pin the hull; cut them free ----
    /// <summary>GDD §12 "each one hit frees a hold": a tentacle falls to one grade-0 ball (6 × 0.8 at the least).</summary>
    public const double TentacleHp = 4.5;
    /// <summary>Seconds between smashes (3 HP and a leak); 12 so a lone carpenter has time between plugging its leaks.</summary>
    public const double KrakenSmashEvery = 12;
    /// <summary>HP a second each free carpenter hacks off a gripping tentacle.</summary>
    public const double KrakenHackRate = 1;
    void KrakenTick(Monster m, RegionType region)
    {
        switch (m.State)
        {
            case MonsterState.Approach:
                if (region != m.Def.Region) { m.Done = true; return; }   // it will not follow her out of the Deep (audit C-06)
                MoveToward(m, Ship.Pos, 20);   // nothing under sail outruns it
                if (m.Pos.DistanceTo(Ship.Pos) < 15)
                {
                    m.State = MonsterState.Grip;
                    m.Surfaced = true;
                    m.Timer = 0;
                    m.Targets.Clear();
                    foreach (double deg in new[] { 70, 110, 250, 290 })
                        m.Targets.Add(new MonsterTarget { Radius = 9, Hp = TentacleHp });
                    Notices.Enqueue("NOTICE_KRAKEN_GRIP");
                }
                break;
            case MonsterState.Grip:
            {
                m.Pos = Ship.Pos;
                Pinned = true;
                Ship.Vel = Ship.Vel * 0.5;
                int i = 0;
                foreach (double deg in new[] { 70, 110, 250, 290 })
                    m.Targets[i++].Pos = Ship.Pos + Vec2.FromAngle(Ship.Heading + Angles.Rad(deg)) * 26;
                m.Timer += Dt;
                // The grind wears the hull but opens no seams; only the timed smash does (P-08).
                MonsterHits(MonsterType.Kraken, 1.5 * Dt, thresholdLeaks: false);
                if (m.Timer >= KrakenSmashEvery) { m.Timer = 0; MonsterHits(MonsterType.Kraken, 3, leaks: 1, thresholdLeaks: false); }
                // Carpenters with no leak to plug hack at a tentacle, so a ship with no shot can still cut herself free (P-03).
                Ship.Split(out _, out _, out int carpenters, out _);
                if (Ship.Leaks == 0 && carpenters > 0 && m.Targets.FirstOrDefault(t => t.Alive) is { } tentacle)
                {
                    tentacle.Hp -= carpenters * KrakenHackRate * Dt;
                    Ship.Hacking = true;
                }
                int alive = m.Targets.Count(t => t.Alive);
                if (alive <= 1)
                {
                    m.State = MonsterState.Retreat;
                    m.Surfaced = false;
                    m.Timer = 6;
                    Unlock(m.Def.EscapeAchievement);
                }
                break;
            }
            case MonsterState.Retreat:
                MoveToward(m, Ship.Pos + Vec2.FromAngle(m.Heading) * 400, 8);
                m.Timer -= Dt;
                if (m.Timer <= 0) m.Done = true;
                break;
        }
    }

    /// <summary>The ghost ship is seen, and fights, only in fog or at night (GDD §12).</summary>
    static bool GhostWeather(Conditions c) => c.Fog > 0.05 || c.Night;

    // ---- Ghost Ship: circles in the fog, fires, only solid while its lanterns flare ----
    void GhostTick(Monster m)
    {
        var c = ConditionsAt(Ship.Pos);
        bool abroad = GhostWeather(c);
        if (!abroad) { m.Timer += Dt; if (m.Timer > 12) { m.Done = true; return; } }
        else m.Timer = 0;
        if (m.Hp <= 0) { Beat(m); return; }
        // Keeps station abeam, 120 m off, on a parallel course; changes sides now and then.
        m.Bites = (int)(m.Age / 35) % 2 == 0 ? 1 : -1;
        var want = Ship.Pos + Ship.Right * (m.Bites * 120) - Ship.Forward * 10;
        MoveToward(m, want, 12);
        m.Heading = Ship.Heading;
        m.Targets[0].Pos = m.Pos;
        m.FlareClock -= Dt;
        if (m.FlareTimer > 0) m.FlareTimer -= Dt;
        else if (m.FlareClock <= 0) { m.FlareTimer = 3; m.FlareClock = 6; Events.Add(new CombatEvent(CombatEventType.Ring, m.Pos, -3, 2)); }
        m.GunClock -= Dt;
        if (m.GunClock <= 0 && abroad)   // fading in clear daylight, it does not fire (audit C-16)
        {
            m.GunClock = 12;
            var dir = (Ship.Pos - m.Pos).Normalized;
            for (int k = 0; k < 2; k++)
                Balls.Add(new Cannonball { Pos = m.Pos + dir * 10, Vel = dir.Rotated(Angles.Rad(Rng.Range(-4, 4))) * BallSpeed, Life = 200 / BallSpeed, Shooter = null, From = MonsterType.GhostShip, Damage = 4, Delay = k * 0.05 });
            Events.Add(new CombatEvent(CombatEventType.Fire, m.Pos, -3, 2));
        }
    }

    // ---- Giant Crocodile: lunges from the bank, jams the rudder, slides back ----
    void CrocodileTick(Monster m, RegionType region)
    {
        if (region != RegionType.Mangrove) { m.Done = true; return; }
        if (m.Hp <= 0) { Beat(m); return; }
        switch (m.State)
        {
            case MonsterState.Perched:
                m.Targets[0].Pos = m.Pos;
                m.Surfaced = true;
                if (Ship.Pos.DistanceTo(m.Perch) < 90 && m.Age > 2) { m.State = MonsterState.Approach; m.Surfaced = false; }
                break;
            case MonsterState.Approach:
                MoveToward(m, Ship.Pos, 14);
                if (m.Pos.DistanceTo(Ship.Pos) < 9)
                {
                    MonsterHits(MonsterType.Crocodile, 10);
                    RudderJam = 5;
                    Notices.Enqueue("NOTICE_RUDDER_JAMMED");
                    m.State = MonsterState.Retreat;
                    m.Surfaced = true;
                    m.Timer = 5;
                }
                if (Ship.Pos.DistanceTo(m.Perch) > 220) { m.State = MonsterState.Retreat; m.Surfaced = true; m.Timer = 4; }
                break;
            case MonsterState.Retreat:
                m.Targets[0].Pos = m.Pos;
                // It slides back surfaced and is on its bank when the timer runs out — at 6 m/s, or faster from farther
                // off, instead of being put there in one tick at the end (a jump of up to ~200 m: audit C-17).
                MoveToward(m, m.Perch, Math.Max(6, m.Pos.DistanceTo(m.Perch) / Math.Max(Dt, m.Timer)));
                m.Timer -= Dt;
                if (m.Timer <= 0 || m.Pos.DistanceTo(m.Perch) < 0.5) { m.State = MonsterState.Perched; m.Pos = m.Perch; m.Age = 0; }
                break;
        }
    }

    // ---- Weed-Kraken: wraps the hull and drags her down; cut the mass ----
    void WeedTick(Monster m, RegionType region)
    {
        switch (m.State)
        {
            case MonsterState.Approach:
                if (region != m.Def.Region) { m.Done = true; return; }   // the weed ends where the Sargasso ends (audit C-06)
                MoveToward(m, Ship.Pos, 13);
                if (m.Pos.DistanceTo(Ship.Pos) < 12)
                {
                    m.State = MonsterState.Grip;
                    m.Surfaced = true;
                    m.Targets.Clear();
                    m.Targets.Add(new MonsterTarget { Radius = 12, Hp = m.Hp });
                    Notices.Enqueue("NOTICE_WEED_GRIP");
                }
                break;
            case MonsterState.Grip:
            {
                m.Pos = Ship.Pos - Ship.Forward * (Ship.Hull.Length * 0.4);
                m.Targets[0].Pos = m.Pos;
                Pinned = true;
                Ship.Vel = Ship.Vel * 0.5;
                Ship.Water = Math.Min(100, Ship.Water + 3 * Dt);
                Ship.Split(out _, out _, out int carpenters, out _);
                m.Targets[0].Hp -= carpenters * 5 * Dt;   // carpenters hack at it
                if (m.Targets[0].Hp <= 0)
                {
                    m.State = MonsterState.Retreat;
                    m.Surfaced = false;
                    m.Timer = 4;
                    Unlock(m.Def.EscapeAchievement);
                }
                break;
            }
            case MonsterState.Retreat:
                m.Timer -= Dt;
                if (m.Timer <= 0) m.Done = true;
                break;
        }
    }

    // ---- Siren: her song pulls the rudder toward her rock while within earshot ----
    void SirenTick(Monster m)
    {
        if (m.Hp <= 0) { Beat(m); return; }
        double d = Ship.Pos.DistanceTo(m.Perch);
        if (d > 250) { m.Timer += Dt; if (m.Timer > 20) m.Done = true; return; }
        m.Timer = 0;
        var toRock = m.Perch - Ship.Pos;
        double bearing = Angles.Wrap(toRock.Angle - Ship.Heading);
        SirenPull = Math.Sign(bearing) * 0.6 * (1 - d / 250 * 0.4);
        // While she sings the rocks bite (P-02): a strike on the coast costs 2 HP per m/s of impact, and a leak from
        // 3 m/s, laid at her door. Grinding along a shore (under 1 m/s) and grounding out of her earshot stay harmless.
        // GunClock (saved) spaces the strikes a second apart so one crash counts once.
        m.GunClock = Math.Max(0, m.GunClock - Dt);
        if (Ship.LastImpact >= SirenRockMinImpact && m.GunClock <= 0)
        {
            m.GunClock = 1;
            MonsterHits(MonsterType.Siren, SirenRockDamage * Ship.LastImpact, leaks: Ship.LastImpact >= SirenRockLeakImpact ? 1 : 0);
        }
    }

    public const double SirenRockDamage = 2, SirenRockMinImpact = 1, SirenRockLeakImpact = 3;

    void Beat(Monster m)
    {
        m.Done = true;
        m.Beaten = true;
        Events.Add(new CombatEvent(CombatEventType.Sink, m.Pos, -3, 20));
        Unlock(m.Def.Achievement);
        Stats.MonstersBeaten++;
    }

    void MoveToward(Monster m, Vec2 target, double speed)
    {
        var d = target - m.Pos;
        double len = d.Length;
        if (len < 1e-6) return;
        var step = d / len * Math.Min(len, speed * Dt);
        m.Pos += step;
        m.Heading = d.Angle;
    }

    // ---- Volcanic eruptions: telegraphed rock showers ----
    void EruptionTick(RegionType region)
    {
        for (int i = Eruptions.Count - 1; i >= 0; i--)
        {
            var e = Eruptions[i];
            e.Age += Dt;
            e.Warning -= Dt;
            if (!e.Landed && e.Warning <= 0)
            {
                e.Landed = true;
                foreach (var ship in AllShips)
                    if (!ship.Sunk && ship.Pos.DistanceTo(e.Pos) <= e.Radius + ship.Hull.Beam * 0.5)
                    {
                        if (ship.IsPlayer) MonsterHits(MonsterType.None, 20, leaks: 1);
                        else ship.Hit(20, null, Rng);
                    }
                Events.Add(new CombatEvent(CombatEventType.Ram, e.Pos, -4, e.Radius));
            }
            if (e.Warning < -3) Eruptions.RemoveAt(i);
        }
        if (region != RegionType.Volcanic || Docked != null) { EruptionClock = Math.Max(EruptionClock, 10); return; }
        EruptionClock -= Dt;
        if (EruptionClock > 0) return;
        EruptionClock = Rng.Range(30, 70) / Math.Max(1, Math.Sqrt(ThreatNow));
        var pos = Ship.Pos + Ship.Vel * 4 + Vec2.FromAngle(Rng.Range(0, Angles.Tau)) * Rng.Range(0, 80);
        Eruptions.Add(new Eruption { Pos = pos, Radius = 60, Warning = 5 });
        Notices.Enqueue("NOTICE_ERUPTION");
    }
}
