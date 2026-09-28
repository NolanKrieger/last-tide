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
    public const double Width = 18000, Height = 13500;
    public const double HalfW = Width / 2, HalfH = Height / 2;

    public int Seed;
    /// <summary>The <see cref="MapGen.Version"/> this map was generated with.</summary>
    public int Version = MapGen.Version;
    public Region[] Regions = Array.Empty<Region>();
    public List<Island> Islands = new();
    public List<Port> Ports = new();
    public List<TreasureSite> Treasures = new();
    public List<Wreck> Wrecks = new();
    /// <summary>The Maelstrom Straits' whirlpools, in the narrows (the open sea beyond the chart has its own: <see cref="Sim.Whirlpools"/>).</summary>
    public List<Whirlpool> Whirlpools = new();
    public NavGrid Nav = null!;
    public int StartPortId;
    public int Attempts;

    public Port StartPort => Ports[StartPortId];

    public static bool InBounds(Vec2 p) => p.X >= -HalfW && p.X <= HalfW && p.Y >= -HalfH && p.Y <= HalfH;

    /// <summary>How far beyond the chart's edge a point lies (m); negative inside it. The sea goes on out there.</summary>
    public static double BeyondEdge(Vec2 p) => Math.Max(Math.Abs(p.X) - HalfW, Math.Abs(p.Y) - HalfH);

    /// <summary>The noise warp applied before the nearest-seed test, so borders wander instead of running straight.</summary>
    Vec2 Warp(Vec2 p)
    {
        double wx = Noise.Value3(Seed + 31, p.X / 2400, p.Y / 2400, 0.3) * 900 + Noise.Value3(Seed + 33, p.X / 800, p.Y / 800, 0.3) * 260;
        double wy = Noise.Value3(Seed + 32, p.X / 2400, p.Y / 2400, 0.7) * 900 + Noise.Value3(Seed + 34, p.X / 800, p.Y / 800, 0.7) * 260;
        return new Vec2(p.X + wx, p.Y + wy);
    }

    /// <summary>
    /// Squared-distance margin over which the weather of two regions blends at their border. With region seeds
    /// ~4.5 km apart, a neighbour's weight falls smoothly from ½ on the border to 0 about 100 m inside it — some
    /// 8 s at a sloop's best speed.
    /// </summary>
    public const double BorderBlendSq = 2 * 4500 * 100;

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
        foreach (var island in IslandsAround(p))
            if (p.DistanceTo(island.Centre) <= island.BoundRadius && island.Contains(p)) return true;
        return false;
    }

    /// <summary>Islands whose bounding circle comes within <paramref name="margin"/> of a point, in id order.</summary>
    public IEnumerable<Island> IslandsNear(Vec2 p, double margin)
    {
        EnsureIndex();
        int x0 = IndexX(p.X - margin), x1 = IndexX(p.X + margin), y0 = IndexY(p.Y - margin), y1 = IndexY(p.Y + margin);
        if (x0 == x1 && y0 == y1)
        {
            foreach (var island in index![y0 * IndexW + x0] ?? NoIslands)
                if (p.DistanceTo(island.Centre) <= island.BoundRadius + margin) yield return island;
            yield break;
        }
        var found = new List<Island>();
        stampNow++;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                foreach (var island in index![y * IndexW + x] ?? NoIslands)
                {
                    if (stamps[island.Id] == stampNow) continue;
                    stamps[island.Id] = stampNow;
                    if (p.DistanceTo(island.Centre) <= island.BoundRadius + margin) found.Add(island);
                }
        found.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (var island in found) yield return island;
    }

    /// <summary>
    /// Every island that could come within <see cref="IndexPad"/> of a point: the few a ship there can touch, in id
    /// order. With a thousand islands on the chart, the hull tests start here rather than with the whole list.
    /// </summary>
    public IReadOnlyList<Island> IslandsAround(Vec2 p)
    {
        EnsureIndex();
        return index![IndexY(p.Y) * IndexW + IndexX(p.X)] ?? NoIslands;
    }

    // ---- Spatial index: islands by 500 m cell (bounding circle + pad), rebuilt whenever the list changes size ----
    public const double IndexCell = 500, IndexPad = 120;
    static readonly int IndexW = (int)Math.Ceiling(Width / IndexCell) + 2, IndexH = (int)Math.Ceiling(Height / IndexCell) + 2;
    static readonly List<Island> NoIslands = new();
    List<Island>?[]? index;
    int indexedCount = -1, stampNow;
    int[] stamps = Array.Empty<int>();

    // Cells run from one cell beyond the chart's west/north edge; points further out share the border cells, which hold no islands.
    static int IndexX(double x) => Math.Clamp((int)Math.Floor((x + HalfW) / IndexCell) + 1, 0, IndexW - 1);
    static int IndexY(double y) => Math.Clamp((int)Math.Floor((y + HalfH) / IndexCell) + 1, 0, IndexH - 1);

    void EnsureIndex()
    {
        if (index != null && indexedCount == Islands.Count) return;
        index = new List<Island>?[IndexW * IndexH];
        foreach (var island in Islands)
        {
            double r = island.BoundRadius + IndexPad;
            for (int y = IndexY(island.Centre.Y - r); y <= IndexY(island.Centre.Y + r); y++)
                for (int x = IndexX(island.Centre.X - r); x <= IndexX(island.Centre.X + r); x++)
                    (index[y * IndexW + x] ??= new List<Island>()).Add(island);
        }
        stamps = new int[Islands.Count == 0 ? 0 : Islands.Max(i => i.Id) + 1];
        indexedCount = Islands.Count;
    }
}
