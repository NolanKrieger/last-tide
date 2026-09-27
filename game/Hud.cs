using System.Globalization;
using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The HUD's art: one atlas (generated icons and ornaments + procedural plates, ribbon and sprites, built by
/// <c>tools/art/hud_atlas.py</c>) and the compass rose. Everything the HUD draws that is not text comes from the atlas,
/// so the whole HUD renders in a handful of batched draw calls. Rects below mirror the builder's layout.
/// </summary>
static class HudArt
{
    public static Texture2D? Atlas => Art.Tex("hud/hud-atlas");
    public static Texture2D? Rose => Art.Tex("hud/compass-rose");

    static readonly string[] IconNames =
    {
        "coin", "crate", "sack", "shot", "timber", "sailor", "cannon", "sail",
        "repair", "pump", "anchor", "spade", "bell", "sun", "moon", "lantern",
        "spyglass", "hourglass", "m_reef_serpent", "m_kraken", "m_ghost_ship", "m_crocodile", "m_weed_kraken", "m_siren",
        "o_corner", "o_seal", "o_pin", "o_rosette",
    };

    /// <summary>
    /// An icon by name. Where the screens have the same thing (gold, crates, stores, crew stations, the anchor) the
    /// HUD uses the UI kit's picture of it, so the port and the HUD show one set; the rest are the HUD's own cells.
    /// </summary>
    public static Rect2 Icon(string name)
    {
        switch (name)
        {
            case "coin": return KitGold;
            case "crate": return KitCrate;
            case "anchor": return KitAnchor;
            case "sack": return KitProvisions;
            case "shot": return KitMunitions;
            case "timber": return KitTimber;
            case "cannon": return KitGuns;
            case "sail": return KitSails;
            case "repair": return KitRepair;
            case "pump": return KitPumps;
        }
        int i = Array.IndexOf(IconNames, name);
        return i < 0 ? Disc : new Rect2(i % 8 * 128, i / 8 * 128, 128, 128);
    }

    // The UI kit's pieces (ui-screens' theme), copied into the atlas by tools/art/hud_atlas.py.
    public static readonly Rect2 KitRim = new(704, 512, 128, 128);          // 9-slice, margins 32 (the screens' rim.png at half size)
    public static readonly Rect2 KitPaper = new(832, 512, 128, 128);        // a crop of the screens' paper, tiled
    public static readonly Rect2 KitKey = new(960, 512, 44, 40);            // 9-slice, margins 10
    public static readonly Rect2 KitKeyHot = new(960, 552, 44, 40);
    public static readonly Rect2 KitCorner = new(704, 640, 64, 64);
    public static readonly Rect2 KitGuns = new(768, 640, 64, 64);
    public static readonly Rect2 KitSails = new(832, 640, 64, 64);
    public static readonly Rect2 KitRepair = new(896, 640, 64, 64);
    public static readonly Rect2 KitPumps = new(960, 640, 64, 64);
    public static readonly Rect2 KitGold = new(512, 704, 64, 64);
    public static readonly Rect2 KitCrate = new(576, 704, 64, 64);
    public static readonly Rect2 KitAnchor = new(640, 704, 64, 64);
    public static readonly Rect2 KitHourglass = new(704, 704, 64, 64);
    public static readonly Rect2 KitProvisions = new(768, 704, 64, 64);
    public static readonly Rect2 KitMunitions = new(832, 704, 64, 64);
    public static readonly Rect2 KitTimber = new(896, 704, 64, 64);
    public static readonly Rect2 KitRibbon = new(512, 768, 269, 64);        // 3-slice at 66.6 / 206.3 (the screens' ribbon cuts)

    public static readonly Rect2 Hand = new(512, 384, 256, 128);
    public static readonly Rect2 Divider = new(768, 384, 256, 128);
    public static readonly Rect2 Disc = new(0, 512, 64, 64);
    public static readonly Rect2 Ring = new(64, 512, 64, 64);
    public static readonly Rect2 RingThin = new(128, 512, 64, 64);
    public static readonly Rect2 Glow = new(192, 512, 128, 128);
    public static readonly Rect2 White = new(28, 540, 8, 8);          // the middle of the disc: a solid white patch for plain boxes
    public static readonly Rect2 Plate = new(320, 512, 192, 192);
    public static readonly Rect2 PlateScale = new(512, 512, 192, 192);
    public static readonly Rect2 RibbonFill = new(0, 704, 512, 64);
    public static readonly Rect2 RibbonLine = new(0, 768, 512, 64);
    public static readonly Rect2 Arrow = new(0, 832, 256, 48);
    public static readonly Rect2 ArrowHalo = new(256, 832, 256, 48);
    public static readonly Rect2 Needle = new(512, 832, 192, 32);
    public static readonly Rect2 NeedleHalo = new(512, 864, 192, 32);
    public static readonly Rect2 Barb = new(704, 832, 48, 12);
    public static readonly Rect2 HullFill = new(768, 832, 64, 28);
    public static readonly Rect2 HullLine = new(832, 832, 64, 28);
    public static readonly Rect2 Tri = new(896, 832, 32, 32);
    public static readonly Rect2 Drop = new(928, 832, 24, 32);
    public static readonly Rect2 BigHullFill = new(0, 896, 192, 72);
    public static readonly Rect2 BigHullLine = new(192, 896, 192, 72);
    public static readonly Rect2 Deck = new(384, 896, 192, 72);
    public static readonly Rect2 SailFill = new(576, 896, 64, 68);
    public static readonly Rect2 SailLine = new(640, 896, 64, 68);

    /// <summary>Compass-rose art geometry in source pixels: the ring's centre and radii.</summary>
    public static readonly Vector2 RoseCentre = new(238.5f, 274f);
    public const float RoseRingInner = 176f, RoseRingOuter = 207f, RoseWidth = 478f, RoseHeight = 512f;
}

/// <summary>The HUD's own inks (the shared palette lives in <see cref="Ink"/>).</summary>
static class HudInk
{
    public static readonly Color KeyFace = new(0.975f, 0.945f, 0.87f);
    public static readonly Color KeyLip = new(0.74f, 0.66f, 0.52f);
    public static readonly Color Gold = new(0.66f, 0.47f, 0.12f);
    public static readonly Color Water = new(0.24f, 0.43f, 0.60f);
    public static readonly Color WaterDeep = new(0.14f, 0.27f, 0.42f);
    public static readonly Color HullWood = new(0.50f, 0.35f, 0.20f);
    public static readonly Color Safe = new(0.13f, 0.40f, 0.34f);
    public static readonly Color Night = new(0.19f, 0.21f, 0.38f);
    public static readonly Color Sky = new(0.83f, 0.87f, 0.86f);
    public static readonly Color Well = new(0.86f, 0.80f, 0.68f);
    public static readonly Color DangerWash = new(1f, 0.90f, 0.86f);
    /// <summary>The plates' paper, for anything that must hide what is behind it on a plate.</summary>
    public static readonly Color PlatePaper = new(0.961f, 0.910f, 0.800f);

    /// <summary>The Threat tiers inked from pale umber (Flat Calm) to blood red (LAST TIDE).</summary>
    public static Color Tier(int tier)
    {
        var a = new Color(0.66f, 0.58f, 0.44f);
        var b = new Color(0.74f, 0.36f, 0.16f);
        var c = new Color(0.42f, 0.05f, 0.05f);
        float t = Math.Clamp(tier / 8f, 0, 1);
        return t < 0.5f ? a.Lerp(b, t * 2) : b.Lerp(c, (t - 0.5f) * 2);
    }
}

/// <summary>Battery state as the HUD shows it (the self-test reads <see cref="Munitions"/>).</summary>
public sealed class GunGauge
{
    public bool PortLoaded = true, StarboardLoaded = true;
    public float PortProgress = 1, StarboardProgress = 1;
    public int Munitions, Cannons, PortGuns, StarboardGuns;
}

/// <summary>
/// A Control that paints from the HUD atlas: sprites, plain boxes (the atlas's white patch), chart plates, key caps,
/// and text queued per face and size so a frame's lettering flushes in as few batches as possible.
/// </summary>
public partial class InkCanvas : Control
{
    protected Texture2D? atlas;
    Transform2D baseX = Transform2D.Identity;

