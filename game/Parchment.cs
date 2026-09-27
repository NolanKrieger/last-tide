using System.Globalization;
using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The one UI theme: an antique chart's paper and ink. Built in code so every panel, button and slider matches.
/// Pieces come from <c>assets/art/ui</c> (the hand-inked kit made by <c>tools/art/ui_kit.py</c> plus Codex ornaments);
/// every piece has a plain drawn fallback, so the game runs without any of the art.
///
/// Type variations (set <see cref="Control.ThemeTypeVariation"/>):
/// Labels — <c>Title</c> (Display), <c>Big</c>, <c>Head</c> (small caps), <c>Caption</c>, <c>Flavour</c> (italic), <c>Warn</c>;
/// Buttons — <c>MenuItem</c> (title/pause menus), <c>RibbonTab</c>, <c>CastOff</c>, <c>KeyCap</c>, <c>Flat</c>, <c>CardButton</c>;
/// Panels — <c>Card</c>, <c>CardOn</c>, <c>Well</c>, <c>SheetPanel</c> (content margins of a <see cref="UiSheet"/>).
/// Keyboard focus is always a pair of red corner brackets, whatever the control.
/// </summary>
public static class Parchment
{
    static Theme? theme;

    /// <summary>Soft ink for secondary text: readable on the paper (the 50% ink of <see cref="Ink.Soft"/> is too faint for 15 px text).</summary>
    public static readonly Color Muted = new(0.16f, 0.13f, 0.10f, 0.72f);
    /// <summary>Sepia used for ledger rules and the aged rim.</summary>
    public static readonly Color Sepia = new(0.47f, 0.34f, 0.22f);
    /// <summary>The ochre wash behind a selected row or a hovered menu line.</summary>
    public static readonly Color Wash = new(0.84f, 0.69f, 0.41f, 0.38f);

    /// <summary>Keeps a control centred on its parent as its minimum size changes (a preset only fits the size at call time).</summary>
    public static void Centre(Control c)
    {
        c.AnchorLeft = c.AnchorRight = c.AnchorTop = c.AnchorBottom = 0.5f;
        c.OffsetLeft = c.OffsetRight = c.OffsetTop = c.OffsetBottom = 0;
        c.GrowHorizontal = Control.GrowDirection.Both;
        c.GrowVertical = Control.GrowDirection.Both;
    }

    /// <summary>A UI kit texture (<c>assets/art/ui/&lt;name&gt;.png</c>), or null.</summary>
    public static Texture2D? Tex(string name) => Art.Tex("ui/" + name);

    static StyleBox Nine(string name, int margin, int content, Color? fallbackFill = null, Color? fallbackBorder = null, bool center = true, int contentV = -1)
    {
        var tex = Tex(name);
        if (tex == null)
        {
            var f = new StyleBoxFlat { BgColor = fallbackFill ?? Ink.Paper, BorderColor = fallbackBorder ?? Ink.Black, DrawCenter = center };
            f.SetBorderWidthAll(1);
            f.SetContentMarginAll(content);
            if (contentV >= 0) { f.ContentMarginTop = f.ContentMarginBottom = contentV; }
            return f;
        }
        var b = new StyleBoxTexture { Texture = tex, DrawCenter = center };
        b.SetTextureMarginAll(margin);
        b.SetContentMarginAll(content);
        if (contentV >= 0) { b.ContentMarginTop = b.ContentMarginBottom = contentV; }
        return b;
    }

    static StyleBox Empty(float h, float v)
    {
        var e = new StyleBoxEmpty();
        e.ContentMarginLeft = e.ContentMarginRight = h;
        e.ContentMarginTop = e.ContentMarginBottom = v;
        return e;
    }

    /// <summary>The rim of a <see cref="UiSheet"/>: deckled, age-browned edge and a printed double rule (9-slice, open centre).</summary>
    public static StyleBox? RimBox { get; private set; }

