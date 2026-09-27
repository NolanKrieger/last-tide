using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Per-map data the world shaders draw from, built once per voyage:
/// <list type="bullet">
/// <item><see cref="Coast"/>: every island's coastline as the smooth curve the chart inks (a Catmull-Rom spline through the
/// sim's polygon, so ports stay on it; it strays from the collision polygon by a metre or two at most).</item>
/// <item><see cref="Sdf"/>: signed distance to that coastline in metres (negative on land), one texel per 6.25 m, exact
/// Euclidean (nearest-point sweep). The sea shader draws the land wash, contours, surf, coastal halo, dotted shallows
/// and deep water from it with no draw calls of its own.</item>
/// <item><see cref="Regions"/>: the region index per 25 m texel (sampled nearest), so the shaders apply the same
/// regional weather rules as the sim; <see cref="WaterTint"/> and <see cref="LandTint"/> are blurred region colours
/// for soft, hand-tinted borders.</item>
/// </list>
/// </summary>
public sealed class CoastField
{
    public const float Cell = 6.25f;
    public const float RegionCell = 25f;
    public static readonly int W = (int)(Map.Width / Cell), H = (int)(Map.Height / Cell);
    public static readonly int RW = (int)(Map.Width / RegionCell), RH = (int)(Map.Height / RegionCell);

    public readonly Dictionary<int, Vector2[]> Coast = new();   // island id → closed curve in metres (not repeated)
    public ImageTexture Sdf { get; private set; } = null!;
    public ImageTexture Regions { get; private set; } = null!;
    public ImageTexture WaterTint { get; private set; } = null!;
    public ImageTexture LandTint { get; private set; } = null!;
    public float[] Distance = Array.Empty<float>();   // the SDF on the CPU (row-major W×H), for placement queries
    public double BuildMs;

    /// <summary>Watercolour for the water of each region (index = RegionType): barely-there tints over the paper.</summary>
    public static readonly Color[] WaterColour =
    {
        new(0.55f, 0.66f, 0.66f),   // Trade Isles: soft sea-green
        new(0.42f, 0.70f, 0.66f),   // Shoals: turquoise
        new(0.30f, 0.40f, 0.52f),   // Deep: slate blue
        new(0.62f, 0.64f, 0.64f),   // Fog Banks: grey
        new(0.38f, 0.44f, 0.52f),   // Storm Reach: storm slate
        new(0.46f, 0.56f, 0.40f),   // Mangrove: brackish green
        new(0.50f, 0.46f, 0.44f),   // Volcanic: ash
        new(0.62f, 0.62f, 0.38f),   // Sargasso: weed gold
        new(0.52f, 0.50f, 0.64f),   // Siren's Ruins: lilac
    };

    /// <summary>Watercolour for the land of each region.</summary>
    public static readonly Color[] LandColour =
    {
        new(0.80f, 0.72f, 0.50f),   // Trade Isles: warm sand with green washes
        new(0.86f, 0.79f, 0.58f),   // Shoals: pale sand
        new(0.66f, 0.66f, 0.54f),   // Deep: grey-green rock
        new(0.64f, 0.66f, 0.56f),   // Fog Banks: moor
        new(0.62f, 0.60f, 0.54f),   // Storm Reach: slate crag
        new(0.52f, 0.60f, 0.40f),   // Mangrove: deep green
        new(0.52f, 0.44f, 0.38f),   // Volcanic: umber and ash
        new(0.84f, 0.78f, 0.56f),   // Sargasso: bleached sand
        new(0.78f, 0.74f, 0.66f),   // Siren's Ruins: pale limestone
    };