    struct TextOp
    {
        public Font F;
        public int Size, Outline;
        public Vector2 Pos;
        public string? S;
        public Color C, OC;
        public Transform2D X;
    }
    readonly List<TextOp> texts = new();

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        MouseFilter = MouseFilterEnum.Ignore;
        atlas = HudArt.Atlas;
    }

    /// <summary>Everything drawn until the next call is transformed by <paramref name="x"/> (pulses, ribbons unfurling).</summary>
    protected void SetBase(Transform2D x)
    {
        baseX = x;
        DrawSetTransformMatrix(x);
    }

    /// <summary>A scale about <paramref name="pivot"/> for a little ink bounce.</summary>
    protected static Transform2D ScaleAbout(Vector2 pivot, float s) => new(0, new Vector2(s, s), 0, pivot - pivot * s);

    protected void Sprite(Rect2 src, Rect2 dest, Color c)
    {
        if (atlas != null) DrawTextureRectRegion(atlas, dest, src, c);
        else DrawRect(dest, c with { A = c.A * 0.4f });
    }

    /// <summary>A sprite centred on <paramref name="centre"/>, rotated by <paramref name="angle"/> (sprites point along +X).</summary>
    protected void SpriteAt(Rect2 src, Vector2 centre, Vector2 size, float angle, Color c)
    {
        DrawSetTransformMatrix(baseX * new Transform2D(angle, centre));
        Sprite(src, new Rect2(-size / 2, size), c);
        DrawSetTransformMatrix(baseX);
    }

    protected void Box(Rect2 r, Color c) => Sprite(HudArt.White, r, c);

    protected void Frame(Rect2 r, float w, Color c)
    {
        Box(new Rect2(r.Position, new Vector2(r.Size.X, w)), c);
        Box(new Rect2(r.Position.X, r.End.Y - w, r.Size.X, w), c);
        Box(new Rect2(r.Position.X, r.Position.Y + w, w, r.Size.Y - 2 * w), c);
        Box(new Rect2(r.End.X - w, r.Position.Y + w, w, r.Size.Y - 2 * w), c);
    }

    /// <summary>A straight stroke as a rotated box.</summary>
    protected void Stroke(Vector2 a, Vector2 b, float w, Color c)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 0.01f) return;
        DrawSetTransformMatrix(baseX * new Transform2D(d.Angle(), a));
        Sprite(HudArt.White, new Rect2(0, -w / 2, len, w), c);
        DrawSetTransformMatrix(baseX);
    }

    protected void Icon(string name, Rect2 dest, Color? c = null) => Sprite(HudArt.Icon(name), dest, c ?? Colors.White);

    protected void Disc(Vector2 centre, float r, Color c) => Sprite(HudArt.Disc, new Rect2(centre - new Vector2(r, r) * 1.0667f, new Vector2(r, r) * 2.1333f), c);

    protected void RingAt(Vector2 centre, float r, Color c, bool thin = false) =>
        Sprite(thin ? HudArt.RingThin : HudArt.Ring, new Rect2(centre - new Vector2(r, r) * 1.0667f, new Vector2(r, r) * 2.1333f), c);

    /// <summary>
    /// A chart plate in the screens' language: their paper tiled inside, their deckled, age-browned rim with its printed
    /// double rule (9-sliced, edges tiled so the deckle never stretches) and, when <paramref name="corners"/>, the gold
    /// scrollwork in each corner. <paramref name="scale"/> is kept for callers; the plate is the same either way.
    /// </summary>
    protected void Plate(Rect2 r, bool scale = false, Color? tint = null, bool corners = false)
    {
        var c = tint ?? Colors.White;
        const float m = 32, d = 14;                 // rim margin in the atlas, and on screen
        float iw = r.Size.X - 2 * d, ih = r.Size.Y - 2 * d;
        if (iw < 1 || ih < 1) { Box(r, Ink.Paper); return; }
        // Paper: 128 px tiles, cropped at the far edges.
        var inner = r.Grow(-4);
        for (float y = inner.Position.Y; y < inner.End.Y - 0.5f; y += 128)
            for (float x = inner.Position.X; x < inner.End.X - 0.5f; x += 128)
            {
                float w = Math.Min(128, inner.End.X - x), h = Math.Min(128, inner.End.Y - y);
                Sprite(new Rect2(HudArt.KitPaper.Position, new Vector2(w, h)), new Rect2(x, y, w, h), c);
            }
        var p = HudArt.KitRim.Position;
        float sw = HudArt.KitRim.Size.X, sh = HudArt.KitRim.Size.Y, span = sw - 2 * m;
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        var rimTint = Colors.White with { A = c.A };
        Sprite(new Rect2(p, new Vector2(m, m)), new Rect2(x0, y0, d, d), rimTint);
        Sprite(new Rect2(p.X + sw - m, p.Y, m, m), new Rect2(x1 - d, y0, d, d), rimTint);
        Sprite(new Rect2(p.X, p.Y + sh - m, m, m), new Rect2(x0, y1 - d, d, d), rimTint);
        Sprite(new Rect2(p.X + sw - m, p.Y + sh - m, m, m), new Rect2(x1 - d, y1 - d, d, d), rimTint);
        float step = span * d / m;                  // one tile of edge on screen
        for (float t = 0; t < iw - 0.01f; t += step)
        {
            float len = Math.Min(step, iw - t), src = len * m / d;
            Sprite(new Rect2(p.X + m, p.Y, src, m), new Rect2(x0 + d + t, y0, len, d), rimTint);
            Sprite(new Rect2(p.X + m, p.Y + sh - m, src, m), new Rect2(x0 + d + t, y1 - d, len, d), rimTint);
        }
        for (float t = 0; t < ih - 0.01f; t += step)
        {
            float len = Math.Min(step, ih - t), src = len * m / d;
            Sprite(new Rect2(p.X, p.Y + m, m, src), new Rect2(x0, y0 + d + t, d, len), rimTint);
            Sprite(new Rect2(p.X + sw - m, p.Y + m, m, src), new Rect2(x1 - d, y0 + d + t, d, len), rimTint);
        }
        if (!corners) return;
        // The gold flourish in each corner, its corner on the printed rule. A negative size flips the texture in place
        // (the rect still runs from its position to position + |size|), as the screens' sheets do it.
        float cs = Mathf.Clamp(Math.Min(r.Size.X, r.Size.Y) * 0.3f, 22, 34);
        var cr = HudArt.KitCorner;
        const float o = 5;
        Sprite(cr, new Rect2(x0 + o, y0 + o, cs, cs), rimTint);
        Sprite(cr, new Rect2(x1 - o - cs, y0 + o, -cs, cs), rimTint);
        Sprite(cr, new Rect2(x0 + o, y1 - o - cs, cs, -cs), rimTint);
        Sprite(cr, new Rect2(x1 - o - cs, y1 - o - cs, -cs, -cs), rimTint);
    }

    /// <summary>A key cap with the bound key's label; returns its width. <paramref name="at"/> is the top-left.</summary>
    protected float KeyCap(Vector2 at, string label, float h = 22, Color? ink = null)
    {
        const int size = 15;
        float tw = TextW(Fonts.Body, size, label);
        float w = Math.Max(h, tw + 11);
        var r = new Rect2(at, new Vector2(w, h));
        var col = ink ?? Ink.Black;
        KeyFace(r, col == Ink.Black ? 1f : Math.Max(0.45f, col.A), false);
        Txt(Fonts.Body, size, new Vector2(r.Position.X + (w - tw) / 2, r.Position.Y + h * 0.5f + size * 0.28f), label, col);
        return w;
    }

    /// <summary>The screens' key cap (<c>key.png</c>, 9-sliced) behind a label; <paramref name="hot"/> is the red-rimmed one.</summary>
    protected void KeyFace(Rect2 r, float alpha, bool hot)
    {
        var src = hot ? HudArt.KitKeyHot : HudArt.KitKey;
        const float m = 10, d = 7;
        var c = Colors.White with { A = alpha };
        var p = src.Position;
        float sw = src.Size.X, sh = src.Size.Y, x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        float iw = Math.Max(0, r.Size.X - 2 * d), ih = Math.Max(0, r.Size.Y - 2 * d);
        Sprite(new Rect2(p, new Vector2(m, m)), new Rect2(x0, y0, d, d), c);
        Sprite(new Rect2(p.X + m, p.Y, sw - 2 * m, m), new Rect2(x0 + d, y0, iw, d), c);
        Sprite(new Rect2(p.X + sw - m, p.Y, m, m), new Rect2(x1 - d, y0, d, d), c);
        Sprite(new Rect2(p.X, p.Y + m, m, sh - 2 * m), new Rect2(x0, y0 + d, d, ih), c);
        Sprite(new Rect2(p.X + m, p.Y + m, sw - 2 * m, sh - 2 * m), new Rect2(x0 + d, y0 + d, iw, ih), c);
        Sprite(new Rect2(p.X + sw - m, p.Y + m, m, sh - 2 * m), new Rect2(x1 - d, y0 + d, d, ih), c);
        Sprite(new Rect2(p.X, p.Y + sh - m, m, m), new Rect2(x0, y1 - d, d, d), c);
        Sprite(new Rect2(p.X + m, p.Y + sh - m, sw - 2 * m, m), new Rect2(x0 + d, y1 - d, iw, d), c);
        Sprite(new Rect2(p.X + sw - m, p.Y + sh - m, m, m), new Rect2(x1 - d, y1 - d, d, d), c);
    }

    protected static float KeyCapW(string label, float h = 22) => Math.Max(h, TextW(Fonts.Body, 15, label) + 11);

    static readonly Dictionary<(Font, int), Dictionary<string, float>> widths = new();

    /// <summary>A string's advance width, measured once and cached per face and size.</summary>
    protected static float TextW(Font f, int size, string s)
    {
        if (s.Length == 0) return 0;
        if (!widths.TryGetValue((f, size), out var d)) widths[(f, size)] = d = new Dictionary<string, float>();
        if (d.TryGetValue(s, out float w)) return w;
        if (d.Count > 512) d.Clear();
        w = f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
        d[s] = w;
        return w;
    }

    /// <summary>Queues a string at a baseline position; <see cref="FlushText"/> draws the queue grouped by face and size.</summary>
    protected void Txt(Font f, int size, Vector2 baseline, string s, Color c, int outline = 0, Color outlineColour = default)
    {
        if (string.IsNullOrEmpty(s)) return;
        texts.Add(new TextOp { F = f, Size = size, Pos = baseline, S = s, C = c, Outline = outline, OC = outlineColour, X = baseX });
    }

    /// <summary>Queues a string centred on <paramref name="cx"/>.</summary>
    protected float TxtC(Font f, int size, float cx, float baseline, string s, Color c, int outline = 0, Color outlineColour = default)
    {
        float w = TextW(f, size, s);
        Txt(f, size, new Vector2(cx - w / 2, baseline), s, c, outline, outlineColour);
        return w;
    }

    /// <summary>Queues a string ending at <paramref name="right"/>.</summary>
    protected float TxtR(Font f, int size, float right, float baseline, string s, Color c)
    {
        float w = TextW(f, size, s);
        Txt(f, size, new Vector2(right - w, baseline), s, c);
        return w;
    }

    protected void FlushText()
    {
        int n = texts.Count;
        var current = Transform2D.Identity;
        DrawSetTransformMatrix(current);
        for (int i = 0; i < n; i++)
        {
            var a = texts[i];
            if (a.S == null) continue;
            for (int pass = 0; pass < 2; pass++)
                for (int j = i; j < n; j++)
                {
                    var b = texts[j];
                    if (b.S == null || b.F != a.F || b.Size != a.Size) continue;
                    if (pass == 0 && b.Outline <= 0) continue;
                    if (b.X != current) { current = b.X; DrawSetTransformMatrix(current); }
                    if (pass == 0) DrawStringOutline(b.F, b.Pos, b.S, HorizontalAlignment.Left, -1, b.Size, b.Outline, b.OC);
                    else
                    {
                        DrawString(b.F, b.Pos, b.S, HorizontalAlignment.Left, -1, b.Size, b.C);
                        b.S = null;
                        texts[j] = b;
                    }
                }
        }
        texts.Clear();
        baseX = Transform2D.Identity;
        DrawSetTransformMatrix(baseX);
    }
}

/// <summary>
/// Ink on the chart's margins, laid out as chart ornaments: the compass rose (wind, heading, the no-go wedge) with the
/// wind and conditions beneath it; the day, watch and Threat cartouche at the top; the ship's purse top-right; the
/// helm (sails, speed, point of sail) bottom-left; the ship card (hull, water, batteries, crew) bottom-right; prompts,
/// notices, the key legend, the last stand and markers for what lies off the edge of the screen. Every line comes from
/// the string table; everything is painted by <see cref="HudCanvas"/> in a few batched draws.
/// </summary>
public partial class Hud : CanvasLayer
{
    HudCanvas canvas = null!;
    EdgeMarkers markers = null!;
    SunkCanvas sunk = null!;
    internal float OverAge;
    internal World World = null!;

    // ---- the state the canvas paints (refreshed every frame from the world) ----
    internal float Heading, WindDir, WindSpeed, PointDeg, Polar;
    internal bool InIrons, SailsSet;
    internal string WindKn = "", WindFrom = "", HeadingLine = "";
    internal readonly List<(string Text, Color Colour)> Conditions = new();
    internal string DayText = "", WatchText = "";
    internal float HourOfDay;
    internal float ThreatValue = 1;
    internal int Tier, SailLevel;
    internal string TierName = "";
    internal float SailFraction;
    internal string SailName = "", SpeedText = "", PointName = "";
    internal float HullFrac = 1, WaterFrac;
    internal int Leaks;
    internal readonly GunGauge Guns = new();
    internal readonly int[] Stations = new int[4];
    internal string OrderName = "";
    internal string GoldText = "", HoldText = "", CrewText = "", ShipName = "", HullName = "";
    internal int Provisions, Munitions, Timber;
    internal bool ProvisionsLow, MunitionsLow;
    internal string PromptText = "", PromptKey = "", PromptIcon = "";
    internal bool PromptVisible;
    internal Color PromptColour = Ink.Black;
    internal float PromptProgress = -1;
    internal string NoticeText = "";
    internal NoticeKind NoticeKindNow;
    internal float NoticeAge, NoticeLeft;
    internal string BannerTier = "";
    internal float BannerAge = -1;
    internal string RegionName = "";
    internal float RegionFlash;   // 1 → 0 over a few seconds after she enters a region
    internal bool Stand, StandWaterOnly, Over;
    internal float Hourglass, HourglassMax = 20;
    internal string StandTitle = "", StandSeconds = "", StandLine = "", HarbourLine = "", OverText = "";
    internal bool HarbourReachable;
    internal Port? RescueHarbour;
    internal string StateBanner = "";
    internal float LegendAlpha = 1;
    internal float GoldPulse, SailPulse, ThreatPulse, TierFlash;
    internal readonly float[] ReadyPulse = new float[2];
    readonly bool[] wasLoaded = { true, true };
    internal float Clock;
    internal string HullPct = "", WaterPct = "", ProvisionsText = "", MunitionsText = "", TimberText = "";
    internal readonly string[] StationText = { "", "", "", "" };

    long kWindKn = long.MinValue, kWindFrom = long.MinValue, kHeading = long.MinValue, kDay = long.MinValue, kWatch = long.MinValue,
        kCond = long.MinValue, kTier = long.MinValue, kSail = long.MinValue, kSpeed = long.MinValue, kPoint = long.MinValue,
        kOrder = long.MinValue, kGold = long.MinValue, kHold = long.MinValue, kCrew = long.MinValue, kStores = long.MinValue,
        kStations = long.MinValue, kPrompt = long.MinValue, kStand = long.MinValue, kHarbour = long.MinValue, kPct = long.MinValue,
        kOver = long.MinValue;
    readonly Dictionary<string, string> keyCache = new();
    int keyVersion;

    double seaTime, legendShowUntil;

    /// <summary>The region name lettered across the top on entering a region ("" when none is showing).</summary>
    /// <summary>The region she has just entered while its name is still inked bold on the conditions line ("" otherwise).
    /// The sea itself letters the region across the water (world's chart view); the HUD only marks the change.</summary>
    public string RegionCalled => RegionFlash > 0 ? RegionName : "";

    /// <summary>Self-test only: pretends she has been at sea this long, so the key legend's two-minute welcome is over.</summary>
    internal void AgeForTest(double seconds) => seaTime += seconds;
    int lastGold = int.MinValue, lastTierSeen = -1;
    RegionType? lastRegion;
    const float NoticeHold = 5f, BannerHold = 4.2f;

    /// <summary>The label of the key bound to an action ("W", "Space"); Main points it at the live settings.</summary>
    public Func<string, string>? KeyLabel { get; set; }

    public bool DockPromptVisible => PromptVisible;
    /// <summary>The prompt's words; the key cap beside them is <see cref="DockPromptKey"/>.</summary>
    public string DockPromptText => PromptText;
    /// <summary>The key cap on the prompt (the bound Dock or Sail-down key), empty when the prompt has none.</summary>
    public string DockPromptKey => PromptKey;
    public bool LastStandVisible => Stand;
    public string ThreatText => TierName;
    public string WeatherText { get; private set; } = "";
    public GunGauge GunGauge => Guns;
    public string PointText => PointName;
    public string SailText => SailName;
    public string NoticeShown => NoticeText;
    public bool LegendVisible => LegendAlpha > 0.5f;
    public int MarkerCount => markers.Count;
    public IReadOnlyList<EdgeMarkers.Marker> Markers => markers.Placed;
    /// <summary>The marker drawn for a ship (null when she has none).</summary>
    public EdgeMarkers.Marker? MarkerFor(int shipId)
    {
        foreach (var m in markers.Placed)
            if (m.ShipId == shipId) return m;
        return null;
    }
    /// <summary>The harbour the last stand points her to ("" when none).</summary>
    public string LastStandRefuge => Stand ? RescueHarbour?.Name ?? "" : "";
    /// <summary>The HUD's plates as laid out for the current screen (for overlap checks and hint placement).</summary>
    public IReadOnlyList<Rect2> Plates => canvas.Plates;
    /// <summary>True when any two of the HUD's plates overlap at the current screen size and UI scale.</summary>
    public bool PlatesOverlap()
    {
        var p = canvas.Plates;
        for (int i = 0; i < p.Count; i++)
            for (int j = i + 1; j < p.Count; j++)
                if (p[i].Intersects(p[j])) return true;
        foreach (var r in p)
            if (r.Position.X < 0 || r.Position.Y < 0 || r.End.X > canvas.Size.X || r.End.Y > canvas.Size.Y) return true;
        return false;
    }

    string keyLine = "";
    /// <summary>The key hints as one line (Main builds it from the bindings); the legend draws the same keys as caps.</summary>
    public string KeyLine
    {
        get => keyLine;
        set
        {
            if (keyLine.Length > 0 && value != keyLine) legendShowUntil = seaTime + 8;   // bindings changed: show them again
            keyLine = value;
            keyCache.Clear();
            keyVersion++;
        }
    }

    /// <summary>The label of the key bound to an action, cached until the bindings change (Main resets the key line then).</summary>
    internal string Key(string action)
    {
        if (keyCache.TryGetValue(action, out var label)) return label;
        label = KeyLabel?.Invoke(action) ?? action switch
        {
            "SailUp" => "W", "SailDown" => "S", "Port" => "A", "Starboard" => "D", "FirePort" => "Q", "FireStarboard" => "E",
            "Crew" => "C", "Lantern" => "L", "Dock" => "F", "Chart" => "M", "Order1" => "1", "Order2" => "2", "Order3" => "3", "Order4" => "4", _ => "?",
        };
        keyCache[action] = label;
        return label;
    }

    public void Init(World world)
    {
        Layer = 10;
        World = world;
        canvas = new HudCanvas { Hud = this };
        canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(canvas);
        markers = new EdgeMarkers { Hud = this };
        markers.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(markers);
        sunk = new SunkCanvas { Hud = this };
        sunk.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(sunk);
        HullName = Text.Get("HULL_" + world.Ship.Hull.Id);
    }

