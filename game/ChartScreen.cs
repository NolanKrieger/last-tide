using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The chart (M): the whole archipelago as the player has inked it so far, drawn as an antique sea chart — a cartouche,
/// a degree-ticked neatline, compass roses with their rhumb lines, region names lettered in the sea, monsters in the
/// blank margins, a key to the glyphs — with the ship, ports, ink pins with notes, cove rumours, treasure marks, the
/// bottle-map sketches, and the player's own ledger when hovering a port (GDD §5). The sim pauses while it is open.
/// </summary>
public partial class ChartScreen : CanvasLayer
{
    World world = null!;
    ChartCanvas canvas = null!;
    LineEdit note = null!;
    Vec2 pendingPin;

    public bool IsOpen => Visible;

    public void Init(World w, Font f, ImageTexture reveal)
    {
        world = w;
        Layer = 12;   // above the HUD: the chart is a full page
        Visible = false;
        canvas = new ChartCanvas { World = w, Owner_ = this, Theme = Parchment.Theme(f) };
        canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        canvas.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(canvas);
        canvas.Build(reveal);
        note = new LineEdit { Visible = false, PlaceholderText = Text.Get("CHART_PIN_PROMPT"), MaxLength = 40, Theme = Parchment.Theme(f) };
        var box = new StyleBoxFlat { BgColor = Ink.Paper, BorderColor = Ink.Red, BorderWidthBottom = 2, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1 };
        box.ContentMarginLeft = box.ContentMarginRight = 8; box.ContentMarginTop = 3; box.ContentMarginBottom = 4;
        note.AddThemeStyleboxOverride("normal", box);
        note.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        note.AddThemeFontSizeOverride("font_size", 18);
        note.CustomMinimumSize = new Vector2(260, 34);
        note.TextSubmitted += OnNoteSubmitted;
        AddChild(note);
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (!Visible) CancelPin();
        else canvas.Open();
    }

    public void Close()
    {
        Visible = false;
        CancelPin();
    }

    public void BeginPin(Vec2 worldPos, Vector2 screen)
    {
        pendingPin = worldPos;
        note.Position = screen + new Vector2(8, -16);
        note.Text = "";
        note.Visible = true;
        note.GrabFocus();
    }

    public void CancelPin()
    {
        note.Visible = false;
        note.ReleaseFocus();
        canvas.GrabFocusIfOpen();
    }

    void OnNoteSubmitted(string text)
    {
        Audio.Instance?.Play("quill", 0.6, 0.1);   // the pin is inked in
        world.AddPin(pendingPin, text.Trim());
        CancelPin();
        canvas.Changed();
    }

    public bool NoteHasFocus => note.Visible && note.HasFocus();

    /// <summary>Test hook: submits a note as if Enter were pressed in the field.</summary>
    public void SubmitNote(string text) => OnNoteSubmitted(text);

    public ChartCanvas Canvas => canvas;
}

/// <summary>
/// The chart page itself: paper, neatline and the screen-fixed furniture (cartouche, key, bottle maps, hint); the map
/// is drawn by <see cref="ChartMap"/> clipped inside the neatline, and the hover card by <see cref="ChartOverlay"/>.
/// The map redraws only when something changes (open, pan, zoom, a pin), not every frame.
/// </summary>
public partial class ChartCanvas : Control
{
    public World World = null!;
    public ChartScreen Owner_ = null!;
    public Rect2 View;
    ChartMap map = null!;
    ChartOverlay overlay = null!;
    ColorRect wash = null!;
    UiCartouche cartouche = null!;
    Label subtitle = null!, hint = null!;
    ChartKey key = null!;
    BottleMapsPanel maps = null!;
    float zoom = 1f;
    Vector2 pan;
    bool dragging;
    Port? hover;
    public Port? Hover => hover;
    public float Zoom => zoom;

    float BaseScale => Mathf.Min(View.Size.X / (float)Map.Width, View.Size.Y / (float)Map.Height);
    /// <summary>Pixels per metre on the chart at the current zoom (was <c>Scale</c>, which shadowed <see cref="Control.Scale"/>).</summary>
    public float MapScale => BaseScale * zoom;
    Vector2 Origin => View.Position + View.Size * 0.5f + pan;

    public Vector2 ToScreen(Vec2 m) => Origin + new Vector2((float)m.X, (float)m.Y) * MapScale;
    public Vec2 ToWorld(Vector2 s) => new((s.X - Origin.X) / MapScale, (s.Y - Origin.Y) / MapScale);