    public static Theme Theme(Font font)
    {
        if (theme != null) return theme;
        var t = new Theme { DefaultFont = font, DefaultFontSize = 18 };

        // ---- Panels
        var card = Nine("card", 14, 14, Ink.Shade with { A = 0.3f });
        t.SetStylebox("panel", "PanelContainer", card);
        t.SetStylebox("panel", "Panel", card);
        t.SetTypeVariation("Card", "PanelContainer");
        t.SetStylebox("panel", "Card", Nine("card", 14, 12, Ink.Shade with { A = 0.3f }));
        t.SetTypeVariation("CardOn", "PanelContainer");
        t.SetStylebox("panel", "CardOn", Nine("card-on", 14, 12, Ink.Shade, Ink.Red));
        t.SetTypeVariation("Well", "PanelContainer");
        t.SetStylebox("panel", "Well", Nine("well", 12, 10, new Color(0.88f, 0.83f, 0.72f, 0.55f), Colors.Transparent));
        t.SetTypeVariation("SheetPanel", "PanelContainer");
        t.SetStylebox("panel", "SheetPanel", Empty(38, 34));
        t.SetTypeVariation("Bare", "PanelContainer");
        t.SetStylebox("panel", "Bare", Empty(0, 0));
        var rim = Tex("rim");
        if (rim != null)
        {
            var r = new StyleBoxTexture { Texture = rim, DrawCenter = false, AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile, AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile };
            r.SetTextureMarginAll(64);
            RimBox = r;
        }

        // ---- Labels
        t.SetColor("font_color", "Label", Ink.Black);
        Variation(t, "Title", "Label", Fonts.Display, 40, Ink.Black);
        Variation(t, "Big", "Label", Fonts.Display, 30, Ink.Black);
        Variation(t, "Head", "Label", Fonts.SmallCaps, 22, Ink.Black);
        Variation(t, "Caption", "Label", Fonts.SmallCaps, 16, Muted);
        Variation(t, "Flavour", "Label", Fonts.Italic, 17, Muted);
        Variation(t, "Warn", "Label", Fonts.Italic, 17, Ink.Red);
        Variation(t, "Data", "Label", Fonts.Body, 18, Ink.Black);

        // ---- Buttons: an inked label in a hand-ruled box
        var focus = Nine("focus", 12, 0, Colors.Transparent, Ink.Red, false);
        t.SetStylebox("normal", "Button", Nine("btn", 10, 12, contentV: 5));
        t.SetStylebox("hover", "Button", Nine("btn-hover", 10, 12, Ink.Shade, contentV: 5));
        t.SetStylebox("pressed", "Button", Nine("btn-press", 10, 12, Ink.Black, contentV: 5));
        t.SetStylebox("hover_pressed", "Button", Nine("btn-press", 10, 12, Ink.Black, contentV: 5));
        t.SetStylebox("disabled", "Button", Nine("btn-off", 10, 12, Ink.Paper, Ink.Faint, contentV: 5));
        t.SetStylebox("focus", "Button", focus);
        t.SetColor("font_color", "Button", Ink.Black);
        t.SetColor("font_hover_color", "Button", Ink.Black);
        t.SetColor("font_pressed_color", "Button", Ink.Paper);
        t.SetColor("font_hover_pressed_color", "Button", Ink.Paper);
        t.SetColor("font_focus_color", "Button", Ink.Black);
        t.SetColor("font_disabled_color", "Button", Ink.Soft);
        t.SetConstant("h_separation", "Button", 8);
        t.SetConstant("icon_max_width", "Button", 28);

        // Title and pause menus: bare lettering; a watercolour wash rises behind the line under the pointer.
        t.SetTypeVariation("MenuItem", "Button");
        t.SetFont("font", "MenuItem", Fonts.SmallCaps);
        t.SetFontSize("font_size", "MenuItem", 28);
        t.SetStylebox("normal", "MenuItem", Empty(26, 4));
        var wash = Tex("wash") != null ? (StyleBox)new StyleBoxTexture { Texture = Tex("wash"), TextureMarginLeft = 40, TextureMarginRight = 40, TextureMarginTop = 12, TextureMarginBottom = 12, ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 4, ContentMarginBottom = 4 } : Empty(26, 4);
        var washRed = Tex("wash-red") != null ? (StyleBox)new StyleBoxTexture { Texture = Tex("wash-red"), TextureMarginLeft = 40, TextureMarginRight = 40, TextureMarginTop = 12, TextureMarginBottom = 12, ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 4, ContentMarginBottom = 4 } : Empty(26, 4);
        t.SetStylebox("hover", "MenuItem", wash);
        t.SetStylebox("pressed", "MenuItem", washRed);
        t.SetStylebox("hover_pressed", "MenuItem", washRed);
        t.SetStylebox("disabled", "MenuItem", Empty(26, 4));
        t.SetColor("font_pressed_color", "MenuItem", Ink.Black);
        t.SetColor("font_hover_pressed_color", "MenuItem", Ink.Black);

        // Bookmark ribbons for the port's pages.
        t.SetTypeVariation("RibbonTab", "Button");
        t.SetFont("font", "RibbonTab", Fonts.SmallCaps);
        t.SetFontSize("font_size", "RibbonTab", 21);
        t.SetStylebox("normal", "RibbonTab", Ribbon("tab", Ink.Shade));
        t.SetStylebox("hover", "RibbonTab", Ribbon("tab-hover", Ink.Shade));
        t.SetStylebox("pressed", "RibbonTab", Ribbon("tab-on", Ink.Red));
        t.SetStylebox("hover_pressed", "RibbonTab", Ribbon("tab-on", Ink.Red));
        t.SetStylebox("disabled", "RibbonTab", Ribbon("tab", Ink.Shade));
        t.SetColor("font_pressed_color", "RibbonTab", Ink.Paper);
        t.SetColor("font_hover_pressed_color", "RibbonTab", Ink.Paper);
        t.SetColor("font_disabled_color", "RibbonTab", Ink.Soft);
        t.SetTypeVariation("CastOff", "Button");
        t.SetFont("font", "CastOff", Fonts.SmallCaps);
        t.SetFontSize("font_size", "CastOff", 21);
        t.SetStylebox("normal", "CastOff", Ribbon("tab-cast", Ink.Wind));
        t.SetStylebox("hover", "CastOff", Ribbon("tab-on", Ink.Red));
        t.SetStylebox("pressed", "CastOff", Ribbon("tab-on", Ink.Red));
        t.SetColor("font_color", "CastOff", Ink.Paper);
        t.SetColor("font_hover_color", "CastOff", Ink.Paper);
        t.SetColor("font_focus_color", "CastOff", Ink.Paper);

        // A key cap for the rebinding table.
        t.SetTypeVariation("KeyCap", "Button");
        t.SetFont("font", "KeyCap", Fonts.SmallCaps);
        t.SetFontSize("font_size", "KeyCap", 19);
        t.SetStylebox("normal", "KeyCap", Nine("key", 10, 10, contentV: 4));
        t.SetStylebox("hover", "KeyCap", Nine("key-hot", 10, 10, contentV: 4));
        t.SetStylebox("pressed", "KeyCap", Nine("key-hot", 10, 10, contentV: 4));
        t.SetStylebox("hover_pressed", "KeyCap", Nine("key-hot", 10, 10, contentV: 4));
        t.SetColor("font_pressed_color", "KeyCap", Ink.Red);
        t.SetColor("font_hover_pressed_color", "KeyCap", Ink.Red);

        // Small actions: lettering only until hovered.
        t.SetTypeVariation("Flat", "Button");
        t.SetStylebox("normal", "Flat", Empty(10, 3));
        t.SetStylebox("hover", "Flat", wash);
        t.SetStylebox("pressed", "Flat", washRed);
        t.SetStylebox("hover_pressed", "Flat", washRed);
        t.SetStylebox("disabled", "Flat", Empty(10, 3));
        t.SetColor("font_pressed_color", "Flat", Ink.Black);
        t.SetColor("font_hover_pressed_color", "Flat", Ink.Black);

        // A whole card as a toggle (the voyage page's presets).
        t.SetTypeVariation("CardButton", "Button");
        t.SetStylebox("normal", "CardButton", Nine("card", 14, 10, Ink.Shade with { A = 0.3f }));
        t.SetStylebox("hover", "CardButton", Nine("card-hover", 14, 10, Ink.Shade));
        t.SetStylebox("pressed", "CardButton", Nine("card-on", 14, 10, Ink.Shade, Ink.Red));
        t.SetStylebox("hover_pressed", "CardButton", Nine("card-on", 14, 10, Ink.Shade, Ink.Red));
        t.SetColor("font_pressed_color", "CardButton", Ink.Black);
        t.SetColor("font_hover_pressed_color", "CardButton", Ink.Black);

        // ---- Check boxes: an inked square, ticked in red
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            t.SetStylebox("normal", type, Empty(6, 3));
            t.SetStylebox("pressed", type, Empty(6, 3));
            t.SetStylebox("hover", type, wash);
            t.SetStylebox("hover_pressed", type, wash);
            t.SetStylebox("disabled", type, Empty(6, 3));
            t.SetStylebox("focus", type, focus);
            t.SetColor("font_color", type, Ink.Black);
            t.SetColor("font_hover_color", type, Ink.Black);
            t.SetColor("font_pressed_color", type, Ink.Black);
            t.SetColor("font_hover_pressed_color", type, Ink.Black);
            t.SetColor("font_focus_color", type, Ink.Black);
            t.SetConstant("h_separation", type, 10);
        }
        Icon(t, "unchecked", "CheckBox", "check-off");
        Icon(t, "checked", "CheckBox", "check-on");
        Icon(t, "unchecked_disabled", "CheckBox", "check-off-dis");
        Icon(t, "checked_disabled", "CheckBox", "check-on-dis");

