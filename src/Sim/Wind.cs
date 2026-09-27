namespace LastTide.Sim;

/// <summary>A wind sample. <see cref="Direction"/> is where the wind blows TO; <see cref="From"/> where it comes from.</summary>
public readonly record struct Wind(double Direction, double Speed)
{
    public Vec2 Vector => Vec2.FromAngle(Direction) * Speed;
    public double From => Angles.Wrap(Direction + Math.PI);
    /// <summary>Wind speed in knots for the HUD.</summary>
    public double Knots => Speed * 1.943844;
}

/// <summary>
/// The wind (GDD §4, §9): no prevailing direction. The base direction and speed wander as
/// Ornstein–Uhlenbeck processes; on top rides a slow spatial noise field so the map is not uniform,
/// and patchy gusts. Storms and regional weather (M6) will modulate this.
/// </summary>
public sealed class WindField
{
    public double BaseDirection;   // where it blows to, radians
    public double BaseSpeed;       // m/s
    public double DirRate;         // rad/s, the wander's current rate
    /// <summary>True for tests and screenshots: no wander, no spatial variation, no gusts.</summary>
    public bool Fixed;
    public readonly int Seed;

    public WindField(int seed, double direction, double speed)
    {
        Seed = seed;
        BaseDirection = Angles.Wrap(direction);
        BaseSpeed = speed;
    }

    public void SetFixed(double direction, double speed)
    {
        BaseDirection = Angles.Wrap(direction);
        BaseSpeed = Math.Max(0, speed);   // a negative speed would turn the whole ship NaN (`--wind=FROM,SPEED`)
        DirRate = 0;
        Fixed = true;
    }

    public void Tick(double dt, Rng rng)
    {
        if (Fixed) return;
        double g1 = rng.NextGaussian(), g2 = rng.NextGaussian();
        DirRate += (-DirRate / Tuning.WindDirTau) * dt + Tuning.WindDirRateStd * Math.Sqrt(2 * dt / Tuning.WindDirTau) * g1;
        BaseDirection = Angles.Wrap(BaseDirection + DirRate * dt);
        BaseSpeed += (-(BaseSpeed - Tuning.WindMeanSpeed) / Tuning.WindSpeedTau) * dt
                     + Tuning.WindSpeedStd * Math.Sqrt(2 * dt / Tuning.WindSpeedTau) * g2;
        BaseSpeed = Math.Clamp(BaseSpeed, Tuning.WindMin, Tuning.WindMax);
    }

    /// <summary>Patchy gust factor 0–1 at a point; the sea shader draws the same field.</summary>
    public double Gust(Vec2 p, double t)
    {
        if (Fixed) return 0;
        double n = Noise.Value3(Seed + 2, p.X / Tuning.GustScale, p.Y / Tuning.GustScale, t / Tuning.GustTime);
        return Math.Clamp((n - 0.15) / 0.85, 0, 1);
    }

    public Wind Sample(Vec2 p, double t)
    {
        if (Fixed) return new Wind(BaseDirection, BaseSpeed);
        double dir = BaseDirection + Angles.Rad(Tuning.WindDirNoiseDeg)
                     * Noise.Value3(Seed, p.X / Tuning.WindDirNoiseScale, p.Y / Tuning.WindDirNoiseScale, t / Tuning.WindDirNoiseTime);
        double speed = BaseSpeed
                       * (1 + Tuning.WindSpeedNoise * Noise.Value3(Seed + 1, p.X / Tuning.WindSpeedNoiseScale, p.Y / Tuning.WindSpeedNoiseScale, t / Tuning.WindSpeedNoiseTime))
                       * (1 + Tuning.GustStrength * Gust(p, t));
        return new Wind(Angles.Wrap(dir), Math.Clamp(speed, 0.5, Tuning.WindMaxLocal));
    }
}
