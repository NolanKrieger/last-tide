using Godot;

namespace LastTide;

/// <summary>
/// Batches hand-inked strokes (tapered, wobbling, any colour) into one triangle mesh, drawn with
/// assets/shaders/ink.gdshader in a single draw call. Each vertex carries its stroke's half-width in UV.x and its signed
/// distance across the stroke in UV.y (world px), so the shader anti-aliases the edge at any zoom and fades strokes
/// thinner than a screen pixel instead of widening them. Build once, draw many times (chunks cache the mesh).
/// </summary>
public sealed class InkMesh
{
    /// <summary>Geometry margin beyond the ink so the edge can fade over a screen pixel even at the widest zoom (0.3).</summary>
    public const float Margin = 3.6f;

    readonly List<Vector2> verts = new();
    readonly List<Color> cols = new();
    readonly List<Vector2> uvs = new();
    readonly List<int> idx = new();

    public int Vertices => verts.Count;

    /// <summary>A polyline stroke. <paramref name="halfWidth"/> gives the half-width (world px) at each point.</summary>
    public void Stroke(IReadOnlyList<Vector2> pts, Func<int, float> halfWidth, Color col, bool closed = false)
    {
        int n = pts.Count;
        if (n < 2) return;
        int start = verts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = closed ? pts[(i - 1 + n) % n] : pts[Math.Max(0, i - 1)];
            Vector2 next = closed ? pts[(i + 1) % n] : pts[Math.Min(n - 1, i + 1)];
            Vector2 cur = pts[i];
            Vector2 d0 = (cur - prev).LengthSquared() > 1e-8f ? (cur - prev).Normalized() : (next - cur).Normalized();
            Vector2 d1 = (next - cur).LengthSquared() > 1e-8f ? (next - cur).Normalized() : d0;
            Vector2 nrm = new Vector2(-(d0.Y + d1.Y), d0.X + d1.X);
            if (nrm.LengthSquared() < 1e-8f) nrm = new Vector2(-d0.Y, d0.X);
            nrm = nrm.Normalized();
            float miter = 1f / Mathf.Max(0.35f, nrm.Dot(new Vector2(-d0.Y, d0.X)));
            float hw = Mathf.Max(0f, halfWidth(i));
            float reach = (hw + Margin) * Mathf.Min(miter, 2.5f);
            verts.Add(cur + nrm * reach); uvs.Add(new Vector2(hw, hw + Margin)); cols.Add(col);
            verts.Add(cur - nrm * reach); uvs.Add(new Vector2(hw, -(hw + Margin))); cols.Add(col);
        }
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            int a = start + 2 * i, b = start + 2 * ((i + 1) % n);
            idx.Add(a); idx.Add(a + 1); idx.Add(b);
            idx.Add(b); idx.Add(a + 1); idx.Add(b + 1);
        }
    }

    /// <summary>A single tapered tick from <paramref name="a"/> (half-width <paramref name="wa"/>) to <paramref name="b"/> (<paramref name="wb"/>).</summary>
    public void Tick(Vector2 a, Vector2 b, float wa, float wb, Color col)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return;
        d /= len;
        var nrm = new Vector2(-d.Y, d.X);
        int s = verts.Count;
        // pull the ends out by the margin so the rounded fade at the tips has room
        var a2 = a - d * Mathf.Min(Margin, wa + 1f);
        var b2 = b + d * Mathf.Min(Margin, wb + 1f);
        verts.Add(a2 + nrm * (wa + Margin)); uvs.Add(new Vector2(wa, wa + Margin)); cols.Add(col);
        verts.Add(a2 - nrm * (wa + Margin)); uvs.Add(new Vector2(wa, -(wa + Margin))); cols.Add(col);
        verts.Add(b2 + nrm * (wb + Margin)); uvs.Add(new Vector2(wb, wb + Margin)); cols.Add(col);
        verts.Add(b2 - nrm * (wb + Margin)); uvs.Add(new Vector2(wb, -(wb + Margin))); cols.Add(col);
        idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
        idx.Add(s + 2); idx.Add(s + 1); idx.Add(s + 3);
    }

    /// <summary>A round pen dot of radius <paramref name="r"/> (world px), anti-aliased in the shader.</summary>
    public void Dot(Vector2 p, float r, Color col)
    {
        float e = r + Margin * 0.5f;
        float k = e / Mathf.Max(r, 0.01f);
        int s = verts.Count;
        verts.Add(p + new Vector2(-e, -e)); uvs.Add(new Vector2(-10 - k, -k)); cols.Add(col);
        verts.Add(p + new Vector2(e, -e)); uvs.Add(new Vector2(-10 + k, -k)); cols.Add(col);
        verts.Add(p + new Vector2(e, e)); uvs.Add(new Vector2(-10 + k, k)); cols.Add(col);
        verts.Add(p + new Vector2(-e, e)); uvs.Add(new Vector2(-10 - k, k)); cols.Add(col);
        idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
        idx.Add(s); idx.Add(s + 2); idx.Add(s + 3);
    }

    /// <summary>A filled convex polygon (flags, pennants): drawn solid, its edge anti-aliased by an outline stroke.</summary>
    public void Fill(IReadOnlyList<Vector2> poly, Color col)
    {
        if (poly.Count < 3) return;
        int s = verts.Count;
        foreach (var p in poly)
        {
            verts.Add(p); uvs.Add(new Vector2(1000f, 0f)); cols.Add(col);
        }
        for (int i = 1; i + 1 < poly.Count; i++)
        {
            idx.Add(s); idx.Add(s + i); idx.Add(s + i + 1);
        }
        Stroke(poly, _ => 0.45f, col, closed: true);
    }

    public ArrayMesh? Build()
    {
        if (idx.Count == 0) return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, Mesh.ArrayFormat.FlagUse2DVertices);
        return mesh;
    }

    static ShaderMaterial? material;
    /// <summary>The shared ink material (anti-aliased strokes from the UV convention above).</summary>
    public static ShaderMaterial Material => material ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/ink.gdshader") };
}