        // ---- Sliders: a ruled line with a wax-seal grabber
        var track = Tex("slider") != null ? (StyleBox)new StyleBoxTexture { Texture = Tex("slider"), TextureMarginLeft = 4, TextureMarginRight = 4, ContentMarginTop = 7, ContentMarginBottom = 7, AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile } : Line(Ink.Black);
        var fill = Tex("slider-fill") != null ? (StyleBox)new StyleBoxTexture { Texture = Tex("slider-fill"), TextureMarginLeft = 4, TextureMarginRight = 4, ContentMarginTop = 7, ContentMarginBottom = 7, AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile } : Line(Sepia);
        var fillHi = Tex("slider-fill-hi") != null ? (StyleBox)new StyleBoxTexture { Texture = Tex("slider-fill-hi"), TextureMarginLeft = 4, TextureMarginRight = 4, ContentMarginTop = 7, ContentMarginBottom = 7, AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile } : Line(Ink.Red);
        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", fill);
        t.SetStylebox("grabber_area_highlight", "HSlider", fillHi);
        t.SetStylebox("focus", "HSlider", focus);
        Icon(t, "grabber", "HSlider", "grabber");
        Icon(t, "grabber_highlight", "HSlider", "grabber-hi");
        Icon(t, "grabber_disabled", "HSlider", "grabber-off");

