namespace LastTide.Sim;

public sealed partial class World
{
    public Weather Weather { get; private set; }
    public bool Lantern { get; private set; } = true;
    public const double TornSailChancePerSecond = 0.03;

    /// <summary>
    /// Vision by day 500 m; at night 250 lit / 150 doused; fog 180; ash cuts it too (GDD §4, §9). The lantern part
    /// widens only the lit night radius (GDD §19 M8); the lookout widens every radius (<see cref="VisionMult"/>).
    /// </summary>
    public double VisionAt(Vec2 p)
    {
        var c = ConditionsAt(p);
        double night = Lantern ? 250 * Ship.LanternMult : 150;
        double v = c.Night ? night : 500;
        if (c.Dusk > 0 && !c.Night) v = 500 - (500 - night) * c.Dusk;
        if (c.Fog > 0) v = Math.Min(v, 500 - 320 * c.Fog);
        if (c.Rain > 0) v *= 1 - 0.4 * c.Rain;
        if (c.Ash > 0) v *= 1 - 0.4 * c.Ash;
        return Math.Max(90, v) * VisionMult;
    }

    /// <summary>The lookout officer widens sight (M8); 1 by default. The lantern part is not in here: see <see cref="VisionAt"/>.</summary>
    public double VisionMult { get; set; } = 1;

    /// <summary>How far another ship can spot the player: lantern lit at night is a beacon, doused she is a shadow (GDD §9).</summary>
    public double DetectRangeOfPlayer()
    {
        var c = ConditionsAt(Ship.Pos);
        double night = Lantern ? 650 : 200;
        double d = c.Night ? night : 500;
        if (c.Dusk > 0 && !c.Night) d = 500 + (night - 500) * c.Dusk;   // through dusk and dawn, like her own sight
        if (c.Fog > 0) d = Math.Min(d, 500 - 320 * c.Fog);
        if (c.Rain > 0) d *= 1 - 0.4 * c.Rain;
        return Math.Max(80, d) * (1 - StealthBonus);
    }

    /// <summary>Ghost-grey sails and the like (M9 black market).</summary>
    public double StealthBonus { get; set; }

    // ---- Spyglass (GDD §4): hold RMB to see ~900 m along a 20° cone toward the cursor ----
    public bool SpyglassOn { get; private set; }
    public double SpyglassDir { get; private set; }
    public double SpyglassRange => 900 * Ship.LanternMult;
    public const double SpyglassHalfAngle = 10 * Math.PI / 180;

    /// <summary>Raised or lowered by each tick's <see cref="ShipInput"/>, so replays see it too.</summary>
    public void SetSpyglass(bool on, double direction)
    {
        SpyglassOn = on;
        SpyglassDir = direction;
    }

    /// <summary>Is a point within the lookout's sight: the vision circle, or the spyglass cone when held.</summary>
    public bool PlayerSees(Vec2 p, double slack = 0)
    {
        double d = p.DistanceTo(Ship.Pos);
        if (d <= VisionRadius + slack) return true;
        if (!SpyglassOn || d > SpyglassRange + slack) return false;
        double off = Math.Abs(Angles.Wrap((p - Ship.Pos).Angle - SpyglassDir));
        return off <= SpyglassHalfAngle;
    }

    public bool CanSpotPlayer(Ship observer) => observer.Pos.DistanceTo(Ship.Pos) <= DetectRangeOfPlayer();

    public Conditions ConditionsAt(Vec2 p)
    {
        double hour = HourOfDay;
        double dusk = hour >= Tuning.DuskHour - 1 && hour < Tuning.DuskHour ? hour - (Tuning.DuskHour - 1)
                    : hour >= Tuning.DawnHour && hour < Tuning.DawnHour + 1 ? 1 - (hour - Tuning.DawnHour) : 0;
        double storm = Weather.StormAt(p);
        var (fog, dold, ash, _) = RegionWeather(p, hour, needFog: true);
        return new Conditions(fog, dold, storm, storm, ash, IsNight, dusk);
    }

