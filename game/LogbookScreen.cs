using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The recap page (GDD §3): the lost voyage written up as a page of the ship's log — ruled lines, a red margin, the
/// entries in a clerk's italic, the day's figures with dotted leaders, an ink blot, a quick sketch of her going down and,
/// when it was a best, the captain's wax seal stamped across the page.
/// </summary>
public partial class LogbookScreen : CanvasLayer
{
    public event Action? NewVoyage, ToTitle;
    const float Row = 34;
    float row = Row;
    readonly List<Control> ruled = new();   // every control that takes one ruled row (or two), resized with the page
    Control head = null!, earned = null!;
    Control root = null!;
    UiSheet page = null!;
    Label title = null!, lost = null!, cause = null!, best = null!;
    TextureRect seal = null!, sinking = null!, blot = null!;
    HBoxContainer earnedRow = null!;
    Label earnedHead = null!;
    Button again = null!, home = null!;
    readonly List<LeaderLine> rows = new();
    static readonly string[] RowKeys = { "DAYS", "GOLD", "SUNK", "PORTS", "LEAGUES", "TRADE", "TREASURES", "COVES" };

    public bool IsOpen => root.Visible;
    public string CauseText => cause.Text;
    public bool BestVisible => best.Visible;
    public bool SealVisible => seal.Visible;

    public void Init(Font font)
    {
        Layer = 21;
        root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = Parchment.Theme(font), Visible = false };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.Resized += Layout;
        AddChild(root);
        root.AddChild(Parchment.Backdrop(0.42f, 0.9f, new Color(0.08f, 0.1f, 0.16f)));

        page = new UiSheet { Ornate = false, Aged = true, Margin = 70, RulePitch = Row };
        // The right-hand strip of the page is left for marginalia: the blot, the captain's seal, the sketch of her going down.
        var pad = new StyleBoxEmpty { ContentMarginLeft = 96, ContentMarginRight = 250, ContentMarginTop = 30, ContentMarginBottom = 30 };
        page.AddThemeStyleboxOverride("panel", pad);
        root.AddChild(page);
        var col = Parchment.Column(0);
        page.AddChild(col);

        // Two ruled rows of heading: the log's own caption and the ship's name.
        head = new Control { CustomMinimumSize = new Vector2(0, Row * 2) };
        col.AddChild(head);
        var caption = Parchment.L(Text.Get("LOGBOOK_HEAD"), "Caption");
        caption.Position = new Vector2(0, -8);
        head.AddChild(caption);
        title = Parchment.L("", "");
        title.AddThemeFontOverride("font", Fonts.DisplayItalic);
        title.AddThemeFontSizeOverride("font_size", 40);
        title.Position = new Vector2(0, Row * 2 - 58);
        head.AddChild(title);

        lost = Entry(col, "", Fonts.Italic, 22, Ink.Red);
        cause = Entry(col, "", Fonts.Italic, 22, Ink.Black);
        foreach (var k in RowKeys)
        {
            var line = new LeaderLine { Key = Text.Get("LOGBOOK_" + k), CustomMinimumSize = new Vector2(0, Row) };
            col.AddChild(line);
            rows.Add(line);
            ruled.Add(line);
        }
        best = Entry(col, "", Fonts.DisplayItalic, 30, Ink.Red);
        earned = new Control { CustomMinimumSize = new Vector2(0, Row * 2) };
        col.AddChild(earned);
        earnedHead = Parchment.L(Text.Get("LOGBOOK_EARNED_HEAD"), "Caption");
        earnedHead.Position = new Vector2(0, 4);
        earned.AddChild(earnedHead);
        earnedRow = Parchment.Row(10);
        earnedRow.Position = new Vector2(0, Row - 4);
        earned.AddChild(earnedRow);

        var buttons = Parchment.Row(18);
        buttons.CustomMinimumSize = new Vector2(0, Row * 1.6f);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        col.AddChild(buttons);
        again = Parchment.B(Text.Get("LOGBOOK_NEW"), () => NewVoyage?.Invoke(), "", 22);
        again.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        buttons.AddChild(again);
        home = Parchment.B(Text.Get("LOGBOOK_TITLE_BTN"), () => ToTitle?.Invoke(), "Flat", 20);
        home.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        buttons.AddChild(home);