        // ---- Scroll bars: a hairline with a slim ink thumb
        foreach (var (type, vertical) in new[] { ("VScrollBar", true), ("HScrollBar", false) })
        {
            var line = new StyleBoxLine { Color = Ink.Faint, Thickness = 1, Vertical = vertical };
            if (vertical) { line.ContentMarginLeft = line.ContentMarginRight = 5; } else { line.ContentMarginTop = line.ContentMarginBottom = 5; }
            t.SetStylebox("scroll", type, line);
            t.SetStylebox("scroll_focus", type, line);
            t.SetStylebox("grabber", type, Thumb(Ink.Soft, vertical));
            t.SetStylebox("grabber_highlight", type, Thumb(Ink.Black, vertical));
            t.SetStylebox("grabber_pressed", type, Thumb(Ink.Red, vertical));
        }
        t.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        t.SetStylebox("focus", "ScrollContainer", new StyleBoxEmpty());

        // ---- LineEdit: a ruled writing line, lettered in italic like a hand entry
        var ruled = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.12f), BorderColor = Ink.Black with { A = 0.8f }, BorderWidthBottom = 2 };
        ruled.ContentMarginLeft = ruled.ContentMarginRight = 8; ruled.ContentMarginTop = 4; ruled.ContentMarginBottom = 6;
        var ruledFocus = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = Ink.Red, BorderWidthBottom = 3, DrawCenter = false };
        ruledFocus.SetContentMarginAll(0);
        t.SetStylebox("normal", "LineEdit", ruled);
        t.SetStylebox("read_only", "LineEdit", ruled);
        t.SetStylebox("focus", "LineEdit", ruledFocus);
        t.SetFont("font", "LineEdit", Fonts.Italic);
        t.SetFontSize("font_size", "LineEdit", 22);
        t.SetColor("font_color", "LineEdit", Ink.Black);
        t.SetColor("font_placeholder_color", "LineEdit", Ink.Soft);
        t.SetColor("caret_color", "LineEdit", Ink.Red);
        t.SetColor("selection_color", "LineEdit", Wash);

        // ---- Tooltips
        t.SetStylebox("panel", "TooltipPanel", Nine("card", 14, 10, Ink.Paper));
        t.SetColor("font_color", "TooltipLabel", Ink.Black);
        t.SetFont("font", "TooltipLabel", Fonts.Italic);
        t.SetFontSize("font_size", "TooltipLabel", 17);

        theme = t;
        return t;
    }

    static void Variation(Theme t, string name, string baseType, Font font, int size, Color colour)
    {
        t.SetTypeVariation(name, baseType);
        t.SetFont("font", name, font);
        t.SetFontSize("font_size", name, size);
        t.SetColor("font_color", name, colour);
    }

    static void Icon(Theme t, string name, string type, string tex)
    {
        var x = Tex(tex);
        if (x != null) t.SetIcon(name, type, x);
    }

    static StyleBox Ribbon(string tex, Color fallback)
    {
        var x = Tex(tex);
        if (x == null)
        {
            var f = new StyleBoxFlat { BgColor = fallback, BorderColor = Ink.Black };
            f.SetBorderWidthAll(1);
            f.ContentMarginLeft = f.ContentMarginRight = 26;
            f.ContentMarginTop = 5; f.ContentMarginBottom = 7;
            return f;
        }
        var b = new StyleBoxTexture { Texture = x, TextureMarginLeft = 24, TextureMarginRight = 24, TextureMarginTop = 8, TextureMarginBottom = 8 };
        b.ContentMarginLeft = b.ContentMarginRight = 30;
        b.ContentMarginTop = 5; b.ContentMarginBottom = 7;
        return b;
    }

    static StyleBoxLine Line(Color c) => new() { Color = c, Thickness = 2, ContentMarginTop = 4, ContentMarginBottom = 4 };

    static StyleBoxFlat Thumb(Color c, bool vertical)
    {
        var b = new StyleBoxFlat { BgColor = c };
        b.SetCornerRadiusAll(3);
        if (vertical) { b.ContentMarginLeft = b.ContentMarginRight = 3; b.ContentMarginTop = b.ContentMarginBottom = 10; }
        else { b.ContentMarginTop = b.ContentMarginBottom = 3; b.ContentMarginLeft = b.ContentMarginRight = 10; }
        return b;
    }

    // ---------------------------------------------------------------- factories

    /// <summary>A label in one of the theme's variations. Labels never take the mouse.</summary>
    public static Label L(string text, string style = "", HorizontalAlignment align = HorizontalAlignment.Left)
        => new() { Text = text, ThemeTypeVariation = style, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A button with the UI click sound.</summary>
    public static Button B(string text, Action onPress, string style = "", int fontSize = 0)
    {
        var b = new Button { Text = text, ThemeTypeVariation = style };
        if (fontSize > 0) b.AddThemeFontSizeOverride("font_size", fontSize);
        b.Pressed += () => { Audio.Ui(); onPress(); };
        return b;
    }

    /// <summary>A texture shown at a fixed size, kept in proportion.</summary>
    public static TextureRect Picture(Texture2D? tex, float w, float h, Color? modulate = null) => new()
    {
        Texture = tex,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(w, h),
        TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        SelfModulate = modulate ?? Colors.White,
    };

    public static HBoxContainer Row(int separation = 8, params Control[] children)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", separation);
        foreach (var c in children) r.AddChild(c);
        return r;
    }

    public static VBoxContainer Column(int separation = 8, params Control[] children)
    {
        var c = new VBoxContainer();
        c.AddThemeConstantOverride("separation", separation);
        foreach (var ch in children) c.AddChild(ch);
        return c;
    }

    /// <summary>A full-screen wash behind a menu: warm ink, darker toward the edges.</summary>
    public static ColorRect Backdrop(float centre, float edge, Color? tint = null)
    {
        var r = SoftWash(centre, edge, tint);
        r.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return r;
    }

    /// <summary>A soft elliptical wash (the backdrop's shader) for any rectangle: e.g. paper pooled behind the title's menu.</summary>
    public static ColorRect SoftWash(float centre, float edge, Color? tint = null, float power = 1.8f)
    {
        var r = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Color = Colors.White };
        var shader = GD.Load<Shader>("res://assets/shaders/ui_vignette.gdshader");
        if (shader == null) { r.Color = (tint ?? Ink.Black) with { A = centre }; return r; }
        var m = new ShaderMaterial { Shader = shader };
        m.SetShaderParameter("tint", tint ?? Ink.Black);
        m.SetShaderParameter("centre", centre);
        m.SetShaderParameter("edge", edge);
        m.SetShaderParameter("power", power);
        m.SetShaderParameter("reach", edge < centre ? 2.0f : 1.5f);
        r.Material = m;
        return r;
    }

    // ---------------------------------------------------------------- icons (atlases batch into one draw call)

    static readonly Dictionary<string, AtlasTexture?> cells = new();

    static AtlasTexture? Cell(string atlas, int index, int cols, int cell)
    {
        string key = atlas + "#" + index;
        if (cells.TryGetValue(key, out var c)) return c;
        var tex = Art.Tex(atlas);
        c = tex == null ? null : new AtlasTexture { Atlas = tex, Region = new Rect2(index % cols * cell, index / cols * cell, cell, cell), FilterClip = true };
        cells[key] = c;
        return c;
    }

    /// <summary>The good's illustration (27 in <c>goods/goods.png</c>, 8 × 128 px cells in enum order).</summary>
    public static Texture2D? GoodIcon(Good g) => Cell("goods/goods", (int)g, 8, 128);
    /// <summary>The part's illustration (9 in <c>parts/parts.png</c>, 3 × 128 px cells in enum order).</summary>
    public static Texture2D? PartIcon(Part p) => Cell("parts/parts", (int)p, 3, 128);
    /// <summary>An achievement medallion; a faded grey copy while it is still locked.</summary>
    public static Texture2D? AchievementIcon(string key, bool unlocked)
    {
        int i = Array.IndexOf(Achievements.All, key);
        return i < 0 ? null : Cell(unlocked ? "ui/achievements" : "ui/achievements-locked", i, 6, 128);
    }
    /// <summary>The faction's crest, drawn in black ink only (tint it with <see cref="Ink.Faction"/>).</summary>
    public static Texture2D? Crest(Faction f, bool cove) => Tex(cove ? "crest-cove" : f switch { Faction.Crown => "crest-crown", Faction.Brethren => "crest-pennant", _ => "crest-anchor" });
    public static Texture2D? Portrait(string who) => Art.Tex("people/" + who);

    // ---------------------------------------------------------------- words and numbers

    public static string N(double v, string fmt = "0") => v.ToString(fmt, CultureInfo.InvariantCulture);
    /// <summary>A score: days survived to the hundredth ("0.42", "12.40").</summary>
    public static string Days(double days) => days.ToString("0.00", CultureInfo.InvariantCulture);
    /// <summary>"Day 3 · forenoon watch" for a days-survived value (the HUD's own wording).</summary>
    public static string DayWatch(double days)
    {
        int day = 1 + (int)Math.Floor(days);
        double hour = (Tuning.DawnHour + (days - Math.Floor(days)) * 24) % 24;
        int watch = (int)(hour / 4) % 6;
        return Text.Get("HUD_DAY", day, Text.Get("WATCH_" + watch));
    }
}