    public void Build(ImageTexture reveal)
    {
        FocusMode = FocusModeEnum.All;
        TextureRepeat = TextureRepeatEnum.Enabled;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        map = new ChartMap { Canvas = this };
        AddChild(map);
        wash = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = Colors.White };
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/chart_wash.gdshader") };
        mat.SetShaderParameter("reveal", reveal);
        mat.SetShaderParameter("texel", new Vector2(1f / Math.Max(1, reveal.GetWidth()), 1f / Math.Max(1, reveal.GetHeight())));
        wash.Material = mat;
        wash.ShowBehindParent = true;
        map.AddChild(wash);
        overlay = new ChartOverlay { Canvas = this };
        AddChild(overlay);
        cartouche = new UiCartouche { Title = Text.Get("CHART_TITLE"), PlateHeight = 96, FontSize = 40 };
        AddChild(cartouche);
        subtitle = Parchment.L("", "Flavour");
        subtitle.AddThemeFontSizeOverride("font_size", 18);
        AddChild(subtitle);
        key = new ChartKey();
        AddChild(key);
        maps = new BottleMapsPanel { World = World };
        AddChild(maps);
        hint = Parchment.L(Text.Get("CHART_HINT"), "Flavour");
        hint.AddThemeFontSizeOverride("font_size", 15);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.VerticalAlignment = VerticalAlignment.Bottom;
        AddChild(hint);
        Resized += Layout;
    }

    public void Open()
    {
        hint.Text = Text.Get("CHART_HINT");   // key names follow the bindings
        Layout();
        map.Prepare();
        Changed();
        GrabFocus();
    }

    public void GrabFocusIfOpen() { if (IsVisibleInTree()) GrabFocus(); }

    /// <summary>Something on the map changed (pins, pan, zoom): redraw the map and the furniture that follows it.</summary>
    public void Changed()
    {
        var tl = ToScreen(new Vec2(-Map.HalfW, -Map.HalfH)) - View.Position;
        var br = ToScreen(new Vec2(Map.HalfW, Map.HalfH)) - View.Position;
        wash.Position = tl;
        wash.Size = br - tl;
        key.PxPerMetre = MapScale;
        key.QueueRedraw();
        map.QueueRedraw();
        overlay.QueueRedraw();
        QueueRedraw();
    }

    void Layout()
    {
        var s = Size;
        if (s.X < 10) return;
        bool small = s.Y < 700;
        int mapCount = World.Player.BottleMaps.Count(m => !World.Map.Treasures[m.Treasure].Dug);
        cartouche.PlateHeight = small ? 66 : 92;
        cartouche.FontSize = small ? 28 : 40;
        float top = cartouche.PlateHeight + (small ? 30 : 42);
        float bottom = small ? 34 : 46;
        // The neatline keeps the chart's own 4:3; the key stands in the left margin, bottle maps and the hint in the right.
        float side = Mathf.Clamp(s.X * 0.15f, 150, 270);
        float h = s.Y - top - bottom;
        float w = Mathf.Min(h * (float)(Map.Width / Map.Height), s.X - 2 * side - 40);
        h = Mathf.Min(h, w * (float)(Map.Height / Map.Width));
        View = new Rect2(new Vector2((s.X - w) / 2, top + (s.Y - top - bottom - h) / 2), new Vector2(w, h));
        map.Position = View.Position;
        map.Size = View.Size;
        overlay.Position = Vector2.Zero;
        overlay.Size = s;
        var cs = cartouche.GetCombinedMinimumSize();
        cartouche.Size = cs;
        cartouche.Position = new Vector2((s.X - cs.X) / 2, small ? 10 : 14);
        subtitle.Position = new Vector2(View.Position.X, View.Position.Y - 36);
        subtitle.Size = new Vector2(View.Size.X, 26);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.AddThemeFontSizeOverride("font_size", small ? 15 : 18);
        float colW = View.Position.X - 50;
        key.Position = new Vector2(30, View.Position.Y);
        key.Size = new Vector2(colW, View.Size.Y);
        key.Small = small;
        maps.Visible = mapCount > 0;
        maps.Position = new Vector2(View.End.X + 24, View.Position.Y);
        maps.Size = new Vector2(colW - 4, View.Size.Y * (mapCount > 0 ? 0.72f : 0));
        hint.AddThemeFontSizeOverride("font_size", small ? 14 : 16);
        hint.CustomMinimumSize = new Vector2(colW - 4, 0);
        hint.Size = new Vector2(colW - 4, View.Size.Y * 0.25f);
        hint.Position = new Vector2(View.End.X + 24, View.End.Y - hint.Size.Y);
        ClampPan();
    }

    public override void _Process(double delta)
    {
        // Keep the subtitle current (the sim is paused while the chart is open, so this is cheap and rarely changes).
        if (!IsVisibleInTree()) return;
        double charted = World.Reveal.RevealedCells / (double)(World.Reveal.W * World.Reveal.H);
        string t = Text.Get("CHART_SUBTITLE", World.Player.ShipName, Parchment.DayWatch(World.DaysSurvived), Math.Round(charted * 100));
        if (subtitle.Text != t) subtitle.Text = t;
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m:
                if (dragging) { pan += m.Relative; ClampPan(); Changed(); }
                var h = View.HasPoint(m.Position) ? PortNear(m.Position) : null;
                if (h != hover) { hover = h; overlay.QueueRedraw(); }
                overlay.Mouse = m.Position;
                if (hover != null) overlay.QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } w:
                ZoomAt(1.25f, w.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } w:
                ZoomAt(0.8f, w.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mid:
                dragging = mid.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } r:
                int idx = PinNear(r.Position);
                if (idx >= 0) { Audio.Ui(); World.RemovePin(idx); Changed(); }
                else dragging = true;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
                dragging = false;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } l:
                if (Owner_.NoteHasFocus) { Owner_.CancelPin(); break; }
                var wp = ToWorld(l.Position);
                if (View.HasPoint(l.Position) && Map.InBounds(wp)) Owner_.BeginPin(wp, l.Position);
                break;
            case InputEventKey { Pressed: true } k:
                // Arrows pan, + and − zoom about the middle of the chart.
                var step = new Vector2(0, 0);
                if (e.IsActionPressed("ui_left", true)) step.X = 90;
                else if (e.IsActionPressed("ui_right", true)) step.X = -90;
                else if (e.IsActionPressed("ui_up", true)) step.Y = 90;
                else if (e.IsActionPressed("ui_down", true)) step.Y = -90;
                if (step != Vector2.Zero) { pan += step; ClampPan(); Changed(); AcceptEvent(); }
                else if (k.Keycode is Key.Equal or Key.Plus or Key.KpAdd) { ZoomAt(1.25f, View.GetCenter()); AcceptEvent(); }
                else if (k.Keycode is Key.Minus or Key.KpSubtract) { ZoomAt(0.8f, View.GetCenter()); AcceptEvent(); }
                break;
        }
    }

    void ZoomAt(float factor, Vector2 at)
    {
        var before = ToWorld(at);
        zoom = Mathf.Clamp(zoom * factor, 1f, 5f);
        var after = ToScreen(before);
        pan += at - after;
        ClampPan();
        Changed();
    }

    /// <summary>The map may slide, but some of it always stays inside the neatline.</summary>
    void ClampPan()
    {
        float w = (float)Map.Width * MapScale, h = (float)Map.Height * MapScale;
        float mx = Math.Max(0, (w - View.Size.X) / 2) + View.Size.X * 0.25f;
        float my = Math.Max(0, (h - View.Size.Y) / 2) + View.Size.Y * 0.25f;
        pan = new Vector2(Mathf.Clamp(pan.X, -mx, mx), Mathf.Clamp(pan.Y, -my, my));
    }

    Port? PortNear(Vector2 screen)
    {
        Port? best = null;
        float bestD = 16;
        foreach (var port in World.Map.Ports)
        {
            if (!Shown(port)) continue;
            float d = ToScreen(port.Pos).DistanceTo(screen);
            if (d < bestD)
            {
                bestD = d;
                best = port;
            }
        }
        return best;
    }

    int PinNear(Vector2 screen)
    {
        for (int i = 0; i < World.Pins.Count; i++)
            if (ToScreen(World.Pins[i].Pos).DistanceTo(screen) < 12) return i;
        return -1;
    }

    public bool Shown(Port port) => port.Secret ? port.Discovered : World.Reveal.IsRevealed(port.Harbor) || port.Discovered;

    public override void _Draw()
    {
        UiSheet.DrawSheet(this, new Rect2(Vector2.Zero, Size).Grow(-6), true, false);
        // Neatline: an outer rule, a chequered band ticked every 250 m of the chart, an inner rule.
        var v = View;
        const float band = 9;
        var outer = v.Grow(band + 3);
        DrawRect(outer, Ink.Black, false, 2f);
        DrawRect(v.Grow(band), Ink.Black, false, 1f);
        DrawRect(v, Ink.Black, false, 1.2f);
        const double tick = 250;
        var ink = Ink.Black with { A = 0.85f };
        // Top and bottom: blocks along x.
        var w0 = ToWorld(v.Position); var w1 = ToWorld(v.End);
        for (double x = Math.Floor(w0.X / tick) * tick; x < w1.X; x += tick)
        {
            if (((long)Math.Floor(x / tick) & 1) != 0) continue;
            float a = Mathf.Max(v.Position.X, ToScreen(new Vec2(x, 0)).X), b = Mathf.Min(v.End.X, ToScreen(new Vec2(x + tick, 0)).X);
            if (b <= a) continue;
            DrawRect(new Rect2(a, v.Position.Y - band, b - a, band), ink);
            DrawRect(new Rect2(a, v.End.Y, b - a, band), ink);
        }
        for (double y = Math.Floor(w0.Y / tick) * tick; y < w1.Y; y += tick)
        {
            if (((long)Math.Floor(y / tick) & 1) != 0) continue;
            float a = Mathf.Max(v.Position.Y, ToScreen(new Vec2(0, y)).Y), b = Mathf.Min(v.End.Y, ToScreen(new Vec2(0, y + tick)).Y);
            if (b <= a) continue;
            DrawRect(new Rect2(v.Position.X - band, a, band, b - a), ink);
            DrawRect(new Rect2(v.End.X, a, band, b - a), ink);
        }
    }
}

