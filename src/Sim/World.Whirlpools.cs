namespace LastTide.Sim;

public sealed partial class World
{
    /// <summary>Hull damage a second in a whirlpool's core, and a fresh leak every this many seconds she stays there.</summary>
    public const double WhirlpoolDamage = 3, WhirlpoolLeakEvery = 4;
    /// <summary>How hard the swirl turns a hull with it (share of the water's angular speed).</summary>
    public const double WhirlpoolSpin = 0.6;

    /// <summary>When a whirlpool last hurt her, for the logbook's cause of sinking.</summary>
    public double WhirlpoolHitTime { get; private set; } = -1;
    /// <summary>The whirlpool she is caught in this tick (null when clear): the HUD, the view and the audio read it.</summary>
    public Whirlpool? InWhirlpool { get; private set; }
    double coreClock;
    readonly List<Whirlpool> nearWhirlpools = new();

    /// <summary>
    /// The water carries every hull in a whirlpool's reach: she drifts with the current (her own way through the water
    /// is untouched), the swirl turns her with it, and in the core she is wrenched and holed. The player can meet them
    /// in the Maelstrom Straits and beyond the chart; other ships only in the straits, whose cores their lanes avoid.
    /// </summary>
    void WhirlpoolTick()
    {
        InWhirlpool = null;
        nearWhirlpools.Clear();
        Whirlpools.Near(Map, Ship.Pos, 0, nearWhirlpools);
        bool inCore = false;
        foreach (var w in nearWhirlpools)
        {
            if (Drift(Ship, w)) inCore = true;
            if (InWhirlpool == null || w.Pos.DistanceTo(Ship.Pos) < InWhirlpool.Value.Pos.DistanceTo(Ship.Pos)) InWhirlpool = w;
        }
        if (inCore)
        {
            Ship.Hit(WhirlpoolDamage * Dt, null, Rng, casualties: false, thresholdLeaks: false);
            WhirlpoolHitTime = Time;
            coreClock += Dt;
            if (coreClock >= WhirlpoolLeakEvery)
            {
                coreClock = 0;
                Ship.Leaks++;
                Events.Add(new CombatEvent(CombatEventType.Hit, Ship.Pos, 0, 1));
            }
        }
        else coreClock = 0;

        if (Map.Whirlpools.Count == 0) return;
        foreach (var other in Others)
        {
            if (other.Sunk) continue;
            foreach (var w in Map.Whirlpools)
                if (other.Pos.DistanceTo(w.Pos) < w.Radius && Drift(other, w))
                    other.Hit(WhirlpoolDamage * Dt, null, Rng, casualties: false, thresholdLeaks: false);
        }
    }

    /// <summary>Carries one hull a tick; true when she is in the core.</summary>
    bool Drift(Ship ship, Whirlpool w)
    {
        var c = w.Current(ship.Pos);
        if (c == Vec2.Zero) return false;
        ship.Pos += c * Dt;
        var d = ship.Pos - w.Pos;
        double r = Math.Max(d.Length, w.Core * 0.5);
        double spin = c.Dot(d.Perp.Normalized) / r * WhirlpoolSpin;   // rad/s, with the water
        ship.Heading = Angles.Wrap(ship.Heading + spin * Dt);
        return d.Length < w.Core;
    }

    /// <summary>
    /// Beyond the chart the sea belongs to the beasts: every <see cref="EdgeMonsterScale"/> metres further out, rolls
    /// come quicker and land more often, until far out one rises almost as soon as the last is gone.
    /// </summary>
    public const double EdgeMonsterScale = 600;

    /// <summary>How hard the open sea presses at this distance beyond the chart's edge (0 on the chart).</summary>
    public static double EdgePressure(double beyond) => Math.Max(0, beyond) / EdgeMonsterScale;

    void OuterMonsterRoll(double beyond)
    {
        double k = EdgePressure(beyond);
        MonsterClock = Math.Max(6, Tuning.SecondsPerHour / (1 + 3 * k));
        if (Docked != null) return;
        double chance = Math.Min(0.95, MonsterBaseChancePerHour * 3 * ThreatNow * (1 + 4 * k) * (IsNight ? 2 : 1));
        if (Rng.NextDouble() >= chance) return;
        // Whatever lives in the open ocean: the kraken most of all, serpents, and in fog or dark the ghost ship.
        double roll = Rng.NextDouble();
        var type = roll < 0.5 ? MonsterType.Kraken
            : roll < 0.8 || !GhostWeather(ConditionsAt(Ship.Pos)) ? MonsterType.ReefSerpent
            : MonsterType.GhostShip;
        SpawnMonster(type);
    }
}
