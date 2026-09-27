using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The chart under the ship. The land wash, contours, coastal halo, shallows and surf come from the sea shader (via the
/// <see cref="CoastField"/>); this layer inks everything else as cached, batched meshes per chunk: each coastline as a
/// wobbling quill stroke with a hachured fringe that lengthens on the shaded (south-east) shores, pictorial stamps
/// scattered over every island by its region (palms, jungle, pines, crags, mangroves, volcanoes, ruins …), and a little
/// illustrated harbour town at every port with its quay, flag and a faint dotted harbour ring. A chunk costs about four
/// draw calls whatever it holds, and chunks are built lazily as the camera approaches. Names are lettered by
/// <see cref="ChartLabels"/>; marks that change live in <see cref="MarksView"/>.
/// </summary>
public partial class ChartView : Node2D
{
    ChartChunk[] chunks = Array.Empty<ChartChunk>();
    const int Cols = 6, Rows = 5;
    public CoastField Field { get; private set; } = null!;
    Map map = null!;

    /// <summary>Volcano summits (world px) for the smoke drawn by the life layer.</summary>
    public readonly List<Vector2> Volcanoes = new();

    /// <summary>Rebuilds every chunk (palette change: flags and marks change colour).</summary>
    public void RedrawAll()
    {
        foreach (var c in chunks) c.Invalidate();
        allBuilt = false;
    }

    bool allBuilt;

    /// <summary>The portolan wind roses (world px) the sea's rhumb lines radiate from; each is drawn here.</summary>
    public Vector2[] Roses { get; private set; } = Array.Empty<Vector2>();

    public void Init(Map m, Font font, CoastField field)
    {
        map = m;
        Field = field;
        ZIndex = 0;
        // The roses first, so every island inks over them; multiply-blended so they are printed into the paper.
        Roses = SeaLayer.PlaceHubs(field).Select(h => h * Ink.PxPerM).ToArray();
        if (WorldArt.Decor.TryGetValue("rose", out var rose) && Art.Tex("decor/atlas") is { } decor)
        {
            var rm = new StampMesh(WorldArt.DecorSize);
            for (int i = 0; i < Roses.Length; i++)
                rm.Add(rose, Roses[i], (i == 0 ? 110 : 84) * Ink.PxPerM, new Vector2(0.5f, 0.5f), new Color(1, 1, 1, 0.92f));
            var layer = new StampLayer { Mesh = rm.Build(), Atlas = decor };
            layer.Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stamp_mul.gdshader") };
            AddChild(layer);
        }
        chunks = new ChartChunk[Cols * Rows];
        for (int i = 0; i < chunks.Length; i++)
        {
            int cx = i % Cols, cy = i / Cols;
            var rect = new Rect2((float)(-Map.HalfW + cx * Map.Width / Cols) * Ink.PxPerM, (float)(-Map.HalfH + cy * Map.Height / Rows) * Ink.PxPerM,
                (float)(Map.Width / Cols) * Ink.PxPerM, (float)(Map.Height / Rows) * Ink.PxPerM);
            chunks[i] = new ChartChunk { Map = map, Field = field, Bounds = rect };
            AddChild(chunks[i]);
        }
        foreach (var island in map.Islands)
            chunks[ChunkOf(island.Centre)].Islands.Add(island);
        foreach (var port in map.Ports)
            if (!port.Secret) chunks[ChunkOf(port.Harbor)].Ports.Add(port);
        foreach (var island in map.Islands)
            if (island.Region == RegionType.Volcanic && ChartArt.VolcanoSite(island, field) is { } v)
                Volcanoes.Add(v.Summit);
    }

    static int ChunkOf(Vec2 p)
    {
        int cx = Math.Clamp((int)((p.X + Map.HalfW) / (Map.Width / Cols)), 0, Cols - 1);
        int cy = Math.Clamp((int)((p.Y + Map.HalfH) / (Map.Height / Rows)), 0, Rows - 1);
        return cy * Cols + cx;
    }

    /// <summary>How many chunks have their meshes built (the self-test checks the lazy build).</summary>
    public int BuiltChunks => chunks.Count(c => c.Built);

    public override void _Process(double delta)
    {
        // Build every chunk on screen at once, then the rest of the chart one chunk a frame, nearest first (each takes
        // a few ms), so by the time she sails anywhere its chunk is waiting and nothing hitches mid-voyage.
        if (allBuilt) return;
        var cam = GetViewport().GetCamera2D();
        if (cam == null) return;
        var centre = cam.GetScreenCenterPosition();
        var half = GetViewportRect().Size / cam.Zoom * 0.5f;
        bool builtOffScreen = false;
        allBuilt = true;
        foreach (var c in chunks.Where(c => !c.Built).OrderBy(c => DistanceTo(c.Bounds, centre)))
        {
            allBuilt = false;
            bool onScreen = DistanceTo(c.Bounds, centre) < half.Length();
            if (!onScreen)
            {
                if (builtOffScreen) break;
                builtOffScreen = true;
            }
            c.Build();
        }
    }

    static float DistanceTo(Rect2 r, Vector2 p)
    {
        float dx = Mathf.Max(0, Mathf.Max(r.Position.X - p.X, p.X - r.End.X));
        float dy = Mathf.Max(0, Mathf.Max(r.Position.Y - p.Y, p.Y - r.End.Y));
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// A port's faction mark, lettered small (ChartScreen draws it at the chart scale too): a crown for the Crown's
    /// colonies, an anchor for the free ports, a pennant for the Brethren; a bar under it for a fort; a cove is an X in a
    /// ring. Colour is never the only cue (the colorblind palette swaps inks, the shapes stay).
    /// </summary>
    public static void DrawFactionMark(CanvasItem c, Vector2 p, Faction faction, bool fort, bool secret)
    {
        if (secret)
        {
            c.DrawArc(p, 9, 0, Mathf.Tau, 28, Ink.Red, 1.6f, true);
            c.DrawLine(p + new Vector2(-5.5f, -5.5f), p + new Vector2(5.5f, 5.5f), Ink.Red, 2.2f, true);
            c.DrawLine(p + new Vector2(-5.5f, 5.5f), p + new Vector2(5.5f, -5.5f), Ink.Red, 2.2f, true);
            return;
        }
        var col = Ink.Faction(faction);
        switch (faction)
        {
            case Faction.Crown:
                // A crown: a band with three points topped by pearls.
                c.DrawColoredPolygon(new[] { p + new Vector2(-8, 2), p + new Vector2(-9, -8), p + new Vector2(-4.5f, -3), p + new Vector2(0, -11), p + new Vector2(4.5f, -3), p + new Vector2(9, -8), p + new Vector2(8, 2) }, col);
                c.DrawRect(new Rect2(p + new Vector2(-8.5f, 2), new Vector2(17, 3.5f)), col);
                c.DrawCircle(p + new Vector2(-9, -8.5f), 1.6f, col);
                c.DrawCircle(p + new Vector2(0, -11.5f), 1.6f, col);
                c.DrawCircle(p + new Vector2(9, -8.5f), 1.6f, col);
                c.DrawLine(p + new Vector2(-7, 3.7f), p + new Vector2(7, 3.7f), Ink.Paper, 1f, true);
                break;
            case Faction.FreeTraders:
                // An anchor: ring, shank, stock and two curved arms with flukes.
                c.DrawArc(p + new Vector2(0, -10), 2.6f, 0, Mathf.Tau, 12, col, 1.6f, true);
                c.DrawLine(p + new Vector2(0, -7.4f), p + new Vector2(0, 9), col, 2.2f, true);
                c.DrawLine(p + new Vector2(-5.5f, -4.5f), p + new Vector2(5.5f, -4.5f), col, 2f, true);
                c.DrawArc(p + new Vector2(0, 2), 7.5f, 0.25f, Mathf.Pi - 0.25f, 14, col, 2f, true);
                c.DrawColoredPolygon(new[] { p + new Vector2(-9.5f, 2.5f), p + new Vector2(-6.2f, 1.2f), p + new Vector2(-7.4f, 5.2f) }, col);
                c.DrawColoredPolygon(new[] { p + new Vector2(9.5f, 2.5f), p + new Vector2(6.2f, 1.2f), p + new Vector2(7.4f, 5.2f) }, col);
                break;
            default:
                // A swallow-tailed pennant on a staff.
                c.DrawLine(p + new Vector2(0, 9), p + new Vector2(0, -13), col, 2f, true);
                c.DrawColoredPolygon(new[] { p + new Vector2(0, -13), p + new Vector2(13, -11), p + new Vector2(8.5f, -8.5f), p + new Vector2(13, -5.5f), p + new Vector2(0, -4) }, col);
                break;
        }
        if (fort)
        {
            c.DrawLine(p + new Vector2(-11, 8.5f), p + new Vector2(11, 8.5f), col, 2.2f, true);
            c.DrawLine(p + new Vector2(-11, 8.5f), p + new Vector2(-11, 5.5f), col, 1.6f, true);
            c.DrawLine(p + new Vector2(11, 8.5f), p + new Vector2(11, 5.5f), col, 1.6f, true);
        }
    }
}

/// <summary>One cached piece of the chart: an ink mesh (coasts, fringes, quays, rings), the land and town stamp meshes,
/// and an ink layer on top for flags. Built on demand; invalidated on a palette change.</summary>
public partial class ChartChunk : Node2D
{
    public Map Map = null!;
    public CoastField Field = null!;
    public Rect2 Bounds;
    public readonly List<Island> Islands = new();
    public readonly List<Port> Ports = new();
    public bool Built { get; private set; }

    ArrayMesh? ink;
    StampLayer landLayer = null!, townLayer = null!;
    InkLayer topLayer = null!;

    public override void _Ready()
    {
        Material = InkMesh.Material;
        landLayer = new StampLayer();
        townLayer = new StampLayer();
        topLayer = new InkLayer();
        AddChild(landLayer);
        AddChild(townLayer);
        AddChild(topLayer);
    }

    public void Invalidate()
    {
        Built = false;
        ink = null;
        landLayer.Mesh = townLayer.Mesh = null;
        topLayer.Mesh = null;
        QueueRedraw();
        landLayer.QueueRedraw();
        townLayer.QueueRedraw();
        topLayer.QueueRedraw();
    }

    public static double BuildMsTotal, BuildMsMax;

    public void Build()
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        BuildInner();
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        BuildMsTotal += ms;
        BuildMsMax = Math.Max(BuildMsMax, ms);
    }

    void BuildInner()
    {
        Built = true;
        var inkB = new InkMesh();
        var top = new InkMesh();
        var land = new StampMesh(WorldArt.LandSize);
        var town = new StampMesh(WorldArt.PortsSize);
        var towns = Ports.Select(p => p.Pos).ToList();
        foreach (var island in Islands)
        {
            ChartArt.InkCoast(inkB, island, Field);
            ChartArt.StampIsland(land, island, Field, Map.Seed, Map.Ports.Where(p => p.IslandId == island.Id).Select(p => p.Pos).ToList());
        }
        foreach (var port in Ports)
        {
            ChartArt.InkPort(inkB, top, port, Field);
            ChartArt.StampTown(town, port, Field, Map.Seed);
        }
        ink = inkB.Build();
        landLayer.Mesh = land.Build();
        landLayer.Atlas = Art.Tex("land/atlas");
        townLayer.Mesh = town.Build();
        townLayer.Atlas = Art.Tex("ports/atlas");
        topLayer.Mesh = top.Build();
        QueueRedraw();
        landLayer.QueueRedraw();
        townLayer.QueueRedraw();
        topLayer.QueueRedraw();
    }

    public override void _Draw()
    {
        if (ink != null) DrawMesh(ink, null);
    }
}

/// <summary>Draws one cached stamp mesh with its atlas (one draw call).</summary>
public partial class StampLayer : Node2D
{
    public ArrayMesh? Mesh;
    public Texture2D? Atlas;

    public override void _Ready() => TextureFilter = TextureFilterEnum.LinearWithMipmaps;

    public override void _Draw()
    {
        if (Mesh != null && Atlas != null) DrawMesh(Mesh, Atlas);
    }
}

/// <summary>Draws one cached ink mesh with the ink material (one draw call).</summary>
public partial class InkLayer : Node2D
{
    public ArrayMesh? Mesh;

    public override void _Ready() => Material = InkMesh.Material;

    public override void _Draw()
    {
        if (Mesh != null) DrawMesh(Mesh, null);
    }
}

/// <summary>The drawing recipes for the chart: coasts, stamps, towns. Deterministic per map seed and island/port id.</summary>
public static class ChartArt
{
    const float P = Ink.PxPerM;

    static float Nz(float x, float y, int salt) => (float)LastTide.Sim.Noise.Value3(salt, x, y, 0.5);

    static Vector2 V(Vec2 v) => new((float)v.X, (float)v.Y);

    static float SignedArea(Vector2[] c)
    {
        float a = 0;
        for (int i = 0, j = c.Length - 1; i < c.Length; j = i++) a += c[j].X * c[i].Y - c[i].X * c[j].Y;
        return a * 0.5f;
    }

    /// <summary>The coastline as a quill stroke that wobbles and swells, plus the hachured fringe on the water side.</summary>
    public static void InkCoast(InkMesh ink, Island island, CoastField field)
    {
        var curve = field.Coast[island.Id];
        int n = curve.Length;
        float orient = SignedArea(curve) > 0 ? 1f : -1f;
        var pts = new Vector2[n];
        var hw = new float[n];
        var outward = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            var p = curve[i];
            var t = (curve[(i + 1) % n] - curve[(i - 1 + n) % n]).Normalized();
            var o = new Vector2(t.Y, -t.X) * orient;
            outward[i] = o;
            float wob = Nz(p.X / 9f, p.Y / 9f, 71) * 0.32f + Nz(p.X / 2.6f, p.Y / 2.6f, 73) * 0.1f;
            pts[i] = (p + o * wob) * P;
            hw[i] = 1.05f + 0.55f * Nz(p.X / 16f, p.Y / 16f, 79);
        }
        ink.Stroke(pts, i => hw[i], Ink.Black, closed: true);

        // The fringe: short strokes out into the water, longer on the shores facing away from a north-west light.
        var shadeDir = new Vector2(0.6f, 0.8f);
        var fringe = Ink.Black with { A = 0.62f };
        for (int i = 0; i < n; i++)
        {
            var p = curve[i];
            var o = outward[i];
            float shade = Mathf.Clamp(o.Dot(shadeDir) * 0.5f + 0.5f, 0, 1);
            float jitter = Nz(p.X / 3f, p.Y / 3f, 83) * 0.5f + 0.5f;
            float len = 0.7f + 3.2f * shade * shade + 0.9f * jitter;
            if (shade < 0.3f && (i & 1) == 1) continue;       // the lit shores are barely hatched
            var a = pts[i] + o * (hw[i] + 0.4f * P);
            var b = a + o * len * P;
            ink.Tick(a, b, 0.5f, 0.06f, fringe);
        }
    }

    // ---- stamps ----

    record struct Flora(string Name, float Height, float Weight);

    static readonly Dictionary<RegionType, Flora[]> FloraByRegion = new()
    {
        [RegionType.TradeIsles] = new Flora[] { new("palm", 14, 3), new("palm-pair", 14, 2), new("palm-lean", 12, 2), new("jungle", 10, 2.2f), new("tuft", 4, 2.5f), new("jungle-tree", 13, 0.8f), new("rocks", 5, 0.5f), new("fern", 5, 1) },
        [RegionType.Shoals] = new Flora[] { new("palm", 13, 2), new("palm-lean", 12, 2.5f), new("palm-pair", 13, 1), new("tuft", 4, 3), new("dune", 5, 2.5f), new("rocks", 5, 0.8f) },
        [RegionType.Deep] = new Flora[] { new("conifer", 12, 1.5f), new("knoll", 7, 1.5f), new("slate-rocks", 6, 1.5f), new("heather", 5, 1.2f), new("tuft", 4, 1.2f), new("rocks", 6, 1), new("bent-tree", 10, 0.6f) },
        [RegionType.FogBanks] = new Flora[] { new("conifer", 12, 2), new("conifers", 13, 2), new("heather", 5, 2.2f), new("knoll", 7, 1.2f), new("bent-tree", 10, 1), new("slate-rocks", 6, 0.8f) },
        [RegionType.StormReach] = new Flora[] { new("slate-rocks", 6, 2), new("bent-tree", 10, 2.2f), new("heather", 5, 1.5f), new("knoll", 7, 1.2f), new("conifer", 12, 1), new("dead-tree", 10, 0.6f) },
        [RegionType.Mangrove] = new Flora[] { new("mangrove", 12, 3), new("mangroves", 12, 3), new("reeds", 7, 2), new("fern", 6, 2), new("jungle-tree", 14, 1.2f), new("jungle", 10, 1) },
        [RegionType.Volcanic] = new Flora[] { new("lava-rocks", 6, 2.5f), new("dead-tree", 10, 1.8f), new("fern", 5, 0.8f), new("rocks", 5, 0.8f), new("tuft", 3.5f, 0.8f) },
        [RegionType.Sargasso] = new Flora[] { new("dune", 5, 2.5f), new("palm-lean", 12, 1.5f), new("tuft", 4, 2.5f), new("palm", 13, 1), new("rocks", 5, 0.8f) },
        [RegionType.SirenRuins] = new Flora[] { new("palm", 13, 1.6f), new("tuft", 4, 1.6f), new("rocks", 5, 1), new("column", 10, 1.2f), new("fallen-columns", 5, 1.2f), new("ruined-wall", 7, 0.8f), new("obelisk", 12, 0.35f), new("statue", 10, 0.3f), new("altar", 5, 0.4f), new("stone-head", 6, 0.25f) },
    };

    /// <summary>The one big landmark an island gets near its heart, if it is large enough (name, height m, min inland m).</summary>
    static readonly Dictionary<RegionType, (string Name, float Height)[]> Landmarks = new()
    {
        [RegionType.TradeIsles] = new[] { ("hill", 9f), ("peaks", 16f) },
        [RegionType.Shoals] = new[] { ("dune", 7f) },
        [RegionType.Deep] = new[] { ("crag", 18f), ("peaks", 18f) },
        [RegionType.FogBanks] = new[] { ("crag", 16f), ("ruin-tower", 13f) },
        [RegionType.StormReach] = new[] { ("crag", 18f), ("ruin-tower", 13f) },
        [RegionType.Mangrove] = new[] { ("jungle-tree", 16f) },
        [RegionType.Volcanic] = new[] { ("volcano", 44f) },
        [RegionType.Sargasso] = new[] { ("dune", 7f), ("hill", 9f) },
        [RegionType.SirenRuins] = new[] { ("colonnade", 15f), ("arch", 11f), ("stone-head", 8f) },
    };

    /// <summary>Where an island's volcano stands (its most inland point), for the smoke; null if too small.</summary>
    public static (Vector2 Base, Vector2 Summit, float Height)? VolcanoSite(Island island, CoastField field)
    {
        var (at, inland) = MostInland(island, field);
        if (inland < 22) return null;
        float h = Mathf.Clamp(inland * 0.9f, 26f, 58f);
        return (at, (at - new Vector2(0, h * 0.62f)) * P, h);   // the crater: the cone's stamp stands at at + 0.2h, 0.93 up
    }

    static (Vector2 At, float Inland) MostInland(Island island, CoastField field)
    {
        var c = V(island.Centre);
        float best = float.MaxValue;
        var at = c;
        float r = (float)island.BoundRadius;
        for (float y = -r; y <= r; y += 6)
            for (float x = -r; x <= r; x += 6)
            {
                var q = c + new Vector2(x, y);
                float d = field.DistanceAt(q);
                if (d < best) { best = d; at = q; }
            }
        return (at, -best);
    }

    record struct Placed(string Name, Vector2 Base, float Height, bool Flip, float Shade);

    /// <summary>Pictorial stamps scattered over an island: clustered like real cover, clear of the shore and the town.</summary>
    public static void StampIsland(StampMesh mesh, Island island, CoastField field, int seed, List<Vec2> towns)
    {
        var rng = new Rng(unchecked((ulong)(seed * 7919L + island.Id * 104729L + 17)));
        var flora = FloraByRegion[island.Region];
        float total = flora.Sum(f => f.Weight);
        var placed = new List<Placed>();
        var townPts = towns.Select(V).ToList();
        var c = V(island.Centre);
        float r = (float)island.BoundRadius;

        bool Clear(Vector2 p, float room)
        {
            foreach (var q in placed)
                if (q.Base.DistanceTo(p) < (room + q.Height * 0.42f)) return false;
            foreach (var t in townPts)
                if (t.DistanceTo(p) < 52) return false;
            return true;
        }

        // The landmark first, at the heart of the island.
        var (heart, inland) = MostInland(island, field);
        if (Landmarks.TryGetValue(island.Region, out var marks) && inland > 16)
        {
            var (name, h) = marks[rng.Next(marks.Length)];
            if (island.Region == RegionType.Volcanic && VolcanoSite(island, field) is { } v) h = v.Height;
            else h *= Mathf.Clamp(inland / 30f, 0.8f, 1.5f);
            if (Clear(heart, 0)) placed.Add(new Placed(name, heart + new Vector2(0, h * 0.2f), h, rng.NextDouble() < 0.5, 1));
        }

        // Then the cover: dart-throwing with a clustering field, sized to the island.
        float area = Mathf.Pi * r * r * 0.6f;
        int want = Math.Clamp((int)(area / 230f), 3, 420);
        for (int tries = 0; tries < want * 5 && placed.Count < want; tries++)
        {
            var p = c + new Vector2((float)rng.Range(-r, r), (float)rng.Range(-r, r));
            float d = field.DistanceAt(p);
            if (d > -4.5f) continue;
            float cluster = Nz(p.X / 42f, p.Y / 42f, 97 + island.Id) * 0.5f + 0.5f;
            if (rng.NextDouble() > 0.25 + 0.9 * cluster) continue;
            double pick = rng.NextDouble() * total;
            var f = flora[0];
            foreach (var fl in flora)
            {
                if ((pick -= fl.Weight) <= 0) { f = fl; break; }
            }
            float h = f.Height * (float)rng.Range(0.85, 1.15);
            if (-d < h * 0.3f && f.Height > 8) continue;   // tall stamps stay off the beach
            if (!Clear(p, h * 0.3f)) continue;
            placed.Add(new Placed(f.Name, p, h, rng.NextDouble() < 0.5, (float)rng.Range(0.9, 1.04)));
        }
        foreach (var s in placed.OrderBy(s => s.Base.Y))
        {
            if (!WorldArt.Land.TryGetValue(s.Name, out var src)) continue;
            mesh.Add(src, s.Base * P, s.Height * P, new Vector2(0.5f, 0.93f), new Color(s.Shade, s.Shade, s.Shade * 0.98f), s.Flip);
        }
    }

    // ---- ports ----

    static readonly Dictionary<Faction, (string Name, float Height)[]> Buildings = new()
    {
        [Faction.Crown] = new[] { ("church", 15f), ("customs-house", 9.5f), ("townhouse", 11f), ("row", 9f), ("house", 8f), ("warehouse", 8f), ("house", 8f), ("townhouse", 11f), ("row", 9f), ("barrels", 4.5f), ("house", 8f) },
        [Faction.FreeTraders] = new[] { ("warehouse", 8.5f), ("house", 8f), ("row", 9f), ("windmill", 12f), ("customs-house", 9.5f), ("barrels", 4.5f), ("townhouse", 11f), ("house", 8f), ("row", 9f), ("warehouse", 8.5f), ("house", 8f) },
        [Faction.Brethren] = new[] { ("shack", 7.5f), ("stilt-huts", 8f), ("barrels", 4.5f), ("watchtower", 12f), ("stilt-hut", 8f), ("shack", 7.5f), ("house", 8f), ("barrels", 4.5f), ("stilt-hut", 8f) },
    };

    /// <summary>The town: buildings clustered inland of the quay (count by port size, kind by faction).</summary>
    public static void StampTown(StampMesh mesh, Port port, CoastField field, int seed)
    {
        var rng = new Rng(unchecked((ulong)(seed * 31L + port.Id * 7777L + 5)));
        var pos = V(port.Pos);
        var inward = (pos - V(port.Harbor)).Normalized();
        var side = new Vector2(-inward.Y, inward.X);
        var kinds = Buildings[port.Faction];
        int count = port.Secret ? 1 : port.Size switch { 0 => 4, 1 => 7, _ => 11 };
        var placed = new List<Placed>();
        // The jetty (plan view) runs from the shore out toward the harbour ring.
        if (WorldArt.Ports.TryGetValue("jetty", out var jetty))
        {
            var outDir = -inward;
            float len = port.Secret ? 10 : 15 + port.Size * 3;
            mesh.Add(jetty, (pos - outDir * 3) * P, (len + 3) * P, new Vector2(0.5f, 1f), Colors.White, false, Mathf.Atan2(outDir.Y, outDir.X) + Mathf.Pi / 2);
        }
        // A Crown fort: a star-shaped bastion (plan view) on the nearest headland along the shore.
        if (port.Fort && !port.Secret && WorldArt.Ports.TryGetValue("star-fort", out var fort))
        {
            for (int k = 0; k < 24; k++)
            {
                var q = pos + side * ((k % 2 == 0 ? 1 : -1) * (26 + k * 2.2f)) + inward * 16;
                if (field.DistanceAt(q) > -12f) continue;
                mesh.Add(fort, q * P, 24 * P, new Vector2(0.5f, 0.5f), Colors.White);
                placed.Add(new Placed("star-fort", q + new Vector2(0, 14), 26, false, 1));
                break;
            }
        }
        if (port.Secret)
        {
            // A cove: a hut on stilts and a smuggler's lantern at the water's edge.
            var hut = pos + inward * 9 + side * 5;
            if (field.DistanceAt(hut) < -2f) placed.Add(new Placed("stilt-hut", hut, 8, false, 1));
            var lamp = pos + inward * 4 - side * 4;
            if (field.DistanceAt(lamp) < -1f) placed.Add(new Placed("lantern", lamp, 6, false, 1));
        }
        if (port.Size == 2 && !port.Secret)
        {
            // A lighthouse on the shore beside the harbour mouth.
            for (int k = 0; k < 12; k++)
            {
                var q = pos + side * ((k % 2 == 0 ? 1 : -1) * (14 + k * 2.5f)) + inward * 3;
                if (field.DistanceAt(q) < -3f) { placed.Add(new Placed("lighthouse", q, 14, false, 1)); break; }
            }
        }
        int extras = placed.Count;
        for (int tries = 0; tries < 120 && placed.Count - extras < count; tries++)
        {
            int i = placed.Count - extras;
            var (name, h) = port.Secret ? ("shack", 7f) : kinds[i % kinds.Length];
            float depth = 7 + (float)rng.NextDouble() * (14 + count * 3.2f);
            float spread = (float)(rng.NextDouble() - 0.5) * (22 + count * 4f);
            var q = pos + inward * depth + side * spread;
            if (field.DistanceAt(q) > -3.2f) continue;
            if (placed.Any(p => p.Base.DistanceTo(q) < (h + p.Height) * 0.36f)) continue;
            placed.Add(new Placed(name, q, h * (float)rng.Range(0.92, 1.08), rng.NextDouble() < 0.5 && name != "church", 1));
        }
        foreach (var s in placed.Where(s => s.Name != "star-fort").OrderBy(s => s.Base.Y))
            if (WorldArt.Ports.TryGetValue(s.Name, out var src))
                mesh.Add(src, s.Base * P, s.Height * P, new Vector2(0.5f, 0.95f), Colors.White, s.Flip);
    }

    /// <summary>The quay (a plank jetty out toward the harbour), the faction flag, and the faint dotted harbour ring.</summary>
    public static void InkPort(InkMesh ink, InkMesh top, Port port, CoastField field)
    {
        var pos = V(port.Pos);
        var harbor = V(port.Harbor);
        var outDir = (harbor - pos).Normalized();
        var side = new Vector2(-outDir.Y, outDir.X);
        // The jetty: from a little inland to ~14 m out, two metres wide, planked.
        if (!WorldArt.Ports.ContainsKey("jetty"))
        {
            // Fallback when the art is absent: an inked plank jetty.
            float len = port.Secret ? 8 : 12 + port.Size * 3;
            var a = pos - outDir * 3;
            var b = pos + outDir * len;
            var deck = new[] { (a + side * 1.2f) * P, (b + side * 1.2f) * P, (b - side * 1.2f) * P, (a - side * 1.2f) * P };
            ink.Fill(deck, new Color(0.60f, 0.46f, 0.30f));
            for (float s = 0; s <= len + 3; s += 1.1f)
            {
                var m = a + outDir * s;
                ink.Tick((m + side * 1.2f) * P, (m - side * 1.2f) * P, 0.35f, 0.35f, Ink.Black with { A = 0.55f });
            }
        }

        // The flag at the jetty's root: a staff and the faction's colours, flying east as charts draw them.
        if (!port.Secret)
        {
            var root = (pos - outDir * 5 + side * 3.5f) * P;
            var topP = root + new Vector2(0, -13 * P);
            top.Stroke(new[] { root, topP }, i => i == 0 ? 0.9f : 0.6f, Ink.Black);
            var col = Ink.Faction(port.Faction);
            float fw = 7.5f * P, fh = 4.2f * P;
            switch (port.Faction)
            {
                case Faction.Crown:
                    top.Fill(new[] { topP, topP + new Vector2(fw, 0.6f * P), topP + new Vector2(fw * 0.97f, fh), topP + new Vector2(0, fh) }, col);
                    top.Stroke(new[] { topP + new Vector2(fw * 0.42f, 0.3f * P), topP + new Vector2(fw * 0.42f, fh) }, _ => 0.9f, Ink.Paper);
                    top.Stroke(new[] { topP + new Vector2(0, fh * 0.5f), topP + new Vector2(fw * 0.98f, fh * 0.52f) }, _ => 0.9f, Ink.Paper);
                    break;
                case Faction.FreeTraders:
                    top.Fill(new[] { topP, topP + new Vector2(fw, 0.9f * P), topP + new Vector2(fw * 0.62f, fh * 0.55f), topP + new Vector2(fw, fh * 1.05f), topP + new Vector2(0, fh) }, col);
                    break;
                default:
                    top.Fill(new[] { topP, topP + new Vector2(fw * 1.05f, fh * 0.45f), topP + new Vector2(0, fh * 0.95f) }, col);
                    top.Dot(topP + new Vector2(fw * 0.32f, fh * 0.45f), 1.4f, Ink.Paper);
                    break;
            }
        }

        // The harbour ring: faint dots (the sea layer brightens it when she is in range).
        float rr = (float)port.RingRadius;
        int dots = (int)(Mathf.Tau * rr / 3.2f);
        var faint = Ink.Black with { A = port.Secret ? 0.35f : 0.28f };
        for (int k = 0; k < dots; k++)
        {
            float t = k * Mathf.Tau / dots;
            ink.Dot((harbor + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * rr) * P, 0.9f, faint);
        }
    }
}

