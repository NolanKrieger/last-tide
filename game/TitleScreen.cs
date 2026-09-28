using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The title (GDD §3 "start of a run"): new voyage (preset, cosmetic loadout, ship's name), resume the
/// suspended voyage, the logbook of bests and achievements, quit. It sits over the chart of a fresh sea:
/// Codex key art on the left, the name and an inked menu on the right; the voyage and logbook pages are sheets of paper.
/// </summary>
public partial class TitleScreen : CanvasLayer
{
    public event Action<Preset, string, string[]>? SetSail;
    public event Action? ResumePressed, QuitPressed, OptionsPressed;

    Control root = null!, home = null!;
    UiSheet voyage = null!, logbook = null!;
    TextureRect keyArt = null!, rule = null!;
    ColorRect menuWash = null!;
    VBoxContainer menu = null!, homeColumn = null!;
    Label gameTitle = null!, bestsLine = null!, version = null!;
    UiBanner tagline = null!;
    Button newVoyage = null!, resume = null!, logbookButton = null!, optionsButton = null!, quitButton = null!;
    readonly Button[] presetButtons = new Button[3];
    readonly TextureRect[] presetArt = new TextureRect[3];
    readonly Label[] presetBest = new Label[3], presetName = new Label[3], presetLine = new Label[3];
    readonly Label[] slotLabels = new Label[5];
    readonly Swatch[] slotSwatches = new Swatch[5];
    Label unlockedLine = null!;
    LineEdit nameEdit = null!;
    Button setSailButton = null!, voyageBack = null!, logbookBack = null!;
    ShipPreview preview = null!;
    UiCartouche voyageHead = null!, logbookHead = null!;
    Label difficultyHead = null!;
    readonly Button[] cyclers = new Button[10];
    bool compact;   // a short screen (UI scale 150 %): smaller plates, vignettes and rows
    Control confirm = null!;
    Label confirmLine = null!;
    Button keepButton = null!, abandonButton = null!;
    VBoxContainer bestRows = null!;
    GridContainer achievementGrid = null!;
    ScrollContainer achievementScroll = null!;
    Label achHead = null!;
    Profile profile = null!;
    Rng nameRng = new(12345);
    Preset preset = Preset.RoughSeas;
    readonly string[] loadout = { "", "", "", "", "" };
    string page = "home";

    public bool IsOpen => root.Visible;
    public string Page => page;
    public Preset ChosenPreset => preset;
    /// <summary>The cosmetic keys the voyage page has chosen (flag, sails, figurehead, hull, wake).</summary>
    public IReadOnlyList<string> Loadout => loadout;
    public ShipPreview Preview => preview;
    public string ShipName => nameEdit.Text;

    public void Init(Profile p, Font font)
    {
        Layer = 20;
        profile = p;
        nameRng = new Rng((ulong)DateTime.Now.Ticks);
        root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = Parchment.Theme(font), Visible = false };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        root.AddChild(Parchment.Backdrop(0.06f, 0.62f));
        root.Resized += Layout;

        BuildHome();
        BuildVoyage();
        BuildLogbook();
        BuildConfirm();

        version = Parchment.L(Text.Get("TITLE_VERSION"), "Caption", HorizontalAlignment.Right);
        version.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, 14);
        version.GrowHorizontal = Control.GrowDirection.Begin;
        version.GrowVertical = Control.GrowDirection.Begin;
        root.AddChild(version);
    }

    // ------------------------------------------------------------------ home

    void BuildHome()
    {
        home = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        home.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(home);
        // Paper pooled behind the name and the menu, so the sea's strokes and her hull at the quay never cross the lettering.
        menuWash = Parchment.SoftWash(0.92f, 0f, Ink.Paper, 2.4f);
        home.AddChild(menuWash);
        keyArt = Parchment.Picture(Art.Tex("title/keyart"), 10, 10);
        home.AddChild(keyArt);

        homeColumn = Parchment.Column(4);
        homeColumn.Alignment = BoxContainer.AlignmentMode.Center;
        home.AddChild(homeColumn);
        gameTitle = Parchment.L(Text.Get("GAME_TITLE"), "", HorizontalAlignment.Center);
        gameTitle.AddThemeFontOverride("font", Fonts.DisplayCaps);
        gameTitle.AddThemeFontSizeOverride("font_size", 112);
        homeColumn.AddChild(gameTitle);
        rule = Parchment.Picture(Parchment.Tex("divider"), 360, 44);
        rule.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        homeColumn.AddChild(rule);
        tagline = new UiBanner { Line = Text.Get("TITLE_TAGLINE"), Height = 70, FontSize = 21, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        homeColumn.AddChild(tagline);
        homeColumn.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        menu = Parchment.Column(2);
        menu.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        homeColumn.AddChild(menu);
        newVoyage = MenuLine("TITLE_NEW", () => Show("voyage"));
        resume = MenuLine("TITLE_RESUME", () => ResumePressed?.Invoke());
        logbookButton = MenuLine("TITLE_LOGBOOK", () => Show("logbook"));
        optionsButton = MenuLine("TITLE_OPTIONS", () => OptionsPressed?.Invoke());
        quitButton = MenuLine("TITLE_QUIT", () => QuitPressed?.Invoke());
        homeColumn.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        bestsLine = Parchment.L("", "Flavour", HorizontalAlignment.Center);
        bestsLine.AddThemeFontSizeOverride("font_size", 18);
        homeColumn.AddChild(bestsLine);
    }

    Button MenuLine(string key, Action a)
    {
        var b = Parchment.B(Text.Get(key), a, "MenuItem");
        b.CustomMinimumSize = new Vector2(300, 0);
        menu.AddChild(b);
        return b;
    }

    // ------------------------------------------------------------------ voyage

    void BuildVoyage()
    {
        voyage = new UiSheet { Visible = false };
        root.AddChild(voyage);
        var col = Parchment.Column(8);
        voyage.AddChild(col);
        voyageHead = new UiCartouche { Title = Text.Get("TITLE_VOYAGE_HEAD"), PlateHeight = 96, FontSize = 38, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        col.AddChild(voyageHead);

        difficultyHead = Parchment.L(Text.Get("TITLE_DIFFICULTY"), "Head");
        col.AddChild(difficultyHead);
        var presets = Parchment.Row(14);
        presets.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        col.AddChild(presets);
        var group = new ButtonGroup();
        string[] art = { "title/preset-calm", "title/preset-rough", "title/preset-tempest" };
        for (int i = 0; i < 3; i++)
        {
            var pr = (Preset)i;
            string key = pr.ToString().ToUpperInvariant();
            var b = new Button { ToggleMode = true, ButtonGroup = group, ThemeTypeVariation = "CardButton", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            b.Pressed += () => { Audio.Ui(); preset = pr; RefreshVoyage(); };
            presets.AddChild(b);
            presetButtons[i] = b;
            var inner = Parchment.Column(2);
            inner.Alignment = BoxContainer.AlignmentMode.Center;
            inner.MouseFilter = Control.MouseFilterEnum.Ignore;
            inner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 12);
            b.AddChild(inner);
            presetArt[i] = Parchment.Picture(Art.Tex(art[i]), 120, 120);
            presetArt[i].SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            inner.AddChild(presetArt[i]);
            presetName[i] = Parchment.L(Text.Get("PRESET_" + key), "Big", HorizontalAlignment.Center);
            inner.AddChild(presetName[i]);
            var line = Parchment.L(Text.Get("PRESET_LINE_" + key), "Flavour", HorizontalAlignment.Center);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            inner.AddChild(line);
            presetLine[i] = line;
            presetBest[i] = Parchment.L("", "Caption", HorizontalAlignment.Center);
            presetBest[i].AddThemeColorOverride("font_color", Ink.Black);
            presetBest[i].AddThemeFontSizeOverride("font_size", 18);
            inner.AddChild(presetBest[i]);
        }
        col.AddChild(new UiDivider(true, 22));

        var bottom = Parchment.Row(24);
        col.AddChild(bottom);
        // Colours: five slots cycling through what the profile has unlocked.
        var cos = Parchment.Column(4);
        cos.CustomMinimumSize = new Vector2(360, 0);
        bottom.AddChild(cos);
        cos.AddChild(Parchment.L(Text.Get("TITLE_COLOURS"), "Head"));
        var grid = new GridContainer { Columns = 5 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 2);
        cos.AddChild(grid);
        for (int i = 0; i < 5; i++)
        {
            int slot = i;
            var name = Parchment.L(Text.Get("SLOT_" + Cosmetics.Slots[i]), "Caption");
            name.CustomMinimumSize = new Vector2(104, 0);
            grid.AddChild(name);
            var prev = Parchment.B("‹", () => Cycle(slot, -1), "Flat", 24);
            grid.AddChild(prev);
            cyclers[i * 2] = prev;
            slotSwatches[i] = new Swatch();
            grid.AddChild(slotSwatches[i]);
            slotLabels[i] = Parchment.L("", "Data");
            slotLabels[i].CustomMinimumSize = new Vector2(168, 0);
            grid.AddChild(slotLabels[i]);
            var next = Parchment.B("›", () => Cycle(slot, 1), "Flat", 24);
            grid.AddChild(next);
            cyclers[i * 2 + 1] = next;
        }
        unlockedLine = Parchment.L("", "Flavour");
        unlockedLine.AddThemeFontSizeOverride("font_size", 15);
        unlockedLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        unlockedLine.CustomMinimumSize = new Vector2(360, 0);
        cos.AddChild(unlockedLine);

        // The ship as she will sail, in the chosen colours.
        preview = new ShipPreview { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(220, 170) };
        bottom.AddChild(preview);

        var right = Parchment.Column(8);
        right.CustomMinimumSize = new Vector2(300, 0);
        bottom.AddChild(right);
        right.AddChild(Parchment.L(Text.Get("TITLE_SHIP_NAME"), "Head"));
        var nameRow = Parchment.Row(8);
        right.AddChild(nameRow);
        nameEdit = new LineEdit { MaxLength = 24, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = Text.Get("TITLE_NAME_PLACEHOLDER") };
        nameEdit.TextChanged += t => preview.ShipName = t;
        nameEdit.TextSubmitted += _ => { Audio.Ui(); PressSetSail(); };
        nameRow.AddChild(nameEdit);
        var another = Parchment.B(Text.Get("TITLE_ANOTHER"), () => { nameEdit.Text = Names.Ship(nameRng); preview.ShipName = nameEdit.Text; }, "Flat");
        another.TooltipText = Text.Get("TITLE_ANOTHER_TIP");
        nameRow.AddChild(another);
        right.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        setSailButton = Parchment.B(Text.Get("TITLE_SET_SAIL"), PressSetSail, "", 28);
        setSailButton.AddThemeFontOverride("font", Fonts.SmallCaps);
        setSailButton.CustomMinimumSize = new Vector2(0, 52);
        right.AddChild(setSailButton);
        voyageBack = Parchment.B(Text.Get("TITLE_BACK"), () => Show("home"), "Flat");
        voyageBack.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        right.AddChild(voyageBack);
    }

    // ------------------------------------------------------------------ logbook

    void BuildLogbook()
    {
        logbook = new UiSheet { Visible = false };
        root.AddChild(logbook);
        var col = Parchment.Column(8);
        logbook.AddChild(col);
        logbookHead = new UiCartouche { Title = Text.Get("TITLE_LOGBOOK"), PlateHeight = 96, FontSize = 38, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        col.AddChild(logbookHead);
        var cols = Parchment.Row(28);
        cols.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        col.AddChild(cols);

        var left = Parchment.Column(8);
        left.CustomMinimumSize = new Vector2(360, 0);
        left.SizeFlagsStretchRatio = 0.8f;
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cols.AddChild(left);
        left.AddChild(Parchment.L(Text.Get("LOG_BESTS"), "Head"));
        bestRows = Parchment.Column(8);
        left.AddChild(bestRows);

        var right = Parchment.Column(6);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cols.AddChild(right);
        achHead = Parchment.L("", "Head");
        right.AddChild(achHead);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        achievementScroll = scroll;
        right.AddChild(scroll);
        achievementGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        achievementGrid.AddThemeConstantOverride("h_separation", 12);
        achievementGrid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(achievementGrid);

        logbookBack = Parchment.B(Text.Get("TITLE_BACK"), () => Show("home"), "Flat");
        logbookBack.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        col.AddChild(logbookBack);
    }

    // ------------------------------------------------------------------ abandon the saved voyage? (audit R-1)

    /// <summary>
    /// A new voyage writes over the one suspend save at its first dock (and sinking deletes it), so with a voyage saved,
    /// Set sail asks first. "Keep her" (the default, and Esc) goes back to the voyage page.
    /// </summary>
    void BuildConfirm()
    {
        confirm = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        confirm.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(confirm);
        confirm.AddChild(Parchment.Backdrop(0.45f, 0.8f));
        var card = new UiSheet { CustomMinimumSize = new Vector2(560, 0) };
        confirm.AddChild(card);
        Parchment.Centre(card);
        var col = Parchment.Column(10);
        card.AddChild(col);
        col.AddChild(Parchment.L(Text.Get("TITLE_ABANDON_HEAD"), "Big", HorizontalAlignment.Center));
        confirmLine = Parchment.L("", "Flavour", HorizontalAlignment.Center);
        confirmLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        confirmLine.CustomMinimumSize = new Vector2(480, 0);
        confirmLine.AddThemeFontSizeOverride("font_size", 19);
        col.AddChild(confirmLine);
        col.AddChild(new UiDivider(true, 22));
        var row = Parchment.Row(14);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        col.AddChild(row);
        keepButton = Parchment.B(Text.Get("TITLE_ABANDON_NO"), CloseConfirm, "", 22);
        abandonButton = Parchment.B(Text.Get("TITLE_ABANDON_YES"), () => { confirm.Visible = false; SetSailNow(); }, "", 22);
        abandonButton.AddThemeColorOverride("font_color", Ink.Red);
        abandonButton.AddThemeColorOverride("font_hover_color", Ink.Red);
        row.AddChild(keepButton);
        row.AddChild(abandonButton);
    }

    void CloseConfirm()
    {
        confirm.Visible = false;
        setSailButton.GrabFocus();
    }

    public bool ConfirmOpen => confirm.Visible;
    public Button KeepButton => keepButton;
    public Button AbandonButton => abandonButton;

    /// <summary>The saved voyage's ship and day, read from the save without loading the world.</summary>
    (string Ship, int Day)? SavedVoyage()
    {
        var json = profile.ReadSuspend();
        if (json == null) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var r = doc.RootElement;
            string ship = r.TryGetProperty("ShipName", out var n) ? n.GetString() ?? "" : "";
            long ticks = r.TryGetProperty("Ticks", out var t) ? t.GetInt64() : 0;
            return (ship, 1 + (int)Math.Floor(ticks / (double)Tuning.TicksPerSecond / Tuning.SecondsPerDay));
        }
        catch (Exception) { return ("", 1); }
    }

    // ------------------------------------------------------------------ layout (every UI scale, window and aspect)

    void Layout()
    {
        var s = root.Size;
        if (s.X < 10) return;
        // Home: the key art fills the left, the name and menu stand on the right.
        float colW = Mathf.Clamp(s.X * 0.4f, 380, 700);
        float artW = Mathf.Min(s.X - colW - 24, s.Y * 1.1f);
        keyArt.Position = new Vector2(Mathf.Max(8, (s.X - colW - artW) * 0.45f), s.Y * 0.05f);
        keyArt.Size = new Vector2(artW, s.Y * 0.9f);
        homeColumn.Position = new Vector2(s.X - colW - s.X * 0.03f, 0);
        homeColumn.Size = new Vector2(colW, s.Y);
        menuWash.Position = homeColumn.Position - new Vector2(colW * 0.25f, -s.Y * 0.08f);
        menuWash.Size = new Vector2(colW * 1.5f, s.Y * 0.84f);
        int titleSize = (int)Mathf.Clamp(Mathf.Min(colW / 4.3f, s.Y / 5.6f), 60, 150);
        gameTitle.AddThemeFontSizeOverride("font_size", titleSize);
        rule.CustomMinimumSize = new Vector2(Mathf.Min(colW * 0.8f, 440), Mathf.Min(colW * 0.8f, 440) * 0.12f);
        tagline.Height = Mathf.Clamp(s.Y * 0.075f, 52, 84);
        tagline.FontSize = (int)Mathf.Clamp(s.Y * 0.024f, 17, 26);
        int menuSize = (int)Mathf.Clamp(s.Y * 0.034f, 22, 34);
        foreach (var b in new[] { newVoyage, resume, logbookButton, optionsButton, quitButton })
            b.AddThemeFontSizeOverride("font_size", menuSize);

        // Pages: a sheet as large as the screen allows, up to a comfortable reading size.
        float m = Mathf.Clamp(s.X * 0.02f, 12, 36);
        foreach (var sheet in new[] { voyage, logbook })
        {
            var size = new Vector2(Mathf.Min(s.X - 2 * m, 1440), Mathf.Min(s.Y - 2 * m, 860));
            sheet.Position = (s - size) / 2;
            sheet.Size = size;
        }
        // Short screens (UI scale 150 % leaves 1067 × 600): smaller plates and vignettes, no section head over the cards.
        bool wasCompact = compact;
        compact = s.Y < 800;   // UI scale 125 % (720 tall) and 150 % (600)
        foreach (var plate in new[] { voyageHead, logbookHead })
        {
            plate.PlateHeight = compact ? 56 : 96;
            plate.FontSize = compact ? 30 : 38;
        }
        difficultyHead.Visible = !compact;
        // The vignettes take whatever height the rest of the page leaves (about 540 px compact, 590 full).
        float art = compact ? Mathf.Clamp(s.Y - 2 * m - 470, 50, 120) : Mathf.Clamp(s.Y - 2 * m - 560, 90, 220);
        for (int i = 0; i < 3; i++)
        {
            presetArt[i].CustomMinimumSize = new Vector2(art, art);
            // The card is a button, which does not size itself to its contents: give it room for the vignette and three lines.
            presetButtons[i].CustomMinimumSize = new Vector2(0, art + (compact ? 124 : 132));
            presetName[i].AddThemeFontSizeOverride("font_size", compact ? 24 : 30);
            presetLine[i].AddThemeFontSizeOverride("font_size", compact ? 15 : 17);
        }
        foreach (var b in cyclers) b.AddThemeFontSizeOverride("font_size", compact ? 18 : 24);
        unlockedLine.Visible = !compact;
        if (wasCompact != compact && logbook.Visible) RefreshLogbook();
    }

    int debugFrame;
    public override void _Process(double delta)
    {
        if (++debugFrame == 80 && OS.GetEnvironment("LT_UI_DEBUG") == "1") DumpSizes();
    }

    void DumpSizes()
    {
        void Walk(Node n, int d)
        {
            if (n is Control c && c.IsVisibleInTree()) GD.Print(new string(' ', d * 2) + c.GetType().Name + " min=" + c.GetCombinedMinimumSize() + " size=" + c.Size);
            if (d < 7) foreach (var ch in n.GetChildren()) Walk(ch, d + 1);
        }
        Walk(logbook.Visible ? logbook : voyage, 0);
    }

    // ------------------------------------------------------------------ pages

    public void Show(string which = "home")
    {
        confirm.Visible = false;
        page = which;
        root.Visible = true;
        home.Visible = which == "home";
        voyage.Visible = which == "voyage";
        logbook.Visible = which == "logbook";
        version.Visible = which == "home";
        if (which == "home")
        {
            resume.Visible = profile.HasSuspend;
            var bests = profile.Bests.OrderByDescending(b => b.Value.Days).ToList();
            bestsLine.Text = bests.Count == 0 ? Text.Get("TITLE_NO_VOYAGES")
                : Text.Get("TITLE_BESTS_LINE", profile.Voyages, Parchment.Days(bests[0].Value.Days), Text.Get("PRESET_" + bests[0].Key.ToUpperInvariant()));
            (profile.HasSuspend ? resume : newVoyage).CallDeferred(Control.MethodName.GrabFocus);
        }
        else if (which == "voyage")
        {
            if (Enum.TryParse<Preset>(profile.LastPreset, out var last)) preset = last;
            for (int i = 0; i < 5; i++) loadout[i] = profile.HasCosmetic(profile.LastLoadout[i]) ? profile.LastLoadout[i] : "";
            nameEdit.Text = profile.LastShipName.Length > 0 ? profile.LastShipName : Names.Ship(nameRng);
            preview.ShipName = nameEdit.Text;
            RefreshVoyage();
            presetButtons[(int)preset].CallDeferred(Control.MethodName.GrabFocus);
        }
        else
        {
            RefreshLogbook();
            logbookBack.CallDeferred(Control.MethodName.GrabFocus);
        }
        Layout();
    }

    void RefreshVoyage()
    {
        for (int i = 0; i < 3; i++)
        {
            var pr = (Preset)i;
            string key = pr.ToString();
            presetBest[i].Text = profile.Bests.TryGetValue(key, out var b) ? Text.Get("PRESET_BEST", Parchment.Days(b.Days)) : Text.Get("PRESET_NO_BEST");
            presetButtons[i].SetPressedNoSignal(pr == preset);
        }
        for (int i = 0; i < 5; i++)
        {
            slotLabels[i].Text = loadout[i].Length == 0 ? Text.Get("COSMETIC_PLAIN") : Text.Get("COSMETIC_" + loadout[i]);
            slotSwatches[i].Colour = SlotColour(i, loadout[i]);
            slotSwatches[i].Striped = i == 1 && Cosmetic.Striped(loadout[i]);
        }
        int total = Cosmetics.Slots.Sum(s => Cosmetics.Options(s).Length - 1);
        unlockedLine.Text = Text.Get("TITLE_UNLOCKED", profile.Cosmetics.Count, total);
        preview.SetLoadout(loadout);
    }

    static Color? SlotColour(int slot, string key) => Cosmetics.Slots[slot] switch
    {
        "flag" => Cosmetic.Flag(key),
        "sails" => Cosmetic.Sail(key),
        "hull" => Cosmetic.Hull(key),
        "wake" => Cosmetic.Wake(key),
        _ => null,
    };

    void Cycle(int slot, int dir)
    {
        var options = Cosmetics.Options(Cosmetics.Slots[slot]).Where(profile.HasCosmetic).ToList();
        int at = Math.Max(0, options.IndexOf(loadout[slot]));
        loadout[slot] = options[(at + dir + options.Count) % options.Count];
        RefreshVoyage();
        if (slot == 1) preview.Snap();
    }

    void RefreshLogbook()
    {
        foreach (var c in bestRows.GetChildren()) c.QueueFree();
        foreach (var c in achievementGrid.GetChildren()) c.QueueFree();
        string[] art = { "title/preset-calm", "title/preset-rough", "title/preset-tempest" };
        for (int i = 0; i < 3; i++)
        {
            string key = ((Preset)i).ToString();
            var card = new PanelContainer { ThemeTypeVariation = "Card" };
            bestRows.AddChild(card);
            var row = Parchment.Row(12);
            card.AddChild(row);
            row.AddChild(Parchment.Picture(Art.Tex(art[i]), compact ? 46 : 64, compact ? 46 : 64));
            var text = Parchment.Column(0);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(text);
            text.AddChild(Parchment.L(Text.Get("PRESET_" + key.ToUpperInvariant()), "Head"));
            if (profile.Bests.TryGetValue(key, out var b))
            {
                var days = Parchment.L(Text.Get("LOG_BEST_DAYS", Parchment.Days(b.Days)), "Big");
                if (compact) days.AddThemeFontSizeOverride("font_size", 24);
                text.AddChild(days);
                text.AddChild(OneLine(Text.Get("LOG_BEST_LOST", Parchment.DayWatch(b.Days), Text.Get(b.Cause.Length > 0 ? b.Cause : "SUNK_SEA")), "Flavour"));
                if (!compact) text.AddChild(OneLine(Text.Get("LOG_BEST_SHIP", b.ShipName, b.Date), "Caption"));
            }
            else text.AddChild(Parchment.L(Text.Get("PRESET_NO_BEST"), "Flavour"));
        }
        int done = Achievements.All.Count(profile.Achievements.Contains);
        achHead.Text = Text.Get("LOG_ACHIEVEMENTS", done, Achievements.All.Length);
        foreach (var a in Achievements.All)
        {
            bool has = profile.Achievements.Contains(a);
            var row = Parchment.Row(10);
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(Parchment.Picture(Parchment.AchievementIcon(a, has), 54, 54));
            var text = Parchment.Column(0);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(text);
            var name = Parchment.L(Text.Get("ACH_" + a + "_NAME"), "Head");
            name.AddThemeFontSizeOverride("font_size", 19);
            if (!has) name.AddThemeColorOverride("font_color", Parchment.Muted);
            text.AddChild(name);
            var desc = OneLine(Text.Get("ACH_" + a + "_DESC"), "Flavour");
            desc.AddThemeFontSizeOverride("font_size", 16);
            text.AddChild(desc);
            if (Achievements.Reward(a) is { } r)
            {
                var reward = Parchment.L(Text.Get(has ? "LOG_REWARD_HAVE" : "LOG_REWARD", Text.Get("COSMETIC_" + r)), "Caption");
                reward.AddThemeFontSizeOverride("font_size", 15);
                if (has) reward.AddThemeColorOverride("font_color", Ink.Red);
                text.AddChild(reward);
            }
            achievementGrid.AddChild(row);
        }
    }

    /// <summary>On the logbook page the arrow and page keys scroll the medallions (nothing in the list takes the focus).</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (!root.Visible || !logbook.Visible || confirm.Visible || e is not InputEventKey { Pressed: true }) return;
        int step = e.IsActionPressed("ui_down", true) ? 70 : e.IsActionPressed("ui_up", true) ? -70
            : e.IsActionPressed("ui_page_down", true) ? 300 : e.IsActionPressed("ui_page_up", true) ? -300 : 0;
        if (step == 0) return;
        achievementScroll.ScrollVertical += step;
        GetViewport().SetInputAsHandled();
    }

    public int AchievementScroll => achievementScroll.ScrollVertical;
    public Button LogbookButton => logbookButton;

    /// <summary>A one-line label that trims with an ellipsis instead of widening its column (autowrapped labels in
    /// unsized containers settle at absurd heights on the first layout pass).</summary>
    static Label OneLine(string text, string style)
    {
        var l = Parchment.L(text, style);
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        l.TooltipText = text;
        l.MouseFilter = Control.MouseFilterEnum.Pass;
        return l;
    }

    /// <summary>Hides the title (the old <c>Hide()</c> shadowed <see cref="CanvasLayer.Hide"/>).</summary>
    public void Close() => root.Visible = false;

    /// <summary>Esc: a sub-page goes home; home does nothing.</summary>
    public void Back()
    {
        if (confirm.Visible) { CloseConfirm(); return; }
        if (page != "home") Show("home");
    }

    // ---- driven by the self-test as well as the mouse ----
    public void SelectPreset(Preset p) { preset = p; RefreshVoyage(); }
    /// <summary>Writes the ship's name (the old <c>SetName</c> shadowed <see cref="Node.SetName"/>).</summary>
    public void SetShipName(string name) { nameEdit.Text = name; preview.ShipName = name; }
    public void PressSetSail()
    {
        if (profile.HasSuspend && SavedVoyage() is { } saved)
        {
            confirmLine.Text = saved.Ship.Length > 0 ? Text.Get("TITLE_ABANDON_LINE", saved.Ship, saved.Day) : Text.Get("TITLE_ABANDON_LINE_NONAME");
            confirm.Visible = true;
            keepButton.CallDeferred(Control.MethodName.GrabFocus);
            return;
        }
        SetSailNow();
    }

    void SetSailNow()
    {
        string name = nameEdit.Text.Trim();
        if (name.Length == 0) name = Names.Ship(nameRng);
        profile.LastPreset = preset.ToString();
        profile.LastShipName = name;
        profile.LastLoadout = loadout.ToList();
        profile.Save();
        SetSail?.Invoke(preset, name, (string[])loadout.Clone());
    }
    public void PressResume() => ResumePressed?.Invoke();
    /// <summary>Test hook: the cosmetic cycler's own path.</summary>
    public void CycleSlot(int slot, int dir) => Cycle(slot, dir);
    /// <summary>The ‹ (dir −1) or › (dir +1) button of a cosmetic slot, for clicks through the real input path.</summary>
    public Button Cycler(int slot, int dir) => cyclers[slot * 2 + (dir > 0 ? 1 : 0)];
    public Button NewVoyageButton => newVoyage;
    public LineEdit NameField => nameEdit;
    public Button SetSailButton => setSailButton;
}