    /// <summary>After a hull purchase the hull name changes.</summary>
    public void Rebind(World world)
    {
        World = world;
        HullName = Text.Get("HULL_" + world.Ship.Hull.Id);
    }

    public void PulseSail() => SailPulse = 1;

    Vector2 mousePos = new(-1, -1);

    /// <summary>The pointer as the last mouse event left it (asking the display server every frame is a blocking round trip).</summary>
    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouse m) mousePos = m.Position;
    }

    /// <summary>Screen rect of an instrument, for hint notes that point at it (null when unknown).</summary>
    public Rect2? AnchorOf(string what) => canvas.AnchorOf(what);

    /// <summary>Where a leader from <paramref name="from"/> should touch the instrument (the rose is a circle); null = its rect.</summary>
    public Vector2? LeaderTarget(string what, Vector2 from) => canvas.LeaderTarget(what, from);

    /// <summary>Where a hint note sits when it points at nothing in particular: above the bottom-centre stack.</summary>
    public Rect2 HintSlot => canvas.HintSlot;

    /// <summary>The slot notices and the tier banner unfurl in (at its tallest), which hint notes keep clear of.</summary>
    public Rect2 NoticeZone => canvas.NoticeZone;

    /// <summary>The areas edge markers must keep clear of.</summary>
    internal IReadOnlyList<Rect2> KeepOut => canvas.KeepOut;

    internal static NoticeKind KindOf(string key)
    {
        if (key.StartsWith("NOTICE_COSMETIC_") || key.StartsWith("ACHIEVEMENT_")) return NoticeKind.Good;
        return key switch
        {
            "NOTICE_COVE" or "NOTICE_BY_A_HAIR" or "NOTICE_MONSTER_BEATEN" or "NOTICE_MONSTER_ESCAPED" or "NOTICE_MAP_MATCHED"
                or "NOTICE_TREASURE" or "NOTICE_SALVAGE" => NoticeKind.Good,
            "NOTICE_BOTTLE_MAP" or "NOTICE_DIG_INTERRUPTED" or "NOTICE_FURL_FIRST" => NoticeKind.Info,
            _ => NoticeKind.Danger,
        };
    }

    /// <param name="delta">Seconds since the last frame: every HUD timer and animation runs on it.</param>
    /// <param name="covered">A full screen (port, chart, title, logbook, options) covers the HUD.</param>
    public void Refresh(World world, bool isPaused, double delta, bool covered = false)
    {
        World = world;
        float dt = (float)delta;
        // Everything that times itself holds still while the game is paused or a screen covers the HUD, so a notice
        // raised on docking (unpaid hands deserted) is read after casting off instead of fading unseen.
        bool held = isPaused || covered || world.IsDocked;
        float live = held ? 0 : dt;
        Clock += dt;
        if (!held) seaTime += dt;

        // Strings are formatted again only when what they show changes: the HUD makes no garbage in steady state.
        var ship = world.Ship;
        var w = ship.LocalWind;
        Heading = (float)ship.Heading;
        WindDir = (float)w.Direction;
        WindSpeed = (float)w.Speed;
        PointDeg = (float)ship.PointDeg;
        SailsSet = ship.SailFraction > 0.05;
        InIrons = ship.InIrons && SailsSet;
        Polar = (float)Math.Clamp(ship.PolarFraction, 0, 1);
        long kn = (long)Math.Round(w.Knots, MidpointRounding.AwayFromZero);
        if (kn != kWindKn) { kWindKn = kn; WindKn = Text.Get("HUD_WIND_KN", kn); }
        long from = Angles.PointIndex(w.From);
        if (from != kWindFrom) { kWindFrom = from; WindFrom = Text.Get("HUD_WIND_FROM", Text.Get("PT_" + from)); }
        long compass = (long)Math.Round(Angles.CompassDeg(ship.Heading)) % 360, point = Angles.PointIndex(ship.Heading);
        if (compass * 16 + point != kHeading) { kHeading = compass * 16 + point; HeadingLine = Text.Get("HUD_HEADING", compass, Text.Get("PT_" + point)); }

        if (world.Day != kDay) { kDay = world.Day; DayText = Text.Get("HUD_DAY_N", world.Day); }
        if (world.WatchIndex != kWatch) { kWatch = world.WatchIndex; WatchText = Text.Get("HUD_WATCH", Text.Get("WATCH_" + world.WatchIndex)); }
        HourOfDay = (float)world.HourOfDay;

        // Conditions: where she is, the weather, and anything that has hold of her (danger in red).
        var cond = world.ConditionsAt(ship.Pos);
        var region = world.Map.RegionAt(ship.Pos).Type;
        var beast = world.Monster;
        long sig = (cond.Storm > 0.05 ? 1L : 0) + (cond.Fog > 0.05 ? 2L : 0) + (cond.Doldrums > 0.3 ? 4L : 0) + (cond.Ash > 0.05 ? 8L : 0)
            + (cond.Night ? 16L : 0) + (world.Lantern ? 32L : 0) + (ship.TornSails ? 64L : 0) + (world.Pinned ? 128L : 0)
            + (world.RudderJam > 0 ? 256L : 0) + (world.SirenPull != 0 ? 512L : 0) + (ship.Aground ? 1024L : 0)
            + (world.MangroveBlocked ? 2048L : 0) + (beast == null ? 0L : (int)beast.Type + 1L) * 4096 + (long)region * 1_048_576;
        if (sig != kCond)
        {
            kCond = sig;
            Conditions.Clear();
            Conditions.Add((Text.Get("REGION_" + RegionDef.Of(region).Key), Parchment.Muted));
            if (cond.Storm > 0.05) Conditions.Add((Text.Get("WX_STORM"), Ink.Red));
            if (cond.Fog > 0.05) Conditions.Add((Text.Get("WX_FOG"), Ink.Wind));
            if (cond.Doldrums > 0.3) Conditions.Add((Text.Get("WX_DOLDRUMS"), Ink.Wind));
            if (cond.Ash > 0.05) Conditions.Add((Text.Get("WX_ASH"), Ink.Wind));
            if (cond.Night) Conditions.Add((Text.Get(world.Lantern ? "WX_NIGHT_LIT" : "WX_NIGHT_DOUSED"), HudInk.Night));
            if (ship.TornSails) Conditions.Add((Text.Get("WX_TORN"), Ink.Red));
            if (beast != null) Conditions.Add((Text.Get("MONSTER_" + beast.Def.Key), Ink.Red));
            if (world.Pinned) Conditions.Add((Text.Get("WX_PINNED"), Ink.Red));
            if (world.RudderJam > 0) Conditions.Add((Text.Get("WX_JAMMED"), Ink.Red));
            if (world.SirenPull != 0) Conditions.Add((Text.Get("WX_SONG"), Ink.Red));
            if (ship.Aground) Conditions.Add((Text.Get("HUD_AGROUND"), Ink.Red));
            var sb = new System.Text.StringBuilder();
            foreach (var (text, _) in Conditions) { if (sb.Length > 0) sb.Append(" · "); sb.Append(text); }
            WeatherText = sb.ToString();
            StateBanner = world.MangroveBlocked ? Text.Get("HUD_MANGROVE") : "";
        }

        ThreatValue = (float)world.ThreatNow;
        Tier = world.ThreatTier;
        if (Tier != kTier) { kTier = Tier; TierName = Text.Get("TIER_" + Threat.TierKey[Tier]); }
        if (lastTierSeen >= 0 && Tier > lastTierSeen)
        {
            ThreatPulse = 1;
            TierFlash = 1;
            BannerTier = TierName;
            BannerAge = 0;
        }
        lastTierSeen = Tier;

        // Entering a region letters its name across the top for a few seconds.
        if (region != lastRegion)
        {
            if (lastRegion != null)
            {
                RegionName = Text.Get("REGION_" + RegionDef.Of(region).Key);
                RegionFlash = 1;
            }
            lastRegion = region;
        }
        if (RegionFlash > 0) RegionFlash = Math.Max(0, RegionFlash - live / 5f);

        SailLevel = ship.SailTarget;
        SailFraction = (float)ship.SailFraction;
        if (SailLevel != kSail) { kSail = SailLevel; SailName = Text.Get("SAIL_" + SailLevel); }
        long speed10 = (long)Math.Round(ship.Knots * 10, MidpointRounding.AwayFromZero);
        if (speed10 != kSpeed) { kSpeed = speed10; SpeedText = (speed10 / 10.0).ToString("0.0", CultureInfo.InvariantCulture); }
        if ((long)ship.PointOfSail != kPoint) { kPoint = (long)ship.PointOfSail; PointName = Text.Get("POS_" + ship.PointOfSail.ToString().ToUpperInvariant()); }

        var player = world.Player;
        HullFrac = (float)Math.Clamp(ship.HullHp / ship.MaxHp, 0, 1);
        WaterFrac = (float)Math.Clamp(ship.Water / 100, 0, 1);
        long pct = Mathf.RoundToInt(HullFrac * 100) * 1000L + Mathf.RoundToInt(WaterFrac * 100);
        if (pct != kPct) { kPct = pct; HullPct = (pct / 1000).ToString(CultureInfo.InvariantCulture) + "%"; WaterPct = (pct % 1000).ToString(CultureInfo.InvariantCulture) + "%"; }
        Leaks = ship.Leaks;
        Guns.PortLoaded = ship.Loaded[0];
        Guns.StarboardLoaded = ship.Loaded[1];
        for (int s2 = 0; s2 < 2; s2++)
        {
            if (ship.Loaded[s2] && !wasLoaded[s2] && !ship.Foundering) ReadyPulse[s2] = 1;   // a side has just run out loaded
            wasLoaded[s2] = ship.Loaded[s2];
            ReadyPulse[s2] = Math.Max(0, ReadyPulse[s2] - dt * 1.6f);
        }
        Guns.PortProgress = ship.Loaded[0] ? 1 : (float)Math.Clamp(1 - ship.Reload[0] / Math.Max(0.01, ship.ReloadTime), 0, 1);
        Guns.StarboardProgress = ship.Loaded[1] ? 1 : (float)Math.Clamp(1 - ship.Reload[1] / Math.Max(0.01, ship.ReloadTime), 0, 1);
        Guns.Munitions = player.Units(Good.Munitions);
        Guns.Cannons = ship.Cannons;
        Guns.PortGuns = ship.GunsOn(LastTide.Sim.Side.Port);
        Guns.StarboardGuns = ship.GunsOn(LastTide.Sim.Side.Starboard);
        var st = ship.Stations();
        long stations = st[0] + st[1] * 4096L + st[2] * 16_777_216L + st[3] * 68_719_476_736L;
        if (stations != kStations)
        {
            kStations = stations;
            for (int i = 0; i < 4; i++) { Stations[i] = st[i]; StationText[i] = st[i].ToString(CultureInfo.InvariantCulture); }
        }
        if ((long)ship.Order != kOrder) { kOrder = (long)ship.Order; OrderName = Text.Get("ORDER_NAME_" + ship.Order.ToString().ToUpperInvariant()); }

        if (lastGold != int.MinValue && player.Gold != lastGold) GoldPulse = 1;
        lastGold = player.Gold;
        if (player.Gold != kGold) { kGold = player.Gold; GoldText = player.Gold.ToString("#,0", CultureInfo.InvariantCulture); }
        long hold = (long)Math.Round(player.SlotsUsed * 10) * 100000 + ship.CargoCapacity;
        if (hold != kHold) { kHold = hold; HoldText = Text.Get("HUD_HOLD_SHORT", player.SlotsUsed.ToString("0.#", CultureInfo.InvariantCulture), ship.CargoCapacity); }
        long crew = ship.Crew * 10000L + ship.Hull.CrewMax;
        if (crew != kCrew) { kCrew = crew; CrewText = Text.Get("HUD_CREW_SHORT", ship.Crew, ship.Hull.CrewMax); }
        Provisions = player.Units(Good.Provisions);
        Munitions = Guns.Munitions;
        Timber = player.Units(Good.Timber);
        long stores = Provisions * 1000000L + Munitions * 1000L + Timber;
        if (stores != kStores)
        {
            kStores = stores;
            ProvisionsText = Provisions.ToString(CultureInfo.InvariantCulture);
            MunitionsText = Munitions.ToString(CultureInfo.InvariantCulture);
            TimberText = Timber.ToString(CultureInfo.InvariantCulture);
        }
        ProvisionsLow = Provisions < Math.Max(1, (ship.Crew + 3) / 4);
        MunitionsLow = ship.Cannons > 0 && Munitions < ship.Cannons;
        ShipName = player.ShipName;

        // The context prompt: dock, dig, salvage, each with the key that does it.
        var here = world.HarborHere;
        int branch = here != null && !world.IsDocked ? 1 : world.Digging ? 2 : world.CanDigHere || world.CanSalvageHere ? 3 : world.DigSiteHere != null ? 4 : 0;
        bool salvage = world.Salvaging;   // what the sim is actually doing (a wreck can lie inside an unmatched dig ring)
        double total = salvage ? World.SalvageSeconds : World.DigSeconds;
        bool furled = ship.SailTarget == 0 && ship.SailFraction < 0.05;
        bool open = here != null && world.IsOpen(here);
        long secs = branch == 2 ? (long)Math.Ceiling(total - world.DigProgress) : 0;
        long psig = branch + ((here?.Id ?? -1) + 1L) * 8 + (open ? 1L << 20 : 0) + (salvage ? 1L << 21 : 0) + (furled ? 1L << 22 : 0)
            + (world.CanDigHere ? 1L << 23 : 0) + (secs << 24) + ((long)keyVersion << 40);
        PromptVisible = branch != 0;
        PromptProgress = branch == 2 ? (float)Math.Clamp(world.DigProgress / total, 0, 1) : -1;
        if (psig != kPrompt)
        {
            kPrompt = psig;
            PromptKey = PromptIcon = "";
            PromptColour = Ink.Black;
            switch (branch)
            {
                case 1:
                    PromptText = open ? Text.Get("HUD_DOCK", here!.Name) : Text.Get("HUD_DOCK_CLOSED", here!.Name);
                    PromptKey = open ? Key("Dock") : "";
                    PromptIcon = "anchor";
                    PromptColour = open ? Ink.Black : Ink.Red;
                    break;
                case 2:
                    PromptText = Text.Get(salvage ? "HUD_SALVAGING" : "HUD_DIGGING", secs);
                    PromptIcon = salvage ? "crate" : "spade";
                    break;
                case 3:
                    PromptText = Text.Get(world.CanDigHere ? (furled ? "HUD_DIG" : "HUD_DIG_FURL") : (furled ? "HUD_SALVAGE" : "HUD_SALVAGE_FURL"));
                    PromptKey = furled ? Key("Dock") : Key("SailDown");
                    PromptIcon = world.CanDigHere ? "spade" : "crate";
                    break;
                case 4:
                    PromptText = Text.Get("HUD_DIG_NO_MAP");
                    PromptIcon = "spade";
                    PromptColour = Parchment.Muted;
                    break;
                default:
                    PromptText = "";
                    break;
            }
        }

        // Notices: one ribbon at a time; a newer one takes over once the current has been read for a while. A notice
        // leaves the world's queue only when it is inked (Main plays the quill on that).
        if (!held)
        {
            if (world.Notices.Count > 0 && (NoticeText.Length == 0 || NoticeLeft < NoticeHold - 1.8f))
            {
                string key = world.Notices.Dequeue();
                NoticeText = Text.Get(key);
                NoticeKindNow = KindOf(key);
                NoticeLeft = NoticeHold;
                NoticeAge = 0;
            }
            if (NoticeText.Length > 0)
            {
                NoticeAge += live;
                NoticeLeft -= live;
                if (NoticeLeft <= 0) NoticeText = "";
            }
            if (BannerAge >= 0)
            {
                BannerAge += live;
                if (BannerAge > BannerHold) BannerAge = -1;
            }
        }

        // The last stand: the glass, what to do, and the nearest harbour that will still take her in.
        Stand = ship.Foundering && !world.RunOver;
        if (Stand)
        {
            if (ship.Hourglass > HourglassMax || HourglassMax <= 0) HourglassMax = (float)ship.Hourglass;
            Hourglass = (float)ship.Hourglass;
            StandWaterOnly = ship.WaterOnlyStand;
            long stand = (long)Math.Ceiling(ship.Hourglass) * 2 + (ship.WaterOnlyStand ? 1 : 0);
            if (stand != kStand)
            {
                kStand = stand;
                StandTitle = Text.Get("HUD_STAND_TITLE");
                StandSeconds = Math.Ceiling(ship.Hourglass).ToString(CultureInfo.InvariantCulture);
                StandLine = Text.Get(ship.WaterOnlyStand ? "HUD_LAST_STAND_WATER" : "HUD_LAST_STAND_HULL");
            }
            RescueHarbour = NearestRefuge(world, out double dist);
            if (RescueHarbour != null)
            {
                double sp = Math.Max(0.5, Math.Min(ship.Speed, ship.TopSpeed * 0.5));
                double eta = Math.Ceiling(dist / sp);
                HarbourReachable = eta <= ship.Hourglass;
                long h = RescueHarbour.Id + 1 + ((long)Math.Round(dist / 5) << 10) + ((long)Math.Min(eta, 9999) << 30) + ((HarbourReachable ? 1L : 0) << 45);
                if (h != kHarbour)
                {
                    kHarbour = h;
                    HarbourLine = Text.Get(HarbourReachable ? "HUD_STAND_HARBOUR" : "HUD_STAND_HARBOUR_FAR", RescueHarbour.Name, Math.Round(dist / 5) * 5, eta);
                }
            }
            else if (kHarbour != 0) { kHarbour = 0; HarbourLine = Text.Get("HUD_STAND_NO_HARBOUR"); }
        }
        else
        {
            HourglassMax = 0;
            RescueHarbour = null;
        }
        Over = world.RunOver;
        if (Over && world.Day != kOver) { kOver = world.Day; OverText = Text.Get("HUD_SUNK", world.Day); }
        // Once she is gone the instruments fade from under the SUNK title, leaving the chart for the logbook.
        OverAge = Over ? OverAge + dt : 0;
        canvas.Modulate = Colors.White with { A = Over ? 1 - Mathf.Clamp((OverAge - 0.6f) / 1.4f, 0, 1) : 1 };

        // The key legend shows for the first two minutes at sea, then steps aside; it comes back while paused, when
        // the bindings change, and whenever the pointer rests on the bottom of the screen.
        var view = canvas.Size;
        bool hover = mousePos.Y > view.Y - 70 && mousePos.Y <= view.Y && mousePos.X >= 0 && mousePos.X <= view.X;
        bool want = seaTime < 120 || isPaused || seaTime < legendShowUntil || hover;
        LegendAlpha = Mathf.MoveToward(LegendAlpha, want ? 1 : 0, dt * (want ? 4f : 0.8f));

        GoldPulse = Math.Max(0, GoldPulse - dt * 2.6f);
        SailPulse = Math.Max(0, SailPulse - dt * 2.6f);
        ThreatPulse = Math.Max(0, ThreatPulse - dt * 1.6f);
        TierFlash = Math.Max(0, TierFlash - live * 0.35f);

        canvas.QueueRedraw();
        markers.QueueRedraw();
        if (Over) sunk.QueueRedraw();
    }

    /// <summary>The nearest charted harbour that is open to her and has not already hauled her in once this voyage.</summary>
    static Port? NearestRefuge(World world, out double distance)
    {
        Port? best = null;
        distance = double.MaxValue;
        foreach (var p in world.Map.Ports)
        {
            if (!p.Discovered || !world.IsOpen(p) || world.RescuedAt.Contains(p.Id)) continue;
            double d = Math.Max(0, p.Harbor.DistanceTo(world.Ship.Pos) - p.RingRadius);
            if (d < distance) { distance = d; best = p; }
        }
        return best;
    }
}