/// <summary>
/// A sheet of the chart's paper: tiled paper, a deckled and age-browned rim with a printed double rule, and (when
/// <see cref="Ornate"/>) a scrollwork flourish in each corner. Children sit inside the rule.
/// </summary>
public partial class UiSheet : PanelContainer
{
    public bool Ornate = true;
    public bool Aged;
    /// <summary>Draw a red double margin rule this far from the left edge (a logbook page), 0 for none.</summary>
    public float Margin;
    /// <summary>Faint ruled lines every <see cref="RulePitch"/> px from <see cref="RuleTop"/> (a logbook page), 0 for none.</summary>
    public float RulePitch, RuleTop;

    public UiSheet()
    {
        ThemeTypeVariation = "SheetPanel";
        TextureRepeat = TextureRepeatEnum.Enabled;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Draw() => DrawSheet(this, new Rect2(Vector2.Zero, Size), Ornate, Aged, Margin, RulePitch, RuleTop);

    /// <summary>Draws a sheet into any canvas item (the chart and the logbook reuse it). The item needs texture repeat enabled.</summary>
    public static void DrawSheet(CanvasItem c, Rect2 r, bool ornate, bool aged, float margin = 0, float rulePitch = 0, float ruleTop = 0)
    {
        var paper = Parchment.Tex(aged ? "paper-aged" : "paper");
        var inner = r.Grow(-7);
        if (paper != null) c.DrawTextureRect(paper, inner, true);
        else c.DrawRect(inner, aged ? Ink.Shade : Ink.Paper);
        if (rulePitch > 0)
        {
            var lines = new List<Vector2>();
            for (float y = r.Position.Y + ruleTop; y < r.End.Y - 30; y += rulePitch)
            {
                lines.Add(new Vector2(r.Position.X + 30, y));
                lines.Add(new Vector2(r.End.X - 30, y));
            }
            if (lines.Count > 0) c.DrawMultiline(lines.ToArray(), new Color(0.35f, 0.45f, 0.6f, 0.22f), 1f);
        }
        if (margin > 0)
        {
            var red = Ink.Red with { A = 0.55f };
            c.DrawLine(new Vector2(r.Position.X + margin, r.Position.Y + 26), new Vector2(r.Position.X + margin, r.End.Y - 26), red, 1.2f);
            c.DrawLine(new Vector2(r.Position.X + margin + 4, r.Position.Y + 26), new Vector2(r.Position.X + margin + 4, r.End.Y - 26), red, 1.2f);
        }
        if (Parchment.RimBox != null) c.DrawStyleBox(Parchment.RimBox, r);
        else
        {
            c.DrawRect(r.Grow(-18), Ink.Black, false, 2f);
            c.DrawRect(r.Grow(-24), Ink.Soft, false, 1f);
        }
        if (!ornate) return;
        var corner = Parchment.Tex("corner");
        if (corner == null) return;
        float s = Mathf.Clamp(Mathf.Min(r.Size.X, r.Size.Y) * 0.13f, 40f, 84f);
        float o = 11;   // the flourish's corner sits on the printed rule
        var sz = new Vector2(s, s * corner.GetHeight() / corner.GetWidth());
        // A negative size flips the texture in place (the rect still runs from its position to position + |size|).
        c.DrawTextureRect(corner, new Rect2(r.Position + new Vector2(o, o), sz), false);
        c.DrawTextureRect(corner, new Rect2(new Vector2(r.End.X - o - sz.X, r.Position.Y + o), new Vector2(-sz.X, sz.Y)), false);
        c.DrawTextureRect(corner, new Rect2(new Vector2(r.Position.X + o, r.End.Y - o - sz.Y), new Vector2(sz.X, -sz.Y)), false);
        c.DrawTextureRect(corner, new Rect2(r.End - new Vector2(o, o) - sz, -sz), false);
    }
}

/// <summary>
/// The engraved title plate (Codex cartouche): scrolled ends stay true, two ribbon bands stretch for a long title,
/// and the lettering shrinks before the plate would outgrow <see cref="MaxWidth"/>.
/// </summary>
public partial class UiCartouche : Control
{
    // Geometry of assets/art/ui/cartouche.png (1024 × 444): the blank panel, and the two bands that may stretch.
    const float TexW = 1024, TexH = 444, InnerX0 = 150, InnerX1 = 874, InnerY0 = 139, InnerY1 = 328;
    static readonly float[] Cuts = { 0, 300, 340, 684, 724, 1024 };

