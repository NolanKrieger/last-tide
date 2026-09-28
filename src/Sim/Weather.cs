namespace LastTide.Sim;

/// <summary>A moving storm cell (GDD §9): strong swirling wind, rain, swells and torn-sail risk inside.</summary>
public sealed class StormCell
{
    public Vec2 Pos;
    public double Radius;
    public double Strength;   // 0–1
    public double Life;       // seconds left
    public double Age;

    /// <summary>Seconds a cell takes to gather and to blow out; the chart's swirl fades over the same spans.</summary>
    public const double GatherSeconds = 10, BlowOutSeconds = 15;

    /// <summary>0 as a cell forms, 1 once gathered, back to 0 as it blows out: its wind, rain and swell ramp with it.</summary>
    public double Fade => Math.Clamp(Math.Min(Age / GatherSeconds, Life / BlowOutSeconds), 0, 1);

    public double Inside(Vec2 p)
    {
        double d = p.DistanceTo(Pos) / Radius;
        return d >= 1 ? 0 : 1 - d * d;   // 1 at the eye, 0 at the rim
    }

    /// <summary>How hard the cell is felt at a point: <see cref="Inside"/> scaled by <see cref="Fade"/>.</summary>
    public double Felt(Vec2 p) => Inside(p) * Fade;
}

/// <summary>Per-region weather table (GDD §5, §9): wind factor, fog, doldrums, storms, ash.</summary>
public sealed record WeatherDef(RegionType Region, double WindFactor, double FogChance, double DoldrumsChance, double StormRate, bool DawnFog, bool Ash)
{
    public static readonly WeatherDef[] All =
    {
        new(RegionType.TradeIsles, 1.0, 0.03, 0.0, 0.25, false, false),
        new(RegionType.Shoals, 1.0, 0.03, 0.30, 0.10, false, false),
        new(RegionType.Deep, 1.3, 0.02, 0.0, 0.35, false, false),
        new(RegionType.FogBanks, 0.85, 0.75, 0.05, 0.15, false, false),
        new(RegionType.StormReach, 1.1, 0.05, 0.0, 1.0, false, false),
        new(RegionType.Mangrove, 0.8, 0.10, 0.10, 0.15, true, false),
        new(RegionType.Volcanic, 0.7, 0.0, 0.25, 0.10, false, true),
        new(RegionType.Sargasso, 0.75, 0.05, 0.45, 0.10, false, false),
        new(RegionType.SirenRuins, 0.8, 0.05, 0.20, 0.10, false, false),
        new(RegionType.IceReach, 1.15, 0.45, 0.0, 0.40, true, false),
        new(RegionType.Maelstrom, 1.2, 0.10, 0.0, 0.60, false, false),
        new(RegionType.CorsairKeys, 1.0, 0.04, 0.15, 0.20, false, false),
    };

    public static WeatherDef Of(RegionType t) => All[(int)t];
}

/// <summary>What the sea is doing at one spot: the view and the vision rules read this.</summary>
public readonly record struct Conditions(double Fog, double Doldrums, double Storm, double Rain, double Ash, bool Night, double Dusk);

public sealed class Weather
{
    public const int MaxStorms = 3;
    public const double StormBaseChancePerSecond = 1.0 / 180;   // per unit of region storm rate, at Threat 1: one every ~3 min in Storm Reach
    public readonly List<StormCell> Storms = new();
    public double SpawnClock;
    /// <summary>Diagnostics: spawn checks made, dice won, placements rejected.</summary>
    public int Checks, Wins, Rejected;
    readonly int seed;

    public Weather(int seed) { this.seed = seed; }

    /// <summary>Slow, patchy fields: fog and doldrums come and go over minutes and drift over hundreds of metres.</summary>
    public double FogField(Vec2 p, double t) => Noise.Value3(seed + 41, p.X / 700, p.Y / 700, t / 240) * 0.5 + 0.5;
    public double DoldrumsField(Vec2 p, double t) => Noise.Value3(seed + 42, p.X / 500, p.Y / 500, t / 200) * 0.5 + 0.5;
    public double AshField(Vec2 p, double t) => Noise.Value3(seed + 43, p.X / 400, p.Y / 400, t / 90) * 0.5 + 0.5;

    /// <summary>Fog 0–1 at a point: dense in the Fog Banks most of the time, at dawn in the mangroves, rare elsewhere.</summary>
    public double Fog(Vec2 p, double t, WeatherDef def, double hour) => FogFrom(FogField(p, t), def, hour);

