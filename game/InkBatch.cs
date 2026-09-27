using Godot;

namespace LastTide;

/// <summary>
/// Collects coloured triangles (fills, washes, feathered ink lines) and draws them as ONE triangle-array
/// command, so a whole ship's rigging, sails and ink costs a single draw call. Lines and fills get a
/// one-screen-pixel feathered edge, which antialiases them at every zoom. Buffers are reused frame to
/// frame: nothing here allocates once they have grown to size.
/// </summary>
public sealed class InkBatch
{
    Vector2[] pts = new Vector2[512];
    Color[] cols = new Color[512];
    Vector2[] uvs = new Vector2[512];
    int[] idx = new int[1536];
    int nv, ni;
    bool textured;

    /// <summary>One screen pixel in local units; set it before building (feather width and hairlines).</summary>
    public float Px = 1f;
    public int Vertices => nv;
    public int Indices => ni;

    public void Clear()
    {
        nv = ni = 0;
        textured = false;
    }

    void Grow(int v, int i)
    {
        if (nv + v > pts.Length)
        {
            int n = Math.Max(pts.Length * 2, nv + v);
            Array.Resize(ref pts, n);
            Array.Resize(ref cols, n);
            Array.Resize(ref uvs, n);
        }
        if (ni + i > idx.Length) Array.Resize(ref idx, Math.Max(idx.Length * 2, ni + i));
    }

    int V(Vector2 p, Color c)
    {
        pts[nv] = p;
        cols[nv] = c;
        uvs[nv] = Vector2.Zero;
        return nv++;
    }

    int V(Vector2 p, Color c, Vector2 uv)
    {
        pts[nv] = p;
        cols[nv] = c;
        uvs[nv] = uv;
        return nv++;
    }

    void T(int a, int b, int c)
    {
        idx[ni++] = a;
        idx[ni++] = b;
        idx[ni++] = c;
    }

    /// <summary>A flat or gradient quad (a-b-c-d in order around it).</summary>
    public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
    {
        Grow(4, 6);
        int i = V(a, ca), j = V(b, cb), k = V(c, cc), l = V(d, cd);
        T(i, j, k);
        T(i, k, l);
    }

