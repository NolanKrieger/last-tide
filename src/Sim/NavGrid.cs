namespace LastTide.Sim;

/// <summary>
/// Coarse sea/land grid over the map for reachability, distances and AI lanes. A cell is land if its centre is
/// within <see cref="Margin"/> of any island, so paths keep clear of coasts — and, on maps of generator v4 and later,
/// also if the coastline itself passes through it, so no "sea" cell holds a corner of real land.
/// </summary>
public sealed class NavGrid
{
    public const double Cell = 25;
    public const double Margin = 10;
    public readonly int W, H;
    readonly bool[] land;
    /// <summary>Every island's coast pushed out by <see cref="Margin"/>: what a straight lane must not cross.</summary>
    readonly List<Island> shells = new();

    public NavGrid(List<Island> islands, bool closeCoasts = true)
    {
        W = (int)Math.Ceiling(Map.Width / Cell);
        H = (int)Math.Ceiling(Map.Height / Cell);
        land = new bool[W * H];
        foreach (var island in islands)
        {
            // Test against the coast pushed outward by the margin: one containment test per cell.
            var pts = island.Points;
            var grown = new Vec2[pts.Length];
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < pts.Length; i++)
            {
                var prev = pts[(i + pts.Length - 1) % pts.Length];
                var next = pts[(i + 1) % pts.Length];
                var n = ((pts[i] - prev).Perp.Normalized + (next - pts[i]).Perp.Normalized).Normalized;
                // Perp points inward for these clockwise-on-screen polygons; push the other way.
                grown[i] = pts[i] - n * Margin;
                minX = Math.Min(minX, grown[i].X); maxX = Math.Max(maxX, grown[i].X);
                minY = Math.Min(minY, grown[i].Y); maxY = Math.Max(maxY, grown[i].Y);
            }
            var shell = new Island(grown);
            shells.Add(shell);
            int x0 = Math.Max(0, ToX(minX)), x1 = Math.Min(W - 1, ToX(maxX));
            int y0 = Math.Max(0, ToY(minY)), y1 = Math.Min(H - 1, ToY(maxY));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (land[y * W + x]) continue;
                    if (shell.Contains(Centre(x, y))) land[y * W + x] = true;
                }
            if (closeCoasts)
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                    MarkCellsCrossed(pts[j], pts[i]);
        }
    }

    /// <summary>Marks as land every cell the segment passes through (grid traversal, corners included).</summary>
    void MarkCellsCrossed(Vec2 a, Vec2 b)
    {
        int x = ToX(a.X), y = ToY(a.Y), x1 = ToX(b.X), y1 = ToY(b.Y);
        var d = b - a;
        int stepX = Math.Sign(d.X), stepY = Math.Sign(d.Y);
        // Distance along the segment (as a fraction) to the next vertical / horizontal cell boundary, and per cell.
        double nextX = stepX == 0 ? double.MaxValue : ((x + (stepX > 0 ? 1 : 0)) * Cell - Map.HalfW - a.X) / d.X;
        double nextY = stepY == 0 ? double.MaxValue : ((y + (stepY > 0 ? 1 : 0)) * Cell - Map.HalfH - a.Y) / d.Y;
        double deltaX = stepX == 0 ? double.MaxValue : Cell / Math.Abs(d.X);
        double deltaY = stepY == 0 ? double.MaxValue : Cell / Math.Abs(d.Y);
        for (int guard = 0; guard < 4096; guard++)
        {
            if (x >= 0 && y >= 0 && x < W && y < H) land[y * W + x] = true;
            if (x == x1 && y == y1) break;
            if (Math.Abs(nextX - nextY) < 1e-12)
            {
                // Through a corner exactly: close both neighbours too, so no diagonal gap is left.
                if (x + stepX >= 0 && x + stepX < W && y >= 0 && y < H) land[y * W + x + stepX] = true;
                if (x >= 0 && x < W && y + stepY >= 0 && y + stepY < H) land[(y + stepY) * W + x] = true;
                x += stepX; y += stepY; nextX += deltaX; nextY += deltaY;
            }
            else if (nextX < nextY) { x += stepX; nextX += deltaX; }
            else { y += stepY; nextY += deltaY; }
        }
    }

    public static int ToX(double x) => (int)Math.Floor((x + Map.HalfW) / Cell);
    public static int ToY(double y) => (int)Math.Floor((y + Map.HalfH) / Cell);
    public static Vec2 Centre(int x, int y) => new(-Map.HalfW + (x + 0.5) * Cell, -Map.HalfH + (y + 0.5) * Cell);

    public bool IsLand(int x, int y) => x < 0 || y < 0 || x >= W || y >= H || land[y * W + x];
    public bool IsLand(Vec2 p) => IsLand(ToX(p.X), ToY(p.Y));
    public bool IsSea(Vec2 p) => !IsLand(p);

    bool[]? open;

    /// <summary>Closes every cell within <paramref name="radius"/> of a point, as if land (a whirlpool's core).</summary>
    public void Close(Vec2 c, double radius)
    {
        for (int y = ToY(c.Y - radius); y <= ToY(c.Y + radius); y++)
            for (int x = ToX(c.X - radius); x <= ToX(c.X + radius); x++)
                if (x >= 0 && y >= 0 && x < W && y < H && Centre(x, y).DistanceTo(c) <= radius) land[y * W + x] = true;
        open = null;
    }

    /// <summary>
    /// True in the largest connected body of sea: the open water every ship shares. A lagoon whose pass is too tight for
    /// the grid, or a pocket of water walled in by islands, is sea but not open sea.
    /// </summary>
    public bool IsOpenSea(Vec2 p)
    {
        open ??= LargestSea();
        int x = ToX(p.X), y = ToY(p.Y);
        return x >= 0 && y >= 0 && x < W && y < H && open[y * W + x];
    }

    bool[] LargestSea()
    {
        var label = new int[W * H];
        int best = 0, bestSize = -1, next = 0;
        var queue = new Queue<int>();
        for (int s = 0; s < label.Length; s++)
        {
            if (land[s] || label[s] != 0) continue;
            label[s] = ++next;
            int size = 0;
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                size++;
                int x = c % W, y = c / W;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx == 0 && dy == 0) || IsLand(nx, ny) || label[ny * W + nx] != 0) continue;
                        if (dx != 0 && dy != 0 && IsLand(x + dx, y) && IsLand(x, y + dy)) continue;   // the same corner rule as Distances
                        label[ny * W + nx] = next;
                        queue.Enqueue(ny * W + nx);
                    }
            }
            if (size > bestSize) { bestSize = size; best = next; }
        }
        var result = new bool[W * H];
        for (int i = 0; i < result.Length; i++) result[i] = label[i] == best;
        return result;
    }

    /// <summary>Breadth-first sea distances (in metres, 8-connected) from a point; −1 where unreachable.</summary>
    public double[] Distances(Vec2 from) => Distances(from, null);

    /// <summary>As <see cref="Distances(Vec2)"/>, treating the cells <paramref name="closed"/> names as land too.</summary>
    public double[] Distances(Vec2 from, Func<int, int, bool>? closed)
    {
        var dist = new double[W * H];
        Array.Fill(dist, -1);
        bool Blocked(int x, int y) => IsLand(x, y) || closed != null && closed(x, y);
        int sx = ToX(from.X), sy = ToY(from.Y);
        if (IsLand(sx, sy))
        {
            // Start on the nearest sea cell so a harbour tucked against the coast still counts.
            (sx, sy) = NearestSea(sx, sy);
        }
        if (Blocked(sx, sy)) return dist;
        var queue = new Queue<(int, int)>();
        dist[sy * W + sx] = 0;
        queue.Enqueue((sx, sy));
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            double d = dist[y * W + x];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (Blocked(nx, ny) || dist[ny * W + nx] >= 0) continue;   // Blocked first: it bounds-checks
                    // No corner-cutting between two land cells.
                    if (dx != 0 && dy != 0 && Blocked(x + dx, y) && Blocked(x, y + dy)) continue;
                    dist[ny * W + nx] = d + (dx != 0 && dy != 0 ? Cell * 1.41421356 : Cell);
                    queue.Enqueue((nx, ny));
                }
        }
        return dist;
    }

    /// <summary>A sea-distance field (m, −1 unreachable) confined to a window around a point, for local lanes.</summary>
    public readonly record struct LocalField(int X0, int Y0, int W, int H, float[] D)
    {
        public float At(int x, int y) => x < X0 || y < Y0 || x >= X0 + W || y >= Y0 + H ? -1 : D[(y - Y0) * W + x - X0];
    }

    /// <summary>As <see cref="Distances(Vec2)"/>, but only within <paramref name="radius"/> (square window) of the start.</summary>
    public LocalField LocalDistances(Vec2 from, double radius)
    {
        int r = (int)Math.Ceiling(radius / Cell);
        int sx = ToX(from.X), sy = ToY(from.Y);
        if (IsLand(sx, sy)) (sx, sy) = NearestSea(sx, sy);
        int x0 = Math.Max(0, sx - r), y0 = Math.Max(0, sy - r), x1 = Math.Min(W - 1, sx + r), y1 = Math.Min(H - 1, sy + r);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        var dist = new float[w * h];
        Array.Fill(dist, -1f);
        var field = new LocalField(x0, y0, w, h, dist);
        if (IsLand(sx, sy)) return field;
        bool Blocked(int x, int y) => x < x0 || y < y0 || x > x1 || y > y1 || IsLand(x, y);
        var queue = new Queue<(int, int)>();
        dist[(sy - y0) * w + sx - x0] = 0;
        queue.Enqueue((sx, sy));
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            float d = dist[(y - y0) * w + x - x0];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (Blocked(nx, ny) || dist[(ny - y0) * w + nx - x0] >= 0) continue;
                    if (dx != 0 && dy != 0 && Blocked(x + dx, y) && Blocked(x, y + dy)) continue;
                    dist[(ny - y0) * w + nx - x0] = d + (float)(dx != 0 && dy != 0 ? Cell * 1.41421356 : Cell);
                    queue.Enqueue((nx, ny));
                }
        }
        return field;
    }

    public (int, int) NearestSea(int x, int y)
    {
        for (int r = 1; r < 20; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (!IsLand(x + dx, y + dy)) return (x + dx, y + dy);
        return (x, y);
    }

    public double DistanceAt(double[] distances, Vec2 p)
    {
        int x = ToX(p.X), y = ToY(p.Y);
        if (IsLand(x, y)) (x, y) = NearestSea(x, y);
        return x < 0 || y < 0 || x >= W || y >= H ? -1 : distances[y * W + x];
    }

    /// <summary>
    /// True when the straight segment keeps clear of every coast by the <see cref="Margin"/>: it neither crosses nor
    /// starts inside a grown coastline. Exact, unlike sampling the grid (a leg could clip a coast between samples).
    /// </summary>
    public bool SegmentClear(Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = ab.LengthSq;
        foreach (var shell in shells)
        {
            // Skip shells whose bounding circle the segment does not reach.
            double t = len2 > 1e-12 ? Math.Clamp((shell.Centre - a).Dot(ab) / len2, 0, 1) : 0;
            if ((a + ab * t).DistanceTo(shell.Centre) > shell.BoundRadius) continue;
            if (shell.Contains(a) || shell.Contains(b)) return false;
            var pts = shell.Points;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                if (SegmentsCross(a, b, pts[j], pts[i])) return false;
        }
        return true;
    }

    static bool SegmentsCross(Vec2 p, Vec2 q, Vec2 r, Vec2 s)
    {
        double d1 = (q - p).Cross(r - p), d2 = (q - p).Cross(s - p);
        double d3 = (s - r).Cross(p - r), d4 = (s - r).Cross(q - r);
        return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
    }

    public int SeaCells
    {
        get
        {
            int n = 0;
            foreach (var l in land) if (!l) n++;
            return n;
        }
    }
}
