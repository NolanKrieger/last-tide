using System.Runtime.CompilerServices;

namespace LastTide.Sim;

/// <summary>
/// Traces a landform's field into island polygons: sample on a grid, close channels too narrow to sail and drop slivers
/// too thin to stand (morphological closing, then opening), fill any water the sea cannot reach, march the squares, and
/// simplify. Every polygon is simple, wound like <see cref="Island.Blob"/> (positive shoelace area) and has no holes.
/// </summary>
public static class Coastlines
{
    // Scratch arrays reused trace to trace (per thread: tests generate maps in parallel), so a map's thousand traces
    // don't churn the garbage collector. Each is sized to at least the current grid; only the first N entries are used.
    [ThreadStatic] static double[]? poolV, poolTmp;
    [ThreadStatic] static bool[]? poolSure, poolBand, poolReached, poolSeen;
    [ThreadStatic] static int[]? poolSum, poolNext, poolStack;
    [ThreadStatic] static Vec2[]? poolPos;

    static T[] Rent<T>(ref T[]? pool, int n)
    {
        if (pool == null || pool.Length < n) pool = new T[Math.Max(n, (pool?.Length ?? 0) * 3 / 2)];
        return pool;
    }
    public const double Step = 10;          // default sample spacing (m)
    public const double MinArea = 450;      // smaller specks are dropped (m²)
    const double Tolerance = 1.2;           // simplification error (m)
    const double MaxEdge = 28;              // longest edge kept, so the smoothed coast follows the polygon

    // Closing by an octagon of radius 2 samples (a 3×3 square, then a plus) fills gaps under ~40 m; opening by a 3×3
    // square takes away slivers under ~20 m. Each pass is a few taps, and only samples near a coast are touched.

    /// <param name="slope">A practical bound on how fast the field changes (per metre): coarse cells whose corners all
    /// lie further from zero than it could fall across them are filled by interpolation instead of sampled.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static List<Vec2[]> Trace(Func<Vec2, double> field, Vec2 centre, double reach, double step = Step, double slope = 2.5)
    {
        const int C = 4;   // coarse cell, in samples
        int n = (int)Math.Ceiling((reach + 5 * step) / step / C) * C;
        int nx = 2 * n + 1, ny = nx;
        var origin = centre - new Vec2(n * step, n * step);
        int N = nx * ny;
        var v = Rent(ref poolV, N);
        for (int y = 0; y < ny; y += C)
            for (int x = 0; x < nx; x += C)
                v[y * nx + x] = field(origin + new Vec2(x * step, y * step));
        double safe = slope * C * step * 0.75;
        int cells = (nx - 1) / C;
        var sure = Rent(ref poolSure, cells * cells);
        for (int j = 0; j < cells; j++)
            for (int i = 0; i < cells; i++)
            {
                int cx = i * C, cy = j * C;
                double a = v[cy * nx + cx], b = v[cy * nx + cx + C], c = v[(cy + C) * nx + cx], d = v[(cy + C) * nx + cx + C];
                sure[j * cells + i] = Math.Min(Math.Min(a, b), Math.Min(c, d)) > safe || Math.Max(Math.Max(a, b), Math.Max(c, d)) < -safe;
            }
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                if (x % C == 0 && y % C == 0) continue;   // a coarse corner, already sampled
                // Every coarse cell this sample touches (two along a shared edge) must be sure to skip it.
                int i0 = x % C == 0 ? x / C - 1 : x / C, i1 = Math.Min(x / C, cells - 1);
                int j0 = y % C == 0 ? y / C - 1 : y / C, j1 = Math.Min(y / C, cells - 1);
                bool skip = true;
                for (int j = Math.Max(0, j0); j <= j1 && skip; j++)
                    for (int i = Math.Max(0, i0); i <= i1 && skip; i++)
                        skip = sure[j * cells + i];
                if (!skip)
                {
                    v[y * nx + x] = field(origin + new Vec2(x * step, y * step));
                    continue;
                }
                int ci = Math.Min(x / C, cells - 1), cj = Math.Min(y / C, cells - 1);
                int cx = ci * C, cy = cj * C;
                double tx = (x - cx) / (double)C, ty = (y - cy) / (double)C;
                double a = v[cy * nx + cx], b = v[cy * nx + cx + C], c = v[(cy + C) * nx + cx], d = v[(cy + C) * nx + cx + C];
                v[y * nx + x] = (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
            }

        // The passes reach six samples: only samples that close to a coast can change side.
        var band = Band(v, nx, ny, 6);
        var tmp = Rent(ref poolTmp, N);
        Array.Copy(v, tmp, N);
        Square(v, tmp, nx, ny, dilate: true, band);
        Plus(v, tmp, nx, ny, dilate: true, band);
        Square(v, tmp, nx, ny, dilate: false, band);
        Plus(v, tmp, nx, ny, dilate: false, band);
        Square(v, tmp, nx, ny, dilate: false, band);
        Square(v, tmp, nx, ny, dilate: true, band);

        for (int i = 0; i < N; i++)
        {
            int x = i % nx, y = i / nx;
            if (x == 0 || y == 0 || x == nx - 1 || y == ny - 1) v[i] = Math.Min(v[i], -step);
            else if (v[i] == 0) v[i] = -1e-6;   // land is strictly positive
        }
        FillLakes(v, nx, ny, step);
        return March(v, nx, ny, origin, step);
    }