    public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col) => Quad(a, b, c, d, col, col, col, col);

    public void Tri(Vector2 a, Vector2 b, Vector2 c, Color col)
    {
        Grow(3, 3);
        T(V(a, col), V(b, col), V(c, col));
    }

    /// <summary>A textured quad (one texture per mesh: pass it to <see cref="Flush"/>).</summary>
    public void Sprite(Vector2 centre, Vector2 halfX, Vector2 halfY, Rect2 uv, Color col)
    {
        Grow(4, 6);
        textured = true;
        int i = V(centre - halfX - halfY, col, uv.Position);
        int j = V(centre + halfX - halfY, col, new Vector2(uv.End.X, uv.Position.Y));
        int k = V(centre + halfX + halfY, col, uv.End);
        int l = V(centre - halfX + halfY, col, new Vector2(uv.Position.X, uv.End.Y));
        T(i, j, k);
        T(i, k, l);
    }

    /// <summary>
    /// A sprite bent along a spine: the texture region <paramref name="uv"/> (lying along +X, its top edge to
    /// the left of travel) is laid over the spine points with the given half-widths. Serpents, tentacles,
    /// tails and weed fronds coil with this. <paramref name="alphas"/> (optional, per point) fades sections,
    /// e.g. the humps of a serpent dipping under water.
    /// </summary>
    public void Strip(ReadOnlySpan<Vector2> spine, ReadOnlySpan<float> halfWidths, Rect2 uv, Color col, ReadOnlySpan<float> alphas = default)
    {
        int n = spine.Length;
        if (n < 2) return;
        Grow(n * 2, (n - 1) * 6);
        textured = true;
        int s = nv;
        for (int k = 0; k < n; k++)
        {
            var d = k == 0 ? spine[1] - spine[0] : k == n - 1 ? spine[n - 1] - spine[n - 2] : spine[k + 1] - spine[k - 1];
            float len = d.Length();
            d = len > 1e-5f ? d / len : Vector2.Right;
            var nrm = new Vector2(d.Y, -d.X);   // the sprite's top edge
            float u = uv.Position.X + uv.Size.X * k / (n - 1);
            var c = alphas.Length == n ? col with { A = col.A * alphas[k] } : col;
            V(spine[k] + nrm * halfWidths[k], c, new Vector2(u, uv.Position.Y));
            V(spine[k] - nrm * halfWidths[k], c, new Vector2(u, uv.End.Y));
        }
        for (int k = 0; k < n - 1; k++)
        {
            int a = s + k * 2;
            T(a, a + 2, a + 3);
            T(a, a + 3, a + 1);
        }
    }

    /// <summary>A textured quad rotated by <paramref name="angle"/> (radians), <paramref name="size"/> in local units.</summary>
    public void SpriteRot(Vector2 centre, Vector2 size, float angle, Rect2 uv, Color col)
    {
        var (sn, cs) = Mathf.SinCos(angle);
        var hx = new Vector2(cs, sn) * size.X * 0.5f;
        var hy = new Vector2(-sn, cs) * size.Y * 0.5f;
        Sprite(centre, hx, hy, uv, col);
    }

    /// <summary>A convex polygon (fan) with a feathered rim; <paramref name="feather"/> 0 = hard edge.</summary>
    public void Convex(ReadOnlySpan<Vector2> ring, Color col, float feather = -1)
    {
        int n = ring.Length;
        if (n < 3) return;
        if (feather < 0) feather = Px;
        Grow(n * 2 + 1, n * 9);
        var centre = Vector2.Zero;
        foreach (var p in ring) centre += p;
        centre /= n;
        int c0 = V(centre, col);
        int first = nv;
        for (int k = 0; k < n; k++) V(ring[k], col);
        for (int k = 0; k < n; k++) T(c0, first + k, first + (k + 1) % n);
        if (feather <= 0) return;
        var clear = col with { A = 0 };
        int outer = nv;
        for (int k = 0; k < n; k++)
        {
            var d = ring[k] - centre;
            float len = d.Length();
            V(ring[k] + (len > 1e-4f ? d / len : Vector2.Zero) * feather, clear);
        }
        for (int k = 0; k < n; k++)
        {
            int k2 = (k + 1) % n;
            T(first + k, outer + k, outer + k2);
            T(first + k, outer + k2, first + k2);
        }
    }

    /// <summary>A filled disc (or ellipse via <paramref name="squash"/>) with a feathered edge.</summary>
    public void Disc(Vector2 c, float r, Color col, int segments = 12, float squash = 1f, float angle = 0f)
    {
        Span<Vector2> ring = stackalloc Vector2[Math.Clamp(segments, 3, 48)];
        var (s, co) = Mathf.SinCos(angle);
        for (int k = 0; k < ring.Length; k++)
        {
            float a = k * Mathf.Tau / ring.Length;
            var local = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * squash);
            ring[k] = c + new Vector2(local.X * co - local.Y * s, local.X * s + local.Y * co);
        }
        Convex(ring, col);
    }

    /// <summary>A soft radial wash: <paramref name="inner"/> at the centre fading to <paramref name="outer"/> at the rim.</summary>
    public void Glow(Vector2 c, float r, Color inner, Color outer, int segments = 24, float squash = 1f, float angle = 0f)
    {
        int n = Math.Clamp(segments, 6, 64);
        Grow(n * 2 + 1, n * 9);
        var (sn, cs) = Mathf.SinCos(angle);
        int c0 = V(c, inner);
        var mid = inner.Lerp(outer, 0.55f) with { A = Mathf.Lerp(inner.A, outer.A, 0.7f) };
        int first = nv;
        for (int k = 0; k < n; k++)
        {
            float a = k * Mathf.Tau / n;
            var local = new Vector2(Mathf.Cos(a) * r * 0.5f, Mathf.Sin(a) * r * 0.5f * squash);
            V(c + new Vector2(local.X * cs - local.Y * sn, local.X * sn + local.Y * cs), mid);
        }
        int rim = nv;
        for (int k = 0; k < n; k++)
        {
            float a = k * Mathf.Tau / n;
            var local = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * squash);
            V(c + new Vector2(local.X * cs - local.Y * sn, local.X * sn + local.Y * cs), outer);
        }
        for (int k = 0; k < n; k++)
        {
            int k2 = (k + 1) % n;
            T(c0, first + k, first + k2);
            T(first + k, rim + k, rim + k2);
            T(first + k, rim + k2, first + k2);
        }
    }

    /// <summary>A ring (circle outline) of the given stroke width.</summary>
    public void Circle(Vector2 c, float r, float width, Color col, int segments = 24, float a0 = 0, float a1 = Mathf.Tau)
    {
        int n = Math.Clamp(segments, 3, 96);
        Span<Vector2> pts2 = stackalloc Vector2[n + 1];
        for (int k = 0; k <= n; k++)
        {
            float a = a0 + (a1 - a0) * k / n;
            pts2[k] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        bool closed = Mathf.Abs(a1 - a0) >= Mathf.Tau - 1e-3f;
        Polyline(closed ? pts2[..n] : pts2, width, col, closed);
    }

    public void Line(Vector2 a, Vector2 b, float width, Color col)
    {
        Span<Vector2> two = stackalloc Vector2[2] { a, b };
        Polyline(two, width, col);
    }

    /// <summary>A line whose width tapers from <paramref name="w0"/> to <paramref name="w1"/> (spars, tentacles, strokes).</summary>
    public void Taper(Vector2 a, Vector2 b, float w0, float w1, Color c0, Color c1)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-5f) return;
        var n = new Vector2(-d.Y, d.X) / len;
        float f = Px;
        var z0 = c0 with { A = 0 };
        var z1 = c1 with { A = 0 };
        Grow(8, 18);
        float h0 = Mathf.Max(w0 * 0.5f - f * 0.5f, 0.01f), h1 = Mathf.Max(w1 * 0.5f - f * 0.5f, 0.01f);
        int i0 = V(a + n * h0, c0), i1 = V(a - n * h0, c0), i2 = V(b + n * h1, c1), i3 = V(b - n * h1, c1);
        int o0 = V(a + n * (h0 + f), z0), o1 = V(a - n * (h0 + f), z0), o2 = V(b + n * (h1 + f), z1), o3 = V(b - n * (h1 + f), z1);
        T(i0, i2, i3); T(i0, i3, i1);
        T(o0, o2, i2); T(o0, i2, i0);
        T(i1, i3, o3); T(i1, o3, o1);
    }

    /// <summary>
    /// An ink stroke through the points: mitred joins, feathered sides. Widths under a pixel fade out
    /// instead of shimmering, so hairlines stay honest at far zoom.
    /// </summary>
    public void Polyline(ReadOnlySpan<Vector2> p, float width, Color col, bool closed = false)
        => PolylineWidths(p, width, width, col, col, closed);

    /// <summary>A stroke whose width and colour run from the first point's values to the last point's.</summary>
    public void PolylineWidths(ReadOnlySpan<Vector2> p, float w0, float w1, Color c0, Color c1, bool closed = false)
    {
        int n = p.Length;
        if (n < 2) return;
        float f = Px;
        int segs = closed ? n : n - 1;
        Grow(n * 4 + 4, segs * 18);
        int start = nv;
        for (int k = 0; k < n; k++)
        {
            Vector2 prev = k > 0 ? p[k - 1] : closed ? p[n - 1] : p[k] * 2 - p[k + 1];
            Vector2 next = k < n - 1 ? p[k + 1] : closed ? p[0] : p[k] * 2 - p[k - 1];
            var d0 = (p[k] - prev).Normalized();
            var d1 = (next - p[k]).Normalized();
            if (d0 == Vector2.Zero) d0 = d1;
            if (d1 == Vector2.Zero) d1 = d0;
            var n0 = new Vector2(-d0.Y, d0.X);
            var n1 = new Vector2(-d1.Y, d1.X);
            var m = (n0 + n1);
            float ml = m.Length();
            m = ml > 1e-4f ? m / ml : n0;
            float dot = Mathf.Max(0.35f, m.Dot(n0));   // miter limit
            float t = n == 1 ? 0 : k / (float)(n - 1);
            float w = Mathf.Lerp(w0, w1, t);
            var c = c0.Lerp(c1, t);
            if (w < f)
            {
                c.A *= w / f;   // sub-pixel: thin to a faint pixel line rather than alias
                w = f;
            }
            float h = Mathf.Max(w * 0.5f - f * 0.5f, 0.01f) / dot;
            float o = (w * 0.5f + f * 0.5f) / dot;
            var clear = c with { A = 0 };
            V(p[k] + m * h, c);
            V(p[k] - m * h, c);
            V(p[k] + m * o, clear);
            V(p[k] - m * o, clear);
        }
        for (int k = 0; k < segs; k++)
        {
            int a = start + k * 4, b = start + ((k + 1) % n) * 4;
            T(a, b, b + 1); T(a, b + 1, a + 1);            // core
            T(a + 2, b + 2, b); T(a + 2, b, a);            // feather +
            T(a + 1, b + 1, b + 3); T(a + 1, b + 3, a + 3); // feather −
        }
    }

    /// <summary>A strip between two rails (same length): a filled band with per-row colours (sails, weed, bands).</summary>
    public void Band(ReadOnlySpan<Vector2> railA, ReadOnlySpan<Vector2> railB, Color ca, Color cb)
    {
        int n = Math.Min(railA.Length, railB.Length);
        if (n < 2) return;
        Grow(n * 2, (n - 1) * 6);
        int s = nv;
        for (int k = 0; k < n; k++)
        {
            V(railA[k], ca);
            V(railB[k], cb);
        }
        for (int k = 0; k < n - 1; k++)
        {
            int a = s + k * 2;
            T(a, a + 2, a + 3);
            T(a, a + 3, a + 1);
        }
    }

    /// <summary>
    /// A grid patch: <paramref name="rows"/> rails of <paramref name="cols"/> points each, row-major in
    /// <paramref name="p"/>, one colour per point. The sails are built from these.
    /// </summary>
    public void Grid(ReadOnlySpan<Vector2> p, ReadOnlySpan<Color> c, int rows, int cols)
    {
        if (rows < 2 || cols < 2) return;
        Grow(rows * cols, (rows - 1) * (cols - 1) * 6);
        int s = nv;
        for (int k = 0; k < rows * cols; k++) V(p[k], c[k]);
        for (int r = 0; r < rows - 1; r++)
            for (int q = 0; q < cols - 1; q++)
            {
                int a = s + r * cols + q;
                T(a, a + 1, a + cols + 1);
                T(a, a + cols + 1, a + cols);
            }
    }

    /// <summary>Draws everything collected as one command on <paramref name="item"/> and clears.</summary>
    public void Flush(CanvasItem item, Texture2D? texture = null)
    {
        if (ni > 0)
        {
            var rid = item.GetCanvasItem();
            var ix = new ReadOnlySpan<int>(idx, 0, ni);
            var ps = new ReadOnlySpan<Vector2>(pts, 0, nv);
            var cs = new ReadOnlySpan<Color>(cols, 0, nv);
            if (texture != null || textured)
                RenderingServer.CanvasItemAddTriangleArray(rid, ix, ps, cs, new ReadOnlySpan<Vector2>(uvs, 0, nv),
                    ReadOnlySpan<int>.Empty, ReadOnlySpan<float>.Empty, texture?.GetRid() ?? default, -1);
            else
                RenderingServer.CanvasItemAddTriangleArray(rid, ix, ps, cs, ReadOnlySpan<Vector2>.Empty,
                    ReadOnlySpan<int>.Empty, ReadOnlySpan<float>.Empty, default, -1);
        }
        Clear();
    }
}