/// <summary>A little ink-ruled colour chip beside a cosmetic's name (none for figureheads).</summary>
public partial class Swatch : Control
{
    Color? colour;
    bool striped;
    public Color? Colour { get => colour; set { colour = value; QueueRedraw(); } }
    public bool Striped { get => striped; set { striped = value; QueueRedraw(); } }
    public Swatch() { CustomMinimumSize = new Vector2(22, 22); MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        if (colour is not { } c) return;
        var r = new Rect2(3, 4, 16, 14);
        DrawRect(r, c);
        if (striped)
            for (int i = 0; i < 3; i++) DrawRect(new Rect2(r.Position.X, r.Position.Y + 2 + i * 4.5f, r.Size.X, 1.6f), Ink.Red);
        DrawRect(r, Ink.Black, false, 1.2f);
    }
}

/// <summary>
/// The voyage page's live preview: the player's sloop (the real <see cref="ShipView"/>) under sail on a small patch of
/// sea, in the chosen colours, with her wake inked behind her and her name lettered below. She rocks gently.
/// </summary>
public partial class ShipPreview : Control
{
    readonly Ship ship;
    readonly Node2D holder = new();
    ShipView? view;
    string[] loadout = { "", "", "", "", "" };
    string name = "";
    float time;

    public string ShipName { get => name; set { name = value; QueueRedraw(); } }
    public string[] ShownLoadout => loadout;

