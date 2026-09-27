using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The chart's small life, cheap and occasional, deterministic per voyage: gulls wheeling over the harbours by day,
/// dolphins and fish leaping in open water, a whale surfacing to blow in the Deep, sargassum drifting in the Weed Sea,
/// smoke rolling off the volcanoes, and the sea's regions lettered large across the water like an old map (the
/// region she enters writes itself out ahead of her bow). Sprites go through two MultiMeshes (one draw call per
/// atlas); nothing here touches the sim.
/// </summary>
public partial class LifeView : Node2D
{
    World world = null!;
    CoastField field = null!;
    ChartView chart = null!;
    SpriteLayer sea = null!, sky = null!;
    RegionLetters letters = null!;
    Rng rng = null!;
    float time;
    const float P = Ink.PxPerM;

    enum Kind { Dolphin, Fish, Whale }

    struct Leap
    {
        public Kind Kind;
        public Vector2 A, B;       // world px
        public float Start, Dur;
        public bool Left;
    }

    readonly List<Leap> leaps = new();
    float nextLeap = 3f, nextWhale = 12f;
    readonly List<Vector2> rings = new();      // splash rings drawn this frame: (centre, radius) pairs
    readonly List<float> ringAlpha = new();

    public RegionLetters Letters => letters;

    /// <summary>Sprites drawn last frame (the self-test checks the harbour has its gulls by day).</summary>
    public int Gulls { get; private set; }
    public int SeaCreatures { get; private set; }

    public void Init(World w, CoastField f, ChartView c)
    {
        world = w;
        field = f;
        chart = c;
        rng = new Rng(unchecked((ulong)w.Seed * 2654435761UL + 99));
        sea = new SpriteLayer { ZIndex = 7 };
        sea.Setup(Art.Tex("life/atlas"), WorldArt.LifeSize, 160);
        AddChild(sea);
        sky = new SpriteLayer { ZIndex = 13 };
        sky.Setup(Art.Tex("life/atlas"), WorldArt.LifeSize, 48);
        AddChild(sky);
        smoke = new SpriteLayer { ZIndex = 13 };
        smoke.Setup(Art.Tex("land/atlas"), WorldArt.LandSize, 40);
        AddChild(smoke);
        letters = new RegionLetters { ZIndex = 4 };
        letters.Init(w, f);
        AddChild(letters);
        rings.Capacity = 64;
    }

    SpriteLayer smoke = null!;

    public override void _Process(double delta)
    {
        bool paused = (GetParent() as Main)?.Paused ?? false;
        float dt = paused ? 0 : (float)Math.Min(delta, 0.1);
        time += dt;
        var cam = GetViewport().GetCamera2D();
        if (cam == null) return;
        var centre = cam.GetScreenCenterPosition();
        var half = GetViewportRect().Size / cam.Zoom * 0.5f;
        var view = new Rect2(centre - half, half * 2);
        sea.Begin();
        sky.Begin();
        smoke.Begin();
        rings.Clear();
        ringAlpha.Clear();
        var cond = world.ConditionsAt(world.Ship.Pos);
        Gulls = DrawGulls(view, cond.Night, cam.Zoom.X);
        Schedule(dt, view);
        SeaCreatures = DrawLeaps(view);
        if (cam.Zoom.X > 0.42f) DrawWeed(view);
        DrawSmoke(view);
        sea.End();
        sky.End();
        smoke.End();
        QueueRedraw();
    }