    /// <summary>
    /// The regions' weather at a point — fog, doldrums, ash and the wind factor — mixed across borders by
    /// <see cref="Map.RegionWeights"/> so none of them jumps where one region meets the next.
    /// </summary>
    (double Fog, double Doldrums, double Ash, double WindFactor) RegionWeather(Vec2 p, double hour, bool needFog)
    {
        Span<double> weights = stackalloc double[Map.Regions.Length];
        Map.RegionWeights(p, weights);
        double fogField = double.NaN, doldField = double.NaN, ashField = double.NaN;
        double fog = 0, dold = 0, ash = 0, factor = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            double wt = weights[i];
            if (wt <= 0) continue;
            var def = WeatherDef.Of(Map.Regions[i].Type);
            factor += wt * def.WindFactor;
            if (def.DoldrumsChance > 0)
            {
                if (double.IsNaN(doldField)) doldField = Weather.DoldrumsField(p, Time);
                dold += wt * Weather.DoldrumsFrom(doldField, def);
            }
            if (needFog)
            {
                if (double.IsNaN(fogField)) fogField = Weather.FogField(p, Time);
                fog += wt * Weather.FogFrom(fogField, def, hour);
                if (def.Ash)
                {
                    if (double.IsNaN(ashField)) ashField = Weather.AshField(p, Time);
                    ash += wt * Weather.AshFrom(ashField);
                }
            }
        }
        return (fog, dold, ash, factor);
    }

    /// <summary>The wind a hull actually feels: the wandering field, the region, doldrums and storms (GDD §9).</summary>
    public Wind WindAt(Vec2 p)
    {
        var w = Wind.Sample(p, Time);
        if (Wind.Fixed) return w;   // pinned wind (tests, screenshots) means no regional weather either
        var (_, dold, _, factor) = RegionWeather(p, 0, needFog: false);
        double speed = w.Speed * factor;
        if (dold > 0) speed *= 1 - 0.92 * dold;
        var result = new Wind(w.Direction, speed);
        // Each cell adds its own push to the wind; where cells overlap their pushes add up. (Taking only the
        // strongest cell switched the wind abruptly on the line where two overlapping cells are felt equally.)
        Vec2 v = result.Vector, sum = v;
        int felt = 0;
        foreach (var c in Weather.Storms)
        {
            double inside = c.Felt(p);
            if (inside <= 0) continue;
            sum += Weather.StormWind(result, c, p, Time, inside).Vector - v;
            felt++;
        }
        if (felt == 0) return result;
        return new Wind(sum.LengthSq > 1e-12 ? sum.Angle : result.Direction, Math.Min(sum.Length, 30));
    }

    void WeatherTick(ShipInput input)
    {
        if (input.ToggleLantern) Lantern = !Lantern;
        var def = WeatherDef.Of(Map.RegionAt(Ship.Pos).Type);
        Weather.Tick(Dt, Ship.Pos, Wind.Sample(Ship.Pos, Time), def, ThreatNow, Rng, spawnAllowed: !Wind.Fixed);
        VisionRadius = VisionAt(Ship.Pos);
        // Full sail in a storm tears canvas (GDD §4).
        double storm = Weather.StormAt(Ship.Pos);
        if (storm > 0.15 && Ship.SailTarget == 3 && !Ship.TornSails && Rng.NextDouble() < TornSailChancePerSecond * storm * Dt)
        {
            Ship.TornSails = true;
            Notices.Enqueue("NOTICE_TORN_SAILS");
        }
        foreach (var other in Others)
        {
            double s = Weather.StormAt(other.Pos);
            if (s > 0.15 && other.SailTarget == 3 && !other.TornSails && Rng.NextDouble() < TornSailChancePerSecond * s * Dt)
                other.TornSails = true;
        }
    }

    /// <summary>Storm swells shove the hull sideways; applied to near ships each tick.</summary>
    void Swells(Ship ship)
    {
        double s = Weather.StormAt(ship.Pos);
        if (s <= 0) return;
        double phase = Time * 0.9 + ship.Pos.X * 0.02 + ship.Pos.Y * 0.013;
        var push = Vec2.FromAngle(WindAt(ship.Pos).Direction + Math.PI / 2) * (Math.Sin(phase) * 1.4 * s * Dt);
        ship.Vel += push;
    }
}