/// <summary>
/// The map inside the neatline (clipped to it): rhumb lines and compass roses printed on the paper, monsters in the
/// blank margins, the charted wash, islands with hachured coasts and dotted shallows, region names, ports, marks,
/// pins and the ship. Glyphs and lettering stay the same size at every zoom; the geography scales.
/// </summary>
public partial class ChartMap : Control
{
    public ChartCanvas Canvas = null!;
    World World => Canvas.World;
    readonly List<Island> shown = new();
    int[] triIndices = Array.Empty<int>();
    Vector2[] triPoints = Array.Empty<Vector2>();
    Color[] triColors = Array.Empty<Color>();
    readonly List<(Vec2 Pos, string Tex)> monsters = new();
    readonly List<(Vec2 Pos, string Name)> regionNames = new();
    Vec2[] roses = { new(-Map.HalfW * 0.52, Map.HalfH * 0.42), new(Map.HalfW * 0.58, -Map.HalfH * 0.46) };
    int rosesFor = int.MinValue;
    FontVariation? regionFont;

    public ChartMap()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    static bool IslandShown(RevealMask reveal, Island island)
    {
        if (reveal.IsRevealed(island.Centre)) return true;
        for (int k = 0; k < 6; k++)
            if (reveal.IsRevealed(island.Centre + Vec2.FromAngle(k * Angles.Tau / 6) * island.Radius * 0.8)) return true;
        return false;
    }

    /// <summary>On opening: which islands are charted (triangulated once into one draw), where the monsters swim, where the names go.</summary>
    public void Prepare()
    {
        shown.Clear();
        var idx = new List<int>();
        var pts = new List<Vector2>();
        var cols = new List<Color>();
        foreach (var island in World.Map.Islands)
        {
            if (!IslandShown(World.Reveal, island)) continue;
            shown.Add(island);
            var poly = island.Points.Select(p => new Vector2((float)p.X, (float)p.Y)).ToArray();
            var tris = Geometry2D.TriangulatePolygon(poly);
            if (tris.Length == 0) continue;
            int b = pts.Count;
            pts.AddRange(poly);
            // Hand-coloured land: each region's islands take their own pale wash, as the old chart colourists did.
            var tint = LandTint(island.Region);
            for (int k = 0; k < poly.Length; k++) cols.Add(tint);
            foreach (var t in tris) idx.Add(b + t);
        }
        triPoints = pts.ToArray();
        triIndices = idx.ToArray();
        triColors = cols.ToArray();
        if (rosesFor != World.Seed) { PlaceRoses(); rosesFor = World.Seed; }
        PlaceMonsters();
        PlaceRegionNames();
    }