    // ---- gulls: a few wheel over every harbour by day (never over a secret cove: they would give it away) ----
    int DrawGulls(Rect2 view, bool night, float zoom)
    {
        if (night) return 0;
        int n = 0;
        var grow = view.Grow(400);
        foreach (var port in world.Map.Ports)
        {
            if (port.Secret) continue;
            var hub = Ink.V(Vec2.Lerp(port.Pos, port.Harbor, 0.35));
            if (!grow.HasPoint(hub)) continue;
            int count = port.Size == 2 ? 5 : 3;
            for (int i = 0; i < count; i++)
            {
                uint h = (uint)(port.Id * 7919 + i * 104729) * 2654435761u;
                float r1 = (h & 0xFFFF) / 65535f, r2 = (h >> 16) / 65535f;
                float radius = (14 + 20 * r1) * P;
                float dir = i % 2 == 0 ? 1 : -1;
                float w = dir * (0.28f + 0.22f * r2);
                float ph = time * w + r2 * Mathf.Tau;
                var c = hub + new Vector2(Mathf.Cos(r1 * 9), Mathf.Sin(r1 * 9)) * 10 * P;
                var pos = c + new Vector2(Mathf.Cos(ph), Mathf.Sin(ph)) * radius;
                var vel = new Vector2(-Mathf.Sin(ph), Mathf.Cos(ph)) * dir;
                float heading = Mathf.Atan2(vel.Y, vel.X) + Mathf.Pi / 2;   // the sprite's head points up
                // flap for a moment every few seconds, then glide
                float cyc = (time + r1 * 5) % (3.2f + 2 * r2);
                string frame = cyc < 0.9f ? ((int)(cyc * 7) % 2 == 0 ? "gull-flap" : "gull-spread") : "gull-glide";
                float span = (5.2f + 1.2f * r1) * P;
                // a faint shadow on the water below, offset as if lit from the north-west
                sky.Add(WorldArt.Life[frame], pos + new Vector2(7, 9) * P * 0.5f, span, heading, new Color(0.16f, 0.13f, 0.1f, 0.18f));
                sky.Add(WorldArt.Life[frame], pos, span, heading, Colors.White);
                n++;
            }
        }
        return n;
    }

    // ---- leaps and the whale ----
    void Schedule(float dt, Rect2 view)
    {
        if (dt <= 0) return;
        nextLeap -= dt;
        nextWhale -= dt;
        var region = field.RegionAt(view.GetCenter() / P);
        if (nextLeap <= 0)
        {
            nextLeap = (float)rng.Range(4, 11);
            bool dolphins = region is RegionType.TradeIsles or RegionType.Shoals or RegionType.SirenRuins or RegionType.Deep or RegionType.StormReach;
            var kind = dolphins && rng.NextDouble() < 0.6 ? Kind.Dolphin : Kind.Fish;
            if (FindWater(view, 25, out var at))
            {
                bool left = rng.NextDouble() < 0.5;
                float len = (kind == Kind.Dolphin ? 11 : 4) * P;
                var b = at + new Vector2(left ? -len : len, (float)rng.Range(-0.2, 0.2) * len);
                leaps.Add(new Leap { Kind = kind, A = at, B = b, Start = time, Dur = kind == Kind.Dolphin ? 1.5f : 0.75f, Left = left });
                if (kind == Kind.Dolphin && rng.NextDouble() < 0.5)
                    leaps.Add(new Leap { Kind = kind, A = at + new Vector2(3, 5) * P, B = b + new Vector2(3, 5) * P, Start = time + 0.35f, Dur = 1.5f, Left = left });
            }
        }
        if (nextWhale <= 0)
        {
            nextWhale = (float)rng.Range(22, 40);
            if (region == RegionType.Deep && FindWater(view, 70, out var at))
                leaps.Add(new Leap { Kind = Kind.Whale, A = at, B = at, Start = time, Dur = 8.5f, Left = rng.NextDouble() < 0.5 });
        }
    }

    /// <summary>A random open-water point in the middle of the view, clear of the ship.</summary>
    bool FindWater(Rect2 view, float clearOfLand, out Vector2 at)
    {
        var inner = view.Grow(-Mathf.Min(view.Size.X, view.Size.Y) * 0.18f);
        var ship = Ink.V(world.Ship.Pos);
        for (int k = 0; k < 12; k++)
        {
            var p = inner.Position + new Vector2((float)rng.NextDouble() * inner.Size.X, (float)rng.NextDouble() * inner.Size.Y);
            if (p.DistanceTo(ship) < 45 * P) continue;
            if (field.DistanceAt(p / P) < clearOfLand) continue;
            if (!world.Reveal.IsRevealed(Ink.M(p))) continue;
            at = p;
            return true;
        }
        at = default;
        return false;
    }