    /// <summary>
    /// Fog from a sample of the fog field. Dawn fog rolls in over 05–06 and burns off over 07–08 (it used to switch
    /// on and off whole at 05:00 and 08:00).
    /// </summary>
    public static double FogFrom(double f, WeatherDef def, double hour)
    {
        double threshold = 1 - def.FogChance;
        if (def.DawnFog)
        {
            double dawn = hour < 5 || hour >= 8 ? 0 : hour < 6 ? hour - 5 : hour < 7 ? 1 : 8 - hour;
            if (dawn > 0) threshold += (Math.Min(threshold, 0.35) - threshold) * dawn;
        }
        if (f < threshold) return 0;
        return Math.Clamp((f - threshold) / Math.Max(0.05, 1 - threshold) * 2, 0, 1);
    }

    /// <summary>Doldrums 0–1: patches of glassy calm where the wind nearly dies.</summary>
    public double Doldrums(Vec2 p, double t, WeatherDef def) => def.DoldrumsChance <= 0 ? 0 : DoldrumsFrom(DoldrumsField(p, t), def);

    public static double DoldrumsFrom(double f, WeatherDef def)
    {
        if (def.DoldrumsChance <= 0) return 0;
        double threshold = 1 - def.DoldrumsChance;
        if (f < threshold) return 0;
        return Math.Clamp((f - threshold) / Math.Max(0.05, 1 - threshold) * 1.5, 0, 1);
    }

    public double Ash(Vec2 p, double t, WeatherDef def) => def.Ash ? AshFrom(AshField(p, t)) : 0;

    public static double AshFrom(double f) => Math.Clamp((f - 0.45) * 2.5, 0, 1);

    public double StormAt(Vec2 p)
    {
        double s = 0;
        foreach (var c in Storms) s = Math.Max(s, c.Felt(p) * c.Strength);
        return s;
    }

    /// <summary>Storms drift downwind, mature and blow out. New cells form near the player, more often with the region and the Threat.</summary>
    public void Tick(double dt, Vec2 player, in Wind wind, WeatherDef def, double threat, Rng rng, bool spawnAllowed)
    {
        for (int i = Storms.Count - 1; i >= 0; i--)
        {
            var c = Storms[i];
            c.Pos += wind.Vector * 0.4 * dt;
            c.Age += dt;
            c.Life -= dt;
            if (!Map.InBounds(c.Pos)) c.Life = Math.Min(c.Life, StormCell.BlowOutSeconds);   // off the chart: it blows out
            if (c.Life <= 0) Storms.RemoveAt(i);
        }
        SpawnClock -= dt;
        if (SpawnClock > 0 || !spawnAllowed) return;
        SpawnClock = 5;
        if (Storms.Count >= MaxStorms) return;
        Checks++;
        double chance = StormBaseChancePerSecond * 5 * def.StormRate * (0.5 + 0.5 * threat);
        if (rng.NextDouble() >= chance) return;
        Wins++;
        // Form upwind of the player, outside sight, so it blows across her path.
        double bearing = wind.From + rng.Range(-0.8, 0.8);
        var pos = player + Vec2.FromAngle(bearing) * rng.Range(700, 1100);
        pos = new Vec2(Math.Clamp(pos.X, -Map.HalfW + 150, Map.HalfW - 150), Math.Clamp(pos.Y, -Map.HalfH + 150, Map.HalfH - 150));
        if (pos.DistanceTo(player) < 450) { Rejected++; return; }
        Storms.Add(new StormCell { Pos = pos, Radius = rng.Range(250, 450), Strength = Math.Clamp(0.5 + 0.15 * threat + rng.Range(-0.2, 0.2), 0.3, 1.0), Life = rng.Range(90, 200) });
    }

    /// <summary>Radius of the eye as a fraction of the cell: inside it the swirl eases off to nothing at the centre.</summary>
    public const double EyeFraction = 0.12;

    /// <summary>
    /// The wind inside a storm: stronger, swirling around the eye, gusting. <paramref name="inside"/> is how hard the
    /// cell is felt here (<see cref="StormCell.Felt"/>). The swirl is mixed in as a direction vector, eased off in
    /// the eye: an angle blend flipped by up to 180° across the line where the swirl opposes the wind, and the swirl
    /// direction itself spins without limit at the centre.
    /// </summary>
    public static Wind StormWind(in Wind baseWind, StormCell cell, Vec2 p, double t, double inside)
    {
        var toEye = cell.Pos - p;
        double swirl = toEye.Angle + Math.PI / 2;   // clockwise around the eye
        double gust = 1 + 0.35 * Math.Sin(t * 1.7 + p.X * 0.01) * inside;
        double k = 0.6 * inside * Math.Min(1, toEye.Length / (EyeFraction * cell.Radius));
        var mix = Vec2.FromAngle(baseWind.Direction) * (1 - k) + Vec2.FromAngle(swirl) * k;
        double dir = mix.LengthSq > 1e-12 ? mix.Angle : baseWind.Direction;
        double speed = baseWind.Speed * (1 + (1.2 + 0.8 * cell.Strength) * inside) * gust;
        return new Wind(dir, Math.Min(speed, 30));
    }
}