    /// <summary>The printed roses sit in the openest water of each half of the chart (fixed for the voyage).</summary>
    void PlaceRoses()
    {
        for (int half = 0; half < 2; half++)
        {
            Vec2 best = roses[half];
            double bestD = -1;
            for (int gx = 0; gx < 10; gx++)
                for (int gy = 0; gy < 12; gy++)
                {
                    var p = new Vec2(-Map.HalfW + (half * 10 + gx + 0.5) * Map.Width / 20, -Map.HalfH + (gy + 0.5) * Map.Height / 12);
                    double edge = Math.Min(Math.Min(p.X + Map.HalfW, Map.HalfW - p.X), Math.Min(p.Y + Map.HalfH, Map.HalfH - p.Y));
                    double d = Math.Min(edge, 900);
                    foreach (var isl in World.Map.IslandsNear(p, 900)) d = Math.Min(d, p.DistanceTo(isl.Centre) - isl.BoundRadius);
                    if (d > bestD) { bestD = d; best = p; }
                }
            roses[half] = best;
            roseSize[half] = Math.Clamp(bestD * 2.2, 380, half == 0 ? 1000 : 700);
        }
    }
    readonly double[] roseSize = { 1000, 640 };

    static Color LandTint(RegionType r) => r switch
    {
        RegionType.Shoals => new Color(0.86f, 0.78f, 0.55f),
        RegionType.Deep => new Color(0.78f, 0.70f, 0.55f),
        RegionType.FogBanks => new Color(0.76f, 0.74f, 0.66f),
        RegionType.StormReach => new Color(0.82f, 0.68f, 0.50f),
        RegionType.Mangrove => new Color(0.66f, 0.72f, 0.52f),
        RegionType.Volcanic => new Color(0.70f, 0.64f, 0.58f),
        RegionType.Sargasso => new Color(0.74f, 0.76f, 0.52f),
        RegionType.SirenRuins => new Color(0.84f, 0.70f, 0.64f),
        _ => Ink.Land,
    };

    void PlaceMonsters()
    {
        monsters.Clear();
        string[] tex = { "monster-whale", "monster-serpent", "monster-hippocamp", "monster-fish" };
        var rng = new Rng((ulong)(World.Seed * 7919 + 3));
        var order = tex.OrderBy(_ => rng.NextDouble()).ToArray();
        var candidates = new List<(Vec2 P, double Score)>();
        for (int gx = 0; gx < 16; gx++)
            for (int gy = 0; gy < 12; gy++)
            {
                var p = new Vec2(-Map.HalfW + (gx + 0.5) * Map.Width / 16, -Map.HalfH + (gy + 0.5) * Map.Height / 12);
                bool blank = true;
                for (int k = 0; k < 9 && blank; k++)
                {
                    var q = k == 0 ? p : p + Vec2.FromAngle(k * Angles.Tau / 8) * 480;
                    if (Map.InBounds(q) && World.Reveal.IsRevealed(q)) blank = false;
                }
                if (!blank || p.DistanceTo(World.Ship.Pos) < 1100) continue;
                if (roses.Any(r => r.DistanceTo(p) < 800) || World.Pins.Any(q => q.Pos.DistanceTo(p) < 600)) continue;
                if (World.Player.CoveHints.Any(h => new Vec2(h.X, h.Y).DistanceTo(p) < h.Radius + 500)) continue;
                double edge = Math.Min(Math.Min(p.X + Map.HalfW, Map.HalfW - p.X), Math.Min(p.Y + Map.HalfH, Map.HalfH - p.Y));
                candidates.Add((p, -edge + rng.NextDouble() * 300));
            }
        foreach (var c in candidates.OrderBy(c => c.Score))
        {
            if (monsters.Count >= 3) break;
            if (monsters.Any(m => m.Pos.DistanceTo(c.P) < 1700)) continue;
            monsters.Add((c.P, order[monsters.Count]));
        }
    }

    void PlaceRegionNames()
    {
        regionNames.Clear();
        regionFont ??= RegionFont();
        float s = Canvas.MapScale;
        foreach (var region in World.Map.Regions)
        {
            if (!World.Reveal.IsRevealed(region.Seed)) continue;
            string name = Text.Get("REGION_" + region.Def.Key).ToUpperInvariant();
            var size = regionFont.GetStringSize(name, HorizontalAlignment.Left, -1, 22) / s;   // in metres at the opening zoom
            Vec2 best = region.Seed;
            double bestScore = double.MaxValue;
            double hx = size.X / 2 + 20, hy = size.Y / 2 + 10;
            for (int ring = 0; ring < 7; ring++)
                for (int a = 0; a < (ring == 0 ? 1 : 12); a++)
                {
                    var c = region.Seed + Vec2.FromAngle(a * Angles.Tau / 12 + ring) * ring * 120;
                    // Keep the whole name on the chart, and in its own region.
                    c = new Vec2(Math.Clamp(c.X, -Map.HalfW + hx + 30, Map.HalfW - hx - 30), Math.Clamp(c.Y, -Map.HalfH + hy + 30, Map.HalfH - hy - 30));
                    if (World.Map.RegionAt(c).Type != region.Type) continue;
                    double score = ring * 0.3;
                    foreach (var isl in World.Map.IslandsNear(c, Math.Max(hx, hy)))
                    {
                        double dx = Math.Max(0, Math.Abs(isl.Centre.X - c.X) - hx), dy = Math.Max(0, Math.Abs(isl.Centre.Y - c.Y) - hy);
                        double gap = Math.Sqrt(dx * dx + dy * dy) - isl.BoundRadius;
                        if (gap < 0) score += 3 + Math.Min(4, -gap / 40);
                    }
                    foreach (var port in World.Map.Ports)
                        if (Math.Abs(port.Pos.X - c.X) < hx + 40 && Math.Abs(port.Pos.Y - c.Y) < hy + 30) score += 5;
                    if (score < bestScore) { bestScore = score; best = c; }
                }
            regionNames.Add((best, name));
        }
    }