    int DrawLeaps(Rect2 view)
    {
        int n = 0;
        for (int i = leaps.Count - 1; i >= 0; i--)
        {
            var l = leaps[i];
            float t = (time - l.Start) / l.Dur;
            if (t > 1.6f) { leaps.RemoveAt(i); continue; }
            if (t < 0) continue;
            if (l.Kind == Kind.Whale) { DrawWhale(l, t); n++; continue; }
            if (t <= 1)
            {
                var pos = l.A.Lerp(l.B, t);
                float lift = Mathf.Sin(t * Mathf.Pi) * (l.Kind == Kind.Dolphin ? 3.5f : 1.6f) * P;
                float tilt = Mathf.Lerp(-0.55f, 0.65f, t) * (l.Left ? -1 : 1);
                float h = (l.Kind == Kind.Dolphin ? 9f : 3.2f) * P;
                string name = l.Kind == Kind.Dolphin ? "dolphin" : "fish";
                float fade = Mathf.Min(1, Mathf.Min(t * 6, (1 - t) * 6));
                sea.Add(WorldArt.Life[name], pos - new Vector2(0, lift), h, tilt, new Color(1, 1, 1, fade), l.Left);
                n++;
            }
            // splash rings where it broke the surface and where it dives back in
            Ring(l.A, t, l.Kind == Kind.Dolphin ? 3.5f : 1.6f);
            if (t > 0.9f) Ring(l.B, t - 0.9f, l.Kind == Kind.Dolphin ? 3.5f : 1.6f);
        }
        return n;
    }

    void DrawWhale(Leap l, float t)
    {
        float s = t * l.Dur;                         // seconds into the sequence
        float bob = Mathf.Sin(s * 1.3f) * 0.3f * P;
        if (s < 5.5f)
        {
            float a = Mathf.Clamp((s - 0.6f) / 0.9f, 0, 1) * Mathf.Clamp((5.5f - s) / 1.0f, 0, 1);
            sea.Add(WorldArt.Life["whale-back"], l.A + new Vector2(0, bob), 9f * P, 0, new Color(1, 1, 1, a), l.Left);
            if (s > 1.4f && s < 3.9f)
            {
                float st = (s - 1.4f) / 2.5f;
                float sa = Mathf.Sin(st * Mathf.Pi);
                var blow = l.A + new Vector2(l.Left ? 8 : -8, -6) * P;
                sea.Add(WorldArt.Life["spout"], blow - new Vector2(0, (6 + 6 * st) * P), (9 + 9 * st) * P, 0, new Color(1, 1, 1, sa * 0.95f));
            }
        }
        if (s > 5f && s < 8.3f)
        {
            float tt = (s - 5f) / 3.3f;
            float a = Mathf.Sin(Mathf.Clamp(tt, 0, 1) * Mathf.Pi);
            var tail = l.A + new Vector2(l.Left ? 10 : -10, 0) * P;
            sea.Add(WorldArt.Life["whale-tail"], tail - new Vector2(0, Mathf.Sin(tt * Mathf.Pi) * 4f * P), 12f * P, (tt - 0.5f) * 0.3f, new Color(1, 1, 1, a), l.Left);
        }
        Ring(l.A, s / 3f, 11f);
        if (s > 7.4f) Ring(l.A + new Vector2(l.Left ? 10 : -10, 0) * P, (s - 7.4f) / 1.5f, 8f);
    }

    void Ring(Vector2 c, float t, float size)
    {
        if (t < 0 || t > 1) return;
        rings.Add(c);
        rings.Add(new Vector2((0.4f + 1.4f * t) * size * P, 0));
        ringAlpha.Add((1 - t) * 0.5f);
    }