    public CoastField(Map map)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var island in map.Islands)
            Coast[island.Id] = Smooth(island.Points);
        BuildSdf(map);
        BuildRegions(map);
        BuildMs = sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>A closed Catmull-Rom spline through the polygon, one point every ~2.5 m.</summary>
    public static Vector2[] Smooth(Vec2[] poly)
    {
        int n = poly.Length;
        var pts = new List<Vector2>(n * 12);
        for (int i = 0; i < n; i++)
        {
            var p0 = V(poly[(i - 1 + n) % n]);
            var p1 = V(poly[i]);
            var p2 = V(poly[(i + 1) % n]);
            var p3 = V(poly[(i + 2) % n]);
            int steps = Math.Max(2, (int)Math.Ceiling(p1.DistanceTo(p2) / 2.5f));
            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                pts.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3));
            }
        }
        return pts.ToArray();
    }

    static Vector2 V(Vec2 v) => new((float)v.X, (float)v.Y);

    static float TexX(int x) => (float)(-Map.HalfW + (x + 0.5) * Cell);
    static float TexY(int y) => (float)(-Map.HalfH + (y + 0.5) * Cell);

    void BuildSdf(Map map)
    {
        int size = W * H;
        var land = new bool[size];
        // Land mask: even-odd scanline fill of each smoothed coast at texel centres.
        var xs = new List<float>();
        foreach (var curve in Coast.Values)
        {
            float minY = curve.Min(p => p.Y), maxY = curve.Max(p => p.Y);
            int y0 = Math.Max(0, (int)Math.Floor((minY + Map.HalfH) / Cell)), y1 = Math.Min(H - 1, (int)Math.Ceiling((maxY + Map.HalfH) / Cell));
            for (int y = y0; y <= y1; y++)
            {
                float cy = TexY(y);
                xs.Clear();
                for (int i = 0, j = curve.Length - 1; i < curve.Length; j = i++)
                {
                    var a = curve[i];
                    var b = curve[j];
                    if ((a.Y > cy) != (b.Y > cy))
                        xs.Add(a.X + (cy - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = Math.Max(0, (int)Math.Ceiling((xs[k] + Map.HalfW) / Cell - 0.5f));
                    int xb = Math.Min(W - 1, (int)Math.Floor((xs[k + 1] + Map.HalfW) / Cell - 0.5f));
                    for (int x = xa; x <= xb; x++) land[y * W + x] = true;
                }
            }
        }

        // Exact nearest coast point for texels within a narrow band of each segment ...
        var nx = new float[size];
        var ny = new float[size];
        var best = new float[size];
        Array.Fill(best, float.MaxValue);
        const float band = 14f;
        foreach (var curve in Coast.Values)
        {
            for (int i = 0, j = curve.Length - 1; i < curve.Length; j = i++)
            {
                var a = curve[j];
                var b = curve[i];
                int x0 = Math.Max(0, (int)((Math.Min(a.X, b.X) - band + Map.HalfW) / Cell));
                int x1 = Math.Min(W - 1, (int)((Math.Max(a.X, b.X) + band + Map.HalfW) / Cell));
                int y0 = Math.Max(0, (int)((Math.Min(a.Y, b.Y) - band + Map.HalfH) / Cell));
                int y1 = Math.Min(H - 1, (int)((Math.Max(a.Y, b.Y) + band + Map.HalfH) / Cell));
                var ab = b - a;
                float len2 = Math.Max(1e-6f, ab.LengthSquared());
                for (int y = y0; y <= y1; y++)
                {
                    float cy = TexY(y);
                    for (int x = x0; x <= x1; x++)
                    {
                        float cx = TexX(x);
                        float t = Math.Clamp(((cx - a.X) * ab.X + (cy - a.Y) * ab.Y) / len2, 0, 1);
                        float qx = a.X + ab.X * t, qy = a.Y + ab.Y * t;
                        float d2 = (cx - qx) * (cx - qx) + (cy - qy) * (cy - qy);
                        int k = y * W + x;
                        if (d2 < best[k])
                        {
                            best[k] = d2;
                            nx[k] = qx;
                            ny[k] = qy;
                        }
                    }
                }
            }
        }
        // ... then two nearest-point sweeps carry those points everywhere (8SSEDT on stored points).
        void Relax(int k, int kn, float cx, float cy)
        {
            if (best[kn] == float.MaxValue) return;
            float dx = cx - nx[kn], dy = cy - ny[kn];
            float d2 = dx * dx + dy * dy;
            if (d2 < best[k])
            {
                best[k] = d2;
                nx[k] = nx[kn];
                ny[k] = ny[kn];
            }
        }
        for (int y = 0; y < H; y++)
        {
            float cy = TexY(y);
            for (int x = 0; x < W; x++)
            {
                int k = y * W + x;
                float cx = TexX(x);
                if (x > 0) Relax(k, k - 1, cx, cy);
                if (y > 0)
                {
                    Relax(k, k - W, cx, cy);
                    if (x > 0) Relax(k, k - W - 1, cx, cy);
                    if (x < W - 1) Relax(k, k - W + 1, cx, cy);
                }
            }
            for (int x = W - 2; x >= 0; x--) Relax(y * W + x, y * W + x + 1, TexX(x), cy);
        }
        for (int y = H - 1; y >= 0; y--)
        {
            float cy = TexY(y);
            for (int x = W - 1; x >= 0; x--)
            {
                int k = y * W + x;
                float cx = TexX(x);
                if (x < W - 1) Relax(k, k + 1, cx, cy);
                if (y < H - 1)
                {
                    Relax(k, k + W, cx, cy);
                    if (x < W - 1) Relax(k, k + W + 1, cx, cy);
                    if (x > 0) Relax(k, k + W - 1, cx, cy);
                }
            }
            for (int x = 1; x < W; x++) Relax(y * W + x, y * W + x - 1, TexX(x), cy);
        }

        Distance = new float[size];
        var bytes = new byte[size * 2];
        for (int k = 0; k < size; k++)
        {
            float d = best[k] == float.MaxValue ? 2000f : MathF.Sqrt(best[k]);
            if (land[k]) d = -d;
            Distance[k] = d;
            ushort bits = BitConverter.HalfToUInt16Bits((Half)d);
            bytes[2 * k] = (byte)bits;
            bytes[2 * k + 1] = (byte)(bits >> 8);
        }
        Sdf = ImageTexture.CreateFromImage(Image.CreateFromData(W, H, false, Image.Format.Rh, bytes));
    }

    /// <summary>Signed coast distance at a point (metres, negative on land), bilinear like the shader.</summary>
    public float DistanceAt(Vector2 m)
    {
        float fx = (m.X + (float)Map.HalfW) / Cell - 0.5f, fy = (m.Y + (float)Map.HalfH) / Cell - 0.5f;
        int x0 = Math.Clamp((int)MathF.Floor(fx), 0, W - 2), y0 = Math.Clamp((int)MathF.Floor(fy), 0, H - 2);
        float tx = Math.Clamp(fx - x0, 0, 1), ty = Math.Clamp(fy - y0, 0, 1);
        float a = Distance[y0 * W + x0], b = Distance[y0 * W + x0 + 1];
        float c = Distance[(y0 + 1) * W + x0], d = Distance[(y0 + 1) * W + x0 + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    void BuildRegions(Map map)
    {
        var idx = new byte[RW * RH];
        var water = new float[RW * RH * 3];
        var landc = new float[RW * RH * 3];
        for (int y = 0; y < RH; y++)
            for (int x = 0; x < RW; x++)
            {
                var c = new Vec2(-Map.HalfW + (x + 0.5) * RegionCell, -Map.HalfH + (y + 0.5) * RegionCell);
                int r = (int)map.RegionAt(c).Type;
                int k = y * RW + x;
                idx[k] = (byte)r;
                var wc = WaterColour[r];
                var lc = LandColour[r];
                water[3 * k] = wc.R; water[3 * k + 1] = wc.G; water[3 * k + 2] = wc.B;
                landc[3 * k] = lc.R; landc[3 * k + 1] = lc.G; landc[3 * k + 2] = lc.B;
            }
        regionIndex = idx;
        Regions = ImageTexture.CreateFromImage(Image.CreateFromData(RW, RH, false, Image.Format.R8, idx));
        WaterTint = ImageTexture.CreateFromImage(Blurred(water, 4));
        LandTint = ImageTexture.CreateFromImage(Blurred(landc, 3));
    }

    byte[] regionIndex = Array.Empty<byte>();

    /// <summary>The region at a point (metres), from the 25 m table (a cheap stand-in for Map.RegionAt in the views).</summary>
    public RegionType RegionAt(Vector2 m)
    {
        int x = Math.Clamp((int)((m.X + Map.HalfW) / RegionCell), 0, RW - 1);
        int y = Math.Clamp((int)((m.Y + Map.HalfH) / RegionCell), 0, RH - 1);
        return (RegionType)regionIndex[y * RW + x];
    }

    /// <summary>A separable box blur (radius r texels, three passes ≈ Gaussian) packed as RGB8.</summary>
    static Image Blurred(float[] rgb, int r)
    {
        var a = (float[])rgb.Clone();
        var b = new float[a.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            for (int y = 0; y < RH; y++)
                for (int x = 0; x < RW; x++)
                    for (int ch = 0; ch < 3; ch++)
                    {
                        float s = 0;
                        for (int k = -r; k <= r; k++) s += a[3 * (y * RW + Math.Clamp(x + k, 0, RW - 1)) + ch];
                        b[3 * (y * RW + x) + ch] = s / (2 * r + 1);
                    }
            for (int y = 0; y < RH; y++)
                for (int x = 0; x < RW; x++)
                    for (int ch = 0; ch < 3; ch++)
                    {
                        float s = 0;
                        for (int k = -r; k <= r; k++) s += b[3 * (Math.Clamp(y + k, 0, RH - 1) * RW + x) + ch];
                        a[3 * (y * RW + x) + ch] = s / (2 * r + 1);
                    }
        }
        var bytes = new byte[RW * RH * 3];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)Math.Clamp((int)(a[i] * 255 + 0.5f), 0, 255);
        return Image.CreateFromData(RW, RH, false, Image.Format.Rgb8, bytes);
    }
}