    /// <summary>Samples within <paramref name="r"/> (Chebyshev) of both land and water, from a summed-area table.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static bool[] Band(double[] v, int nx, int ny, int r)
    {
        var sum = Rent(ref poolSum, (nx + 1) * (ny + 1));
        Array.Clear(sum, 0, nx + 1);
        for (int y = 0; y < ny; y++) sum[(y + 1) * (nx + 1)] = 0;
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
                sum[(y + 1) * (nx + 1) + x + 1] = (v[y * nx + x] > 0 ? 1 : 0) + sum[y * (nx + 1) + x + 1] + sum[(y + 1) * (nx + 1) + x] - sum[y * (nx + 1) + x];
        var band = Rent(ref poolBand, nx * ny);
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int x0 = Math.Max(0, x - r), x1 = Math.Min(nx, x + r + 1), y0 = Math.Max(0, y - r), y1 = Math.Min(ny, y + r + 1);
                int land = sum[y1 * (nx + 1) + x1] - sum[y0 * (nx + 1) + x1] - sum[y1 * (nx + 1) + x0] + sum[y0 * (nx + 1) + x0];
                band[y * nx + x] = land > 0 && land < (x1 - x0) * (y1 - y0);
            }
        return band;
    }

    /// <summary>Dilates or erodes <paramref name="v"/> in place by a 3×3 square (a row pass, then a column pass).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void Square(double[] v, double[] tmp, int nx, int ny, bool dilate, bool[] band)
    {
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int i = y * nx + x;
                if (!band[i]) { tmp[i] = v[i]; continue; }
                double a = x > 0 ? v[i - 1] : -1e6, b = v[i], c = x < nx - 1 ? v[i + 1] : -1e6;
                tmp[i] = dilate ? Math.Max(a, Math.Max(b, c)) : Math.Min(a, Math.Min(b, c));
            }
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int i = y * nx + x;
                if (!band[i]) { v[i] = tmp[i]; continue; }
                double a = y > 0 ? tmp[i - nx] : -1e6, b = tmp[i], c = y < ny - 1 ? tmp[i + nx] : -1e6;
                v[i] = dilate ? Math.Max(a, Math.Max(b, c)) : Math.Min(a, Math.Min(b, c));
            }
    }

    /// <summary>Dilates or erodes <paramref name="v"/> in place by a plus (the sample and its four neighbours).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void Plus(double[] v, double[] tmp, int nx, int ny, bool dilate, bool[] band)
    {
        Array.Copy(v, tmp, nx * ny);
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int i = y * nx + x;
                if (!band[i]) continue;
                double l = x > 0 ? tmp[i - 1] : -1e6, r = x < nx - 1 ? tmp[i + 1] : -1e6;
                double u = y > 0 ? tmp[i - nx] : -1e6, d = y < ny - 1 ? tmp[i + nx] : -1e6;
                v[i] = dilate ? Math.Max(Math.Max(tmp[i], l), Math.Max(r, Math.Max(u, d)))
                    : Math.Min(Math.Min(tmp[i], l), Math.Min(r, Math.Min(u, d)));
            }
    }

    /// <summary>
    /// Water the open sea cannot reach becomes land, so no island has a hole. Connectivity matches the marching: water
    /// samples touch across a diagonal only where the saddle there resolves to water.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void FillLakes(double[] v, int nx, int ny, double step)
    {
        int N = nx * ny;
        var reached = Rent(ref poolReached, N);
        Array.Clear(reached, 0, N);
        var stack = Rent(ref poolStack, N);
        int top = 0;
        for (int i = 0; i < N; i++)
        {
            int x = i % nx, y = i / nx;
            if (x == 0 || y == 0 || x == nx - 1 || y == ny - 1)
            {
                reached[i] = true;
                stack[top++] = i;
            }
        }
        while (top > 0)
        {
            int i = stack[--top];
            int x = i % nx, y = i / nx;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int sx = x + dx, sy = y + dy;
                    if (sx < 0 || sy < 0 || sx >= nx || sy >= ny) continue;
                    int j = sy * nx + sx;
                    if (reached[j] || v[j] > 0) continue;
                    if (dx != 0 && dy != 0)
                    {
                        double a = v[y * nx + sx], b = v[sy * nx + x];
                        if (a > 0 && b > 0 && (v[i] + v[j] + a + b) / 4 > 0) continue;   // a land-joined saddle
                    }
                    reached[j] = true;
                    stack[top++] = j;
                }
        }
        for (int i = 0; i < N; i++)
            if (!reached[i] && v[i] <= 0) v[i] = step * 0.25;
    }

    /// <summary>Marching squares with oriented segments (land on the left in y-up terms), linked into loops.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static List<Vec2[]> March(double[] v, int nx, int ny, Vec2 origin, double step)
    {
        int hCount = nx * ny;
        int edgeCount = 2 * nx * ny;
        var next = Rent(ref poolNext, edgeCount);
        Array.Fill(next, -1, 0, edgeCount);
        var pos = Rent(ref poolPos, edgeCount);
        Vec2 P(int x, int y) => origin + new Vec2(x * step, y * step);

        int Cross(int e, int x0, int y0, int x1, int y1)
        {
            double a = v[y0 * nx + x0], b = v[y1 * nx + x1];
            pos[e] = Vec2.Lerp(P(x0, y0), P(x1, y1), a / (a - b));
            return e;
        }

        Span<int> edges = stackalloc int[4];
        Span<bool> land = stackalloc bool[4];
        for (int y = 0; y + 1 < ny; y++)
            for (int x = 0; x + 1 < nx; x++)
            {
                // Corners clockwise on screen: top-left, top-right, bottom-right, bottom-left; edge k runs corner k → k+1.
                double c0 = v[y * nx + x], c1 = v[y * nx + x + 1], c2 = v[(y + 1) * nx + x + 1], c3 = v[(y + 1) * nx + x];
                land[0] = c0 > 0; land[1] = c1 > 0; land[2] = c2 > 0; land[3] = c3 > 0;
                if (land[0] == land[1] && land[1] == land[2] && land[2] == land[3]) continue;
                edges[0] = land[0] != land[1] ? Cross(y * nx + x, x, y, x + 1, y) : -1;
                edges[1] = land[1] != land[2] ? Cross(hCount + y * nx + x + 1, x + 1, y, x + 1, y + 1) : -1;
                edges[2] = land[2] != land[3] ? Cross((y + 1) * nx + x, x + 1, y + 1, x, y + 1) : -1;
                edges[3] = land[3] != land[0] ? Cross(hCount + y * nx + x, x, y + 1, x, y) : -1;
                bool saddle = land[0] == land[2] && land[1] == land[3];
                bool landJoined = saddle && (c0 + c1 + c2 + c3) / 4 > 0;
                // Exits (land → water going round) link to an entry (water → land): the next one when the water corners
                // are cut off (one land body), the previous one when the land corners are.
                for (int k = 0; k < 4; k++)
                {
                    if (edges[k] < 0 || !land[k]) continue;   // edge k is an exit when corner k is land
                    int partner = -1;
                    if (!saddle || landJoined)
                    {
                        for (int j = 1; j < 4 && partner < 0; j++)
                            if (edges[(k + j) % 4] >= 0 && !land[(k + j) % 4]) partner = edges[(k + j) % 4];
                    }
                    else
                    {
                        for (int j = 1; j < 4 && partner < 0; j++)
                            if (edges[(k - j + 4) % 4] >= 0 && !land[(k - j + 4) % 4]) partner = edges[(k - j + 4) % 4];
                    }
                    next[edges[k]] = partner;
                }
            }

        var loops = new List<Vec2[]>();
        var seen = Rent(ref poolSeen, edgeCount);
        Array.Clear(seen, 0, edgeCount);
        var loop = new List<Vec2>();
        for (int start = 0; start < edgeCount; start++)
        {
            if (next[start] < 0 || seen[start]) continue;
            loop.Clear();
            int e = start;
            while (e >= 0 && !seen[e])
            {
                seen[e] = true;
                loop.Add(pos[e]);
                e = next[e];
            }
            if (e != start || loop.Count < 4) continue;
            var poly = Simplify(loop);
            if (poly.Length >= 4 && Area(poly) >= MinArea) loops.Add(poly);
        }
        return loops;
    }

    /// <summary>Shoelace area: positive for the winding every island uses.</summary>
    public static double Area(Vec2[] poly)
    {
        double a = 0;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) a += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
        return a / 2;
    }

    public static double Perimeter(Vec2[] poly)
    {
        double s = 0;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) s += poly[j].DistanceTo(poly[i]);
        return s;
    }

    /// <summary>Douglas–Peucker on a closed loop, never leaving an edge longer than <see cref="MaxEdge"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static Vec2[] Simplify(List<Vec2> pts)
    {
        int n = pts.Count;
        int far = 0;
        double best = -1;
        for (int i = 1; i < n; i++)
        {
            double d = (pts[i] - pts[0]).LengthSq;
            if (d > best) { best = d; far = i; }
        }
        var keep = new bool[n];
        keep[0] = keep[far] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, far));
        stack.Push((far, n));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            if (b - a < 2) continue;
            var pa = pts[a];
            var pb = pts[b % n];
            var ab = pb - pa;
            double len2 = ab.LengthSq;
            int worst = -1;
            double worstD = -1;
            for (int i = a + 1; i < b; i++)
            {
                var ap = pts[i] - pa;
                double t = len2 > 1e-12 ? Math.Clamp(ap.Dot(ab) / len2, 0, 1) : 0;
                double d = (ap - ab * t).LengthSq;
                if (d > worstD) { worstD = d; worst = i; }
            }
            if (worstD > Tolerance * Tolerance || len2 > MaxEdge * MaxEdge)
            {
                keep[worst] = true;
                stack.Push((a, worst));
                stack.Push((worst, b));
            }
        }
        var outp = new List<Vec2>();
        for (int i = 0; i < n; i++)
            if (keep[i] && (outp.Count == 0 || outp[^1].DistanceTo(pts[i]) > 0.5)) outp.Add(pts[i]);
        if (outp.Count > 1 && outp[0].DistanceTo(outp[^1]) <= 0.5) outp.RemoveAt(outp.Count - 1);
        return outp.ToArray();
    }
}