    static FontVariation RegionFont()
    {
        var f = new FontVariation { BaseFont = Fonts.DisplayItalic };
        f.SetSpacing(TextServer.SpacingType.Glyph, 3);
        return f;
    }

    public override void _Draw()
    {
        var c = Canvas;
        float s = c.MapScale;
        DrawSetTransform(-c.View.Position);   // draw in the chart page's coordinates; the clip stays on this rect

        // Rhumb lines from two compass roses, printed faintly over the whole sheet.
        var rhumbs = new List<Vector2>(128);
        float reach = (float)(Map.Width * 1.2) * s;
        foreach (var r in roses)
        {
            var o = c.ToScreen(r);
            for (int k = 0; k < 32; k++)
            {
                float a = k * Mathf.Tau / 32;
                rhumbs.Add(o);
                rhumbs.Add(o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * reach);
            }
        }
        DrawMultiline(rhumbs.ToArray(), new Color(0.47f, 0.34f, 0.22f, 0.13f), 1f);
        if (Art.Tex("hud/compass-rose") is { } rose)
            for (int i = 0; i < roses.Length; i++)
            {
                float size = (float)roseSize[i] * s;
                var o = c.ToScreen(roses[i]);
                DrawTextureRect(rose, new Rect2(o - new Vector2(size, size * rose.GetHeight() / rose.GetWidth()) / 2, new Vector2(size, size * rose.GetHeight() / rose.GetWidth())), false, new Color(1, 1, 1, 0.5f));
            }

        // Here be monsters, in the blank margins.
        float mon = Mathf.Clamp(c.View.Size.Y * 0.2f, 110, 250) * Mathf.Sqrt(c.Zoom);
        foreach (var (pos, tex) in monsters)
            if (Parchment.Tex(tex) is { } t)
            {
                var o = c.ToScreen(pos);
                var sz = new Vector2(mon, mon * t.GetHeight() / t.GetWidth());
                DrawTextureRect(t, new Rect2(o - sz / 2, sz), false, new Color(1, 1, 1, 0.85f));
            }

        // Islands: every charted island's land in one triangle draw (in metres), then coasts, hachures and shallows.
        if (triIndices.Length > 0)
        {
            DrawSetTransform(c.ToScreen(new Vec2(0, 0)) - c.View.Position, 0, new Vector2(s, s));
            RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), triIndices, triPoints, triColors);
            DrawSetTransform(-c.View.Position);
        }
        var coast = new List<Vector2>(4096);
        var hatch = new List<Vector2>(4096);
        var dots = new List<Vector2>(4096);
        foreach (var island in shown)
        {
            int count = island.Points.Length;
            var sp = new Vector2[count];
            for (int i = 0; i < count; i++) sp[i] = c.ToScreen(island.Points[i]);
            float area = 0;
            for (int i = 0, j = count - 1; i < count; j = i++) area += (sp[j].X - sp[i].X) * (sp[j].Y + sp[i].Y);
            float inward = area > 0 ? 1 : -1;
            float carry = 0;
            for (int i = 0; i < count; i++)
            {
                var a = sp[i];
                var b = sp[(i + 1) % count];
                coast.Add(a); coast.Add(b);
                var d = b - a;
                float len = d.Length();
                if (len < 0.5f) continue;
                var dir = d / len;
                var nIn = new Vector2(-dir.Y, dir.X) * inward;
                // Hachures every ~5 px along the coast, a short stroke inland; stipple dots offshore.
                for (float t = carry; t < len; t += 5)
                {
                    var p = a + dir * t;
                    float l = 4 + 3 * Ink.Jitter((int)p.X, (int)p.Y, 3);
                    hatch.Add(p + nIn * 1.5f); hatch.Add(p + nIn * (1.5f + l));
                }
                for (float t = carry; t < len; t += 7)
                {
                    var q = a + dir * t;
                    var p = q - nIn * (5 + 2 * Ink.Jitter((int)q.X + 7, (int)q.Y, 5));
                    dots.Add(p); dots.Add(p + dir * 1.4f);
                }
                carry = 0;
            }
        }
        if (coast.Count > 0)
        {
            DrawMultiline(dots.ToArray(), new Color(0.2f, 0.32f, 0.45f, 0.55f), 1.4f);
            DrawMultiline(hatch.ToArray(), Ink.Black with { A = 0.35f }, 1f);
            DrawMultiline(coast.ToArray(), Ink.Black, c.Zoom > 2 ? 1.6f : 1.3f);
        }

        // Region names lettered in the sea.
        regionFont ??= RegionFont();
        int rsize = (int)(22 * Mathf.Pow(c.Zoom, 0.35f));
        foreach (var (pos, name) in regionNames)
        {
            var at = c.ToScreen(pos);
            var sz = regionFont.GetStringSize(name, HorizontalAlignment.Left, -1, rsize);
            DrawString(regionFont, at + new Vector2(-sz.X / 2, sz.Y * 0.3f), name, HorizontalAlignment.Left, -1, rsize, new Color(0.3f, 0.24f, 0.18f, 0.55f));
        }

        // Treasure and wreck marks, cove rumours.
        var red = new List<Vector2>();
        foreach (var t in World.Map.Treasures.Where(t => t.Dug))
        {
            var p = c.ToScreen(t.Pos);
            red.Add(p + new Vector2(-4, -4)); red.Add(p + new Vector2(4, 4)); red.Add(p + new Vector2(-4, 4)); red.Add(p + new Vector2(4, -4));
        }
        if (red.Count > 0) DrawMultiline(red.ToArray(), Ink.Red with { A = 0.5f }, 1.5f);
        var xs = new List<Vector2>();
        foreach (var bm in World.Player.BottleMaps)
        {
            if (!bm.Solved || World.Map.Treasures[bm.Treasure].Dug) continue;
            var p = c.ToScreen(World.Map.Treasures[bm.Treasure].Pos);
            xs.Add(p + new Vector2(-7, -7)); xs.Add(p + new Vector2(7, 7)); xs.Add(p + new Vector2(-7, 7)); xs.Add(p + new Vector2(7, -7));
        }
        if (xs.Count > 0) DrawMultiline(xs.ToArray(), Ink.Red, 3f);
        var dashes = new List<Vector2>();
        foreach (var hint in World.Player.CoveHints)
        {
            var o = c.ToScreen(new Vec2(hint.X, hint.Y));
            float r = (float)hint.Radius * s;
            for (int k = 0; k < 36; k += 2)
            {
                dashes.Add(o + Vec(k * Mathf.Tau / 36) * r);
                dashes.Add(o + Vec((k + 1) * Mathf.Tau / 36) * r);
            }
        }
        if (dashes.Count > 0) DrawMultiline(dashes.ToArray(), Ink.Red, 1.6f);
        var wrecks = new List<Vector2>();
        foreach (var w in World.Map.Wrecks)
        {
            if (w.Salvaged || !World.Reveal.IsRevealed(w.Pos)) continue;
            var p = c.ToScreen(w.Pos);
            wrecks.Add(p + new Vector2(-6, 3)); wrecks.Add(p + new Vector2(6, 3));
            wrecks.Add(p + new Vector2(-1, 3)); wrecks.Add(p + new Vector2(-3, -8));
            wrecks.Add(p + new Vector2(3, 3)); wrecks.Add(p + new Vector2(4, -5));
        }
        if (wrecks.Count > 0) DrawMultiline(wrecks.ToArray(), Ink.Black, 1.8f);

        // Ports: the faction glyph (the same drawing as the sea chart's) and the name, haloed so it reads over the lines.
        var body = Fonts.Body;
        foreach (var port in World.Map.Ports)
        {
            if (!c.Shown(port)) continue;
            var p = c.ToScreen(port.Pos);
            DrawSetTransform(p - c.View.Position, 0, Vector2.One * 0.8f);
            ChartView.DrawFactionMark(this, Vector2.Zero, port.Faction, port.Fort, port.Secret);
        }
        DrawSetTransform(-c.View.Position);
        var placed = PlaceNames(body, 15);
        foreach (var (port, at) in placed)
            DrawStringOutline(body, at, port.Name, HorizontalAlignment.Left, -1, 15, 5, Ink.Paper with { A = 0.85f });
        foreach (var (port, at) in placed)
            DrawString(body, at, port.Name, HorizontalAlignment.Left, -1, 15, port.Secret ? Ink.Red : Ink.Black);

        // Rumour marks and pin notes (italic).
        var it = Fonts.Italic;
        foreach (var hint in World.Player.CoveHints)
            DrawString(Fonts.DisplayItalic, c.ToScreen(new Vec2(hint.X, hint.Y)) + new Vector2(-8, 12), "?", HorizontalAlignment.Left, -1, 34, Ink.Red);
        var pinLines = new List<Vector2>();
        foreach (var pin in World.Pins)
        {
            var p = c.ToScreen(pin.Pos);
            pinLines.Add(p); pinLines.Add(p + new Vector2(0, -14));
        }
        if (pinLines.Count > 0) DrawMultiline(pinLines.ToArray(), Ink.Red, 2f);
        foreach (var pin in World.Pins)
        {
            var p = c.ToScreen(pin.Pos);
            DrawCircle(p + new Vector2(0, -14), 4, Ink.Red);
            if (pin.Note.Length > 0)
            {
                DrawStringOutline(it, p + new Vector2(7, -12), pin.Note, HorizontalAlignment.Left, -1, 16, 5, Ink.Paper with { A = 0.9f });
                DrawString(it, p + new Vector2(7, -12), pin.Note, HorizontalAlignment.Left, -1, 16, Ink.Black);
            }
        }

        // The ship, and how far she sees.
        var ship = World.Ship;
        var at2 = c.ToScreen(ship.Pos);
        DrawArc(at2, (float)World.VisionRadius * s, 0, Mathf.Tau, 64, Ink.Red with { A = 0.55f }, 1.2f, true);
        var h = new Vector2(Mathf.Cos((float)ship.Heading), Mathf.Sin((float)ship.Heading));
        var n = new Vector2(-h.Y, h.X);
        var hull = new[] { at2 + h * 13, at2 + h * 4 + n * 5, at2 - h * 9 + n * 4, at2 - h * 9 - n * 4, at2 + h * 4 - n * 5 };
        DrawColoredPolygon(hull, Ink.Red);
        DrawPolyline(Ink.Closed(hull), Ink.Black, 1.2f, true);
        DrawSetTransform(Vector2.Zero);
    }

    static Vector2 Vec(float a) => new(Mathf.Cos(a), Mathf.Sin(a));

    /// <summary>Each port's name goes right, left, above or below its glyph, whichever overlaps the names already set least.</summary>
    List<(Port Port, Vector2 At)> PlaceNames(Font font, int size)
    {
        var c = Canvas;
        var taken = new List<Rect2>();
        var result = new List<(Port, Vector2)>();
        var ports = World.Map.Ports.Where(c.Shown).ToList();
        foreach (var port in ports) taken.Add(new Rect2(c.ToScreen(port.Pos) - new Vector2(9, 12), new Vector2(18, 20)));
        foreach (var port in ports)
        {
            var p = c.ToScreen(port.Pos);
            var sz = font.GetStringSize(port.Name, HorizontalAlignment.Left, -1, size);
            Vector2[] at = { p + new Vector2(11, -6), p + new Vector2(-11 - sz.X, -6), p + new Vector2(-sz.X / 2, -16), p + new Vector2(-sz.X / 2, 24) };
            Vector2 best = at[0];
            float bestScore = float.MaxValue;
            for (int i = 0; i < at.Length; i++)
            {
                var r = new Rect2(at[i] - new Vector2(0, sz.Y * 0.75f), sz);
                float score = i * 0.5f;
                if (!c.View.Encloses(r)) score += 50;
                foreach (var t in taken)
                    if (t.Intersects(r)) score += t.Intersection(r).Area / 40f + 2;
                if (score < bestScore) { bestScore = score; best = at[i]; }
            }
            taken.Add(new Rect2(best - new Vector2(0, sz.Y * 0.75f), sz));
            result.Add((port, best));
        }
        return result;
    }
}

