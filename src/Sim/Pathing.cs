namespace LastTide.Sim;

/// <summary>A* over the nav grid with a wind-aware cost, plus string-pulling so ships sail straight legs.</summary>
public static class Pathing
{
    static readonly (int dx, int dy, double len)[] Moves =
    {
        (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
        (1, 1, 1.41421356), (1, -1, 1.41421356), (-1, 1, 1.41421356), (-1, -1, 1.41421356),
    };

    /// <summary>Waypoints from <paramref name="from"/> to <paramref name="to"/>, or an empty list if no sea route exists.</summary>
    public static List<Vec2> Find(NavGrid nav, Vec2 from, Vec2 to, double windFrom, double pointDeg)
    {
        int sx = NavGrid.ToX(from.X), sy = NavGrid.ToY(from.Y);
        int gx = NavGrid.ToX(to.X), gy = NavGrid.ToY(to.Y);
        if (nav.IsLand(sx, sy)) (sx, sy) = nav.NearestSea(sx, sy);
        if (nav.IsLand(gx, gy)) (gx, gy) = nav.NearestSea(gx, gy);
        int W = nav.W, H = nav.H;
        var g = new double[W * H];
        Array.Fill(g, double.MaxValue);
        var came = new int[W * H];
        Array.Fill(came, -1);
        var open = new PriorityQueue<int, double>();
        int start = sy * W + sx, goal = gy * W + gx;
        g[start] = 0;
        open.Enqueue(start, Heuristic(sx, sy, gx, gy));
        double noGo = Angles.Rad(pointDeg);
        // Direction costs: beating into the wind is slow, running is fast.
        var moveCost = new double[Moves.Length];
        for (int m = 0; m < Moves.Length; m++)
        {
            double heading = Math.Atan2(Moves[m].dy, Moves[m].dx);
            double off = Math.Abs(Angles.Wrap(heading - windFrom));
            double polar = off < noGo ? 0.35 : Polar.Fraction(Rig.ForeAndAft, Angles.Deg(off), pointDeg);
            moveCost[m] = Moves[m].len / Math.Max(0.3, polar);
        }
        var closed = new bool[W * H];
        while (open.Count > 0)
        {
            int cur = open.Dequeue();
            if (cur == goal) break;
            if (closed[cur]) continue;
            closed[cur] = true;
            int cx = cur % W, cy = cur / W;
            for (int m = 0; m < Moves.Length; m++)
            {
                int nx = cx + Moves[m].dx, ny = cy + Moves[m].dy;
                if (nav.IsLand(nx, ny)) continue;
                if (Moves[m].dx != 0 && Moves[m].dy != 0 && (nav.IsLand(cx + Moves[m].dx, cy) || nav.IsLand(cx, cy + Moves[m].dy))) continue;
                int n = ny * W + nx;
                double cost = g[cur] + moveCost[m];
                if (cost < g[n])
                {
                    g[n] = cost;
                    came[n] = cur;
                    open.Enqueue(n, cost + Heuristic(nx, ny, gx, gy));
                }
            }
        }
        if (came[goal] < 0 && goal != start) return new List<Vec2>();
        var cells = new List<int>();
        for (int c = goal; c >= 0; c = came[c])
        {
            cells.Add(c);
            if (c == start) break;
        }
        cells.Reverse();
        var pts = cells.Select(c => NavGrid.Centre(c % W, c / W)).ToList();
        pts[^1] = to;
        return Smooth(nav, pts);
    }

    static double Heuristic(int x, int y, int gx, int gy)
    {
        int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
        return Math.Max(dx, dy) + 0.41421356 * Math.Min(dx, dy);
    }

    /// <summary>Drops waypoints that a straight line already covers while keeping the grid's margin off every coast.</summary>
    public static List<Vec2> Smooth(NavGrid nav, List<Vec2> pts)
    {
        if (pts.Count <= 2) return pts;
        var result = new List<Vec2> { pts[0] };
        int i = 0;
        while (i < pts.Count - 1)
        {
            int j = pts.Count - 1;
            while (j > i + 1 && !(Clear(nav, pts[i], pts[j]) && nav.SegmentClear(pts[i], pts[j]))) j--;
            result.Add(pts[j]);
            i = j;
        }
        return result;
    }

    /// <summary>True when every 8 m step from a to b is open water on the grid.</summary>
    /// <remarks>
    /// Not enough on its own to straighten a lane: a 25 m cell counts as sea when its centre is, so a leg tested this
    /// way could clip a coast between samples. <see cref="Smooth"/> also demands <see cref="NavGrid.SegmentClear"/>.
    /// </remarks>
    public static bool Clear(NavGrid nav, Vec2 a, Vec2 b)
    {
        double len = a.DistanceTo(b);
        int n = Math.Max(1, (int)(len / 8));
        for (int k = 0; k <= n; k++)
            if (nav.IsLand(Vec2.Lerp(a, b, k / (double)n))) return false;
        return true;
    }
}
