namespace LastTide.Sim;

/// <summary>
/// Flotsam adrift (Nolan, 2026-09-28: "there should be floating barrels rarely"). Now and then a barrel lost from some
/// merchant's deck comes drifting across her course: a roll once an in-game hour while she has steerage way, and the
/// barrel lies ahead of her inside her sight, fine on one bow and clear of land. It holds a few units of what the waters
/// there produce, or ship's stores, and floats longer than a sinking's spill so she has time to steer for it. The roll
/// and the barrel are pure functions of the seed, the hour and where she is, so nothing new is saved and a replay draws
/// the same barrels.
/// </summary>
public sealed partial class World
{
    /// <summary>Chance per in-game hour under way: about one barrel every two days at sea (four real minutes).</summary>
    public const double DriftChancePerHour = 0.02;
    /// <summary>Seconds a barrel adrift stays afloat (a sinking's spill: 60).</summary>
    public const double DriftLife = 150;
    /// <summary>Only a ship making way meets a barrel coming by (m/s).</summary>
    public const double DriftMinSpeed = 1.5;
    /// <summary>Off for the physics fixtures (as the monsters and the director are); saved with the voyage.</summary>
    public bool DriftEnabled { get; set; } = true;

    static readonly Good[] ShipsStores = { Good.Provisions, Good.Munitions, Good.Timber };

    static long TicksPerHour => (long)Math.Round(Tuning.SecondsPerHour * Tuning.TicksPerSecond);

    /// <summary>The draws for in-game hour <paramref name="hour"/>: the roll first, then the barrel.</summary>
    Rng DriftRng(long hour) => new((ulong)hour * 0xD1B54A32D192ED03UL + (ulong)(uint)Seed * 0x9E3779B97F4A7C15UL + 0xB4A2E1);

    /// <summary>Whether a barrel comes by in in-game hour <paramref name="hour"/> (if she is under way then).</summary>
    public bool DriftRoll(long hour) => DriftRng(hour).NextDouble() < DriftChancePerHour;

    void DriftTick()
    {
        if (!DriftEnabled || Ticks % TicksPerHour != 0 || Ship.Speed < DriftMinSpeed || Ship.Foundering) return;
        var rng = DriftRng(Ticks / TicksPerHour);
        if (rng.NextDouble() >= DriftChancePerHour) return;
        SpawnDrift(rng);
    }

    /// <summary>
    /// Puts a barrel ahead of her, inside her sight and fine on one bow, where the run from her to it crosses no land,
    /// and has the lookout sing out. Returns it, or null when every spot tried was behind land.
    /// </summary>
    public Flotsam? SpawnDrift(Rng rng)
    {
        double ahead = Math.Clamp(VisionRadius * 0.6, 120, 240);
        int side = rng.NextDouble() < 0.5 ? -1 : 1;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            double off = (attempt % 2 == 0 ? side : -side) * rng.Range(25, 70);
            var pos = Ship.Pos + Ship.Forward * rng.Range(ahead * 0.75, ahead) + Ship.Right * off;
            if (LandAlong(Ship.Pos, pos) != null) continue;
            var region = Map.InBounds(pos) && Map.Regions.Length > 0 ? RegionDef.Of(Map.RegionAt(pos).Type) : null;
            bool stores = region == null || region.Produces.Length == 0 || rng.NextDouble() < 0.3;
            var good = stores ? ShipsStores[rng.Next(ShipsStores.Length)] : region!.Produces[rng.Next(region.Produces.Length)];
            var barrel = new Flotsam { Pos = pos, Good = good, Units = 2 + rng.Next(stores ? 3 : 4), Life = DriftLife, Bob = rng.Range(0, 6) };
            Flotsam.Add(barrel);
            Notices.Enqueue(off > 0 ? "NOTICE_FLOTSAM_STARBOARD" : "NOTICE_FLOTSAM_PORT");
            return barrel;
        }
        return null;
    }
}