/// <summary>Screen-fixed chart furniture that follows the pointer: the hovered port's ring and the ledger card.</summary>
public partial class ChartOverlay : Control
{
    public ChartCanvas Canvas = null!;
    public Vector2 Mouse;
    public ChartOverlay() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        var c = Canvas;
        if (c.Hover is not { } hp) return;
        var w = c.World;
        var p = c.ToScreen(hp.Pos);
        DrawArc(p, 14, 0, Mathf.Tau, 28, Ink.Red, 2f, true);

        // The player's own ledger for this port: what it sells cheap and buys dear, as last seen (GDD §5).
        var lines = new List<(string Text, Font Font, int Size, Color Colour)>
        {
            (hp.Name, Fonts.SmallCaps, 22, Ink.Black),
            (hp.Secret ? Text.Get("CHART_COVE_LINE") : Text.Get("CHART_PORT", Text.Get("FACTION_" + hp.Faction.ToString().ToLowerInvariant()), Text.Get("REGION_" + RegionDef.Of(hp.Region).Key)), Fonts.Italic, 17, Parchment.Muted),
        };
        double lastDay = -1;
        bool rumour = false;
        string Prices(IEnumerable<Good> goods)
        {
            var parts = new List<string>();
            foreach (var g in goods)
                if (w.Player.Remembered(hp.Id, g) is { } e)
                {
                    parts.Add(Text.Get("CHART_PRICE", Text.Get("GOOD_" + Goods.Of(g).Key), Math.Round(e.Price)));
                    if (e.Day > lastDay) { lastDay = e.Day; rumour = e.Rumor; }
                }
            return string.Join(" · ", parts.Take(4));
        }
        string sells = Prices(hp.Produces), buys = Prices(hp.Consumes);
        if (sells.Length > 0) lines.Add((Text.Get("CHART_SELLS", sells), Fonts.Body, 17, Ink.Black));
        if (buys.Length > 0) lines.Add((Text.Get("CHART_BUYS", buys), Fonts.Body, 17, Ink.Black));
        if (lastDay >= 0) lines.Add((Text.Get(rumour ? "CHART_HEARD" : "CHART_SEEN", Math.Floor(lastDay) + 1), Fonts.Italic, 16, Parchment.Muted));
        else lines.Add((Text.Get("CHART_NO_PRICES"), Fonts.Italic, 16, Parchment.Muted));