/// <summary>
/// Marks that appear during a run, inked like the chart and redrawn only when something changes: secret coves once
/// found (a hidden jetty, two shacks, a red ring), the X and dig ring of a matched bottle map, revealed wrecks with
/// their salvage ring, the "?" of a tavern rumour, and the player's ink pins. Hosts the port lettering and the
/// harbour-ring glow as children.
/// </summary>
public partial class MarksView : Node2D
{
    World world = null!;
    Font font = null!;
    CoastField field = null!;
    int drawn = int.MinValue;
    ArrayMesh? ink;
    StampLayer coves = null!;
    MarksText text = null!;
    public ChartLabels Labels { get; private set; } = null!;
    public HarbourGlow Glow { get; private set; } = null!;

    public void Init(World w, Font f, CoastField coast)
    {
        world = w;
        font = f;
        field = coast;
        ZIndex = 1;
        Material = InkMesh.Material;
        coves = new StampLayer();
        AddChild(coves);
        text = new MarksText { Font = f };
        AddChild(text);
        Glow = new HarbourGlow();
        Glow.Init(w);
        AddChild(Glow);
        Labels = new ChartLabels();
        Labels.Init(w);
        AddChild(Labels);
    }

    int State()
    {
        var h = new HashCode();
        foreach (var p in world.Map.Ports) if (p.Secret && p.Discovered) h.Add(p.Id);
        foreach (var pin in world.Pins) { h.Add(pin.Pos.X); h.Add(pin.Pos.Y); h.Add(pin.Note); }
        foreach (var t in world.Map.Treasures) if (t.Dug) h.Add(t.Id * 3 + 1);
        foreach (var m in world.Player.BottleMaps) if (m.Solved) h.Add(m.Treasure * 3 + 2);
        foreach (var wr in world.Map.Wrecks) h.Add(wr.Salvaged ? 1 : world.Reveal.IsRevealed(wr.Pos) ? 2 : 3);
        foreach (var c in world.Player.CoveHints) { h.Add(c.X); h.Add(c.Y); }
        h.Add(Ink.PaletteVersion);
        return h.ToHashCode();
    }