public enum NoticeKind { Danger, Good, Info }

/// <summary>Paints the whole HUD in passes: plates, then sprites and boxes (one atlas), then text grouped by face.</summary>
public partial class HudCanvas : InkCanvas
{
    public Hud Hud = null!;
    Texture2D? rose;
    readonly List<Rect2> keepOut = new();
    readonly List<Rect2> plates = new();
    public IReadOnlyList<Rect2> KeepOut => keepOut;
    public IReadOnlyList<Rect2> Plates => plates;
    public Rect2 HintSlot { get; private set; }
    public Rect2 NoticeZone { get; private set; }

    // Layout (virtual pixels; recomputed every frame from the viewport size and what is showing).
    Vector2 view;
    Rect2 roseR, windR, dayR, purseR, helmR, shipR, promptR, legendR, noticeR, standR, hullTube, waterTube, gunsR, crewR;
    Vector2 roseC;
    float roseScale, ringR;
    readonly List<(float X, float Y, int Row)> legendAt = new();
    readonly List<(string[] Keys, string Word)> legendItems = new();
    readonly List<float> legendRowW = new();
    readonly List<(float X, int Line, bool Dot)> chips = new();
    string legendBuiltFor = "";

    public override void _Ready()
    {
        base._Ready();
        rose = HudArt.Rose;
    }

    public Vector2? LeaderTarget(string what, Vector2 from)
    {
        if (what != "rose") return null;
        var d = from - roseC;
        return roseC + (d.LengthSquared() > 1 ? d.Normalized() : Vector2.Down) * HudArt.RoseRingOuter * roseScale;
    }

    public Rect2? AnchorOf(string what) => what switch
    {
        "rose" => roseR,
        "wind" => windR,
        "threat" => dayR,
        "helm" or "sails" => helmR,
        "hull" or "water" or "leak" => new Rect2(hullTube.Position - new Vector2(6, 22), waterTube.End - hullTube.Position + new Vector2(40, 40)),
        "guns" => gunsR,
        "crew" => crewR,
        "ship" => shipR,
        "purse" => purseR,
        "prompt" => Hud.PromptVisible ? promptR : null,
        _ => null,
    };

    static readonly (string Action, string Action2, string Word)[] LegendDef =
    {
        ("SailUp", "SailDown", "HUD_LEGEND_SAIL"), ("Port", "Starboard", "HUD_LEGEND_HELM"), ("FirePort", "FireStarboard", "HUD_LEGEND_FIRE"),
        ("", "", "HUD_LEGEND_SPYGLASS"), ("Order1", "Order4", "HUD_LEGEND_ORDERS"), ("Crew", "", "HUD_LEGEND_CREW"), ("Lantern", "", "HUD_LEGEND_LANTERN"),
        ("Dock", "", "HUD_LEGEND_DOCK"), ("Chart", "", "HUD_LEGEND_CHART"), ("", "", "HUD_LEGEND_PAUSE"),
    };

    void BuildLegend()
    {
        string sig = Hud.KeyLine;
        if (sig == legendBuiltFor && legendItems.Count > 0) return;
        legendBuiltFor = sig;
        legendItems.Clear();
        foreach (var (a1, a2, word) in LegendDef)
        {
            string[] keys = word switch
            {
                "HUD_LEGEND_SPYGLASS" => new[] { Text.Get("HUD_KEY_RMB") },
                "HUD_LEGEND_PAUSE" => new[] { Text.Get("HUD_KEY_ESC") },
                "HUD_LEGEND_ORDERS" => new[] { Hud.Key(a1) + "–" + Hud.Key(a2) },
                _ => a2.Length > 0 ? new[] { Hud.Key(a1), Hud.Key(a2) } : new[] { Hud.Key(a1) },
            };
            legendItems.Add((keys, Text.Get(word)));
        }
    }

    static float ItemW((string[] Keys, string Word) it)
    {
        float w = 0;
        foreach (var k in it.Keys) w += KeyCapW(k) + 3;
        return w + 4 + TextW(Fonts.Body, 15, it.Word);
    }

    void Layout()
    {
        view = Size;
        float W = view.X, H = view.Y;
        const float M = 12;
        // Rose: the art's ring centre sits on (roseC); the ring's inner radius is ringR.
        roseScale = 206f / HudArt.RoseWidth;
        roseR = new Rect2(M, 2, 206, HudArt.RoseHeight * roseScale);
        roseC = roseR.Position + HudArt.RoseCentre * roseScale;
        ringR = HudArt.RoseRingInner * roseScale;
        float condH = LayoutChips(M + 16, M + 226 - 16) * 20;
        windR = new Rect2(M, roseR.End.Y + 2, 226, 70 + condH);
        dayR = new Rect2(W / 2 - 240, 6, 480, 96);
        purseR = new Rect2(W - M - 238, 6, 238, 126);
        helmR = new Rect2(M, H - M - 118, 300, 118);
        shipR = new Rect2(W - M - 346, H - M - 152, 346, 152);
        hullTube = new Rect2(shipR.Position.X + 24, shipR.Position.Y + 36, 18, 84);
        waterTube = new Rect2(shipR.Position.X + 66, shipR.Position.Y + 36, 18, 84);
        gunsR = new Rect2(shipR.Position.X + 104, shipR.Position.Y + 14, 118, 124);
        crewR = new Rect2(shipR.Position.X + 234, shipR.Position.Y + 14, 100, 124);

        // Bottom centre: the prompt at the foot, the key legend above it, hint notes above both.
        float left = helmR.End.X + M, right = shipR.Position.X - M, free = right - left, cx = (left + right) / 2;
        float promptW = PromptWidth();
        promptR = new Rect2(cx - promptW / 2, H - M - 42, promptW, 42);
        if (promptW > free) promptR.Position = new Vector2(W / 2 - promptW / 2, shipR.Position.Y - 8 - 42);
        BuildLegend();
        legendAt.Clear();
        float rowW = 0, maxRow = Math.Max(160, Math.Min(free, 720) - 28);
        int row = 0;
        var widths = legendRowW;
        widths.Clear();
        foreach (var it in legendItems)
        {
            float iw = ItemW(it);
            if (rowW > 0 && rowW + 18 + iw > maxRow) { widths.Add(rowW); row++; rowW = 0; }
            legendAt.Add((rowW + (rowW > 0 ? 18 : 0), 0, row));
            rowW += (rowW > 0 ? 18 : 0) + iw;
        }
        widths.Add(rowW);
        float widest = 0;
        foreach (float rw in widths) widest = Math.Max(widest, rw);
        float legendW = widest + 28, legendH = (row + 1) * 27 + 14;
        float legendBottom = promptR.Position.Y - 6;
        legendR = new Rect2(cx - legendW / 2, legendBottom - legendH, legendW, legendH);
        for (int i = 0; i < legendAt.Count; i++)
        {
            var (x, _, r) = legendAt[i];
            float off = (legendW - 28 - widths[r]) / 2;
            legendAt[i] = (legendR.Position.X + 14 + off + x, legendR.Position.Y + 8 + r * 27, r);
        }
        float hintBottom = legendR.Position.Y - 10;   // the legend's place stays reserved while it is faded out
        float hintW = Math.Clamp(free + 40, 320, 600);
        HintSlot = new Rect2(W / 2 - hintW / 2, hintBottom - 96, hintW, 96);

        float top = dayR.End.Y + 6;
        noticeR = new Rect2(W / 2 - 300, top, 600, Hud.BannerAge >= 0 ? 88 : 50);
        NoticeZone = new Rect2(W / 2 - 320, top, 640, 90);
        float standW = Math.Clamp(Math.Max(TextW(Fonts.Body, 16, Hud.StandLine), TextW(Fonts.Body, 16, Hud.HarbourLine) + 50) + 150, 520, W - 2 * M);
        standR = new Rect2(W / 2 - standW / 2, noticeR.End.Y + 8, standW, Hud.StandWaterOnly ? 156 : 128);

        plates.Clear();
        plates.Add(roseR);
        plates.Add(windR);
        plates.Add(dayR);
        plates.Add(purseR);
        plates.Add(helmR);
        plates.Add(shipR);
        if (Hud.PromptVisible) plates.Add(promptR);
        if (Hud.LegendAlpha > 0.05f) plates.Add(legendR);
        if (Hud.Stand) plates.Add(standR);
        keepOut.Clear();
        keepOut.Add(new Rect2(0, 0, Math.Max(roseR.End.X, windR.End.X) + 6, windR.End.Y + 6));
        keepOut.Add(dayR.Grow(6));
        keepOut.Add(purseR.Grow(6));
        keepOut.Add(helmR.Grow(6));
        keepOut.Add(shipR.Grow(6));
        if (Hud.PromptVisible) keepOut.Add(promptR.Grow(6));
        keepOut.Add(legendR.Grow(6));   // even faded: markers and notes must not jump when it comes back
        if (Hud.NoticeText.Length > 0 || Hud.BannerAge >= 0 || Hud.StateBanner.Length > 0) keepOut.Add(noticeR.Grow(6));
        if (Hud.Stand) keepOut.Add(standR.Grow(6));
    }

