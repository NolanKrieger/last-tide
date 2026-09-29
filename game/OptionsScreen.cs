using Godot;

namespace LastTide;

/// <summary>
/// Options (polish bar): display, sound, accessibility and key bindings. Every change applies at once
/// and is saved when the page closes. Reached from the title and from the pause menu. The key table is a ship's
/// "orders" card: each action with a dotted leader to its key cap; click a cap (or Enter on it) and press a key.
/// </summary>
public partial class OptionsScreen : CanvasLayer
{
    public event Action? Closed, Changed;
    Control root = null!;
    UiSheet sheet = null!;
    Settings settings = null!;
    CheckBox fullscreen = null!, colorblind = null!, hints = null!;
    readonly Button[] resolutionButtons = new Button[Settings.Resolutions.Length];
    HSlider scale = null!, master = null!, ambience = null!, sfx = null!;
    Label scaleValue = null!, masterValue = null!, ambienceValue = null!, sfxValue = null!, captureLine = null!;
    readonly Dictionary<string, Button> keyButtons = new();
    Button backButton = null!;
    string? capturing;
    bool building, scaleDragging;

    static readonly (string Group, string[] Actions)[] Groups =
    {
        ("OPT_GROUP_SAILING", new[] { "SailUp", "SailDown", "Port", "Starboard" }),
        ("OPT_GROUP_GUNS", new[] { "FirePort", "FireStarboard" }),
        ("OPT_GROUP_CREW", new[] { "Crew" }),
        ("OPT_GROUP_SHIP", new[] { "Lantern", "Dock", "Chart" }),
    };

    public bool IsOpen => root.Visible;
    public string? Capturing => capturing;
    public Button KeyButton(string action) => keyButtons[action];

    public void Init(Settings s, Font font)
    {
        Layer = 22;
        settings = s;
        root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = Parchment.Theme(font), Visible = false };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.Resized += Layout;
        AddChild(root);
        root.AddChild(Parchment.Backdrop(0.45f, 0.85f));
        sheet = new UiSheet();
        root.AddChild(sheet);
        var col = Parchment.Column(6);
        sheet.AddChild(col);
        col.AddChild(new UiCartouche { Title = Text.Get("OPT_TITLE"), PlateHeight = 88, FontSize = 38, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter });

        var columns = Parchment.Row(40);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        col.AddChild(columns);
        var left = Scrolling(columns, 8);
        var right = Scrolling(columns, 2);

        // Display
        Section(left, "OPT_DISPLAY");
        fullscreen = Check(left, "OPT_FULLSCREEN", on => { settings.Fullscreen = on; Apply(); });
        left.AddChild(Parchment.L(Text.Get("OPT_RESOLUTION"), "Caption"));
        var resRow = Parchment.Row(6);
        left.AddChild(resRow);
        var group = new ButtonGroup();
        for (int i = 0; i < Settings.Resolutions.Length; i++)
        {
            var (w, h) = Settings.Resolutions[i];
            var b = Parchment.B($"{w}×{h}", () => { settings.Width = w; settings.Height = h; Apply(); });
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.AddThemeFontSizeOverride("font_size", 17);
            resRow.AddChild(b);
            resolutionButtons[i] = b;
        }
        // The UI scale re-lays out every screen: while the seal is being dragged only the figure changes, and the new
        // scale applies when it is let go (arrow keys apply at once).
        (scale, scaleValue) = SliderRow(left, "OPT_UI_SCALE", 0.75, 1.5, 0.05, v => { if (!scaleDragging) { settings.UiScale = v; Apply(); } }, v => $"{v * 100:0}%");
        scale.DragStarted += () => scaleDragging = true;
        scale.DragEnded += changed => { scaleDragging = false; if (changed) { settings.UiScale = scale.Value; Apply(); } };