        float width = 0, height = 18;
        foreach (var l in lines)
        {
            width = Mathf.Max(width, l.Font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, l.Size).X);
            height += l.Size + 9;
        }
        var box = new Rect2(p + new Vector2(20, 14), new Vector2(width + 30, height));
        if (box.End.X > Size.X - 20) box.Position -= new Vector2(box.Size.X + 40, 0);
        if (box.End.Y > Size.Y - 20) box.Position -= new Vector2(0, box.End.Y - Size.Y + 20);
        var style = GetThemeStylebox("panel", "TooltipPanel");
        DrawRect(box, Ink.Paper);
        DrawStyleBox(style, box);
        float y = box.Position.Y + 12;
        foreach (var l in lines)
        {
            y += l.Size + 2;
            DrawString(l.Font, new Vector2(box.Position.X + 15, y), l.Text, HorizontalAlignment.Left, -1, l.Size, l.Colour);
            y += 7;
        }
    }
}

/// <summary>The key to the chart's glyphs down the left margin, with a scale of sea miles under it.</summary>
public partial class ChartKey : Control
{
    public bool Small;
    /// <summary>Pixels per metre on the chart now (the scale bar follows the zoom).</summary>
    public float PxPerMetre;
    public ChartKey() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        var sc = Fonts.SmallCaps;
        int size = Small ? 14 : 16;
        float rowH = Small ? 24 : 30;
        DrawString(Fonts.SmallCaps, new Vector2(0, 20), Text.Get("CHART_KEY"), HorizontalAlignment.Left, -1, Small ? 17 : 20, Ink.Black);
        DrawLine(new Vector2(0, 28), new Vector2(Mathf.Min(Size.X, 170), 28), Ink.Black with { A = 0.6f }, 1f);
        float x = 4, y = 30 + rowH * 0.7f;
        (string Key, Action<Vector2> Glyph)[] items =
        {
            ("CHART_KEY_CROWN", p => Mark(p, Faction.Crown, false, false)),
            ("CHART_KEY_FORT", p => Mark(p, Faction.Crown, true, false)),
            ("CHART_KEY_FREE", p => Mark(p, Faction.FreeTraders, false, false)),
            ("CHART_KEY_HAVEN", p => Mark(p, Faction.Brethren, false, false)),
            ("CHART_KEY_COVE", p => Mark(p, Faction.FreeTraders, false, true)),
            ("CHART_KEY_TREASURE", p => { DrawLine(p + new Vector2(-6, -6), p + new Vector2(6, 6), Ink.Red, 3f); DrawLine(p + new Vector2(-6, 6), p + new Vector2(6, -6), Ink.Red, 3f); }),
            ("CHART_KEY_RUMOUR", p => { for (int k = 0; k < 12; k += 2) DrawArc(p, 8, k * Mathf.Tau / 12, (k + 1) * Mathf.Tau / 12, 4, Ink.Red, 1.4f); }),
            ("CHART_KEY_WRECK", p => { DrawLine(p + new Vector2(-6, 4), p + new Vector2(6, 4), Ink.Black, 1.8f); DrawLine(p + new Vector2(-1, 4), p + new Vector2(-3, -7), Ink.Black, 1.8f); }),
            ("CHART_KEY_PIN", p => { DrawLine(p + new Vector2(0, 7), p + new Vector2(0, -5), Ink.Red, 2f); DrawCircle(p + new Vector2(0, -6), 3.5f, Ink.Red); }),
            ("CHART_KEY_SHIP", p => DrawColoredPolygon(new[] { p + new Vector2(10, 0), p + new Vector2(2, 5), p + new Vector2(-7, 4), p + new Vector2(-7, -4), p + new Vector2(2, -5) }, Ink.Red)),
        };
        foreach (var (key, glyph) in items)
        {
            if (y > Size.Y - 6) break;
            glyph(new Vector2(x + 10, y));
            DrawString(sc, new Vector2(x + 28, y + size * 0.32f), Text.Get(key), HorizontalAlignment.Left, Size.X - 30, size, Ink.Black with { A = 0.8f });
            y += rowH;
        }
        DrawScale(new Vector2(4, y + (Small ? 18 : 34)), size);
    }

    /// <summary>A chequered scale of sea miles (1 852 m), in the largest unit that fits the margin: ¼, ½, 1 or 2 miles.</summary>
    void DrawScale(Vector2 at, int size)
    {
        if (PxPerMetre <= 0 || at.Y + 50 > Size.Y) return;
        const float mile = 1852f;
        float room = Size.X - 16;
        float[] units = { 2f, 1f, 0.5f, 0.25f };
        float unit = units.FirstOrDefault(u => u * mile * PxPerMetre <= room);
        if (unit <= 0) unit = 0.25f;
        float len = unit * mile * PxPerMetre;
        DrawString(Fonts.SmallCaps, at, Text.Get("CHART_SCALE"), HorizontalAlignment.Left, -1, Small ? 15 : 17, Ink.Black);
        var bar = new Rect2(at.X, at.Y + 12, len, 7);
        for (int k = 0; k < 4; k++)
            if (k % 2 == 0) DrawRect(new Rect2(bar.Position.X + len * k / 4, bar.Position.Y, len / 4, bar.Size.Y), Ink.Black);
        DrawRect(bar, Ink.Black, false, 1.2f);
        string Num(float v) => v switch { 0.25f => "¼", 0.5f => "½", 0.75f => "¾", 1.5f => "1½", _ => v.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        for (int k = 0; k <= 2; k++)
        {
            float v = unit * k / 2;
            string t = k == 0 ? "0" : Num(v);
            float w = Fonts.Body.GetStringSize(t, HorizontalAlignment.Left, -1, 15).X;
            DrawString(Fonts.Body, new Vector2(bar.Position.X + len * k / 2 - w / 2, bar.End.Y + 17), t, HorizontalAlignment.Left, -1, 15, Ink.Black);
        }
    }

    public override void _Process(double delta) { }

    void Mark(Vector2 p, Faction f, bool fort, bool secret)
    {
        DrawSetTransform(p, 0, Vector2.One * 0.75f);
        ChartView.DrawFactionMark(this, Vector2.Zero, f, fort, secret);
        DrawSetTransform(Vector2.Zero);
    }
}