    string title = "";
    public Font TitleFont = Fonts.Display;
    public int FontSize = 40;
    public float PlateHeight = 120;
    public float MaxWidth = 900;
    public Color Colour = Ink.Black;

    public string Title { get => title; set { title = value; UpdateMinimumSize(); QueueRedraw(); } }

    public UiCartouche()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    (float Scale, float Extra, int Size, float Width) Fit()
    {
        float s = PlateHeight / TexH;
        float nativeInner = (InnerX1 - InnerX0) * s;
        int size = FontSize;
        float innerH = (InnerY1 - InnerY0) * s;
        size = Math.Min(size, (int)(innerH * 0.78f));
        float need = 0;
        for (; size > 12; size -= 2)
        {
            need = TitleFont.GetStringSize(title, HorizontalAlignment.Left, -1, size).X + 28 * s + 16;
            float extra0 = Math.Max(0, need - nativeInner);
            if (TexW * s + extra0 <= MaxWidth && extra0 <= 2 * (Cuts[2] - Cuts[1]) * s * 2.2f) break;
        }
        float extra = Math.Max(0, need - nativeInner);
        return (s, extra, size, TexW * s + extra);
    }

    public override Vector2 _GetMinimumSize() => new(Fit().Width, PlateHeight);

    public override void _Draw()
    {
        var (s, extra, size, width) = Fit();
        float x0 = (Size.X - width) / 2, y0 = (Size.Y - PlateHeight) / 2;
        var tex = Parchment.Tex("cartouche");
        float innerL, innerR;
        if (tex != null)
        {
            float x = x0;
            for (int i = 0; i < 5; i++)
            {
                float src = Cuts[i + 1] - Cuts[i];
                float w = src * s + (i is 1 or 3 ? extra / 2 : 0);
                DrawTextureRectRegion(tex, new Rect2(x, y0, w, PlateHeight), new Rect2(Cuts[i], 0, src, TexH));
                x += w;
            }
            innerL = x0 + InnerX0 * s;
            innerR = x0 + InnerX1 * s + extra;
        }
        else
        {
            var box = new Rect2(x0, y0 + PlateHeight * 0.2f, width, PlateHeight * 0.6f);
            DrawRect(box, Ink.Paper);
            DrawRect(box, Ink.Black, false, 2f);
            DrawRect(box.Grow(-4), Ink.Soft, false, 1f);
            innerL = box.Position.X; innerR = box.End.X;
        }
        float cy = y0 + (InnerY0 + InnerY1) / 2 * s;
        float asc = TitleFont.GetAscent(size), desc = TitleFont.GetDescent(size);
        DrawString(TitleFont, new Vector2(innerL, cy + (asc - desc) / 2), title, HorizontalAlignment.Center, innerR - innerL, size, Colour);
    }
}

/// <summary>A swallow-tailed ribbon banner (Codex) with a line lettered across it; only the plain middle band stretches.</summary>
public partial class UiBanner : Control
{
    const float TexW = 768, TexH = 183, Cut0 = 190, Cut1 = 589, TextY = 0.40f;
    string text = "";
    public Font TextFont = Fonts.Italic;
    public int FontSize = 22;
    public float Height = 64;
    public Color Colour = Ink.Black;
    public string Line { get => text; set { text = value; UpdateMinimumSize(); QueueRedraw(); } }