    // ---- sargassum: mats adrift across the Weed Sea, world-anchored on a 26 m grid, gathered into drifting patches ----
    void DrawWeed(Rect2 view)
    {
        const float cell = 26f;
        var drift = new Vector2(Mathf.Sin(time * 0.05f), Mathf.Cos(time * 0.04f)) * 1.5f;
        var tl = view.Position / P;
        var br = view.End / P;
        int x0 = (int)Mathf.Floor(tl.X / cell) - 1, x1 = (int)Mathf.Ceil(br.X / cell) + 1;
        int y0 = (int)Mathf.Floor(tl.Y / cell) - 1, y1 = (int)Mathf.Ceil(br.Y / cell) + 1;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                uint h = ((uint)(x * 73856093) ^ (uint)(y * 19349663)) * 2654435761u;
                float r1 = (h & 0xFFFF) / 65535f;
                float r2 = ((h >> 16) & 0xFF) / 255f, r3 = (h >> 24) / 255f;
                var m = new Vector2((x + 0.15f + 0.7f * r2) * cell, (y + 0.15f + 0.7f * r3) * cell) + drift * (0.5f + r2);
                // patches: the weed lies in long drifting mats, open water between them
                float patch = (float)LastTide.Sim.Noise.Value3(world.Seed + 77, m.X / 160, m.Y / 110, 0.5) * 0.5f + 0.5f;
                if (r1 > 0.9f * Mathf.SmoothStep(0.38f, 0.68f, patch)) continue;
                if (field.RegionAt(m) != RegionType.Sargasso) continue;
                if (field.DistanceAt(m) < 8) continue;
                sea.Add(WorldArt.Life["weed"], m * P, (9 + 7 * r3) * P, r1 * 20 + time * 0.02f * (r2 - 0.5f), new Color(1.08f, 1.04f, 0.86f, 0.62f + 0.2f * r2));
            }
    }

    // ---- smoke off the volcanoes, rolling downwind ----
    void DrawSmoke(Rect2 view)
    {
        if (chart.Volcanoes.Count == 0 || !WorldArt.Land.TryGetValue("smoke", out var puff)) return;
        var wind = Ink.V(Vec2.FromAngle(world.Ship.LocalWind.Direction)).Normalized();
        var grow = view.Grow(300);
        foreach (var top in chart.Volcanoes)
        {
            if (!grow.HasPoint(top)) continue;
            if (!world.Reveal.IsRevealed(Ink.M(top))) continue;
            // puffs rise out of the crater, swell and roll away downwind, the oldest drawn first (under the new)
            for (int k = 7; k >= 0; k--)
            {
                float ph = (time / 10f + k / 8f) % 1f;
                var pos = top + (wind * 34 * ph * ph + new Vector2(0, -10 - 30 * ph)) * P;
                float size = (12 + 32 * ph) * P;
                float a = Mathf.Min(1, ph * 6) * (1 - ph) * 0.95f;
                smoke.Add(puff, pos, size, (k - 3) * 0.2f + ph * 0.5f, new Color(1, 1, 1, a), k % 2 == 0);
            }
        }
    }

    readonly List<Vector2> pts = new();
    readonly List<Color> cols = new();

    public override void _Draw()
    {
        // splash rings, batched into one multiline per frame
        if (rings.Count == 0) return;
        pts.Clear();
        cols.Clear();
        for (int i = 0; i < ringAlpha.Count; i++)
        {
            var c = rings[2 * i];
            float r = rings[2 * i + 1].X;
            for (int k = 0; k < 20; k++)
            {
                if (k % 5 == 4) continue;   // broken, like a pen ring
                float a0 = k * Mathf.Tau / 20, a1 = (k + 1) * Mathf.Tau / 20;
                pts.Add(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0) * 0.6f) * r);
                pts.Add(c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1) * 0.6f) * r);
                cols.Add(Ink.Black with { A = ringAlpha[i] });
            }
        }
        DrawMultilineColors(pts.ToArray(), cols.ToArray(), 1.4f, true);
    }
}

/// <summary>A batch of atlas sprites through one MultiMesh (one draw call), refilled each frame.</summary>
public partial class SpriteLayer : Node2D
{
    MultiMesh mm = null!;
    Texture2D? atlas;
    Vector2 atlasSize;
    int count;

    public void Setup(Texture2D? tex, Vector2 size, int capacity)
    {
        atlas = tex;
        atlasSize = size;
        var quad = new ArrayMesh();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        quad.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, Mesh.ArrayFormat.FlagUse2DVertices);
        mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = true,
            Mesh = quad,
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };
        Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/sprites.gdshader") };
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public void Begin() => count = 0;

    /// <summary>A sprite centred at <paramref name="at"/> (world px), <paramref name="height"/> tall, turned by <paramref name="angle"/>.</summary>
    public void Add(Rect2 src, Vector2 at, float height, float angle, Color col, bool flip = false)
    {
        if (count >= mm.InstanceCount) return;
        float w = height * src.Size.X / src.Size.Y;
        var x = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * w * (flip ? -1 : 1);
        var y = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * height;
        mm.SetInstanceTransform2D(count, new Transform2D(x, y, at));
        mm.SetInstanceColor(count, col);
        mm.SetInstanceCustomData(count, new Color(src.Position.X / atlasSize.X, src.Position.Y / atlasSize.Y, src.Size.X / atlasSize.X, src.Size.Y / atlasSize.Y));
        count++;
    }

    public void End()
    {
        mm.VisibleInstanceCount = count;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (atlas != null && count > 0) DrawMultimesh(mm, atlas);
    }
}