    /// <summary>Lays the condition chips out in rows inside the wind plate (one layout for sizing and drawing).</summary>
    int LayoutChips(float x0, float max)
    {
        chips.Clear();
        float cx = x0;
        int line = 0;
        for (int i = 0; i < Hud.Conditions.Count; i++)
        {
            float tw = TextW(i == 0 ? Fonts.SmallCaps : Fonts.Body, 15, Hud.Conditions[i].Text);
            bool dot = false;
            if (i > 0)
            {
                if (cx + 14 + tw > max) { line++; cx = x0; }
                else { cx += 14; dot = true; }
            }
            chips.Add((cx, line, dot));
            cx += tw;
        }
        return line + 1;
    }

    float PromptWidth()
    {
        if (!Hud.PromptVisible) return 300;
        float w = 18 + TextW(Fonts.Body, 20, Hud.PromptText) + 20;
        if (Hud.PromptIcon.Length > 0) w += 32;
        if (Hud.PromptKey.Length > 0) w += KeyCapW(Hud.PromptKey, 26) + 10;
        return w;
    }

    public override void _Draw()
    {
        if (Hud.World == null) return;
        Layout();
        rose ??= HudArt.Rose;

        // ---- pass 1: the paper (plates and the rose's halo) ----
        Sprite(HudArt.Glow, new Rect2(roseC - new Vector2(ringR, ringR) * 1.62f, new Vector2(ringR, ringR) * 3.24f), Ink.Paper with { A = 0.92f });
        Disc(roseC, HudArt.RoseRingOuter * roseScale, Ink.Paper);
        Plate(windR);
        Plate(dayR, corners: true);
        Plate(purseR);
        Plate(helmR);
        Plate(shipR, tint: Hud.Stand ? HudInk.DangerWash : null);
        if (Hud.LegendAlpha > 0.01f) Plate(legendR, tint: Colors.White with { A = Hud.LegendAlpha });
        if (Hud.PromptVisible) Plate(promptR, tint: Hud.PromptColour == Ink.Red ? HudInk.DangerWash : null);
        if (Hud.Stand)
        {
            // A red glow that beats with the glass behind the panel.
            float beat = Mathf.Pow(1 - (Hud.Hourglass % 1f), 2);
            Sprite(HudArt.Glow, standR.Grow(26), Ink.Red with { A = 0.18f + 0.22f * beat });
            Plate(standR, tint: HudInk.DangerWash, corners: true);
        }

        // ---- pass 2: the rose art, then everything drawn over it that is not atlas work ----
        if (rose != null)
            DrawTextureRect(rose, roseR, false);
        DrawNoGo();

        // ---- pass 3: atlas sprites and boxes ----
        DrawRoseOverlay();
        DrawWind();
        DrawDay();
        DrawPurse();
        DrawHelm();
        DrawShipCard();
        DrawPrompt();
        DrawLegend();
        DrawNotice();
        DrawStand();

        // ---- pass 4: lettering ----
        FlushText();
    }

    // ---------------------------------------------------------------- the rose