    public UiBanner()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    float Width
    {
        get
        {
            float s = Height / TexH;
            float mid = Math.Max((Cut1 - Cut0) * s, TextFont.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X + 24);
            return Cut0 * s + mid + (TexW - Cut1) * s;
        }
    }

    public override Vector2 _GetMinimumSize() => new(Width, Height);

    public override void _Draw()
    {
        float s = Height / TexH, w = Width;
        float x0 = (Size.X - w) / 2, y0 = (Size.Y - Height) / 2;
        float mid = w - Cut0 * s - (TexW - Cut1) * s;
        var tex = Parchment.Tex("ribbon");
        if (tex != null)
        {
            DrawTextureRectRegion(tex, new Rect2(x0, y0, Cut0 * s, Height), new Rect2(0, 0, Cut0, TexH));
            DrawTextureRectRegion(tex, new Rect2(x0 + Cut0 * s, y0, mid, Height), new Rect2(Cut0, 0, Cut1 - Cut0, TexH));
            DrawTextureRectRegion(tex, new Rect2(x0 + Cut0 * s + mid, y0, (TexW - Cut1) * s, Height), new Rect2(Cut1, 0, TexW - Cut1, TexH));
        }
        else DrawRect(new Rect2(x0, y0 + Height * 0.15f, w, Height * 0.6f), Ink.Shade);
        float cy = y0 + Height * TextY;
        float asc = TextFont.GetAscent(FontSize), desc = TextFont.GetDescent(FontSize);
        DrawString(TextFont, new Vector2(x0 + Cut0 * s, cy + (asc - desc) / 2), text, HorizontalAlignment.Center, mid, FontSize, Colour);
    }
}

/// <summary>A printed rule across its width; with <see cref="Ornament"/>, a fleuron sits in the middle.</summary>
public partial class UiDivider : Control
{
    public bool Ornament;
    public Color Colour = Ink.Black with { A = 0.75f };
    public UiDivider() : this(false) { }
    public UiDivider(bool ornament, float height = 0)
    {
        Ornament = ornament;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(0, height > 0 ? height : ornament ? 26 : 8);
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Draw()
    {
        float y = Size.Y / 2;
        var fl = Ornament ? Parchment.Tex("fleuron") : null;
        if (fl == null)
        {
            DrawLine(new Vector2(0, y - 1.5f), new Vector2(Size.X, y - 1.5f), Colour, 1.4f);
            DrawLine(new Vector2(0, y + 1.5f), new Vector2(Size.X, y + 1.5f), Colour with { A = Colour.A * 0.6f }, 0.8f);
            return;
        }
        float h = Size.Y, w = h * fl.GetWidth() / fl.GetHeight();
        float cx = Size.X / 2;
        DrawLine(new Vector2(0, y), new Vector2(cx - w / 2 - 6, y), Colour, 1.3f);
        DrawLine(new Vector2(cx + w / 2 + 6, y), new Vector2(Size.X, y), Colour, 1.3f);
        DrawTextureRect(fl, new Rect2(cx - w / 2, 0, w, h), false);
    }
}

/// <summary>Grade pips: <see cref="Count"/> of <see cref="Max"/> filled, drawn from two small textures (one batch).</summary>
public partial class UiPips : Control
{
    int count, max = 5;
    public int Count { get => count; set { count = value; QueueRedraw(); } }
    public int Max { get => max; set { max = value; UpdateMinimumSize(); QueueRedraw(); } }
    public float Pip = 16;