    public ShipPreview()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        // A sloop on a broad reach: bow up and to the right, the wind over her port quarter.
        double heading = Angles.FromCompassDeg(58);
        ship = new Ship(Hulls.Sloop, Vec2.Zero, heading) { IsPlayer = true, SailTarget = 2, SailFraction = 0.75 };
        ship.Step(0, new Wind(Angles.Wrap(heading + Angles.Rad(25)), Tuning.StandardWind), new ShipInput(0, 0), Array.Empty<Island>());
        AddChild(holder);
    }

    public void SetLoadout(string[] l)
    {
        loadout = (string[])l.Clone();
        if (view == null)
        {
            view = new ShipView();
            holder.AddChild(view);
        }
        view.Init(ship, loadout);
        view.SetPose(Vec2.Zero, ship.Heading);
        QueueRedraw();
    }

    /// <summary>The sails belly and settle, as when she makes sail.</summary>
    public void Snap() => view?.SailSnap();

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        time += (float)delta;
        float scale = Mathf.Clamp(Mathf.Min(Size.X / 150f, (Size.Y - 34) / 95f), 1.2f, 3.2f);
        holder.Position = new Vector2(Size.X * 0.52f, (Size.Y - 30) * 0.5f + Mathf.Sin(time * 1.3f) * 2.5f);
        holder.Rotation = Mathf.Sin(time * 0.9f) * 0.02f;
        holder.Scale = new Vector2(scale, scale);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var c = new Vector2(Size.X * 0.52f, (Size.Y - 30) * 0.5f);
        float scale = holder.Scale.X;
        // A pale watercolour patch of sea with a few strokes of swell.
        var sea = new Color(0.55f, 0.66f, 0.72f, 0.28f);
        var pts = new Vector2[40];
        for (int i = 0; i < pts.Length; i++)
        {
            float a = Mathf.Tau * i / pts.Length;
            float wob = 1 + 0.06f * Mathf.Sin(a * 5 + 1.3f) + 0.04f * Mathf.Sin(a * 9);
            pts[i] = c + new Vector2(Mathf.Cos(a) * Size.X * 0.46f, Mathf.Sin(a) * (Size.Y - 30) * 0.42f) * wob;
        }
        DrawColoredPolygon(pts, sea);
        var strokes = new List<Vector2>();
        for (int k = 0; k < 7; k++)
        {
            float y = c.Y - (Size.Y - 30) * 0.3f + k * (Size.Y - 30) * 0.1f;
            float x0 = c.X - Size.X * 0.34f + (k % 3) * 18 + Mathf.Sin(time * 0.6f + k) * 6;
            for (int j = 0; j < 3; j++)
            {
                float x = x0 + j * Size.X * 0.24f;
                strokes.Add(new Vector2(x, y)); strokes.Add(new Vector2(x + 10, y - 3));
                strokes.Add(new Vector2(x + 10, y - 3)); strokes.Add(new Vector2(x + 20, y));
            }
        }
        DrawMultiline(strokes.ToArray(), new Color(0.2f, 0.32f, 0.45f, 0.35f), 1.2f);
        // Her wake, in the chosen ink, streaming astern.
        var wake = Cosmetic.Wake(loadout[4]) with { A = 0.7f };
        var back = -new Vector2(Mathf.Cos((float)ship.Heading), Mathf.Sin((float)ship.Heading));
        var side = new Vector2(-back.Y, back.X);
        float L = (float)ship.Hull.Length * Ink.PxPerM * scale * 0.45f;
        // It fades out astern and ends inside the patch of sea (it used to run on over the bare paper).
        float ea = Size.X * 0.46f, eb = (Size.Y - 30) * 0.42f;
        float edge = 1 / Mathf.Sqrt(back.X * back.X / (ea * ea) + back.Y * back.Y / (eb * eb));
        float run = Mathf.Max(12, edge * 0.9f - L - 28);
        var cols = new Color[8];
        for (int i = 0; i < cols.Length; i++) cols[i] = wake with { A = wake.A * (1 - i / 7f) };
        for (int s = -1; s <= 1; s += 2)
        {
            var w = new Vector2[8];
            for (int i = 0; i < w.Length; i++)
            {
                float t = i / 7f;
                w[i] = c + back * (L + t * run) + side * s * (4 + t * 22 + Mathf.Sin(time * 3 + i) * 1.5f);
            }
            DrawPolylineColors(w, cols, 2f, true);
        }
        if (name.Length > 0)
            DrawString(Fonts.Italic, new Vector2(0, Size.Y - 8), name, HorizontalAlignment.Center, Size.X, 22, Ink.Black);
    }
}
