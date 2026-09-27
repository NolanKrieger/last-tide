namespace LastTide.Sim;

/// <summary>
/// Builds the archipelago (GDD §5): nine warped Voronoi regions, dart-thrown islands with
/// region-specific size and channel width, ports on coasts with a faction mix, goods drawn from
/// region tables and repaired to the global constraints, then validation: every port reachable by
/// sea and a profitable trade within half a day of the start. Deterministic per seed.
/// </summary>
public static class MapGen
{
    public const int MinPorts = 35, MaxPorts = 40, MinCoves = 3, MaxCoves = 5;
    /// <summary>
    /// The generator's rules version. 3: the start-route guarantee ignores secret coves (they are not on the chart).
    /// 4: the nav grid also closes every cell a coastline crosses, so no sea cell holds a corner of land.
    /// 5: a harbour lies in its own island's region, and a big hull can reach every harbour outside the Mangrove Maze.
    /// A suspend save records the version its map was made with, so an older voyage resumes on its own map.
    /// </summary>
    public const int Version = 5;
    public const double StartRouteRange = 600;   // metres of sea: about half a day's sail
    public const double EdgeMargin = 220;

    public static Map Generate(int seed, int version = Version)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var rng = new Rng(unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + (ulong)attempt * 0xBF58476D1CE4E5B9UL + 7));
            var map = TryGenerate(seed, attempt, rng, version);
            if (map != null)
            {
                map.Attempts = attempt + 1;
                return map;
            }
        }
        throw new InvalidOperationException($"map generation failed for seed {seed}");
    }

    static Map? TryGenerate(int seed, int attempt, Rng rng, int version)
    {
        var map = new Map { Seed = unchecked(seed * 131 + attempt), Version = version };
        PlaceRegions(map, rng);
        PlaceIslands(map, rng);
        map.Nav = new NavGrid(map.Islands, closeCoasts: version >= 4);
        if (!PlacePorts(map, rng)) return null;
        AssignGoods(map, rng);
        if (!Validate(map, version)) return null;
        PlaceTreasure(map, rng);
        PlaceWrecks(map, rng);
        NamePorts(map, rng);
        return map;
    }

    // ---- 1. Regions: a jittered 3×3 grid of seeds, the nine types shuffled over it ----
    static void PlaceRegions(Map map, Rng rng)
    {
        var types = Enum.GetValues<RegionType>();
        for (int i = types.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (types[i], types[j]) = (types[j], types[i]);
        }
        map.Regions = new Region[9];
        for (int i = 0; i < 9; i++)
        {
            int col = i % 3, row = i / 3;
            var seedPos = new Vec2((col - 1) * 2000 + rng.Range(-350, 350), (row - 1) * 1500 + rng.Range(-260, 260));
            map.Regions[i] = new Region { Index = i, Type = types[i], Seed = seedPos };
        }
    }

    // ---- 2. Islands: darts with a coast-to-coast channel rule and per-region density caps ----
    static void PlaceIslands(Map map, Rng rng)
    {
        var caps = new int[9];
        var counts = new int[9];
        foreach (var r in map.Regions)
        {
            double area = Map.Width * Map.Height / 9;
            caps[r.Index] = Math.Max(2, (int)(area / (r.Def.IslandSpacing * r.Def.IslandSpacing)));
        }
        var accepted = new List<(Vec2 P, double R, double Channel)>();
        int sinceLast = 0;
        for (int dart = 0; dart < 9000 && sinceLast < 1500; dart++, sinceLast++)
        {
            var p = new Vec2(rng.Range(-Map.HalfW + EdgeMargin, Map.HalfW - EdgeMargin), rng.Range(-Map.HalfH + EdgeMargin, Map.HalfH - EdgeMargin));
            var region = map.RegionAt(p);
            if (counts[region.Index] >= caps[region.Index]) continue;
            var def = region.Def;
            double r = rng.Range(def.IslandMinR, def.IslandMaxR);
            if (Math.Abs(p.X) + r > Map.HalfW - EdgeMargin * 0.6 || Math.Abs(p.Y) + r > Map.HalfH - EdgeMargin * 0.6) continue;
            // Cheap pass first with the largest radius the blob can reach, then the real bounding radius.
            double rMax = r * (1 + def.Roughness * 1.45);
            bool ok = true;
            foreach (var q in accepted)
            {
                if (p.DistanceTo(q.P) < rMax * 0.85 + q.R + Math.Max(def.Channel, q.Channel))
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;
            var island = Island.Blob(map.Seed, map.Islands.Count, p, r, def.Roughness);
            foreach (var q in accepted)
            {
                if (p.DistanceTo(q.P) < island.BoundRadius + q.R + Math.Max(def.Channel, q.Channel))
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;
            sinceLast = 0;
            accepted.Add((p, island.BoundRadius, def.Channel));
            counts[region.Index]++;
            island.Id = map.Islands.Count;
            island.Region = region.Type;
            map.Islands.Add(island);
        }
    }

    // ---- 3. Ports on coasts ----
    static bool PlacePorts(Map map, Rng rng)
    {
        int coves = MinCoves + rng.Next(MaxCoves - MinCoves + 1);
        int total = MinPorts + rng.Next(MaxPorts - MinPorts + 1);
        int regular = total - coves;

        var perRegion = new int[9];
        int sum = 0;
        foreach (var r in map.Regions)
        {
            perRegion[r.Index] = r.Def.PortsMin;
            sum += r.Def.PortsMin;
        }
        int guard = 0;
        while (sum < regular && guard++ < 200)
        {
            var r = map.Regions[rng.Next(9)];
            if (perRegion[r.Index] < r.Def.PortsMax)
            {
                perRegion[r.Index]++;
                sum++;
            }
        }
        if (sum != regular) return false;

        var byRegion = map.Islands.GroupBy(i => i.Region).ToDictionary(g => g.Key, g => g.ToList());
        var usedIslands = new HashSet<int>();
        var candidatesByRegion = new Dictionary<int, List<Island>>();
        var placedByRegion = new int[9];
        foreach (var region in map.Regions)
        {
            var islands = byRegion.TryGetValue(region.Type, out var list) ? list : new List<Island>();
            var candidates = islands.Where(i => i.Radius >= (region.Def.IslandMaxR < 130 ? 50 : 70)).ToList();
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
                if (usedIslands.Contains(island.Id)) continue;
                var port = TryPlacePort(map, rng, island, 90, secret: false, map.Version);
                if (port == null) continue;
                port.Faction = rng.NextDouble() switch { < 0.44 => Faction.Crown, < 0.83 => Faction.FreeTraders, _ => Faction.Brethren };
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
                if (placedByRegion[region.Index] >= region.Def.PortsMax + 1) continue;
                if (PlaceIn(region) != null) any = true;
            }
            if (!any) break;
        }
        if (map.Ports.Count + coves < MinPorts) return false;
        if (map.Regions.Any(r => placedByRegion[r.Index] == 0)) return false;

        // The start: a free port in the Trade Isles. Convert one if the dice gave none.
        var trade = map.Ports.Where(p => p.Region == RegionType.TradeIsles).ToList();
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

        // Secret coves: weighted toward the Mangrove Maze and Fog Banks, on islands without a port.
        var coveCandidates = map.Islands.Where(i => !usedIslands.Contains(i.Id) && i.Radius >= 50).ToList();
        int covesPlaced = 0;
        for (int tries = 0; tries < 400 && covesPlaced < coves && coveCandidates.Count > 0; tries++)
        {
            var island = WeightedPick(coveCandidates, i => RegionDef.Of(i.Region).CoveWeight, rng);
            var port = TryPlacePort(map, rng, island, 70, secret: true, map.Version);
            if (port == null)
            {
                coveCandidates.Remove(island);
                continue;
            }
            port.Faction = Faction.FreeTraders;
            port.Secret = true;
            port.Size = 0;
            usedIslands.Add(island.Id);
            coveCandidates.Remove(island);
            map.Ports.Add(port);
            covesPlaced++;
        }
        if (covesPlaced < MinCoves) return false;
        for (int i = 0; i < map.Ports.Count; i++) map.Ports[i].Id = i;
        return true;
    }

    static Port? TryPlacePort(Map map, Rng rng, Island island, double ring, bool secret, int version)
    {
        var order = Enumerable.Range(0, island.Points.Length).ToList();
        Shuffle(order, rng);
        foreach (int vi in order)
        {
            var pos = island.Points[vi];
            var n = (pos - island.Centre).Normalized;
            var harbor = pos + n * (ring * 0.85);
            if (Math.Abs(harbor.X) > Map.HalfW - 60 || Math.Abs(harbor.Y) > Map.HalfH - 60) continue;
            // v5: the harbour lies in the island's own region. An island near a border could put its harbour across it —
            // a Siren's Ruins port whose harbour sat in the Mangrove Maze, which no big hull could enter.
            if (version >= 5 && map.RegionAt(harbor).Type != island.Region) continue;
            if (!map.Nav.IsSea(harbor)) continue;
            bool clear = true;
            foreach (var other in map.IslandsNear(harbor, ring + 30))
            {
                if (other == island) continue;
                var (_, dist, inside) = other.Closest(harbor);
                if (inside || dist < ring + 20)
                {
                    clear = false;
                    break;
                }
            }
            if (!clear) continue;
            // The ring must be mostly water: sample a few points on it against the home island.
            int wet = 0;
            for (int k = 0; k < 8; k++)
            {
                var s = harbor + Vec2.FromAngle(k * Angles.Tau / 8) * ring;
                if (!island.Contains(s)) wet++;
            }
            if (wet < 5) continue;
            foreach (var p in map.Ports)
                if (p.Harbor.DistanceTo(harbor) < 320) { clear = false; break; }
            if (!clear) continue;
            return new Port
            {
                Pos = pos, Harbor = harbor, RingRadius = ring, Region = island.Region, IslandId = island.Id,
            };
        }
        return null;
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
                if (producers.Any(p => p.Harbor.DistanceTo(c.Harbor) < 1500)) c.Consumes.Remove(g);
            }
        }
    }

    // ---- 5. Validation ----
    static bool Validate(Map map, int version)
    {
        var start = map.StartPort;
        var d = map.Nav.Distances(start.Harbor);
        foreach (var port in map.Ports)
            if (map.Nav.DistanceAt(d, port.Harbor) < 0) return false;
        // v5: only small hulls fit the Mangrove Maze, so every harbour outside it must be reachable without crossing it.
        if (version >= 5)
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
        // buyer must be a charted market: a secret cove is not on the chart (version 3; older maps let one count).
        bool route = false;
        foreach (var g in start.Produces)
            foreach (var port in map.Ports)
            {
                if (port == start || port.Produces.Contains(g) || (version >= 3 && port.Secret)) continue;
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
        int count = 8 + rng.Next(7);
        var portIslands = map.Ports.Select(p => p.IslandId).ToHashSet();
        var candidates = map.Islands.Where(i => i.Radius >= 60 && !portIslands.Contains(i.Id)).ToList();
        for (int tries = 0; tries < 300 && map.Treasures.Count < count && candidates.Count > 0; tries++)
        {
            var island = WeightedPick(candidates, i => RegionDef.Of(i.Region).TreasureWeight, rng);
            candidates.Remove(island);
            double a = rng.Range(0, Angles.Tau);
            var x = island.Centre + Vec2.FromAngle(a) * island.Radius * rng.Range(0.25, 0.5);
            if (!island.Contains(x)) x = island.Centre;
            var (coast, _, _) = island.Closest(x + Vec2.FromAngle(a) * island.Radius * 2);
            var n = (coast - island.Centre).Normalized;
            var ring = coast + n * 60;
            if (!map.Nav.IsSea(ring)) continue;
            map.Treasures.Add(new TreasureSite { Id = map.Treasures.Count, IslandId = island.Id, Pos = x, DigRing = ring });
        }
    }

    static void PlaceWrecks(Map map, Rng rng)
    {
        int count = 3 + rng.Next(4);
        var region = map.RegionOf(RegionType.Sargasso);
        for (int tries = 0; tries < 600 && map.Wrecks.Count < count; tries++)
        {
            var p = region.Seed + new Vec2(rng.Range(-900, 900), rng.Range(-700, 700));
            if (!Map.InBounds(p) || map.RegionAt(p).Type != RegionType.Sargasso) continue;
            if (!map.Nav.IsSea(p) || map.IslandsNear(p, 120).Any(i => i.Closest(p).Dist < 120)) continue;
            if (map.Wrecks.Any(w => w.Pos.DistanceTo(p) < 300)) continue;
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