/// <summary>Torn bottle-map sketches beside the chart: the coast turned as the map was found, the X, matched or not.</summary>
public partial class BottleMapsPanel : Control
{
    public World World = null!;
    public BottleMapsPanel() { MouseFilter = MouseFilterEnum.Ignore; ClipContents = true; }
    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }

    public override void _Draw()
    {
        var maps = World.Player.BottleMaps.Where(m => !World.Map.Treasures[m.Treasure].Dug).ToList();
        DrawString(Fonts.SmallCaps, new Vector2(0, 18), Text.Get("CHART_MAPS"), HorizontalAlignment.Left, -1, 19, Ink.Black);
        float boxW = Size.X, boxH = Mathf.Min(170, (Size.Y - 30) / Mathf.Max(1, Math.Min(4, maps.Count)) - 12), top = 30;
        var card = GetThemeStylebox("panel", "Card");
        for (int i = 0; i < maps.Count && i < 4; i++)
        {
            var map = maps[i];
            var site = World.Map.Treasures[map.Treasure];
            var island = World.Map.Islands[site.IslandId];
            var box = new Rect2(0, top + i * (boxH + 12), boxW, boxH);
            DrawRect(box.Grow(-3), new Color(0.88f, 0.82f, 0.68f));
            DrawStyleBox(card, box);
            // Fit the whole coast inside the card above its caption, turned as the sketch was drawn.
            var inner = new Rect2(box.Position + new Vector2(14, 10), box.Size - new Vector2(28, 40));
            float sc = Mathf.Min(inner.Size.X, inner.Size.Y) / (float)(island.BoundRadius * 2.1);
            var centre = inner.GetCenter();
            var pts = island.Points.Select(q => centre + new Vector2((float)(q.X - island.Centre.X), (float)(q.Y - island.Centre.Y)).Rotated((float)map.Rotation) * sc).ToArray();
            DrawColoredPolygon(pts, Ink.Land);
            DrawPolyline(Ink.Closed(pts), Ink.Black, 1.3f, true);
            var xp = centre + new Vector2((float)(site.Pos.X - island.Centre.X), (float)(site.Pos.Y - island.Centre.Y)).Rotated((float)map.Rotation) * sc;
            DrawLine(xp + new Vector2(-6, -6), xp + new Vector2(6, 6), Ink.Red, 2.5f, true);
            DrawLine(xp + new Vector2(-6, 6), xp + new Vector2(6, -6), Ink.Red, 2.5f, true);
            DrawString(Fonts.Italic, box.Position + new Vector2(12, boxH - 11), Text.Get(map.Solved ? "CHART_MAP_MATCHED" : "CHART_MAP_UNSOLVED"), HorizontalAlignment.Left, boxW - 24, 15, map.Solved ? Ink.Red : Parchment.Muted);
        }
    }
}