    public override void _Process(double delta)
    {
        int state = State();
        if (state == drawn) return;
        drawn = state;
        Rebuild();
    }

    const float P = Ink.PxPerM;

    void Rebuild()
    {
        var m = new InkMesh();
        var town = new StampMesh(WorldArt.PortsSize);
        text.Items.Clear();
        text.Crosses.Clear();
        foreach (var port in world.Map.Ports)
        {
            if (!port.Secret || !port.Discovered) continue;
            ChartArt.InkPort(m, m, port, field);
            ChartArt.StampTown(town, port, field, world.Map.Seed);
            // the smugglers' ring and mark in red, as if added to the chart in a different hand
            var h = Ink.V(port.Harbor);
            float rr = (float)port.RingRadius * P;
            DashedCircle(m, h, rr, 18, 0.55f, Ink.Red with { A = 0.55f }, 1.1f);
        }
        foreach (var pin in world.Pins)
        {
            var p = Ink.V(pin.Pos);
            m.Stroke(new[] { p, p + new Vector2(1.5f, -15), p + new Vector2(2.5f, -24) }, i => new[] { 0.3f, 0.9f, 0.7f }[i], Ink.Black);
            m.Fill(Circle(p + new Vector2(2.6f, -26), 4.8f, 14), Ink.Red);
            m.Dot(p + new Vector2(1.4f, -27.6f), 1.3f, Ink.Paper with { A = 0.8f });
            if (pin.Note.Length > 0) text.Items.Add((p + new Vector2(10, -20), pin.Note, 17, Ink.Black, Fonts.Italic));
        }
        foreach (var map in world.Player.BottleMaps)
        {
            if (!map.Solved) continue;
            var site = world.Map.Treasures[map.Treasure];
            if (site.Dug) continue;
            var x = Ink.V(site.Pos);
            // Two brush strokes, each fat in the middle and flicked at the ends, over a pale wash so it reads on any land.
            var xm = new InkMesh();
            xm.Dot(Vector2.Zero, 19, Ink.Paper with { A = 0.55f });
            xm.Stroke(Brush(new Vector2(-14, -13), new Vector2(14, 14), 2.5f), i => 3.8f * Mathf.Sin((i + 0.6f) / 9.2f * Mathf.Pi), Ink.Red);
            xm.Stroke(Brush(new Vector2(-13, 14), new Vector2(14, -12), -2f), i => 3.6f * Mathf.Sin((i + 0.6f) / 9.2f * Mathf.Pi), Ink.Red);
            if (xm.Build() is { } xmesh) text.Crosses.Add((x, xmesh));
            var ring = Ink.V(site.DigRing);
            float r = (float)site.RingRadius * P;
            DashedCircle(m, ring, r, 30, 0.6f, Ink.Red with { A = 0.8f }, 1.5f);
            // a trail of dots from the ring to the X, the classic treasure-map path
            var from = ring + (x - ring).Normalized() * r;
            float dist = from.DistanceTo(x) - 18;
            for (float s = 12; s < dist; s += 15)
                m.Dot(from + (x - from).Normalized() * s + new Vector2(0, Mathf.Sin(s * 0.05f) * 6), 2.4f, Ink.Red with { A = 0.75f });
            text.Items.Add((ring + new Vector2(0, r + 30), Text.Get("MARK_DIG"), 19, Ink.Red, Fonts.Italic));
        }
        foreach (var wr in world.Map.Wrecks)
        {
            if (wr.Salvaged || !world.Reveal.IsRevealed(wr.Pos)) continue;
            var p = Ink.V(wr.Pos);
            // a broken hull: keel, ribs and a snapped mast, half sunk
            m.Stroke(new[] { p + new Vector2(-26, 4), p + new Vector2(-10, 8), p + new Vector2(8, 7), p + new Vector2(26, 1) }, i => 1.6f, Ink.Black);
            for (int k = 0; k < 6; k++)
            {
                float x0 = -20 + k * 8;
                m.Tick(p + new Vector2(x0, 6), p + new Vector2(x0 + 2, -8 + (k % 2) * 4), 1.1f, 0.4f, Ink.Black);
            }
            m.Tick(p + new Vector2(-4, 5), p + new Vector2(6, -30), 1.4f, 0.8f, Ink.Black);
            m.Tick(p + new Vector2(6, -30), p + new Vector2(12, -24), 0.8f, 0.4f, Ink.Black);
            DashedCircle(m, p, (float)wr.RingRadius * P, 26, 0.5f, Ink.Black with { A = 0.35f }, 1f);
        }
        foreach (var hint in world.Player.CoveHints)
        {
            var c = Ink.V(new Vec2(hint.X, hint.Y));
            float r = (float)hint.Radius * P;
            DashedCircle(m, c, r, 90, 0.55f, Ink.Red with { A = 0.5f }, 1.6f);
            // question marks sprinkled over the searched water
            for (int k = 0; k < 7; k++)
            {
                float a = k * Mathf.Tau / 7 + 0.4f;
                var at = k == 0 ? c : c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.58f;
                text.Items.Add((at, "?", 44, Ink.Red with { A = 0.62f }, Fonts.DisplayItalic));
            }
        }
        ink = m.Build();
        coves.Mesh = town.Build();
        coves.Atlas = Art.Tex("ports/atlas");
        QueueRedraw();
        coves.QueueRedraw();
        text.QueueRedraw();
    }