    void DrawNoGo()
    {
        // The no-go wedge: where she cannot sail. The needle inside it means she is in irons.
        float from = Hud.WindDir + Mathf.Pi;
        float half = Mathf.DegToRad(Hud.PointDeg);
        int n = 14;
        var pts = new Vector2[n + 2];
        pts[0] = roseC;
        float r = ringR * 0.97f;
        for (int i = 0; i <= n; i++)
        {
            float a = from - half + 2 * half * i / n;
            pts[i + 1] = roseC + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        float pulse = Hud.InIrons ? 0.42f + 0.12f * Mathf.Sin(Hud.Clock * 6) : 0.20f;
        DrawColoredPolygon(pts, Ink.Red with { A = pulse });
    }

    void DrawRoseOverlay()
    {
        float r = ringR;
        // The no-go wedge's edges, inked so the boundary reads over the star.
        float from = Hud.WindDir + Mathf.Pi, halfA = Mathf.DegToRad(Hud.PointDeg);
        var edgeInk = Ink.Red with { A = Hud.InIrons ? 0.95f : 0.7f };
        for (int k2 = -1; k2 <= 1; k2 += 2)
        {
            float e = from + k2 * halfA;
            Stroke(roseC + new Vector2(Mathf.Cos(e), Mathf.Sin(e)) * 7, roseC + new Vector2(Mathf.Cos(e), Mathf.Sin(e)) * r * 0.97f, 1.6f, edgeInk);
        }
        // The wind: a bold blue arrow across the rose from where it comes to where it goes, fletched at the tail with a
        // feather for every 10 knots (a short one for 5), and thicker the harder it blows.
        var w = new Vector2(Mathf.Cos(Hud.WindDir), Mathf.Sin(Hud.WindDir));
        var wp = new Vector2(-w.Y, w.X);
        float len = r * 1.92f;
        float kn = Hud.WindSpeed * 1.943844f;
        float thick = 20 + 10 * Mathf.Clamp(kn / 40f, 0, 1);
        SpriteAt(HudArt.ArrowHalo, roseC, new Vector2(len, thick), Hud.WindDir, Ink.Paper);
        SpriteAt(HudArt.Arrow, roseC, new Vector2(len, thick), Hud.WindDir, Ink.Wind);
        int fives = Mathf.Clamp(Mathf.RoundToInt(kn / 5f), 1, 12);
        int full = fives / 2, half = fives % 2;
        var tail = roseC - w * (len / 2 - 6);
        int k = 0;
        for (int i = 0; i < full + half; i++, k++)
        {
            bool shortOne = i == full;   // the half feather goes last
            float fl = shortOne ? 9 : 16;
            var at = tail + w * (k * 7.5f);
            for (float side = -1; side <= 1; side += 2)
            {
                var dir = (-w * 0.62f + wp * side).Normalized();
                var mid = at + dir * (fl / 2);
                SpriteAt(HudArt.Barb, mid, new Vector2(fl + 4, 4.2f), dir.Angle(), Ink.Wind);
            }
        }

        // The ship: a slim needle (red in irons) with a paper halo so it reads over the star.
        var h = new Vector2(Mathf.Cos(Hud.Heading), Mathf.Sin(Hud.Heading));
        float nl = r * 1.28f;
        var nc = roseC + h * (nl * (0.5f - 0.24f));
        SpriteAt(HudArt.NeedleHalo, nc, new Vector2(nl, 13), Hud.Heading, Ink.Paper);
        SpriteAt(HudArt.Needle, nc, new Vector2(nl, 13), Hud.Heading, Hud.InIrons ? Ink.Red : Ink.Black);
        Disc(roseC, 5.5f, Ink.Paper);
        RingAt(roseC, 5.5f, Ink.Black, thin: true);
        if (rose == null)
        {
            // Without the art: the old inked ring so the rose still reads.
            RingAt(roseC, r, Ink.Black, thin: true);
            RingAt(roseC, r * 0.7f, Ink.Soft, thin: true);
        }
    }

    void DrawWind()
    {
        var p = windR.Position;
        float x = p.X + 16, y = p.Y + 36;
        Txt(Fonts.Display, 30, new Vector2(x, y), Hud.WindKn, Ink.Wind);
        float kw = TextW(Fonts.Display, 30, Hud.WindKn);
        Txt(Fonts.Italic, 18, new Vector2(x + kw + 6, y), Hud.WindFrom, Ink.Black);
        Txt(Fonts.Body, 15, new Vector2(x, y + 20), Hud.HeadingLine, Parchment.Muted);
        // Conditions, wrapped as laid out: the region in small caps, then weather and dangers.
        for (int i = 0; i < Hud.Conditions.Count && i < chips.Count; i++)
        {
            var (text, colour) = Hud.Conditions[i];
            var (cx, line, dot) = chips[i];
            float cy = y + 42 + line * 20;
            if (dot) Disc(new Vector2(cx - 7, cy - 5), 1.8f, Ink.Soft);
            if (i == 0 && Hud.RegionFlash > 0)
            {
                // A region just entered: its name inks in bold and an underline draws itself, then both settle.
                float f = Ease(Hud.RegionFlash);
                colour = Parchment.Muted.Lerp(Ink.Black, f);
                float uw = TextW(Fonts.SmallCaps, 15, text) * Mathf.Clamp((1 - Hud.RegionFlash) * 6, 0, 1);
                Box(new Rect2(cx, cy + 3, uw, 1.4f), Ink.Black with { A = f });
            }
            Txt(i == 0 ? Fonts.SmallCaps : Fonts.Body, 15, new Vector2(cx, cy), text, colour);
        }
    }

    // ---------------------------------------------------------------- day, watch and Threat

    void DrawDay()
    {
        var p = dayR.Position;
        float right = dayR.End.X - 22;
        // A sky dial: the sun crosses a little window by day, the moon by night; the ground hides whichever is down.
        var dc = new Vector2(p.X + 48, p.Y + 50);
        float dr = 26;
        float hr = Hud.HourOfDay;
        bool day = hr >= (float)Tuning.DawnHour && hr < (float)Tuning.DuskHour;
        float t = day ? (hr - (float)Tuning.DawnHour) / (float)(Tuning.DuskHour - Tuning.DawnHour)
                      : ((hr - (float)Tuning.DuskHour + 24) % 24) / (float)(24 - Tuning.DuskHour + Tuning.DawnHour);
        Disc(dc, dr, day ? HudInk.Sky : HudInk.Night);
        float ang = Mathf.Pi + t * Mathf.Pi;   // rises on the left of the window and sets on the right
        var body = dc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (dr * 0.60f) + new Vector2(0, 2);
        Icon(day ? "sun" : "moon", new Rect2(body - new Vector2(12, 12), new Vector2(24, 24)));
        Box(new Rect2(dc.X - dr - 1, dc.Y + 5, dr * 2 + 2, dr - 3), HudInk.PlatePaper);
        Box(new Rect2(dc.X - dr + 1, dc.Y + 4, dr * 2 - 2, 1.6f), Ink.Black);
        for (int i = 0; i < 3; i++)
            Box(new Rect2(dc.X - 15 + i * 11, dc.Y + 10 + (i % 2) * 4, 8, 1.4f), Ink.Soft);
        RingAt(dc, dr, Ink.Black, thin: true);

        // Day and watch; the tier's name at the right, over the end of the bar it names.
        float x = p.X + 88;
        Txt(Fonts.Display, 30, new Vector2(x, p.Y + 44), Hud.DayText, Ink.Black);
        float dw = TextW(Fonts.Display, 30, Hud.DayText);
        Txt(Fonts.Italic, 18, new Vector2(x + dw + 10, p.Y + 43), Hud.WatchText, Ink.Black);
        var tierInk = HudInk.Tier(Hud.Tier).Darkened(0.35f);
        if (Hud.TierFlash > 0) tierInk = tierInk.Lerp(Ink.Red, Hud.TierFlash);
        float tnw = TextW(Fonts.SmallCaps, 17, Hud.TierName);
        var tierPivot = new Vector2(right - tnw / 2, p.Y + 38);
        if (Hud.ThreatPulse > 0) SetBase(ScaleAbout(tierPivot, 1 + 0.35f * Ease(Hud.ThreatPulse)));
        Txt(Fonts.SmallCaps, 17, new Vector2(right - tnw, p.Y + 43), Hud.TierName, tierInk);
        if (Hud.ThreatPulse > 0) SetBase(Transform2D.Identity);

        // The Threat bar: nine tiers on a log scale, inked from pale umber to blood red, the current value pointed.
        var bar = new Rect2(x, p.Y + 58, right - x, 13);
        Box(bar, HudInk.Well);
        float X(float v) => bar.Position.X + bar.Size.X * Mathf.Log(Mathf.Clamp(v, 1, 10)) / Mathf.Log(10);
        float fillTo = X(Hud.ThreatValue);
        for (int i = 0; i < Threat.TierStart.Length; i++)
        {
            float a = X((float)Threat.TierStart[i]);
            float b = i + 1 < Threat.TierStart.Length ? X((float)Threat.TierStart[i + 1]) : bar.End.X;
            if (i == Threat.TierStart.Length - 1) a = Math.Min(a, bar.End.X - 6);
            // Pale ahead of her, full ink behind.
            Box(new Rect2(a, bar.Position.Y, b - a, bar.Size.Y), HudInk.Tier(i) with { A = 0.22f });
            float e = Math.Min(b, fillTo);
            if (e > a)
            {
                var col = HudInk.Tier(i);
                if (i == Hud.Tier && Hud.TierFlash > 0) col = col.Lerp(Ink.Red, 0.6f * Mathf.Abs(Mathf.Sin(Hud.Clock * 9)) * Hud.TierFlash);
                Box(new Rect2(a, bar.Position.Y, e - a, bar.Size.Y), col);
            }
            if (i > 0) Box(new Rect2(a - 0.6f, bar.Position.Y - 2, 1.2f, bar.Size.Y + 4), Ink.Black with { A = 0.8f });
        }
        Frame(bar, 1.3f, Ink.Black);
        var tip = new Vector2(Math.Clamp(fillTo, bar.Position.X + 3, bar.End.X - 3), bar.Position.Y - 1);
        SpriteAt(HudArt.Tri, tip + new Vector2(0, -5), new Vector2(11, 11), Mathf.Pi / 2, Ink.Black);
        Box(new Rect2(tip.X - 0.9f, bar.Position.Y, 1.8f, bar.Size.Y), Ink.Black);
    }

    static float Ease(float t) => t * t * (3 - 2 * t);

    // ---------------------------------------------------------------- the purse

    void DrawPurse()
    {
        var p = purseR.Position;
        float x = p.X + 16, right = purseR.End.X - 16;
        // The ship's name and hull.
        Txt(Fonts.Italic, 17, new Vector2(x, p.Y + 26), Hud.ShipName, Ink.Black);
        TxtR(Fonts.SmallCaps, 15, right, p.Y + 26, Hud.HullName, Parchment.Muted);
        Box(new Rect2(x, p.Y + 33, purseR.Size.X - 32, 1), Ink.Faint);
        // Gold.
        var gp = new Vector2(x + 15, p.Y + 55);
        if (Hud.GoldPulse > 0) SetBase(ScaleAbout(gp + new Vector2(40, 0), 1 + 0.22f * Ease(Hud.GoldPulse)));
        Icon("coin", new Rect2(gp - new Vector2(15, 15), new Vector2(30, 30)));
        Txt(Fonts.Display, 30, new Vector2(x + 36, p.Y + 66), Hud.GoldText, Ink.Black);
        float gw = TextW(Fonts.Display, 30, Hud.GoldText);
        Txt(Fonts.SmallCaps, 15, new Vector2(x + 42 + gw, p.Y + 65), Text.Get("HUD_GOLD_WORD"), Parchment.Muted);
        if (Hud.GoldPulse > 0) SetBase(Transform2D.Identity);
        // Hold and hands.
        float y = p.Y + 92;
        Icon("crate", new Rect2(x - 1, y - 16, 22, 22));
        Txt(Fonts.Body, 16, new Vector2(x + 26, y), Hud.HoldText, Ink.Black);
        float mid = x + (purseR.Size.X - 32) * 0.56f;
        Icon("sailor", new Rect2(mid - 1, y - 17, 22, 22));
        Txt(Fonts.Body, 16, new Vector2(mid + 25, y), Hud.CrewText, Ink.Black);
        // Stores: provisions, shot, timber (red when short).
        y = p.Y + 116;
        float sx = x;
        for (int i = 0; i < 3; i++)
        {
            var (icon, s, low) = i switch
            {
                0 => ("sack", Hud.ProvisionsText, Hud.ProvisionsLow),
                1 => ("shot", Hud.MunitionsText, Hud.MunitionsLow),
                _ => ("timber", Hud.TimberText, Hud.Timber == 0),
            };
            Icon(icon, new Rect2(sx - 1, y - 16, 21, 21));
            Txt(Fonts.Body, 16, new Vector2(sx + 24, y), s, low ? Ink.Red : Ink.Black);
            sx += 24 + TextW(Fonts.Body, 16, s) + 22;
        }
    }

    // ---------------------------------------------------------------- the helm

    void DrawHelm()
    {
        var p = helmR.Position;
        // Four little sails on their yards: furled, a third, two thirds, full. The order is inked solid on a paper
        // well; a bar beneath shows the canvas the crew has actually set.
        var pivot = new Vector2(p.X + 86, p.Y + 50);
        if (Hud.SailPulse > 0) SetBase(ScaleAbout(pivot, 1 + 0.14f * Ease(Hud.SailPulse)));
        float sx = p.X + 18, top = p.Y + 22, fullH = 50;
        for (int i = 0; i < 4; i++)
        {
            float cx = sx + i * 36 + 14;
            bool chosen = i == Hud.SailLevel;
            if (chosen) Box(new Rect2(cx - 17, top - 9, 34, fullH + 16), HudInk.Well with { A = 0.75f });
            float a = chosen ? 1f : 0.34f;
            Box(new Rect2(cx - 0.9f, top - 6, 1.8f, fullH + 10), Ink.Black with { A = 0.8f * a });     // mast
            if (i == 0)
            {
                var bundle = new Rect2(cx - 13, top + 1.5f, 26, 6);
                Box(bundle, Ink.Sail with { A = chosen ? 1 : 0.5f });
                Frame(bundle, 1.2f, Ink.Black with { A = a });
                for (int k = 0; k < 3; k++) Box(new Rect2(cx - 8 + k * 7, top + 1.5f, 1.2f, 6), Ink.Black with { A = 0.6f * a });
            }
            else
            {
                float h = fullH * SailHeights[i];
                var sr = new Rect2(cx - 15, top, 30, h + 2);
                Sprite(HudArt.SailFill, sr, (chosen ? Ink.Sail : Ink.Sail with { A = 0.55f }));
                Sprite(HudArt.SailLine, sr, Ink.Black with { A = a });
            }
            Box(new Rect2(cx - 15, top - 1.2f, 30, 2.4f), Ink.Black with { A = a });                  // yard
        }
        var bar = new Rect2(sx, top + fullH + 12, 4 * 36 - 8, 4);
        Box(bar, HudInk.Well);
        Box(new Rect2(bar.Position, new Vector2(bar.Size.X * Hud.SailFraction, bar.Size.Y)), HudInk.HullWood);
        Frame(bar.Grow(0.5f), 0.8f, Ink.Black with { A = 0.6f });
        if (Hud.SailPulse > 0) SetBase(Transform2D.Identity);
        Txt(Fonts.Body, 16, new Vector2(sx, p.Y + 104), Hud.SailName, Ink.Black);
        // Key caps: W more canvas, S less.
        float kx = sx + 4 * 36 - 2;
        float w1 = KeyCap(new Vector2(kx, top - 4), Hud.Key("SailUp"));
        SpriteAt(HudArt.Tri, new Vector2(kx + w1 + 8, top + 7), new Vector2(10, 10), -Mathf.Pi / 2, Ink.Black);
        float w2 = KeyCap(new Vector2(kx, top + 24), Hud.Key("SailDown"));
        SpriteAt(HudArt.Tri, new Vector2(kx + w2 + 8, top + 35), new Vector2(10, 10), Mathf.Pi / 2, Ink.Black);

        // Speed, point of sail and how well she is drawing.
        float x = p.X + 206;
        Box(new Rect2(x - 12, p.Y + 16, 1, helmR.Size.Y - 32), Ink.Faint);
        Txt(Fonts.Display, 30, new Vector2(x, p.Y + 46), Hud.SpeedText, Ink.Black);
        float sw = TextW(Fonts.Display, 30, Hud.SpeedText);
        Txt(Fonts.SmallCaps, 15, new Vector2(x + sw + 5, p.Y + 45), Text.Get("HUD_KNOTS"), Parchment.Muted);
        var pointInk = Hud.InIrons ? Ink.Red : Ink.Black;
        Txt(Fonts.Italic, 18, new Vector2(x, p.Y + 70), Hud.PointName, pointInk);
        var eff = new Rect2(x, p.Y + 78, helmR.End.X - 18 - x, 4);
        Box(eff, HudInk.Well);
        Box(new Rect2(eff.Position, new Vector2(eff.Size.X * Hud.Polar, eff.Size.Y)), Hud.InIrons ? Ink.Red : Ink.Wind);
        float hk = x;
        hk += KeyCap(new Vector2(hk, p.Y + 88), Hud.Key("Port"), 20) + 3;
        hk += KeyCap(new Vector2(hk, p.Y + 88), Hud.Key("Starboard"), 20) + 5;
        Txt(Fonts.Body, 15, new Vector2(hk, p.Y + 103), Text.Get("HUD_LEGEND_HELM"), Parchment.Muted);
    }

    // ---------------------------------------------------------------- the ship card

    void DrawShipCard()
    {
        // Hull and water: two drawn tubes. Hull drains from the top, water rises from the bottom; both go red
        // when she is in danger, and the frame beats while she is.
        bool hullBad = Hud.HullFrac < 0.3f, waterBad = Hud.WaterFrac > 0.7f;
        Tube(hullTube, Hud.HullFrac, hullBad ? Ink.Red : HudInk.HullWood, hullBad, null);
        Tube(waterTube, Hud.WaterFrac, waterBad ? Ink.Red : HudInk.Water, waterBad, Hud.StandWaterOnly && Hud.Stand ? 0.8f : null);
        TxtC(Fonts.SmallCaps, 15, hullTube.GetCenter().X, hullTube.End.Y + 17, Text.Get("HUD_GAUGE_HULL"), Ink.Black);
        TxtC(Fonts.SmallCaps, 15, waterTube.GetCenter().X, waterTube.End.Y + 17, Text.Get("HUD_GAUGE_WATER"), Ink.Black);
        TxtC(Fonts.Body, 15, hullTube.GetCenter().X, hullTube.Position.Y - 8, Hud.HullPct, hullBad ? Ink.Red : Ink.Black);
        TxtC(Fonts.Body, 15, waterTube.GetCenter().X, waterTube.Position.Y - 8, Hud.WaterPct, waterBad ? Ink.Red : Ink.Black);
        // Leaks: a drop each beside the water tube.
        int shown = Hud.Leaks > 6 ? 5 : Hud.Leaks;
        for (int i = 0; i < shown; i++)
            Sprite(HudArt.Drop, new Rect2(waterTube.End.X + 5, waterTube.End.Y - 15 - i * 13, 10, 13), HudInk.WaterDeep);
        if (Hud.Leaks > 6) Txt(Fonts.Body, 15, new Vector2(waterTube.End.X + 3, waterTube.End.Y - 15 - 5 * 13 + 12), "+" + Plus[Math.Min(Hud.Leaks - 5, Plus.Length - 1)], HudInk.WaterDeep);

        // The batteries: the ship in plan, bow up, her guns run out along each side. A loaded side is inked and its
        // key cap is ready; a reloading side fades and its bar fills as the crew works. Q is port (left), E starboard.
        var c = new Vector2(gunsR.GetCenter().X, gunsR.Position.Y + 52);
        var hs = new Vector2(96, 36);
        SpriteAt(HudArt.BigHullFill, c, hs, -Mathf.Pi / 2, Ink.Hull);
        SpriteAt(HudArt.Deck, c, hs, -Mathf.Pi / 2, Ink.Deck);
        SpriteAt(HudArt.BigHullLine, c, hs, -Mathf.Pi / 2, Ink.Black);
        Box(new Rect2(c.X - 0.7f, c.Y - 26, 1.4f, 46), Ink.Soft);   // keel line
        Battery(c, -1, Hud.Guns.PortGuns, Hud.Guns.PortLoaded, Hud.Guns.PortProgress, Hud.Key("FirePort"), Hud.ReadyPulse[0]);
        Battery(c, 1, Hud.Guns.StarboardGuns, Hud.Guns.StarboardLoaded, Hud.Guns.StarboardProgress, Hud.Key("FireStarboard"), Hud.ReadyPulse[1]);
        // Shot in the locker, under the stern.
        string shot = Hud.MunitionsText;
        float shw = TextW(Fonts.Body, 16, shot);
        Icon("shot", new Rect2(c.X - (22 + shw) / 2, gunsR.End.Y - 22, 20, 20));
        Txt(Fonts.Body, 16, new Vector2(c.X - (22 + shw) / 2 + 23, gunsR.End.Y - 6), shot, Hud.MunitionsLow ? Ink.Red : Ink.Black);

        // Crew stations under the order they sail by.
        float x = crewR.Position.X, y = crewR.Position.Y + 12;
        Box(new Rect2(x - 10, crewR.Position.Y + 2, 1, crewR.Size.Y - 4), Ink.Faint);
        Txt(Fonts.Italic, 16, new Vector2(x, y), Hud.OrderName, Ink.Black);
        for (int i = 0; i < 4; i++)
        {
            float ry = y + 6 + i * 20;
            Icon(StationIcons[i], new Rect2(x, ry, 19, 19));
            Txt(Fonts.Body, 16, new Vector2(x + 24, ry + 15), Hud.StationText[i], Ink.Black);
            Txt(Fonts.Body, 15, new Vector2(x + 40, ry + 15), Text.Get("HUD_STATION_" + i), Parchment.Muted);
        }
        float kx = x;
        kx += KeyCap(new Vector2(kx, crewR.End.Y - 22), Hud.Key("Order1") + "–" + Hud.Key("Order4"), 21) + 4;
        KeyCap(new Vector2(kx, crewR.End.Y - 22), Hud.Key("Crew"), 21);
    }

    static readonly string[] Plus = Enumerable.Range(0, 100).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
    static readonly string[] StationIcons = { "cannon", "sail", "repair", "pump" };
    static readonly float[] SailHeights = { 0f, 0.36f, 0.68f, 1f };

    void Tube(Rect2 r, float frac, Color fill, bool danger, float? mark)
    {
        Box(r, HudInk.Well);
        float h = r.Size.Y * frac;
        Box(new Rect2(r.Position.X, r.End.Y - h, r.Size.X, h), fill);
        if (h > 2) Box(new Rect2(r.Position.X, r.End.Y - h, r.Size.X, 1.6f), fill.Darkened(0.35f));
        for (int i = 1; i < 4; i++)
            Box(new Rect2(r.Position.X, r.Position.Y + r.Size.Y * i / 4, 5, 1.2f), Ink.Black);
        if (mark is { } m)
        {
            // The line to pump her below: paper-edged so it reads over any fill, with a pointer at its end.
            float my = r.End.Y - r.Size.Y * m;
            Box(new Rect2(r.Position.X - 3, my - 2, r.Size.X + 6, 4), Ink.Paper);
            Box(new Rect2(r.Position.X - 3, my - 1, r.Size.X + 6, 2), Ink.Black);
            SpriteAt(HudArt.Tri, new Vector2(r.End.X + 8, my), new Vector2(10, 10), Mathf.Pi, Ink.Black);
        }
        float beat = danger ? 0.5f + 0.5f * Mathf.Sin(Hud.Clock * 8) : 0;
        Frame(r.Grow(1), 1.6f, danger ? Ink.Black.Lerp(Ink.Red, beat) : Ink.Black);
    }

    void Battery(Vector2 c, int side, int guns, bool loaded, float progress, string key, float ready)
    {
        float span = 52, start = c.Y - 22;
        var ink = loaded ? Ink.Black : Ink.Black with { A = 0.3f };
        if (guns > 0)
        {
            float step = guns > 1 ? span / (guns - 1) : 0;
            for (int i = 0; i < guns; i++)
            {
                float y = guns > 1 ? start + i * step : c.Y;
                float hullHalf = 17f - Math.Abs(y - (c.Y + 2)) * 0.16f;
                float x0 = c.X + side * (hullHalf - 3);
                var barrel = side < 0 ? new Rect2(x0 - 10, y - 2, 10, 4) : new Rect2(x0, y - 2, 10, 4);
                Box(barrel, ink);
                Box(side < 0 ? new Rect2(barrel.Position.X - 1.5f, y - 2.8f, 2.5f, 5.6f) : new Rect2(barrel.End.X - 1, y - 2.8f, 2.5f, 5.6f), ink);
            }
        }
        // The side's key cap, and under it the reload: full and inked when she is ready to fire.
        float kw = KeyCapW(key);
        float kx = side < 0 ? c.X - 34 - kw : c.X + 34;
        if (ready > 0)
        {
            // Run out and ready: a warm flash behind the cap and a little bounce.
            var kc = new Vector2(kx + kw / 2, c.Y - 5);
            Sprite(HudArt.Glow, new Rect2(kc - new Vector2(26, 26), new Vector2(52, 52)), HudInk.Gold with { A = 0.9f * ready });
            SetBase(ScaleAbout(kc, 1 + 0.25f * Ease(ready)));
        }
        KeyCap(new Vector2(kx, c.Y - 16), key, 22, guns > 0 ? (loaded ? Ink.Black : Ink.Soft) : Ink.Faint);
        if (ready > 0) SetBase(Transform2D.Identity);
        var bar = new Rect2(kx, c.Y + 10, kw, 4);
        Box(bar, HudInk.Well);
        if (guns > 0) Box(new Rect2(bar.Position, new Vector2(bar.Size.X * (loaded ? 1 : progress), bar.Size.Y)), loaded ? Ink.Black : HudInk.Gold);
        Frame(bar.Grow(0.5f), 0.8f, Ink.Black with { A = 0.6f });
    }

    // ---------------------------------------------------------------- prompt, legend, notices, toasts

    void DrawPrompt()
    {
        if (!Hud.PromptVisible) return;
        var r = promptR;
        float x = r.Position.X + 18, cy = r.GetCenter().Y;
        if (Hud.PromptIcon.Length > 0)
        {
            Icon(Hud.PromptIcon, new Rect2(x - 2, cy - 14, 28, 28));
            x += 32;
        }
        if (Hud.PromptKey.Length > 0)
            x += KeyCap(new Vector2(x, cy - 13), Hud.PromptKey, 26) + 10;
        Txt(Fonts.Body, 20, new Vector2(x, cy + 7), Hud.PromptText, Hud.PromptColour);
        if (Hud.PromptProgress >= 0)
        {
            var bar = new Rect2(r.Position.X + 14, r.End.Y - 9, r.Size.X - 28, 3);
            Box(bar, HudInk.Well);
            Box(new Rect2(bar.Position, new Vector2(bar.Size.X * Hud.PromptProgress, bar.Size.Y)), Ink.Black);
        }
    }

    void DrawLegend()
    {
        float a = Hud.LegendAlpha;
        if (a <= 0.01f) return;
        if (a < 0.999f) SetBase(Transform2D.Identity);
        var soft = Parchment.Muted with { A = Parchment.Muted.A * a };
        for (int i = 0; i < legendItems.Count && i < legendAt.Count; i++)
        {
            var (keys, word) = legendItems[i];
            var (x, y, _) = legendAt[i];
            foreach (var k in keys)
                x += KeyCapFaded(new Vector2(x, y), k, a) + 3;
            Txt(Fonts.Body, 15, new Vector2(x + 2, y + 16), word, soft);
        }
    }

    float KeyCapFaded(Vector2 at, string label, float a)
    {
        const float h = 21;
        float tw = TextW(Fonts.Body, 15, label);
        float w = Math.Max(h, tw + 11);
        var r = new Rect2(at, new Vector2(w, h));
        KeyFace(r, a, false);
        Txt(Fonts.Body, 15, new Vector2(r.Position.X + (w - tw) / 2, r.Position.Y + h * 0.5f + 4.5f), label, Ink.Black with { A = a });
        return w;
    }

    void DrawNotice()
    {
        string text;
        Color ink, line, fill;
        float age, left;
        bool banner = Hud.BannerAge >= 0;
        if (banner)
        {
            text = Hud.BannerTier;
            ink = Ink.Red;
            line = Ink.Red;
            fill = HudInk.DangerWash;
            age = Hud.BannerAge;
            left = 4.2f - Hud.BannerAge;
        }
        else if (Hud.NoticeText.Length > 0)
        {
            text = Hud.NoticeText;
            (ink, line, fill) = Hud.NoticeKindNow switch
            {
                NoticeKind.Danger => (Ink.Red, Ink.Red, HudInk.DangerWash),
                NoticeKind.Good => (Ink.Black, HudInk.Gold, new Color(1f, 0.97f, 0.88f)),
                _ => (Ink.Black, Ink.Black, Colors.White),
            };
            age = Hud.NoticeAge;
            left = Hud.NoticeLeft;
        }
        else if (Hud.StateBanner.Length > 0)
        {
            text = Hud.StateBanner;
            ink = Ink.Red;
            line = Ink.Red;
            fill = HudInk.DangerWash;
            age = 1;
            left = 1;
        }
        else return;

        var font = banner ? Fonts.DisplayCaps : Fonts.Italic;
        int size = banner ? 28 : 19;
        float tw = TextW(font, size, text);
        // The screens' ribbon: the band carries the words, the swallow tails fold behind it.
        float top = noticeR.Position.Y + (banner ? 20 : 0);
        float h = banner ? 66 : 50;
        float tails = 66.6f * h / 64f;
        float w = Math.Min(view.X - 40, tw + 2 * tails + 28);
        float appear = Mathf.Clamp(age / 0.28f, 0, 1);
        float alpha = Mathf.Clamp(left / 0.6f, 0, 1) * Mathf.Clamp(age / 0.12f, 0, 1);
        var centre = new Vector2(view.X / 2, top + h / 2);
        // Unfurls from the middle with a small overshoot.
        float sx = 0.35f + 0.65f * Back(appear);
        SetBase(new Transform2D(0, new Vector2(sx, 1), 0, centre - centre * new Vector2(sx, 1)));
        var r = new Rect2(centre.X - w / 2, top, w, h);
        var tint = fill == HudInk.DangerWash ? new Color(1f, 0.86f, 0.80f) : Colors.White;
        float band = KitRibbon(r, tint with { A = alpha });
        TxtC(font, size, centre.X, band + size * 0.36f, text, ink with { A = alpha });
        if (banner)
            // "The sea grows meaner" over the ribbon, inked with a paper halo so it reads on any water.
            TxtC(Fonts.Italic, 17, centre.X, noticeR.Position.Y + 14, Text.Get("HUD_TIER_RISES"), Ink.Black with { A = alpha }, 5, Ink.Paper with { A = 0.9f * alpha });
        SetBase(Transform2D.Identity);
        _ = line;
    }

    static float Back(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1;
        return 1 + c3 * Mathf.Pow(t - 1, 3) + c1 * Mathf.Pow(t - 1, 2);
    }

    /// <summary>
    /// The screens' ribbon banner (swallow tails folded behind a band that stretches), cut where they cut it; a danger
    /// notice tints it rose. Returns the band's centre line for the lettering.
    /// </summary>
    float KitRibbon(Rect2 r, Color tint)
    {
        var src = HudArt.KitRibbon;
        const float c0 = 66.6f, c1 = 206.3f;
        float s = r.Size.Y / src.Size.Y;
        float left = c0 * s, right = (src.Size.X - c1) * s;
        float mid = Math.Max(0, r.Size.X - left - right);
        var p = src.Position;
        Sprite(new Rect2(p, new Vector2(c0, src.Size.Y)), new Rect2(r.Position, new Vector2(left, r.Size.Y)), tint);
        Sprite(new Rect2(p.X + c0, p.Y, c1 - c0, src.Size.Y), new Rect2(r.Position.X + left, r.Position.Y, mid, r.Size.Y), tint);
        Sprite(new Rect2(p.X + c1, p.Y, src.Size.X - c1, src.Size.Y), new Rect2(r.Position.X + left + mid, r.Position.Y, right, r.Size.Y), tint);
        return r.Position.Y + r.Size.Y * 0.40f;
    }

    /// <summary>The HUD's own ink ribbon (fallback when the kit is absent).</summary>
    void Ribbon(Rect2 r, Color fill, Color line)
    {
        float s = r.Size.Y / 64f;
        float tail = Math.Min(56 * s, 44);
        for (int k = 0; k < 2; k++)
        {
            var src = k == 0 ? HudArt.RibbonFill : HudArt.RibbonLine;
            var col = k == 0 ? fill : line;
            Sprite(new Rect2(src.Position, new Vector2(56, 64)), new Rect2(r.Position, new Vector2(tail, r.Size.Y)), col);
            Sprite(new Rect2(src.Position.X + 56, src.Position.Y, 400, 64), new Rect2(r.Position.X + tail, r.Position.Y, r.Size.X - 2 * tail, r.Size.Y), col);
            Sprite(new Rect2(src.Position.X + 456, src.Position.Y, 56, 64), new Rect2(r.End.X - tail, r.Position.Y, tail, r.Size.Y), col);
        }
    }

    // ---------------------------------------------------------------- the last stand

    void DrawStand()
    {
        if (!Hud.Stand) return;
        var r = standR;
        // The glass: sand runs from the upper bulb to the lower one as the stand runs out.
        var g = new Vector2(r.Position.X + 52, r.GetCenter().Y);
        float frac = Hud.HourglassMax > 0 ? Mathf.Clamp(Hud.Hourglass / Hud.HourglassMax, 0, 1) : 0;
        Hourglass(g, frac);
        float x = r.Position.X + 100;
        float beat = 1 + 0.10f * Mathf.Pow(1 - (Hud.Hourglass % 1f), 3);
        Txt(Fonts.DisplayCaps, 30, new Vector2(x, r.Position.Y + 44), Hud.StandTitle, Ink.Red);
        var sp = new Vector2(r.End.X - 30, r.Position.Y + 48);
        SetBase(ScaleAbout(sp + new Vector2(-24, -14), beat));
        TxtR(Fonts.Display, 44, sp.X, sp.Y, Hud.StandSeconds, Ink.Red);
        SetBase(Transform2D.Identity);
        Txt(Fonts.Body, 16, new Vector2(x, r.Position.Y + 72), Hud.StandLine, Ink.Black);
        // The nearest harbour that will take her in: which way, how far, and whether the glass allows it.
        var harbourInk = Hud.HarbourReachable ? HudInk.Safe : Ink.Red;
        Icon("anchor", new Rect2(x - 2, r.Position.Y + 84, 24, 24), Hud.HarbourReachable ? Colors.White : Colors.White with { A = 0.6f });
        float hx = x + 28;
        if (Hud.RescueHarbour != null)
        {
            var bearing = Ink.V(Hud.RescueHarbour.Harbor - Hud.World.Ship.Pos);
            if (bearing.LengthSquared() > 1)
                SpriteAt(HudArt.Arrow, new Vector2(hx + 11, r.Position.Y + 96), new Vector2(24, 11), bearing.Angle(), harbourInk);
            hx += 28;
        }
        Txt(Fonts.Body, 16, new Vector2(hx, r.Position.Y + 102), Hud.HarbourLine, harbourInk);
        if (Hud.StandWaterOnly)
        {
            // Pumping is the other way out: the order that sends every spare hand to the pumps.
            float kx = x;
            kx += KeyCap(new Vector2(kx, r.Position.Y + 114), Hud.Key("Order3")) + 8;
            Txt(Fonts.Body, 16, new Vector2(kx, r.Position.Y + 130), Text.Get("HUD_STAND_PUMP"), Ink.Black);
        }
    }

    void Hourglass(Vector2 c, float frac)
    {
        float hh = 44, hw = 24;
        var wood = HudInk.HullWood;
        Box(new Rect2(c.X - hw - 4, c.Y - hh - 5, hw * 2 + 8, 5), wood);
        Box(new Rect2(c.X - hw - 4, c.Y + hh, hw * 2 + 8, 5), wood);
        Box(new Rect2(c.X - hw - 1, c.Y - hh, 3, hh * 2), wood);
        Box(new Rect2(c.X + hw - 2, c.Y - hh, 3, hh * 2), wood);
        // Bulbs as stacked strips (narrow at the waist); sand fills the top by frac and the bottom by the rest.
        const int strips = 18;
        float sh = hh / strips;
        for (int i = 0; i < strips; i++)
        {
            float t = (i + 0.5f) / strips;             // 0 at the rim, 1 at the waist
            float half = (hw - 5) * (1 - 0.84f * t * t) + 1.5f;
            float yTop = c.Y - hh + i * sh, yBot = c.Y + hh - (i + 1) * sh;
            Box(new Rect2(c.X - half, yTop, half * 2, sh + 0.3f), new Color(0.93f, 0.95f, 0.95f, 0.8f));
            Box(new Rect2(c.X - half, yBot, half * 2, sh + 0.3f), new Color(0.93f, 0.95f, 0.95f, 0.8f));
            // Upper sand sits at the waist end; lower sand heaps from the base.
            if (i >= strips * (1 - frac)) Box(new Rect2(c.X - half, yTop, half * 2, sh + 0.3f), new Color(0.80f, 0.64f, 0.36f));
            if (i < strips * (1 - frac)) Box(new Rect2(c.X - half, yBot, half * 2, sh + 0.3f), new Color(0.80f, 0.64f, 0.36f));
        }
        if (frac > 0.01f)
            Box(new Rect2(c.X - 0.9f, c.Y - 2, 1.8f, hh * (1 - (1 - frac) * 0.7f)), new Color(0.72f, 0.56f, 0.30f));
    }

}

/// <summary>"SUNK · Day N" across the chart once she is gone; the rest of the HUD fades beneath it before the logbook.</summary>
public partial class SunkCanvas : InkCanvas
{
    public Hud Hud = null!;