        // Sound
        Section(left, "OPT_SOUND");
        (master, masterValue) = SliderRow(left, "OPT_MASTER", 0, 1, 0.05, v => { settings.Master = v; Apply(); }, v => $"{v * 100:0}%");
        (ambience, ambienceValue) = SliderRow(left, "OPT_AMBIENCE", 0, 1, 0.05, v => { settings.Ambience = v; Apply(); }, v => $"{v * 100:0}%");
        (sfx, sfxValue) = SliderRow(left, "OPT_SFX", 0, 1, 0.05, v => { settings.Sfx = v; Apply(); }, v => $"{v * 100:0}%");

        // Accessibility
        Section(left, "OPT_ACCESS");
        colorblind = Check(left, "OPT_COLORBLIND", on => { settings.Colorblind = on; Apply(); });
        hints = Check(left, "OPT_HINTS", on => { settings.ShowHints = on; Apply(); });

        // Controls: the orders card, in two columns so every key shows at once.
        Section(right, "OPT_CONTROLS");
        var keyCols = Parchment.Row(24);
        right.AddChild(keyCols);
        var keyCol = new[] { Parchment.Column(2), Parchment.Column(2) };
        foreach (var kc in keyCol) { kc.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; keyCols.AddChild(kc); }
        int gi = 0;
        foreach (var (groupKey, actions) in Groups)
        {
            var into = keyCol[gi++ < 2 ? 0 : 1];
            var g = Parchment.L(Text.Get(groupKey), "Caption");
            g.CustomMinimumSize = new Vector2(0, 26);
            g.VerticalAlignment = VerticalAlignment.Bottom;
            into.AddChild(g);
            foreach (var action in actions)
            {
                var row = Parchment.Row(6);
                into.AddChild(row);
                var name = Parchment.L(Text.Get("ACT_" + action), "Data");
                row.AddChild(name);
                row.AddChild(new DottedLeader { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
                string a = action;
                var b = Parchment.B("", () => BeginCapture(a), "KeyCap");
                b.CustomMinimumSize = new Vector2(64, 34);
                row.AddChild(b);
                keyButtons[action] = b;
            }
        }
        captureLine = Parchment.L(Text.Get("OPT_FIXED_KEYS"), "Flavour");
        captureLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        captureLine.CustomMinimumSize = new Vector2(360, 0);
        right.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        right.AddChild(captureLine);
        var reset = Parchment.B(Text.Get("OPT_RESET_KEYS"), () => { settings.ResetKeys(); Apply(); Refresh(); }, "Flat");
        reset.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        right.AddChild(reset);

        col.AddChild(new UiDivider(true, 20));
        backButton = Parchment.B(Text.Get("TITLE_BACK"), Close, "", 22);
        backButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        backButton.CustomMinimumSize = new Vector2(180, 0);
        col.AddChild(backButton);
    }

    static VBoxContainer Scrolling(Container parent, int separation)
    {
        var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        parent.AddChild(scroll);
        var c = Parchment.Column(separation);
        c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(c);
        return c;
    }

    static void Section(Container parent, string key)
    {
        if (parent.GetChildCount() > 0) parent.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        parent.AddChild(Parchment.L(Text.Get(key), "Head"));
        parent.AddChild(new UiDivider(false, 6));
    }

    CheckBox Check(Container parent, string key, Action<bool> onChange)
    {
        var b = new CheckBox { Text = Text.Get(key), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        b.Toggled += on => { if (building) return; Audio.Ui(); onChange(on); };
        parent.AddChild(b);
        return b;
    }

    (HSlider, Label) SliderRow(Container parent, string key, double min, double max, double step, Action<double> onChange, Func<double, string> fmt)
    {
        var row = Parchment.Row(10);
        parent.AddChild(row);
        var label = Parchment.L(Text.Get(key), "Data");
        label.CustomMinimumSize = new Vector2(120, 0);
        row.AddChild(label);
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(180, 30) };
        row.AddChild(s);
        var value = Parchment.L("", "Caption", HorizontalAlignment.Right);
        value.CustomMinimumSize = new Vector2(56, 0);
        row.AddChild(value);
        s.ValueChanged += v => { value.Text = fmt(v); if (!building) onChange(v); };
        return (s, value);
    }

    void Layout()
    {
        if (sheet == null) return;   // Resized fires while the page is still being built
        var s = root.Size;
        if (s.X < 10) return;
        float m = Mathf.Clamp(s.X * 0.02f, 10, 40);
        var size = new Vector2(Mathf.Min(s.X - 2 * m, 1240), Mathf.Min(s.Y - 2 * m, 800));
        sheet.Size = size;
        sheet.Position = ((s - size) / 2).Floor();
    }

    void Apply()
    {
        settings.Save();
        Changed?.Invoke();
        Refresh();
    }

    /// <summary>Reads the settings back into every control.</summary>
    public void Refresh()
    {
        building = true;
        fullscreen.ButtonPressed = settings.Fullscreen;
        for (int i = 0; i < Settings.Resolutions.Length; i++)
        {
            var (w, h) = Settings.Resolutions[i];
            resolutionButtons[i].SetPressedNoSignal(w == settings.Width && h == settings.Height);
            resolutionButtons[i].Disabled = settings.Fullscreen;
        }
        scale.Value = settings.UiScale;
        scaleValue.Text = $"{settings.UiScale * 100:0}%";
        master.Value = settings.Master;
        masterValue.Text = $"{settings.Master * 100:0}%";
        ambience.Value = settings.Ambience;
        ambienceValue.Text = $"{settings.Ambience * 100:0}%";
        sfx.Value = settings.Sfx;
        sfxValue.Text = $"{settings.Sfx * 100:0}%";
        colorblind.ButtonPressed = settings.Colorblind;
        hints.ButtonPressed = settings.ShowHints;
        foreach (var (action, b) in keyButtons)
        {
            b.Text = capturing == action ? Text.Get("OPT_PRESS_KEY") : Settings.Label(settings.KeyFor(action));
            b.ThemeTypeVariation = "KeyCap";
            if (capturing == action) b.AddThemeColorOverride("font_color", Ink.Red);
            else b.RemoveThemeColorOverride("font_color");
        }
        building = false;
    }

    public void BeginCapture(string action)
    {
        capturing = action;
        captureLine.Text = Text.Get("OPT_CAPTURING", Text.Get("ACT_" + action));
        captureLine.AddThemeColorOverride("font_color", Ink.Red);
        Refresh();
    }

    public override void _Input(InputEvent e)
    {
        if (!IsOpen || capturing == null || e is not InputEventKey { Pressed: true, Echo: false } key) return;
        GetViewport().SetInputAsHandled();
        string action = capturing;
        if (key.Keycode != Key.Escape && key.Keycode != Key.None)
        {
            settings.Bind(capturing, key.Keycode);
            settings.Save();
            Changed?.Invoke();
        }
        capturing = null;
        captureLine.Text = Text.Get("OPT_FIXED_KEYS");
        captureLine.RemoveThemeColorOverride("font_color");
        Refresh();
        keyButtons[action].GrabFocus();
    }

    public void Open()
    {
        capturing = null;
        captureLine.Text = Text.Get("OPT_FIXED_KEYS");
        captureLine.RemoveThemeColorOverride("font_color");
        Refresh();
        root.Visible = true;
        Layout();
        backButton.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        if (!root.Visible) return;
        capturing = null;
        settings.Save();
        root.Visible = false;
        Closed?.Invoke();
    }

    // Self-test hooks: the same paths the buttons take.
    public void SetColorblind(bool on) { settings.Colorblind = on; Apply(); }
    public void SetUiScale(double v) { settings.UiScale = v; Apply(); }
    public void SetShowHints(bool on) { settings.ShowHints = on; Apply(); }
}

/// <summary>A dotted leader between an action and its key, the way an old table of contents runs to its page number.</summary>
public partial class DottedLeader : Control
{
    public DottedLeader() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(20, 0); }
    public override void _Draw()
    {
        float y = Size.Y * 0.62f;
        var dots = new List<Vector2>();
        for (float x = 4; x < Size.X - 4; x += 8) { dots.Add(new Vector2(x, y)); dots.Add(new Vector2(x + 1.5f, y)); }
        if (dots.Count > 0) DrawMultiline(dots.ToArray(), Ink.Black with { A = 0.45f }, 1.5f);
    }
}