    public UiPips() { MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.LinearWithMipmaps; }
    public override Vector2 _GetMinimumSize() => new(max * (Pip + 3), Pip);

    public override void _Draw()
    {
        var on = Parchment.Tex("pip-on");
        var off = Parchment.Tex("pip-off");
        float y = (Size.Y - Pip) / 2;
        for (int i = 0; i < max; i++)
        {
            var r = new Rect2(i * (Pip + 3), y, Pip, Pip);
            var tex = i < count ? on : off;
            if (tex != null) DrawTextureRect(tex, r, false);
            else if (i < count) DrawCircle(r.GetCenter(), Pip * 0.4f, Ink.Red);
            else DrawArc(r.GetCenter(), Pip * 0.4f, 0, Mathf.Tau, 12, Ink.Black, 1.2f);
        }
    }
}

/// <summary>A ruled bar: an ink box filled to <see cref="Value"/>/<see cref="Max"/>, with optional ticks.</summary>
public partial class UiGauge : Control
{
    double value, maxValue = 1;
    public Color Fill = Parchment.Sepia;
    public Color Back = Ink.Shade with { A = 0.5f };
    public int Ticks;
    public double Value { get => value; set { this.value = value; QueueRedraw(); } }
    public double Max { get => maxValue; set { maxValue = value; QueueRedraw(); } }

    public UiGauge() : this(160, 12) { }
    public UiGauge(float w, float h) { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(w, h); }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Back);
        float f = maxValue <= 0 ? 0 : (float)Math.Clamp(value / maxValue, 0, 1);
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * f, r.Size.Y)), Fill);
        if (Ticks > 1)
        {
            var pts = new Vector2[(Ticks - 1) * 2];
            for (int i = 1; i < Ticks; i++)
            {
                float x = r.Size.X * i / Ticks;
                pts[(i - 1) * 2] = new Vector2(x, r.Size.Y * 0.55f);
                pts[(i - 1) * 2 + 1] = new Vector2(x, r.Size.Y);
            }
            DrawMultiline(pts, Ink.Black with { A = 0.45f }, 1f);
        }
        DrawRect(r, Ink.Black, false, 1.4f);
    }
}

/// <summary>
/// A row of little sailors: <see cref="Count"/> hands drawn in ink and colour, then <see cref="Wanted"/> faded outlines
/// for the places still empty. More than fit are summed up as "+N". One texture, one batch.
/// </summary>
public partial class UiFigures : Control
{
    int count, wanted;
    public int Count { get => count; set { count = value; QueueRedraw(); } }
    public int Wanted { get => wanted; set { wanted = value; QueueRedraw(); } }
    public float Figure = 26;

    public UiFigures() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(120, 28); TextureFilter = TextureFilterEnum.LinearWithMipmaps; }

    public override void _Draw()
    {
        var tex = Art.Tex("people/sailor");
        float h = Mathf.Min(Figure, Size.Y), w = tex != null ? h * tex.GetWidth() / tex.GetHeight() : h * 0.5f;
        float step = w + 2;
        int total = count + wanted;
        int room = Math.Max(1, (int)((Size.X - 34) / step));
        int shown = Math.Min(total, room);
        float y = (Size.Y - h) / 2;
        for (int i = 0; i < shown; i++)
        {
            var r = new Rect2(i * step, y, w, h);
            var tint = i < count ? Colors.White : new Color(1, 1, 1, 0.22f);
            if (tex != null) DrawTextureRect(tex, r, false, tint);
            else DrawRect(r.Grow(-2), i < count ? Ink.Black : Ink.Faint);
        }
        if (total > shown)
            DrawString(Fonts.Italic, new Vector2(shown * step + 4, y + h * 0.8f), "+" + (total - shown), HorizontalAlignment.Left, -1, 17, Parchment.Muted);
    }
}
