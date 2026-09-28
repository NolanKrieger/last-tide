namespace LastTide.Sim;

/// <summary>
/// Builds the archipelago (GDD §5): twelve warped Voronoi regions, islands built from real-world landforms with
/// region-specific kinds, sizes and channels, ports on sheltered coasts with a faction mix, goods drawn from region
/// tables and repaired to the global constraints, then validation: every port reachable by sea and a profitable trade
/// within half a day of the start. Deterministic per seed.
/// </summary>
public static class MapGen
{
    public const int MinPorts = 140, MaxPorts = 152, MinCoves = 10, MaxCoves = 14;
    /// <summary>
    /// The generator's rules version, recorded in suspend saves. 6: the 18 × 13.5 km chart of twelve regions whose
    /// islands are built from landforms (<see cref="Landforms"/>). Voyages charted by older versions cannot resume.
    /// </summary>
    public const int Version = 6;
    public const double StartRouteRange = 600;   // metres of sea: about half a day's sail
    public const double EdgeMargin = 400;
    /// <summary>A luxury is not sold within this distance of where it is made (GDD §5 step 4).</summary>
    public const double LuxuryDistance = 3000;

    public static Map Generate(int seed, int version = Version)
    {
        if (version != Version) throw new NotSupportedException($"map version {version} is no longer generated (this build makes {Version})");
        int attempts = 0;
        // The coastlines are the costly part, so a failed roll keeps them and re-rolls only ports, goods and the
        // start; the sea itself is redrawn only after several failures.
        for (int shape = 0; shape < 20; shape++)
        {
            var rng = new Rng(unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + (ulong)shape * 0xBF58476D1CE4E5B9UL + 7));
            var map = new Map { Seed = unchecked(seed * 131 + shape), Version = version };
            PlaceRegions(map, rng);
            PlaceLandforms(map, rng);
            map.Nav = new NavGrid(map.Islands);
            PlaceWhirlpools(map, rng);
            for (int roll = 0; roll < 6; roll++)
            {
                attempts++;
                map.Ports.Clear();
                if (!PlacePorts(map, rng)) continue;
                AssignGoods(map, rng);
                if (!Validate(map)) continue;
                PlaceTreasure(map, rng);
                PlaceWrecks(map, rng);
                NamePorts(map, rng);
                map.Attempts = attempts;
                return map;
            }
        }
        throw new InvalidOperationException($"map generation failed for seed {seed}");
    }


    // ---- 1. Regions: a jittered 4×3 grid of seeds, the twelve types shuffled over it ----
    public const int RegionCols = 4, RegionRows = 3;

    static void PlaceRegions(Map map, Rng rng)
    {
        var types = Enum.GetValues<RegionType>();
        for (int i = types.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (types[i], types[j]) = (types[j], types[i]);
        }
        double cw = Map.Width / RegionCols, ch = Map.Height / RegionRows;
        map.Regions = new Region[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            int col = i % RegionCols, row = i / RegionCols;
            var seedPos = new Vec2(-Map.HalfW + (col + 0.5) * cw + rng.Range(-0.18, 0.18) * cw, -Map.HalfH + (row + 0.5) * ch + rng.Range(-0.18, 0.18) * ch);
            map.Regions[i] = new Region { Index = i, Type = types[i], Seed = seedPos };
        }
    }

    // ---- 2. Islands: real-world landforms, each region's signature first, then a clumped fill ----
    sealed record Footprint(Vec2 C, double Reach, double Channel);

    static void PlaceLandforms(Map map, Rng rng)
    {
        int n = map.Regions.Length;
        // One trend for the archipelago, bent region by region: ridges, chains and long islands line up with it.
        double trend = rng.Range(0, Math.PI);
        var grain = new double[n];
        foreach (var r in map.Regions) grain[r.Index] = trend + rng.NextGaussian() * 0.45;
        var area = new double[n];
        const double s = 150;
        for (double y = -Map.HalfH + s / 2; y < Map.HalfH; y += s)
            for (double x = -Map.HalfW + s / 2; x < Map.HalfW; x += s)
                area[map.RegionAt(new Vec2(x, y)).Index] += s * s;
        var covered = new double[n];
        var placed = new List<Footprint>();

        // Island groups: each region gathers its land around a few centres, as real archipelagos do, leaving wide lanes
        // of open water between them. The first group sits at the region's heart and holds its signature landform.
        var groups = new List<(Vec2 C, double Sigma)>();
        foreach (var region in map.Regions)
        {
            groups.Add((region.Seed, rng.Range(900, 1400)));
            int count = RegionLayout.Of(region.Type).Groups - 1;
            for (int k = 0, t = 0; k < count && t < 300; t++)
            {
                var p = region.Seed + new Vec2(rng.Range(-2600, 2600), rng.Range(-2600, 2600));
                if (Math.Abs(p.X) > Map.HalfW - 900 || Math.Abs(p.Y) > Map.HalfH - 900 || map.RegionAt(p).Type != region.Type) continue;
                if (groups.Any(g => g.C.DistanceTo(p) < 1500)) continue;
                groups.Add((p, rng.Range(500, 1100)));
                k++;
            }
        }
        double Density(Vec2 p)
        {
            double best = 0;
            foreach (var (c, sigma) in groups) best = Math.Max(best, Math.Exp(-(p - c).LengthSq / (2 * sigma * sigma)));
            return best;
        }

        foreach (var region in map.Regions)
        {
            var sig = RegionLayout.Of(region.Type).Signature;
            double before = covered[region.Index];
            for (int t = 0; t < 40; t++)
            {
                var p = region.Seed + Vec2.FromAngle(rng.Range(0, Angles.Tau)) * rng.Range(0, 700);
                if (TryLandform(map, rng, placed, covered, sig, region, p, rng.Range(0.6, 1.0), grain)) break;
            }
            // The signature is extra: it counts for only a third of its land, so the region still fills round it.
            covered[region.Index] = before + (covered[region.Index] - before) / 3;
        }

        // Then the fill: darts thinned by the groups, bigger landforms toward a group's heart, sizes leaning small as
        // they do in real archipelagos, until each region has its share of land.
        int misses = 0;
        for (int dart = 0; dart < 80000 && misses < 4000; dart++, misses++)
        {
            var p = new Vec2(rng.Range(-Map.HalfW + EdgeMargin, Map.HalfW - EdgeMargin), rng.Range(-Map.HalfH + EdgeMargin, Map.HalfH - EdgeMargin));
            var region = map.RegionAt(p);
            var layout = RegionLayout.Of(region.Type);
            if (covered[region.Index] >= layout.LandShare * area[region.Index]) continue;
            double density = Density(p);
            if (rng.NextDouble() > 0.01 + density * density * density) continue;
            var kind = WeightedPick(layout.Forms.ToList(), f => f.Weight, rng).Kind;
            double u = Math.Min(1, Math.Pow(rng.NextDouble(), 1.8) * (0.5 + 0.8 * density));
            if (TryLandform(map, rng, placed, covered, kind, region, p, u, grain)) misses = 0;
        }
    }

    static bool TryLandform(Map map, Rng rng, List<Footprint> placed, double[] covered, Landform kind, Region region, Vec2 p, double u, double[] grain)
    {
        var layout = RegionLayout.Of(region.Type);
        var (min, max) = RegionLayout.SizeOf(kind);
        int seed = unchecked(map.Seed * 7919 + placed.Count * 104729 + map.Islands.Count * 31 + 11);
        var form = Landforms.Build(kind, region.Type, p, (min + (max - min) * u) * layout.Scale, grain[region.Index], rng, seed);
        double baseReach = form.BaseReach;
        if (Math.Abs(form.Centre.X) + baseReach > Map.HalfW - EdgeMargin * 0.6 || Math.Abs(form.Centre.Y) + baseReach > Map.HalfH - EdgeMargin * 0.6) return false;
        foreach (var q in placed)
            if (q.C.DistanceTo(form.Centre) < q.Reach + baseReach + Math.Max(q.Channel, layout.Channel) * 0.3) return false;
        // The delta's channels are the Mangrove Maze: keep it inside the region whose hull rule guards it.
        if (kind == Landform.Delta)
            for (int k = 0; k < 12; k++)
                if (map.RegionAt(form.Centre + Vec2.FromAngle(k * Angles.Tau / 12) * baseReach).Type != region.Type) return false;

        var polys = Coastlines.Trace(form.Field, form.Centre, form.Reach, form.Step);
        if (polys.Count == 0) return false;
        double reach = 0;
        foreach (var poly in polys)
            foreach (var v in poly)
            {
                if (Math.Abs(v.X) > Map.HalfW - EdgeMargin * 0.6 || Math.Abs(v.Y) > Map.HalfH - EdgeMargin * 0.6) return false;
                reach = Math.Max(reach, v.DistanceTo(form.Centre));
            }
        var islands = polys.Select(pts => new Island(pts)).ToList();
        // A rock hard against a bigger island of the same landform reads as a crack in its coast: let the sea have it.
        islands.RemoveAll(rock => Coastlines.Area(rock.Points) < 3000 && islands.Any(big => big != rock
            && Coastlines.Area(big.Points) > Coastlines.Area(rock.Points) && big.Centre.DistanceTo(rock.Centre) < big.BoundRadius + rock.BoundRadius + 25
            && Gap(rock, big) < 25));
        if (islands.Count == 0) return false;
        foreach (var island in islands)
            foreach (var other in map.IslandsNear(island.Centre, island.BoundRadius + 200))
            {
                double need = Math.Max(40, Math.Max(layout.Channel, RegionLayout.Of(other.Region).Channel) * 0.6);
                if (island.Centre.DistanceTo(other.Centre) > island.BoundRadius + other.BoundRadius + need) continue;
                if (Gap(island, other) < need) return false;
            }

        placed.Add(new Footprint(form.Centre, reach, layout.Channel));
        foreach (var island in islands) covered[region.Index] += Coastlines.Area(island.Points);
        foreach (var island in islands)
        {
            island.Id = map.Islands.Count;
            island.Radius = Math.Sqrt(Coastlines.Area(island.Points) / Math.PI);
            island.Region = map.RegionAt(island.Centre).Type;
            island.Form = kind;
            island.Group = placed.Count - 1;
            map.Islands.Add(island);
        }
        return true;
    }

    /// <summary>Shortest distance between two coasts (vertex to edge, both ways).</summary>
    static double Gap(Island a, Island b)
    {
        double best = double.MaxValue;
        foreach (var v in a.Points) best = Math.Min(best, b.Closest(v).Dist);
        foreach (var v in b.Points) best = Math.Min(best, a.Closest(v).Dist);
        return best;
    }

    // ---- 2b. Whirlpools: the Maelstrom Straits' tide races turn in the narrows, and one great maelstrom in open water ----
    /// <summary>Share of a whirlpool's radius closed to the nav grid, so lanes and captains keep out of all but its weak rim.</summary>
    public const double WhirlpoolClosed = 0.85;

    static void PlaceWhirlpools(Map map, Rng rng)
    {
        var region = map.RegionOf(RegionType.Maelstrom);
        var narrows = new List<(Vec2 P, double Width)>();
        for (double y = region.Seed.Y - 2600; y <= region.Seed.Y + 2600; y += 60)
            for (double x = region.Seed.X - 2600; x <= region.Seed.X + 2600; x += 60)
            {
                var p = new Vec2(x, y);
                if (!Map.InBounds(p) || map.RegionAt(p).Type != RegionType.Maelstrom || !map.Nav.IsOpenSea(p)) continue;
                // A narrows: two different shores close by on roughly opposite sides.
                var shores = map.IslandsNear(p, 170).Select(i => i.Closest(p)).Where(c => !c.Inside && c.Dist < 170).ToList();
                bool found = false;
                double width = 0;
                for (int a = 0; a < shores.Count && !found; a++)
                    for (int b = a + 1; b < shores.Count && !found; b++)
                        if ((shores[a].Point - p).Normalized.Dot((shores[b].Point - p).Normalized) < -0.6)
                        {
                            found = true;
                            width = shores[a].Dist + shores[b].Dist;
                        }
                if (found && width > 110) narrows.Add((p, width));
            }
        Shuffle(narrows, rng);
        int want = 6 + rng.Next(5);
        foreach (var (p, width) in narrows)
        {
            if (map.Whirlpools.Count >= want) break;
            if (map.Whirlpools.Any(w => w.Pos.DistanceTo(p) < 600)) continue;
            map.Whirlpools.Add(new Whirlpool(p, Math.Clamp(width * 0.75, 80, 150), rng.Range(3, 4.5)));
        }
        // The great maelstrom: in the openest water near the region's heart.
        Vec2 best = region.Seed;
        double bestD = -1;
        for (int t = 0; t < 200; t++)
        {
            var p = region.Seed + new Vec2(rng.Range(-1800, 1800), rng.Range(-1800, 1800));
            if (!Map.InBounds(p) || map.RegionAt(p).Type != RegionType.Maelstrom || !map.Nav.IsOpenSea(p)) continue;
            double d = map.IslandsNear(p, 500).Select(i => i.Closest(p)).Select(c => c.Inside ? 0 : c.Dist).DefaultIfEmpty(500).Min();
            if (d > bestD && map.Whirlpools.All(w => w.Pos.DistanceTo(p) > 700)) { bestD = d; best = p; }
        }
        if (bestD >= 280) map.Whirlpools.Add(new Whirlpool(best, 260, 6));
        foreach (var w in map.Whirlpools) map.Nav.Close(w.Pos, w.Radius * WhirlpoolClosed);
    }

    // ---- 3. Ports on coasts ----
    static bool PlacePorts(Map map, Rng rng)
    {
        int n = map.Regions.Length;
        int coves = MinCoves + rng.Next(MaxCoves - MinCoves + 1);
        int total = MinPorts + rng.Next(MaxPorts - MinPorts + 1);
        int regular = total - coves;

        var perRegion = new int[n];
        int sum = 0;
        foreach (var r in map.Regions)
        {
            perRegion[r.Index] = r.Def.PortsMin;
            sum += r.Def.PortsMin;
        }
        int guard = 0;
        while (sum < regular && guard++ < 1000)
        {
            var r = map.Regions[rng.Next(n)];
            if (perRegion[r.Index] < r.Def.PortsMax)
            {
                perRegion[r.Index]++;
                sum++;
            }
        }
        if (sum != regular) return false;

        var byRegion = map.Islands.GroupBy(i => i.Region).ToDictionary(g => g.Key, g => g.ToList());
        var usedIslands = new HashSet<int>();
        var failedIslands = new HashSet<int>();
        var candidatesByRegion = new Dictionary<int, List<Island>>();
        var placedByRegion = new int[n];
        foreach (var region in map.Regions)
        {
            var islands = byRegion.TryGetValue(region.Type, out var list) ? list : new List<Island>();
            // A big island can hold a port on each of its coasts.
            var candidates = islands.Where(i => i.Radius >= (region.Def.IslandMaxR < 130 ? 50 : 70))
                .SelectMany(i => Enumerable.Repeat(i, Math.Min(4, 1 + (int)(Coastlines.Perimeter(i.Points) / 1600)))).ToList();
            Shuffle(candidates, rng);
            candidatesByRegion[region.Index] = candidates;
        }

        Port? PlaceIn(Region region)
        {
            var candidates = candidatesByRegion[region.Index];
            while (candidates.Count > 0)
            {
                var island = candidates[^1];
                candidates.RemoveAt(candidates.Count - 1);
                if (failedIslands.Contains(island.Id)) continue;
                var port = TryPlaceHarbour(map, rng, island, 90);
                if (port == null)
                {
                    failedIslands.Add(island.Id);
                    continue;
                }
                double roll = rng.NextDouble();
                port.Faction = roll < region.Def.CrownShare ? Faction.Crown : roll < region.Def.CrownShare + region.Def.FreeShare ? Faction.FreeTraders : Faction.Brethren;
                port.Fort = port.Faction == Faction.Crown;
                port.Size = region.Def.PortSizeBias switch
                {
                    0 => rng.NextDouble() < 0.6 ? 0 : 1,
                    1 => rng.NextDouble() switch { < 0.25 => 0, < 0.75 => 1, _ => 2 },
                    _ => rng.NextDouble() < 0.4 ? 1 : 2,
                };
                usedIslands.Add(island.Id);
                map.Ports.Add(port);
                placedByRegion[region.Index]++;
                return port;
            }
            return null;
        }

        // Every region gets its quota where the geometry allows; the shortfall goes to regions with room.
        foreach (var region in map.Regions)
            while (placedByRegion[region.Index] < perRegion[region.Index] && PlaceIn(region) != null) { }
        int rounds = 0;
        while (map.Ports.Count < regular && rounds++ < 40)
        {
            bool any = false;
            foreach (var region in map.Regions.OrderBy(r => placedByRegion[r.Index] - r.Def.PortsMax))
            {
                if (map.Ports.Count >= regular) break;
                if (placedByRegion[region.Index] >= region.Def.PortsMax + 2) continue;
                if (PlaceIn(region) != null) any = true;
            }
            if (!any) break;
        }
        if (map.Ports.Count + coves < MinPorts) return false;
        if (map.Regions.Any(r => placedByRegion[r.Index] == 0)) return false;

        // The start: a free port in the Trade Isles with a neighbour inside half a day's sail, or there is no first
        // trade to make. Convert one if the dice gave none.
        var trade = map.Ports.Where(p => p.Region == RegionType.TradeIsles
            && map.Ports.Any(o => o != p && o.Harbor.DistanceTo(p.Harbor) < StartRouteRange * 0.8)).ToList();
        if (trade.Count == 0) return false;
        var free = trade.Where(p => p.Faction == Faction.FreeTraders).ToList();
        if (free.Count == 0)
        {
            var conv = trade[rng.Next(trade.Count)];
            conv.Faction = Faction.FreeTraders;
            conv.Fort = false;
            free.Add(conv);
        }
        map.StartPortId = map.Ports.IndexOf(free[rng.Next(free.Count)]);

        // Secret coves: weighted toward the Mangrove Maze, the Fog Banks and the Corsair Keys, on islands without a port.
        var coveCandidates = map.Islands.Where(i => !usedIslands.Contains(i.Id) && i.Radius >= 50).ToList();
        int covesPlaced = 0;
        for (int tries = 0; tries < 800 && covesPlaced < coves && coveCandidates.Count > 0; tries++)
        {
            var island = WeightedPick(coveCandidates, i => RegionDef.Of(i.Region).CoveWeight, rng);
            var port = TryPlaceHarbour(map, rng, island, 70);
            coveCandidates.Remove(island);
            if (port == null) continue;
            port.Faction = Faction.FreeTraders;
            port.Secret = true;
            port.Size = 0;
            usedIslands.Add(island.Id);
            map.Ports.Add(port);
            covesPlaced++;
        }
        if (covesPlaced < MinCoves) return false;
        for (int i = 0; i < map.Ports.Count; i++) map.Ports[i].Id = i;
        return true;
    }

    /// <summary>
    /// The harbour stands off the coast along its true normal, in the open sea, clear of every shore. Of up to a dozen
    /// workable spots the sheltered ones win most often — bays, sounds and lagoons, where real ports grew.
    /// </summary>
    static Port? TryPlaceHarbour(Map map, Rng rng, Island island, double ring)
    {
        var pts = island.Points;
        int n = pts.Length;
        var order = Enumerable.Range(0, n).ToList();
        Shuffle(order, rng);
        var good = new List<(Port Port, double Weight)>();
        foreach (int vi in order)
        {
            if (good.Count >= 12) break;
            var pos = pts[vi];
            var t = pts[(vi + 2) % n] - pts[(vi - 2 + n) % n];
            var outward = new Vec2(t.Y, -t.X).Normalized;
            if (outward == Vec2.Zero) continue;
            var harbor = pos + outward * (ring * 0.85);
            if (Math.Abs(harbor.X) > Map.HalfW - 60 || Math.Abs(harbor.Y) > Map.HalfH - 60) continue;
            // The harbour lies in its island's own region: across a border it could sit in the Mangrove Maze, where no
            // big hull can follow.
            if (map.RegionAt(harbor).Type != island.Region) continue;
            if (!map.Nav.IsSea(harbor) || !map.Nav.IsOpenSea(harbor)) continue;
            if (map.Whirlpools.Any(w => w.Pos.DistanceTo(harbor) < w.Radius + ring)) continue;
            var (_, home, inside) = island.Closest(harbor);
            if (inside || home < ring * 0.6) continue;
            bool clear = true;
            foreach (var other in map.IslandsNear(harbor, ring + 30))
            {
                if (other == island) continue;
                var (_, dist, inOther) = other.Closest(harbor);
                if (inOther || dist < ring + 20)
                {
                    clear = false;
                    break;
                }
            }
            if (!clear) continue;
            // The ring must be mostly water: sample a few points on it against the home island.
            int wet = 0;
            for (int k = 0; k < 8; k++)
                if (!island.Contains(harbor + Vec2.FromAngle(k * Angles.Tau / 8) * ring)) wet++;
            if (wet < 5) continue;
            foreach (var p in map.Ports)
                if (p.Harbor.DistanceTo(harbor) < 320) { clear = false; break; }
            foreach (var g in good)
                if (g.Port.Harbor.DistanceTo(harbor) < 60) { clear = false; break; }
            if (!clear) continue;
            double shelter = Shelter(map, harbor);
            good.Add((new Port { Pos = pos, Harbor = harbor, RingRadius = ring, Region = island.Region, IslandId = island.Id }, 0.02 + Math.Pow(shelter, 4)));
        }
        return good.Count == 0 ? null : WeightedPick(good, g => g.Weight, rng).Port;
    }

    /// <summary>Share of sixteen bearings on which land stands within ~400 m: 0.4 on an open coast, near 1 in a lagoon.</summary>
    public static double Shelter(Map map, Vec2 at)
    {
        int blocked = 0;
        for (int k = 0; k < 16; k++)
        {
            var d = Vec2.FromAngle(k * Angles.Tau / 16);
            for (double r = 50; r <= 420; r += NavGrid.Cell)
                if (map.Nav.IsLand(at + d * r))
                {
                    blocked++;
                    break;
                }
        }
        return blocked / 16.0;
    }

    // ---- 4. Goods ----
    static void AssignGoods(Map map, Rng rng)
    {
        var luxuries = Goods.All.Where(g => g.Group == GoodGroup.Luxury).Select(g => g.Id).ToList();
        var rares = Goods.All.Where(g => g.Group == GoodGroup.Rare).Select(g => g.Id).ToList();
        foreach (var port in map.Ports)
        {
            var def = RegionDef.Of(port.Region);
            if (port.Secret)
            {
                port.Produces.AddRange(Pick(rares, 1 + rng.Next(2), rng));
                port.Produces.Add(luxuries[rng.Next(luxuries.Count)]);
                port.Consumes.AddRange(Pick(new List<Good> { Good.Provisions, Good.Rum, Good.Muskets, Good.Tools }, 2, rng));
                continue;
            }
            port.Produces.AddRange(Pick(def.Produces.ToList(), 2 + rng.Next(3), rng));
            var cons = def.Consumes.Where(g => !port.Produces.Contains(g)).ToList();
            port.Consumes.AddRange(Pick(cons, 3 + rng.Next(3), rng));
        }

        var regular = map.Ports.Where(p => !p.Secret).ToList();
        foreach (var good in Goods.All)
        {
            var g = good.Id;
            if (good.Group == GoodGroup.Rare)
            {
                // Sold only by coves; bought dearly in Storm Reach and at the big ports.
                foreach (var p in regular)
                    if ((p.Region == RegionType.StormReach || p.Size == 2) && !p.Consumes.Contains(g)) p.Consumes.Add(g);
                continue;
            }
            int producers = map.Ports.Count(p => p.Produces.Contains(g));
            var lists = regular.Where(p => RegionDef.Of(p.Region).Produces.Contains(g) && !p.Produces.Contains(g) && !p.Consumes.Contains(g)).ToList();
            while (producers < 2)
            {
                var pool = lists.Count > 0 ? lists : regular.Where(p => !p.Produces.Contains(g) && !p.Consumes.Contains(g)).ToList();
                if (pool.Count == 0) break;
                var pick = pool[rng.Next(pool.Count)];
                pick.Produces.Add(g);
                lists.Remove(pick);
                producers++;
            }
            int consumers = map.Ports.Count(p => p.Consumes.Contains(g));
            var clists = regular.Where(p => RegionDef.Of(p.Region).Consumes.Contains(g) && !p.Produces.Contains(g) && !p.Consumes.Contains(g)).ToList();
            while (consumers < 3)
            {
                var pool = clists.Count > 0 ? clists : regular.Where(p => !p.Produces.Contains(g) && !p.Consumes.Contains(g)).ToList();
                if (pool.Count == 0) break;
                var pick = pool[rng.Next(pool.Count)];
                pick.Consumes.Add(g);
                clists.Remove(pick);
                consumers++;
            }
        }

        // Luxuries sell best far from where they are made.
        foreach (var g in luxuries)
        {
            var producers = map.Ports.Where(p => p.Produces.Contains(g)).ToList();
            var consumers = map.Ports.Where(p => p.Consumes.Contains(g)).ToList();
            foreach (var c in consumers.OrderBy(c => producers.Min(p => p.Harbor.DistanceTo(c.Harbor))))
            {
                if (map.Ports.Count(p => p.Consumes.Contains(g)) <= 3) break;
                if (c.Consumes.Count <= 3) continue;   // never strip a port's market bare
                if (producers.Any(p => p.Harbor.DistanceTo(c.Harbor) < LuxuryDistance)) c.Consumes.Remove(g);
            }
        }
    }

    // ---- 5. Validation ----
    static bool Validate(Map map)
    {
        var start = map.StartPort;
        var d = map.Nav.Distances(start.Harbor);
        foreach (var port in map.Ports)
            if (map.Nav.DistanceAt(d, port.Harbor) < 0) return false;
        // Only small hulls fit the Mangrove Maze, so every harbour outside it must be reachable without crossing it.
        {
            var inMaze = new sbyte[map.Nav.W * map.Nav.H];   // 0 unknown, 1 maze, −1 not: each cell's region asked once
            bool Maze(int x, int y)
            {
                ref sbyte m = ref inMaze[y * map.Nav.W + x];
                if (m == 0) m = map.RegionAt(NavGrid.Centre(x, y)).Type == RegionType.Mangrove ? (sbyte)1 : (sbyte)-1;
                return m > 0;
            }
            var big = map.Nav.Distances(start.Harbor, Maze);
            foreach (var port in map.Ports)
                if (port.Region != RegionType.Mangrove && map.Nav.DistanceAt(big, port.Harbor) < 0) return false;
        }
        // A profitable trade within half a day: something the start port makes that a nearby port does not. The
        // buyer must be a charted market: a secret cove is not on the chart.
        bool route = false;
        foreach (var g in start.Produces)
            foreach (var port in map.Ports)
            {
                if (port == start || port.Produces.Contains(g) || port.Secret) continue;
                double dist = map.Nav.DistanceAt(d, port.Harbor);
                if (dist >= 0 && dist <= StartRouteRange)
                {
                    route = true;
                    break;
                }
            }
        return route;
    }

    // ---- 6. Treasure and wrecks ----
    static void PlaceTreasure(Map map, Rng rng)
    {
        int count = 26 + rng.Next(15);
        var portIslands = map.Ports.Select(p => p.IslandId).ToHashSet();
        var candidates = map.Islands.Where(i => i.Radius >= 60 && !portIslands.Contains(i.Id)).ToList();
        for (int tries = 0; tries < 900 && map.Treasures.Count < count && candidates.Count > 0; tries++)
        {
            var island = WeightedPick(candidates, i => RegionDef.Of(i.Region).TreasureWeight, rng);
            candidates.Remove(island);
            if (DigSite(map, rng, island) is { } site)
            {
                site.Id = map.Treasures.Count;
                map.Treasures.Add(site);
            }
        }
    }

    /// <summary>The X somewhere inland (an island may be any shape), the dig ring off the nearest shore, in the open sea.</summary>
    static TreasureSite? DigSite(Map map, Rng rng, Island island)
    {
        double minX = island.Points.Min(q => q.X), maxX = island.Points.Max(q => q.X);
        double minY = island.Points.Min(q => q.Y), maxY = island.Points.Max(q => q.Y);
        for (int t = 0; t < 40; t++)
        {
            var x = new Vec2(rng.Range(minX, maxX), rng.Range(minY, maxY));
            var (coast, inland, inside) = island.Closest(x);
            if (!inside || inland < Math.Min(25, island.Radius * 0.3)) continue;
            var ring = coast + (coast - x).Normalized * 60;
            if (!map.Nav.IsSea(ring) || !map.Nav.IsOpenSea(ring) || map.Whirlpools.Any(w => w.Pos.DistanceTo(ring) < w.Radius + 70)) continue;
            return new TreasureSite { IslandId = island.Id, Pos = x, DigRing = ring };
        }
        return null;
    }

    static void PlaceWrecks(Map map, Rng rng)
    {
        int count = 9 + rng.Next(7);
        var region = map.RegionOf(RegionType.Sargasso);
        for (int tries = 0; tries < 3000 && map.Wrecks.Count < count; tries++)
        {
            var p = region.Seed + new Vec2(rng.Range(-2600, 2600), rng.Range(-2200, 2200));
            if (!Map.InBounds(p) || map.RegionAt(p).Type != RegionType.Sargasso) continue;
            if (!map.Nav.IsSea(p) || !map.Nav.IsOpenSea(p) || map.IslandsNear(p, 120).Any(i => i.Closest(p).Dist < 120)) continue;
            if (map.Wrecks.Any(w => w.Pos.DistanceTo(p) < 400)) continue;
            map.Wrecks.Add(new Wreck { Id = map.Wrecks.Count, Pos = p });
        }
    }

    static void NamePorts(Map map, Rng rng)
    {
        var taken = new HashSet<string>();
        foreach (var port in map.Ports)
            port.Name = Names.Port(rng, port.Faction, port.Secret, taken);
    }

    // ---- helpers ----
    static void Shuffle<T>(List<T> list, Rng rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    static List<T> Pick<T>(List<T> from, int n, Rng rng)
    {
        var copy = new List<T>(from);
        Shuffle(copy, rng);
        return copy.Take(Math.Min(n, copy.Count)).ToList();
    }

    static T WeightedPick<T>(List<T> items, Func<T, double> weight, Rng rng)
    {
        double total = 0;
        foreach (var i in items) total += weight(i);
        double roll = rng.NextDouble() * total;
        foreach (var i in items)
        {
            roll -= weight(i);
            if (roll <= 0) return i;
        }
        return items[^1];
    }
}