/// <summary>
/// The regions lettered on the sea like an old chart: in spaced italic capitals over each charted region when the
/// view is wide, and, whenever she crosses into a region, its name written out ahead of her bow letter by letter,
/// held a while and let fade — world-anchored, so she sails past it.
/// </summary>
public partial class RegionLetters : Node2D
{
    World world = null!;
    CoastField field = null!;
    RegionType? current;
    string text = "";
    Vector2 anchor;
    float age = 99, angle;
    static FontFile? face;

    /// <summary>The region whose name is being written (the self-test checks a crossing letters the sea).</summary>
    public RegionType? Showing => age < 9 ? current : null;

    public void Init(World w, CoastField f) { world = w; field = f; }

    public override void _Ready()
    {
        if (face == null)
        {
            face = (FontFile)Fonts.DisplayItalic.Duplicate();
            face.MultichannelSignedDistanceField = true;
            face.MsdfPixelRange = 14;
            face.MsdfSize = 56;
        }
    }

    public static string Spaced(string s) => string.Join(' ', s.ToUpperInvariant().ToCharArray()).Replace("   ", " ");

    public override void _Process(double delta)
    {
        bool paused = (GetParent()?.GetParent() as Main)?.Paused ?? false;
        if (!paused) age += (float)delta;
        var here = world.Map.RegionAt(world.Ship.Pos).Type;
        if (here != current)
        {
            bool first = current == null;
            current = here;
            text = Spaced(Text.Get("REGION_" + RegionDef.Of(here).Key));
            // ahead of her bow and a little to one side, lying along her course
            var fwd = Vec2.FromAngle(world.Ship.Heading);
            var side = new Vec2(-fwd.Y, fwd.X);
            anchor = Ink.V(world.Ship.Pos + fwd * 60 + side * 22);
            if (GetViewport().GetCamera2D() is { } cam && face != null)
            {
                // Keep it inside the view (the camera may be anywhere after a teleport) and on open water, clear of
                // the land, the ship and the HUD's corners: the nearest spot to the wanted one where the whole name fits.
                var c = cam.GetScreenCenterPosition();
                var half = GetViewportRect().Size / cam.Zoom * 0.5f * new Vector2(0.8f, 0.72f);
                float s = Mathf.Pow(cam.Zoom.X, -0.75f);
                var size = face.GetStringSize(text, HorizontalAlignment.Left, -1, 40) * s;
                anchor = FindWater(anchor, size * 0.5f + new Vector2(6, 6) * Ink.PxPerM, new Rect2(c - half, half * 2), Ink.V(world.Ship.Pos));
            }
            angle = 0;
            age = first ? -1.5f : 0;
        }
        QueueRedraw();
    }