    public override void _Draw()
    {
        if (Hud.World == null || !Hud.Over) return;
        var view = Size;
        float a = Mathf.Clamp(Hud.OverAge / 0.5f, 0, 1);
        float cx = view.X / 2, y = view.Y / 2 - 20;
        Sprite(HudArt.Glow, new Rect2(cx - 330, y - 110, 660, 190), Ink.Paper with { A = 0.85f * a });
        float w = TxtC(Fonts.DisplayCaps, 60, cx, y, Hud.OverText, Ink.Black with { A = a });
        Box(new Rect2(cx - w / 2, y + 16, w * a, 2.5f), Ink.Red with { A = a });
        FlushText();
    }
}

/// <summary>
/// What lies off the edge of the screen: hostile sails and beasts in sight (and, with a lookout aboard, those his
/// warning range reaches beyond it; GDD §7), plus the nearest harbour during a last stand. Each is a badge on the
/// screen's rim along the bearing from the ship, kept clear of the HUD's plates; badges that crowd merge into one.
/// </summary>
public partial class EdgeMarkers : InkCanvas
{
    public Hud Hud = null!;

    public readonly struct Marker
    {
        public readonly Vector2 At;
        public readonly string Label;
        public readonly int Kind;       // 0 ship, 1 beast, 2 harbour
        public readonly int Count;
        public readonly int ShipId;     // -1 for a beast or a harbour
        public readonly bool Reported;  // beyond sight: the lookout's warning
        public Marker(Vector2 at, string label, int kind, int count, int shipId, bool reported)
        {
            At = at; Label = label; Kind = kind; Count = count; ShipId = shipId; Reported = reported;
        }
    }

