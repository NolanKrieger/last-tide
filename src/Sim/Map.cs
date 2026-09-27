namespace LastTide.Sim;

public enum Faction { Crown, FreeTraders, Brethren }

public sealed class Region
{
    public int Index;
    public RegionType Type;
    public Vec2 Seed;
    public RegionDef Def => RegionDef.Of(Type);
}

/// <summary>A port on a coast with a harbour ring just offshore (GDD §5, §13).</summary>
public sealed class Port
{
    public int Id;
    public string Name = "";
    public Vec2 Pos;            // on the coastline
    public Vec2 Harbor;         // centre of the harbour ring, offshore
    public double RingRadius = 90;
    public Faction Faction;
    public bool Secret;         // a secret cove: not on the chart, always open, rare goods
    public bool Fort;
    public int Size;            // 0 small, 1 medium, 2 large
    public RegionType Region;
    public int IslandId;
    public List<Good> Produces = new();
    public List<Good> Consumes = new();
    public bool Discovered;     // coves start hidden; regular ports are on the chart once their cell is inked
    public Market Market = null!;
    /// <summary>What the player remembered paying here before this visit, for the trend arrows (null = never seen).</summary>
    public double?[]? PricesLastVisit;

    public bool Produces_(Good g) => Produces.Contains(g);
    public bool Consumes_(Good g) => Consumes.Contains(g);
    public bool InHarbor(Vec2 p) => p.DistanceTo(Harbor) <= RingRadius;
}

public sealed class TreasureSite
{
    public int Id;
    public int IslandId;
    public Vec2 Pos;        // the X, on land
    public Vec2 DigRing;    // where the ship anchors, offshore
    public double RingRadius = 70;
    public bool Dug;
}

public sealed class Wreck
{
    public int Id;
    public Vec2 Pos;
    public double RingRadius = 60;
    public bool Salvaged;
}

/// <summary>The archipelago for one run: regions, islands, ports, treasure, wrecks and the nav grid.</summary>
public sealed class Map
{
    public const double Width = 6000, Height = 4500;
    public const double HalfW = Width / 2, HalfH = Height / 2;

    public int Seed;
    /// <summary>The <see cref="MapGen.Version"/> this map was generated with.</summary>
    public int Version = MapGen.Version;
    public Region[] Regions = Array.Empty<Region>();
    public List<Island> Islands = new();
    public List<Port> Ports = new();
    public List<TreasureSite> Treasures = new();
    public List<Wreck> Wrecks = new();
    public NavGrid Nav = null!;
    public int StartPortId;
    public int Attempts;

    public Port StartPort => Ports[StartPortId];

    public static bool InBounds(Vec2 p) => p.X >= -HalfW && p.X <= HalfW && p.Y >= -HalfH && p.Y <= HalfH;

    /// <summary>The gentle noise warp applied before the nearest-seed test, so borders are not straight lines.</summary>
    Vec2 Warp(Vec2 p)
    {
        double wx = Noise.Value3(Seed + 31, p.X / 900, p.Y / 900, 0.3) * 350;
        double wy = Noise.Value3(Seed + 32, p.X / 900, p.Y / 900, 0.7) * 350;
        return new Vec2(p.X + wx, p.Y + wy);
    }

    /// <summary>
    /// Squared-distance margin over which the weather of two regions blends at their border. With region seeds
    /// ~1.5–2 km apart, a neighbour's weight falls smoothly from ½ on the border to 0 about 100 m inside it — some
    /// 8 s at a sloop's best speed.
    /// </summary>
    public const double BorderBlendSq = 2 * 1750 * 100;

    /// <summary>
    /// How much of each region's weather is felt at a point (indexed like <see cref="Regions"/>, summing to 1).
    /// 1 for the region deep inside it; across a border the two sides mix smoothly so wind, fog and doldrums have
    /// no seam. <see cref="RegionAt"/> (which region a point belongs to) is unchanged.
    /// </summary>
    public void RegionWeights(Vec2 p, Span<double> weights)
    {
        var q = Warp(p);
        double nearest = double.MaxValue;
        for (int i = 0; i < Regions.Length; i++) nearest = Math.Min(nearest, (Regions[i].Seed - q).LengthSq);
        double sum = 0;
        for (int i = 0; i < Regions.Length; i++)
        {
            double x = ((Regions[i].Seed - q).LengthSq - nearest) / BorderBlendSq;
            double w = x >= 1 ? 0 : (1 - x) * (1 - x) * (1 - x);
            weights[i] = w;
            sum += w;
        }
        for (int i = 0; i < Regions.Length; i++) weights[i] /= sum;
    }

    /// <summary>Nearest region seed after a gentle noise warp, so borders are not straight lines.</summary>
    public Region RegionAt(Vec2 p)
    {
        var q = Warp(p);
        Region best = Regions[0];
        double bestD = double.MaxValue;
        foreach (var r in Regions)
        {
            double d = (r.Seed - q).LengthSq;
            if (d < bestD)
            {
                bestD = d;
                best = r;
            }
        }
        return best;
    }

    public Region RegionOf(RegionType t) => Regions.First(r => r.Type == t);

    public Port? PortAt(Vec2 p)
    {
        foreach (var port in Ports)
            if (port.InHarbor(p)) return port;
        return null;
    }

    public bool OnLand(Vec2 p)
    {
        foreach (var island in Islands)
            if (p.DistanceTo(island.Centre) <= island.BoundRadius && island.Contains(p)) return true;
        return false;
    }

    /// <summary>Islands whose bounding circle comes within <paramref name="margin"/> of a point.</summary>
    public IEnumerable<Island> IslandsNear(Vec2 p, double margin)
    {
        foreach (var island in Islands)
            if (p.DistanceTo(island.Centre) <= island.BoundRadius + margin) yield return island;
    }
}