    static Vector2[] Circle(Vector2 c, float r, int n)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) pts[i] = c + new Vector2(Mathf.Cos(i * Mathf.Tau / n), Mathf.Sin(i * Mathf.Tau / n)) * r;
        return pts;
    }

    /// <summary>A gently bowed brush stroke from a to b (10 points).</summary>
    static Vector2[] Brush(Vector2 a, Vector2 b, float bow)
    {
        var n = (b - a).Normalized();
        var perp = new Vector2(-n.Y, n.X);
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float t = i / 9f;
            pts[i] = a.Lerp(b, t) + perp * (Mathf.Sin(t * Mathf.Pi) * bow);
        }
        return pts;
    }

    /// <summary>A hand-drawn dashed circle: dashes that swell and thin, the gaps a little uneven.</summary>
    public static void DashedCircle(InkMesh m, Vector2 c, float r, int dashes, float fill, Color col, float width)
    {
        for (int k = 0; k < dashes; k++)
        {
            float a0 = (k + 0.08f * Mathf.Sin(k * 1.7f)) * Mathf.Tau / dashes;
            float a1 = a0 + fill * Mathf.Tau / dashes;
            var pts = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / 5f);
                pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            m.Stroke(pts, i => width * (0.55f + 0.45f * Mathf.Sin((i + 0.5f) / 6f * Mathf.Pi)), col);
        }
    }

    public override void _Draw()
    {
        if (ink != null) DrawMesh(ink, null);
    }
}