/// <summary>
/// Batches textured quads from one atlas into a single mesh (one draw call): the chart's stamps, towns and sea life.
/// Vertex colour tints and fades each quad.
/// </summary>
public sealed class StampMesh
{
    readonly List<Vector2> verts = new();
    readonly List<Color> cols = new();
    readonly List<Vector2> uvs = new();
    readonly List<int> idx = new();
    readonly Vector2 atlasSize;

    public StampMesh(Vector2 atlasSize) { this.atlasSize = atlasSize; }

    public int Count => idx.Count / 6;

    /// <summary>Draws atlas region <paramref name="src"/> (pixels) so its anchor (0..1 of the region) lands at <paramref name="at"/>, <paramref name="height"/> world px tall.</summary>
    public void Add(Rect2 src, Vector2 at, float height, Vector2 anchor, Color col, bool flip = false, float angle = 0f)
    {
        float h = height, w = height * src.Size.X / src.Size.Y;
        var o = new Vector2(-anchor.X * w, -anchor.Y * h);
        Vector2[] corners = { o, o + new Vector2(w, 0), o + new Vector2(w, h), o + new Vector2(0, h) };
        float u0 = src.Position.X / atlasSize.X, v0 = src.Position.Y / atlasSize.Y;
        float u1 = src.End.X / atlasSize.X, v1 = src.End.Y / atlasSize.Y;
        if (flip) (u0, u1) = (u1, u0);
        Vector2[] tc = { new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1) };
        int s = verts.Count;
        float c = Mathf.Cos(angle), sn = Mathf.Sin(angle);
        for (int k = 0; k < 4; k++)
        {
            var p = corners[k];
            verts.Add(at + new Vector2(p.X * c - p.Y * sn, p.X * sn + p.Y * c));
            uvs.Add(tc[k]);
            cols.Add(col);
        }
        idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
        idx.Add(s); idx.Add(s + 2); idx.Add(s + 3);
    }

    public ArrayMesh? Build()
    {
        if (idx.Count == 0) return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, Mesh.ArrayFormat.FlagUse2DVertices);
        return mesh;
    }
}