    /// <summary>The spot nearest <paramref name="want"/> (world px) where a box of <paramref name="half"/> extents lies on
    /// water inside <paramref name="view"/> and clear of the ship; <paramref name="want"/> clamped into view if none.</summary>
    Vector2 FindWater(Vector2 want, Vector2 half, Rect2 view, Vector2 ship)
    {
        // the chart's own marks are obstacles too: harbour rings, dig rings, wreck rings (world px centre, radius)
        var avoid = new List<(Vector2 C, float R)>();
        foreach (var port in world.Map.Ports)
            if (!port.Secret || port.Discovered) avoid.Add((Ink.V(port.Harbor), (float)(port.RingRadius + 12) * Ink.PxPerM));
        foreach (var map in world.Player.BottleMaps)
            if (map.Solved && !world.Map.Treasures[map.Treasure].Dug)
            {
                var t = world.Map.Treasures[map.Treasure];
                avoid.Add((Ink.V(t.DigRing), (float)(t.RingRadius + 10) * Ink.PxPerM));
            }
        foreach (var w in world.Map.Wrecks)
            if (!w.Salvaged) avoid.Add((Ink.V(w.Pos), (float)(w.RingRadius + 10) * Ink.PxPerM));
        bool strict = true;
        bool Fits(Vector2 p)
        {
            if (p.X - half.X < view.Position.X || p.X + half.X > view.End.X || p.Y - half.Y < view.Position.Y || p.Y + half.Y > view.End.Y) return false;
            if (Mathf.Abs(p.X - ship.X) < half.X + 30 * Ink.PxPerM && Mathf.Abs(p.Y - ship.Y) < half.Y + 20 * Ink.PxPerM) return false;
            if (strict) foreach (var (c, r) in avoid)
            {
                var q = new Vector2(Mathf.Clamp(c.X, p.X - half.X, p.X + half.X), Mathf.Clamp(c.Y, p.Y - half.Y, p.Y + half.Y));
                if (q.DistanceTo(c) < r) return false;
            }
            for (int j = -1; j <= 1; j++)
                for (int i = -3; i <= 3; i++)
                {
                    var m = (p + new Vector2(half.X * i / 3f, half.Y * j)) / Ink.PxPerM;
                    if (field.DistanceAt(m) < 3) return false;                       // on open water
                    if (!world.Reveal.IsRevealed(new Vec2(m.X, m.Y))) return false;   // and on the charted part of it
                }
            return true;
        }
        float step = 15 * Ink.PxPerM;
        for (int pass = 0; pass < 2; pass++, strict = false)   // then, if nothing fits, let it cross a ring
            for (float r = 0; r <= 400 * Ink.PxPerM; r += step)
            {
                int n = r == 0 ? 1 : Math.Max(8, (int)(Mathf.Tau * r / step));
                for (int k = 0; k < n; k++)
                {
                    float a = k * Mathf.Tau / n;
                    var p = want + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (Fits(p)) return p;
                }
            }
        return new Vector2(Mathf.Clamp(want.X, view.Position.X + half.X, view.End.X - half.X), Mathf.Clamp(want.Y, view.Position.Y + half.Y, view.End.Y - half.Y));
    }

    public override void _Draw()
    {
        var cam = GetViewport().GetCamera2D();
        if (cam == null || face == null) return;
        float zoom = cam.Zoom.X;
        float s = Mathf.Pow(zoom, -0.75f);
        // Wide views: every charted region's name across its water.
        float wide = Mathf.Clamp((0.6f - zoom) / 0.25f, 0, 1);
        if (wide > 0)
            foreach (var r in world.Map.Regions)
            {
                if (!world.Reveal.IsRevealed(r.Seed)) continue;
                if (Showing == r.Type) continue;          // her own region is being written out already
                string name = Spaced(Text.Get("REGION_" + r.Def.Key));
                DrawName(name, Ink.V(r.Seed), s * 1.2f, Ink.Black with { A = 0.34f * wide }, name.Length);
            }
        // The region she has just entered, written out ahead of her.
        if (age >= 0 && age < 9 && text.Length > 0)
        {
            int shown = Math.Clamp((int)(age / 1.6f * text.Length) + 1, 0, text.Length);
            float a = age < 7 ? 0.62f : 0.62f * (1 - (age - 7) / 2);
            DrawName(text, anchor, s, Ink.Black with { A = a }, shown);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    void DrawName(string name, Vector2 at, float scale, Color col, int shown)
    {
        int size = 40;
        var full = face!.GetStringSize(name, HorizontalAlignment.Left, -1, size);
        DrawSetTransform(at, angle, Vector2.One * scale);
        var start = new Vector2(-full.X / 2, full.Y * 0.3f);
        string part = name[..shown];
        DrawString(face, start, part, HorizontalAlignment.Left, -1, size, col);
        // a flourish under the name once it is written
        if (shown == name.Length)
        {
            var y = full.Y * 0.3f + 12;
            DrawLine(new Vector2(-full.X * 0.3f, y), new Vector2(full.X * 0.3f, y), col with { A = col.A * 0.6f }, 1.4f, true);
        }
    }
}