/// <summary>
/// The lettering on the marks (notes, "dig here", rumour question marks) and the treasure X's: world-anchored but
/// counter-scaled with the zoom so they stay legible from the widest view to the closest.
/// </summary>
public partial class MarksText : Node2D
{
    public Font Font = null!;
    public readonly List<(Vector2 At, string Text, int Size, Color Col, Font Face)> Items = new();
    public readonly List<(Vector2 At, ArrayMesh Mesh)> Crosses = new();
    readonly CrossLayer crosses = new();
    float lastZoom = -1;

    public override void _Ready()
    {
        crosses.Owner_ = this;
        AddChild(crosses);
    }

    /// <summary>The counter-scale for a zoom: marks are ~1.5× bigger on screen at the widest view than at 1.25.</summary>
    public static float ScaleFor(float zoom) => Mathf.Clamp(Mathf.Pow(zoom, -0.7f), 0.55f, 2.6f);

    public override void _Process(double delta)
    {
        float z = GetViewport().GetCamera2D()?.Zoom.X ?? 1;
        if (Mathf.Abs(z - lastZoom) > 0.002f)
        {
            lastZoom = z;
            QueueRedraw();
            crosses.QueueRedraw();
        }
    }

    public new void QueueRedraw()
    {
        base.QueueRedraw();
        crosses.QueueRedraw();
    }