    struct Target
    {
        public Vector2 Screen, At;
        public double Distance;
        public int Kind;          // 0 ship, 1 beast, 2 harbour
        public bool Reported;     // beyond sight: the lookout's warning
        public float Angle;       // ship heading (glyph)
        public Color Faction;
        public string Name, Icon;
        public bool Merged;
        public int Count, ShipId;
    }

    readonly List<Target> targets = new();
    readonly List<Rect2> labelRects = new();
    readonly List<Rect2> badgeRects = new();
    readonly Dictionary<(string, int, long, bool), string> labelCache = new();
    readonly List<Marker> placed = new();
    public int Count => placed.Count;
    public IReadOnlyList<Marker> Placed => placed;
    const float Radius = 17;
    static readonly string[] Digits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    public override void _Draw()
    {
        placed.Clear();
        var world = Hud.World;
        if (world == null || world.RunOver) return;
        var size = Size;
        var xf = GetViewport().GetCanvasTransform();
        var origin = xf * Ink.V(world.Ship.Pos);
        var track = new Rect2(new Vector2(28, 28), size - new Vector2(56, 56));
        targets.Clear();
        double warn = world.VisionRadius + world.WarningRange;

        foreach (var other in world.Others)
        {
            if (other.Sunk || !world.Hostile(other, world.Ship)) continue;
            double d = other.Pos.DistanceTo(world.Ship.Pos);
            bool seen = world.PlayerSees(other.Pos, other.Hull.Length);
            bool reported = !seen && world.WarningRange > 0 && d <= warn;
            if (!seen && !reported) continue;
            var s = xf * Ink.V(other.Pos);
            // A ship in sight on the screen is drawn by the fleet; only one the lookout reports needs a mark there.
            if (seen && track.HasPoint(s) && !UnderPlate(s)) continue;   // on screen and in the open: the fleet draws her
            targets.Add(new Target
            {
                Screen = s, Distance = d, Kind = 0, Reported = reported, Angle = (float)other.Heading,
                Faction = Ink.Faction(other.Faction), Name = Text.Get("HULL_" + other.Hull.Id), Icon = "", Count = 1, ShipId = other.Id,
            });
        }
        if (world.Monster is { Done: false } m)
        {
            double d = m.Pos.DistanceTo(world.Ship.Pos);
            var cond = world.ConditionsAt(world.Ship.Pos);
            bool seen = m.Visible(cond) && world.PlayerSees(m.Pos, 30);
            bool reported = !seen && m.Surfaced && world.WarningRange > 0 && d <= warn;
            var s = xf * Ink.V(m.Pos);
            if ((seen || reported) && !(seen && track.HasPoint(s) && !UnderPlate(s)))
                targets.Add(new Target
                {
                    Screen = s, Distance = d, Kind = 1, Reported = reported, Name = Text.Get("MONSTER_" + m.Def.Key),
                    Icon = "m_" + m.Def.Key, Faction = Ink.Red, Count = 1, ShipId = -1,
                });
        }
        if (Hud.RescueHarbour is { } port)
        {
            var s = xf * Ink.V(port.Harbor);
            if (!track.HasPoint(s) || UnderPlate(s))
                targets.Add(new Target
                {
                    Screen = s, Distance = Math.Max(0, port.Harbor.DistanceTo(world.Ship.Pos) - port.RingRadius), Kind = 2,
                    Name = port.Name, Icon = "anchor", Faction = HudInk.Safe, Count = 1, ShipId = -1,
                });
        }
        if (targets.Count == 0) return;

        // Along the bearing to the rim, then out of the plates' way.
        var keep = Hud.KeepOut;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            var at = track.HasPoint(t.Screen) ? t.Screen : RimPoint(origin, t.Screen, track);
            t.At = Clear(at, track, keep);
            targets[i] = t;
        }
        // Crowded badges merge into the nearest one of them.
        // The refuge harbour first (its label matters most in a last stand), then nearest first.
        targets.Sort((a, b) => a.Kind == 2 != (b.Kind == 2) ? (a.Kind == 2 ? -1 : 1) : a.Distance.CompareTo(b.Distance));
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].Merged) continue;
            var a = targets[i];
            for (int j = i + 1; j < targets.Count; j++)
            {
                var b = targets[j];
                if (b.Merged || b.Kind == 2 || a.Kind == 2 || a.At.DistanceTo(b.At) > Radius * 2.2f) continue;
                b.Merged = true;
                targets[j] = b;
                a.Count++;
            }
            targets[i] = a;
        }

        var labels = labelRects;
        labels.Clear();
        // Every badge is an obstacle for every label (its own too, once a label is clamped to the screen's edge).
        badgeRects.Clear();
        foreach (var t in targets)
            if (!t.Merged) badgeRects.Add(new Rect2(t.At - new Vector2(Radius + 3, Radius + 3), new Vector2(2 * Radius + 6, 2 * Radius + 6)));
        Span<Rect2> order = stackalloc Rect2[4];
        foreach (var t in targets)
        {
            if (t.Merged) continue;
            var ring = t.Kind == 2 ? HudInk.Safe : Ink.Red;
            float alpha = t.Reported ? 0.72f : 1f;
            var bearing = (t.Screen - origin).Normalized();
            if (bearing == Vector2.Zero) bearing = Vector2.Right;
            // The pointer, just outside the badge toward the target.
            SpriteAt(HudArt.Tri, t.At + bearing * (Radius + 7), new Vector2(13, 13), bearing.Angle(), ring with { A = alpha });
            Disc(t.At, Radius, Ink.Paper with { A = 0.96f });
            RingAt(t.At, Radius, ring with { A = alpha }, thin: t.Reported);
            if (t.Kind == 0)
            {
                SpriteAt(HudArt.HullFill, t.At, new Vector2(26, 11.4f), t.Angle, t.Faction with { A = alpha });
                SpriteAt(HudArt.HullLine, t.At, new Vector2(26, 11.4f), t.Angle, Ink.Black with { A = alpha });
            }
            else
                Icon(t.Icon, new Rect2(t.At - new Vector2(14, 14), new Vector2(28, 28)), Colors.White with { A = alpha });
            if (t.Count > 1)
            {
                var bc = t.At + new Vector2(Radius * 0.72f, -Radius * 0.72f);
                Disc(bc, 8, ring);
                TxtC(Fonts.Body, 15, bc.X, bc.Y + 5, t.Count < 10 ? Digits[t.Count] : "+", Ink.Paper);
            }

            // The label goes on the side facing the middle of the screen, if it fits clear of the plates.
            // Distances in tens of metres: steady enough to read, and the labels are cached rather than rebuilt each frame.
            long d10 = (long)Math.Round(t.Distance / 10) * 10;
            var lk = (t.Name, t.Count, d10, t.Reported);
            if (!labelCache.TryGetValue(lk, out var label))
            {
                label = t.Count > 1 ? Text.Get("HUD_MARK_MORE", t.Name, t.Count - 1, d10) : Text.Get("HUD_MARK", t.Name, d10);
                if (t.Reported) label = Text.Get("HUD_MARK_REPORTED", label);
                if (labelCache.Count > 256) labelCache.Clear();
                labelCache[lk] = label;
            }
            float lw = TextW(Fonts.Body, 15, label);
            // Beside the badge toward the middle of the screen first, then the other sides; dropped if none is clear.
            var inward = size / 2 - t.At;
            bool horizontal = Math.Abs(inward.X) / size.X > Math.Abs(inward.Y) / size.Y;
            var right = new Rect2(t.At.X + Radius + 6, t.At.Y - 10, lw, 18);
            var left = new Rect2(t.At.X - Radius - 6 - lw, t.At.Y - 10, lw, 18);
            var below = new Rect2(t.At.X - lw / 2, t.At.Y + Radius + 4, lw, 18);
            var above = new Rect2(t.At.X - lw / 2, t.At.Y - Radius - 22, lw, 18);
            if (horizontal) { order[0] = inward.X > 0 ? right : left; order[1] = below; order[2] = above; order[3] = inward.X > 0 ? left : right; }
            else { order[0] = inward.Y > 0 ? below : above; order[1] = right; order[2] = left; order[3] = inward.Y > 0 ? above : below; }
            Rect2 lr = default;
            bool clear = false;
            for (int c = 0; c < 4 && !clear; c++)
            {
                lr = order[c];
                lr.Position = new Vector2(Math.Clamp(lr.Position.X, 4, size.X - lw - 4), Math.Clamp(lr.Position.Y, 4, size.Y - 22));
                clear = true;
                foreach (var k in keep) if (k.Intersects(lr)) { clear = false; break; }
                if (clear) foreach (var o in labels) if (o.Intersects(lr)) { clear = false; break; }
                if (clear) foreach (var o in badgeRects) if (o.Intersects(lr)) { clear = false; break; }
            }
            if (clear)
            {
                labels.Add(lr);
                Txt(Fonts.Body, 15, new Vector2(lr.Position.X, lr.Position.Y + 14), label, (t.Kind == 2 ? HudInk.Safe : Ink.Red) with { A = alpha }, 5, Ink.Paper with { A = 0.9f * alpha });
            }
            placed.Add(new Marker(t.At, clear ? label : "", t.Kind, t.Count, t.ShipId, t.Reported));
        }
        FlushText();
    }

    /// <summary>A point hidden under one of the HUD's plates (a ship there is off the chart as far as the eye goes).</summary>
    bool UnderPlate(Vector2 p)
    {
        foreach (var r in Hud.Plates) if (r.HasPoint(p)) return true;
        return false;
    }

    /// <summary>Where the ray from <paramref name="origin"/> toward <paramref name="p"/> leaves the track rectangle.</summary>
    static Vector2 RimPoint(Vector2 origin, Vector2 p, Rect2 track)
    {
        var o = new Vector2(Math.Clamp(origin.X, track.Position.X + 1, track.End.X - 1), Math.Clamp(origin.Y, track.Position.Y + 1, track.End.Y - 1));
        var d = p - o;
        float t = float.MaxValue;
        if (d.X > 1e-4f) t = Math.Min(t, (track.End.X - o.X) / d.X);
        if (d.X < -1e-4f) t = Math.Min(t, (track.Position.X - o.X) / d.X);
        if (d.Y > 1e-4f) t = Math.Min(t, (track.End.Y - o.Y) / d.Y);
        if (d.Y < -1e-4f) t = Math.Min(t, (track.Position.Y - o.Y) / d.Y);
        return t == float.MaxValue ? o : o + d * t;
    }

    /// <summary>
    /// The nearest spot to <paramref name="p"/> inside the track that no plate covers. Candidates are p itself, its
    /// projections onto every plate's edges and those projections' own projections (two plates side by side: the
    /// notice ribbon under the day plate, the prompt beside the legend), so a badge never lands on one plate while
    /// escaping another.
    /// </summary>
    static Vector2 Clear(Vector2 p, Rect2 track, IReadOnlyList<Rect2> keep)
    {
        bool Free(Vector2 q)
        {
            if (!track.Grow(1).HasPoint(q)) return false;
            foreach (var k in keep) if (k.Grow(Radius + 3).HasPoint(q)) return false;
            return true;
        }
        if (Free(p)) return p;
        var best = p;
        float bestD = float.MaxValue;
        void Try(Vector2 q)
        {
            float d = q.DistanceSquaredTo(p);
            if (d < bestD && Free(q)) { bestD = d; best = q; }
        }
        Span<Vector2> first = stackalloc Vector2[4];
        foreach (var k0 in keep)
        {
            var k = k0.Grow(Radius + 4);
            first[0] = new(k.Position.X, p.Y);
            first[1] = new(k.End.X, p.Y);
            first[2] = new(p.X, k.Position.Y);
            first[3] = new(p.X, k.End.Y);
            foreach (var q in first)
            {
                Try(q);
                foreach (var j0 in keep)
                {
                    var j = j0.Grow(Radius + 4);
                    if (!j.HasPoint(q)) continue;
                    Try(new Vector2(j.Position.X, q.Y));
                    Try(new Vector2(j.End.X, q.Y));
                    Try(new Vector2(q.X, j.Position.Y));
                    Try(new Vector2(q.X, j.End.Y));
                }
            }
        }
        return best;
    }
}