        // Marginalia over the page (children of the screen, not of the page's container, so they sit where they are put).
        blot = Parchment.Picture(Parchment.Tex("log-blot"), 70, 90, new Color(1, 1, 1, 0.85f));
        root.AddChild(blot);
        sinking = Parchment.Picture(Parchment.Tex("log-sinking"), 170, 196, new Color(1, 1, 1, 0.9f));
        root.AddChild(sinking);
        seal = Parchment.Picture(Parchment.Tex("seal-best"), 150, 160);
        seal.Rotation = -0.21f;
        root.AddChild(seal);
    }

    /// <summary>One ruled line of the log, the text sitting on the rule.</summary>
    Label Entry(Container parent, string text, Font f, int size, Color colour)
    {
        var l = Parchment.L(text, "");
        l.AddThemeFontOverride("font", f);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        l.CustomMinimumSize = new Vector2(0, Row);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        parent.AddChild(l);
        ruled.Add(l);
        return l;
    }

    void Layout()
    {
        if (page == null || earned == null || blot == null) return;   // Resized fires while the page is still being built
        var s = root.Size;
        if (s.X < 10) return;
        // Rows shrink on a short screen (UI scale 150 %) so the whole page always fits: 18 rows + margins.
        float rows = 16 + (earned.Visible ? 2 : 0);
        row = Mathf.Clamp((s.Y - 24 - 60 - 54) / rows, 25, Row);
        foreach (var c in ruled) c.CustomMinimumSize = new Vector2(0, row);
        head.CustomMinimumSize = new Vector2(0, row * 2);
        earned.CustomMinimumSize = new Vector2(0, row * 2);
        title.Position = new Vector2(0, row * 2 - 54);
        earnedRow.Position = new Vector2(0, row - 4);
        page.RulePitch = row;
        page.RuleTop = 30 + row * 2;
        page.Size = Vector2.Zero;
        var min = page.GetCombinedMinimumSize();
        page.Size = new Vector2(Mathf.Min(s.X - 32, 940), Mathf.Min(s.Y - 24, min.Y));
        page.Position = ((s - page.Size) / 2).Floor();
        page.QueueRedraw();
        var p = page.Position;
        float strip = page.Size.X - 250;   // x where the marginalia strip begins
        blot.Size = blot.CustomMinimumSize;
        blot.Position = p + new Vector2(page.Size.X - 110, 26);
        seal.Size = seal.CustomMinimumSize;
        seal.PivotOffset = seal.Size / 2;
        seal.Position = p + new Vector2(strip + 40, 30 + row * 3.2f);
        float sk = Mathf.Min(200, page.Size.Y - (30 + row * 3.2f + 170) - 40);
        sinking.CustomMinimumSize = new Vector2(sk * 0.87f, sk);
        sinking.Size = sinking.CustomMinimumSize;
        sinking.Position = p + new Vector2(strip + (250 - sinking.Size.X) / 2 - 10, page.Size.Y - sk - 34);
    }

    public void Open(World w, bool isBest, IReadOnlyList<string> newAchievements)
    {
        var s = w.Stats;
        title.Text = Text.Get("LOGBOOK_TITLE", w.Player.ShipName);
        lost.Text = Text.Get("LOGBOOK_LOST", w.Day, Text.Get("WATCH_" + w.WatchIndex), Text.Get("PRESET_" + w.Preset.ToString().ToUpperInvariant()));
        cause.Text = Text.Get(w.CauseOfSinking.Length > 0 ? w.CauseOfSinking : "SUNK_SEA");
        string[] values =
        {
            Parchment.Days(w.DaysSurvived),
            s.GoldEarned.ToString(), s.ShipsSunk.ToString(), w.Player.PortsVisited.Count.ToString(),
            s.LeaguesSailed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            s.BestTrade > 0 ? Text.Get("LOGBOOK_TRADE_VALUE", s.BestTrade, Text.Get("GOOD_" + s.BestTradeGood)) : Text.Get("LOGBOOK_NONE"),
            s.TreasuresDug.ToString(), s.CovesFound.ToString(),
        };
        for (int i = 0; i < rows.Count; i++) rows[i].Value = values[i];
        best.Visible = isBest;
        seal.Visible = isBest;
        best.Text = Text.Get("LOGBOOK_BEST", Text.Get("PRESET_" + w.Preset.ToString().ToUpperInvariant()));
        foreach (var c in earnedRow.GetChildren()) c.QueueFree();
        earned.Visible = newAchievements.Count > 0;
        foreach (var a in newAchievements)
        {
            earnedRow.AddChild(Parchment.Picture(Parchment.AchievementIcon(a, true), 30, 30));
            earnedRow.AddChild(Parchment.L(Text.Get("ACH_" + a + "_NAME"), "Head"));
        }
        root.Visible = true;
        Layout();
        again.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() => root.Visible = false;
    public void PressNewVoyage() => NewVoyage?.Invoke();
}

/// <summary>A log line: the item in italic, a dotted leader, the figure on the right; the text sits on the ruled line.</summary>
public partial class LeaderLine : Control
{
    string key = "", value = "";
    public string Key { get => key; set { key = value; QueueRedraw(); } }
    public string Value { get => value; set { this.value = value; QueueRedraw(); } }
    public LeaderLine() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        float y = Size.Y - 7;
        var it = Fonts.Italic;
        var body = Fonts.Body;
        float kw = it.GetStringSize(key, HorizontalAlignment.Left, -1, 20).X;
        float vw = body.GetStringSize(value, HorizontalAlignment.Left, -1, 21).X;
        DrawString(it, new Vector2(0, y), key, HorizontalAlignment.Left, -1, 20, Parchment.Muted);
        DrawString(body, new Vector2(Size.X - vw - 6, y), value, HorizontalAlignment.Left, -1, 21, Ink.Black);
        // The leader: evenly spaced dots between the two, on the line.
        var dots = new List<Vector2>();
        for (float x = kw + 12; x < Size.X - vw - 16; x += 9)
        {
            dots.Add(new Vector2(x, y - 3));
            dots.Add(new Vector2(x + 1.6f, y - 3));
        }
        if (dots.Count > 0) DrawMultiline(dots.ToArray(), Ink.Black with { A = 0.55f }, 1.6f);
    }
}