    public override void _Draw()
    {
        float s = ScaleFor(GetViewport().GetCamera2D()?.Zoom.X ?? 1);
        foreach (var (at, text, size, col, face) in Items)
        {
            var sz = face.GetStringSize(text, HorizontalAlignment.Center, -1, size);
            DrawSetTransform(at, 0, Vector2.One * s);
            DrawString(face, new Vector2(-sz.X / 2, sz.Y * 0.3f), text, HorizontalAlignment.Center, -1, size, col);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>Draws the X meshes with the ink material, counter-scaled about each X.</summary>
    partial class CrossLayer : Node2D
    {
        public MarksText Owner_ = null!;
        public override void _Ready() => Material = InkMesh.Material;
        public override void _Draw()
        {
            float s = ScaleFor(GetViewport().GetCamera2D()?.Zoom.X ?? 1);
            foreach (var (at, mesh) in Owner_.Crosses)
            {
                DrawSetTransform(at, 0, Vector2.One * s);
                DrawMesh(mesh, null);
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}

/// <summary>
/// The harbour ring of the port she is at or approaching, drawn clearer than the chart's faint dotted ring: a slowly
/// turning dashed ring that strengthens as she closes and is strongest inside (where F docks).
/// </summary>
public partial class HarbourGlow : Node2D
{
    World world = null!;
    float time;
    readonly List<Vector2> pts = new();
    public Port? Active { get; private set; }
    public float Strength { get; private set; }

    public void Init(World w) { world = w; ZIndex = 2; }

    public override void _Process(double delta)
    {
        time += (float)delta;
        Port? best = null;
        float bestGap = float.MaxValue;
        foreach (var port in world.Map.Ports)
        {
            if (port.Secret && !port.Discovered) continue;
            float gap = (float)(port.Harbor.DistanceTo(world.Ship.Pos) - port.RingRadius);
            if (gap < bestGap) { bestGap = gap; best = port; }
        }
        float want = best == null || bestGap > 80 ? 0 : bestGap <= 0 ? 1f : 0.45f * (1 - bestGap / 80f);
        if (best != Active && want > 0) { Active = best; }
        Strength += (want - Strength) * Mathf.Min(1, (float)delta * 4);
        if (Strength < 0.01f && want == 0) Active = null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Active == null || Strength < 0.01f) return;
        var c = Ink.V(Active.Harbor);
        float r = (float)Active.RingRadius * Ink.PxPerM;
        pts.Clear();
        int n = 48;
        float spin = time * 0.08f;
        for (int k = 0; k < n; k++)
        {
            float a0 = k * Mathf.Tau / n + spin, a1 = a0 + Mathf.Tau / n * 0.45f;
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / 4f), b = Mathf.Lerp(a0, a1, (i + 1) / 4f);
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                pts.Add(c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * r);
            }
        }
        var col = (Active.Secret ? Ink.Red : Ink.Black) with { A = 0.42f * Strength };
        DrawMultiline(pts.ToArray(), col, 1.6f, true);
    }
}

/// <summary>
/// Port names lettered on the chart: small capitals on a paper ribbon with the faction mark, a cove's name in red
/// italic. World-anchored behind each town but counter-scaled with the zoom so a name stays legible from the widest
/// view (about 13 px) to the closest (about 24 px). Only names on screen and already charted are drawn.
/// </summary>
public partial class ChartLabels : Node2D
{
    World world = null!;
    static FontFile? caps, italic;

    public void Init(World w) { world = w; ZIndex = 3; }

    /// <summary>A crisp-at-any-scale copy of a face (multichannel signed distance field).</summary>
    static FontFile Msdf(FontFile f)
    {
        var d = (FontFile)f.Duplicate();
        d.MultichannelSignedDistanceField = true;
        d.MsdfPixelRange = 12;
        d.MsdfSize = 48;
        return d;
    }

    public override void _Ready()
    {
        caps ??= Msdf(Fonts.SmallCaps);
        italic ??= Msdf(Fonts.Italic);
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>Where a port's name hangs (world px): inland of the town, clear of the harbour ring.</summary>
    public static Vector2 Anchor(Port port)
    {
        var pos = Ink.V(port.Pos);
        var inward = (pos - Ink.V(port.Harbor)).Normalized();
        return pos + inward * ((port.Secret ? 30 : 26 + port.Size * 8) * Ink.PxPerM);
    }

    public override void _Draw()
    {
        var cam = GetViewport().GetCamera2D();
        if (cam == null) return;
        float zoom = cam.Zoom.X;
        float s = Mathf.Pow(zoom, -0.71f);
        var centre = cam.GetScreenCenterPosition();
        var half = GetViewportRect().Size / zoom * 0.5f + new Vector2(400, 200);
        foreach (var port in world.Map.Ports)
        {
            if (port.Secret && !port.Discovered) continue;
            if (!world.Reveal.IsRevealed(port.Pos)) continue;
            var at = Anchor(port);
            if (Mathf.Abs(at.X - centre.X) > half.X || Mathf.Abs(at.Y - centre.Y) > half.Y) continue;
            DrawSetTransform(at, 0, Vector2.One * s);
            if (port.Secret) DrawCove(port); else DrawRibbon(port, at, s);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    void DrawRibbon(Port port, Vector2 at, float s)
    {
        var face = caps!;
        int size = 18;
        var sz = face.GetStringSize(port.Name, HorizontalAlignment.Left, -1, size);
        float w = sz.X + 44, h = 25;
        float x0 = -w / 2, y0 = -h / 2;
        // folded tails behind the band, then the band with a gentle sag
        var tailCol = new Color(0.80f, 0.72f, 0.58f);
        DrawColoredPolygon(new[] { new Vector2(x0 - 14, y0 + 7), new Vector2(x0 + 6, y0 + 7), new Vector2(x0 + 6, y0 + h + 7), new Vector2(x0 - 14, y0 + h + 7), new Vector2(x0 - 7, y0 + h / 2 + 7) }, tailCol);
        DrawColoredPolygon(new[] { new Vector2(x0 + w - 6, y0 + 7), new Vector2(x0 + w + 14, y0 + 7), new Vector2(x0 + w + 7, y0 + h / 2 + 7), new Vector2(x0 + w + 14, y0 + h + 7), new Vector2(x0 + w - 6, y0 + h + 7) }, tailCol);
        var band = new List<Vector2>();
        for (int i = 0; i <= 8; i++) band.Add(new Vector2(x0 + w * i / 8f, y0 + Mathf.Sin(i / 8f * Mathf.Pi) * 2.5f));
        for (int i = 8; i >= 0; i--) band.Add(new Vector2(x0 + w * i / 8f, y0 + h + Mathf.Sin(i / 8f * Mathf.Pi) * 2.5f));
        DrawColoredPolygon(band.ToArray(), new Color(0.95f, 0.91f, 0.82f));
        band.Add(band[0]);
        DrawPolyline(band.ToArray(), Ink.Black, 1.4f, true);
        DrawString(face, new Vector2(x0 + 34, y0 + h / 2 + sz.Y * 0.32f + 1.5f), port.Name, HorizontalAlignment.Left, -1, size, Ink.Black);
        // the faction's mark at the left end of the band
        DrawSetTransform(at + new Vector2(x0 + 17, 3) * s, 0, Vector2.One * s * 0.66f);
        ChartView.DrawFactionMark(this, Vector2.Zero, port.Faction, port.Fort, false);
    }

    void DrawCove(Port port)
    {
        var face = italic!;
        int size = 19;
        var sz = face.GetStringSize(port.Name, HorizontalAlignment.Center, -1, size);
        DrawString(face, new Vector2(-sz.X / 2, 6), port.Name, HorizontalAlignment.Left, -1, size, Ink.Red);
        DrawLine(new Vector2(-sz.X / 2, 11), new Vector2(sz.X / 2, 11), Ink.Red with { A = 0.5f }, 1f, true);
    }
}
