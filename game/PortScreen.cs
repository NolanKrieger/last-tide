using System.Globalization;
using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The port ledger that opens on docking (GDD §13): Market, Shipwright, Tavern, Harbour office, Cast off.
/// Every button issues a <see cref="PortCommand"/> to the sim and the page re-reads the world. Prices shown are the
/// prices charged: standing and the quartermaster are applied exactly as <see cref="World"/> applies them.
/// </summary>
public partial class PortScreen : CanvasLayer
{
    World world = null!;
    Font font = null!;
    Control root = null!;
    UiSheet sheet = null!;
    Button[] tabs = Array.Empty<Button>();
    Button castOff = null!;
    Control[] pages = Array.Empty<Control>();
    int page;
    public event Action? CastOff;

    // Header
    TextureRect crest = null!;
    Label factionName = null!, standingLine = null!, warning = null!, goldLine = null!, holdLine = null!;
    StandingMeter meter = null!;
    UiCartouche title = null!;
    UiGauge holdGauge = null!;
    // Footer
    Label storesLine = null!, crewLine = null!, message = null!, tabsHint = null!;

    // Market
    LedgerTable ledger = null!;
    ScrollContainer ledgerScroll = null!;
    Good selected = Good.Provisions;
    TextureRect detailIcon = null!;
    Label[] elsewhere = Array.Empty<Label>();
    Label detailRole = null!;
    Label detailName = null!, detailGroup = null!, detailBuy = null!, detailSell = null!, detailStock = null!, detailHeld = null!, detailHint = null!, detailQty = null!;
    UiGauge detailStockGauge = null!;
    PanelContainer detail = null!;
    Label marketHint = null!;
    VBoxContainer wrightLeft = null!, hullSection = null!;
    ScrollContainer hullScroll = null!;
    GridContainer hullGrid = null!;
    bool hullsBeside = true;
    HSlider qty = null!;
    Button buy = null!, sell = null!;
    // Shipwright
    Label repairLine = null!, repairCost = null!, cannonLine = null!, tradeInLine = null!, blackHead = null!;
    UiGauge hullGauge = null!;
    GunPorts gunPorts = null!;
    Button repairAll = null!, buyCannon = null!, sellCannon = null!;
    GridContainer partGrid = null!;
    readonly List<(Part Part, UiPips Pips, Button Buy)> partRows = new();
    readonly List<(PanelContainer Card, Label Name, Label Effect, Button Buy)> blackRows = new();
    readonly List<(HullDef Hull, PanelContainer Card, Label Name, Button Buy)> hullRows = new();
    // Tavern
    Label crewTavern = null!, mapLine = null!, officerHead = null!;
    UiFigures figures = null!;
    Button hire1 = null!, hire5 = null!, buyMap = null!;
    readonly List<(OfficerType Type, Label Name, UiPips Tier, Label Effect, Label Terms, Label Aboard, Button Hire, Button Dismiss)> officerRows = new();
    // Harbour office
    Label boardEmpty = null!, heldHead = null!, heldNone = null!, bountyLine = null!, arrivalLine = null!;
    PanelContainer arrivalCard = null!;
    Label[] receiptLines = Array.Empty<Label>();
    readonly List<(PanelContainer Card, TextureRect Icon, Label Title, Label Cargo, Label Terms, Button Sign)> offerRows = new();
    readonly List<(PanelContainer Card, Label Title, Label Detail, Button Abandon)> contractRows = new();

    public bool IsOpen => Visible;
    public int Page => page;
    public Good Selected => selected;
    /// <summary>The market's quantity slider (the self-test drags it through the real input path).</summary>
    public HSlider Quantity => qty;
    public Button BuyButton => buy;
    public Button SellButton => sell;
    public LedgerTable Ledger => ledger;
    public Button[] Tabs => tabs;
    public string BuyText => buy.Text;
    public string CannonOffer => buyCannon.Text;
    public string HullOffer(string id) => hullRows.First(h => h.Hull.Id == id).Buy.Text;
    public Button HireFive => hire5;
    /// <summary>The harbour office's Sign button for a board slot, and Give up for a contract in hand (self-test).</summary>
    public Button SignButton(int slot) => offerRows[slot].Sign;
    public Button AbandonButton(int index) => contractRows[index].Abandon;
    /// <summary>The tavern's Hire button for an officer type (the self-test clicks the cartographer's).</summary>
    public Button OfficerHireButton(OfficerType type) => officerRows.First(r => r.Type == type).Hire;
    public Button MoreButton = null!, MaxButton = null!;

    public void Init(World w, Font f)
    {
        world = w;
        font = f;
        Layer = 12;
        Visible = false;
    }

    bool built;

    /// <summary>The ledger is built the first time the ship docks, not when a voyage starts (keeps the voyage start quick).</summary>
    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = Parchment.Theme(font) };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.Resized += Layout;
        AddChild(root);
        // The sea is not drawn under the ledger (Main hides it): the sheet lies on the dark chart table.
        root.AddChild(Parchment.Backdrop(0.9f, 1f, new Color(0.13f, 0.1f, 0.08f)));
        sheet = new UiSheet();
        root.AddChild(sheet);
        var col = Parchment.Column(6);
        sheet.AddChild(col);

        headerInset = Inset(BuildHeader());
        col.AddChild(headerInset);
        col.AddChild(BuildTabs());
        pages = new Control[] { BuildMarket(), BuildShipwright(), BuildTavern(), BuildOffice() };
        foreach (var p in pages)
        {
            p.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            col.AddChild(p);
        }
        col.AddChild(new UiDivider(false, 6));
        footInset = Inset(BuildFooter());
        col.AddChild(footInset);
        ShowPage(0);
    }

    MarginContainer headerInset = null!, footInset = null!;

    static MarginContainer Inset(Control c)
    {
        var m = new MarginContainer();
        m.AddChild(c);
        return m;
    }

    void Layout()
    {
        if (sheet == null || partGrid == null) return;   // Resized fires while the page is still being built
        var s = root.Size;
        if (s.X < 10) return;
        float m = Mathf.Clamp(s.X * 0.012f, 8, 24);
        sheet.Position = new Vector2(m, m);
        sheet.Size = s - new Vector2(2 * m, 2 * m);
        bool small = s.Y < 800, narrow = s.X < 1400;
        // A short page (UI scale 125–150 %) gives the sheet's rim less room and drops what the detail pane can spare.
        if (small)
        {
            var pad = new StyleBoxEmpty { ContentMarginLeft = 30, ContentMarginRight = 30, ContentMarginTop = 26, ContentMarginBottom = 24 };
            sheet.AddThemeStyleboxOverride("panel", pad);
        }
        else sheet.RemoveThemeStyleboxOverride("panel");
        // On a short page the rim is thin, so the purse and the footer lines step in clear of the corner scrollwork.
        foreach (var inset in new[] { headerInset, footInset })
            foreach (var side in new[] { "margin_left", "margin_right" })
                inset.AddThemeConstantOverride(side, small ? 26 : 4);
        title.PlateHeight = small ? 66 : 100;
        title.FontSize = small ? 28 : 40;
        crest.CustomMinimumSize = small ? new Vector2(40, 50) : new Vector2(60, 74);
        detailIcon.CustomMinimumSize = small ? new Vector2(56, 56) : new Vector2(104, 104);
        detail.CustomMinimumSize = new Vector2(narrow ? 330 : 380, 0);
        marketHint.Visible = !small;
        tabsHint.Visible = s.X >= 1250;
        // The hull catalogue: its own column on a wide page, under the parts on a narrow one.
        bool beside = !narrow;
        if (beside != hullsBeside)
        {
            hullsBeside = beside;
            hullSection.GetParent().RemoveChild(hullSection);
            if (beside) hullScroll.AddChild(hullSection);
            else wrightLeft.AddChild(hullSection);
            hullScroll.Visible = beside;
        }
        float leftW = beside ? (s.X - 110) * 0.56f : s.X - 110;
        partGrid.Columns = Math.Clamp((int)(leftW / 250), 1, 3);
        hullGrid.Columns = beside ? 1 : Math.Clamp((int)(leftW / 430), 1, 2);
    }

    // ------------------------------------------------------------------ header, tabs, footer

    Control BuildHeader()
    {
        var header = Parchment.Row(14);
        var left = Parchment.Row(10);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.SizeFlagsStretchRatio = 1;
        header.AddChild(left);
        crest = Parchment.Picture(null, 60, 74);
        left.AddChild(crest);
        var standing = Parchment.Column(0);
        standing.Alignment = BoxContainer.AlignmentMode.Center;
        left.AddChild(standing);
        factionName = Parchment.L("", "Head");
        standing.AddChild(factionName);
        standingLine = Parchment.L("", "Flavour");
        standing.AddChild(standingLine);
        meter = new StandingMeter();
        standing.AddChild(meter);
        warning = Parchment.L("", "Warn");
        standing.AddChild(warning);

        title = new UiCartouche { PlateHeight = 100, FontSize = 40, MaxWidth = 620 };
        header.AddChild(title);

        var right = Parchment.Column(2);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.Alignment = BoxContainer.AlignmentMode.Center;
        header.AddChild(right);
        var purse = Parchment.Row(8);
        purse.Alignment = BoxContainer.AlignmentMode.End;
        right.AddChild(purse);
        purse.AddChild(Parchment.Picture(Parchment.Tex("icon-gold"), 36, 30));
        goldLine = Parchment.L("", "Big", HorizontalAlignment.Right);
        purse.AddChild(goldLine);
        arrivalLine = Parchment.L("", "Caption", HorizontalAlignment.Right);
        right.AddChild(arrivalLine);
        var hold = Parchment.Row(8);
        hold.Alignment = BoxContainer.AlignmentMode.End;
        right.AddChild(hold);
        hold.AddChild(Parchment.Picture(Parchment.Tex("icon-crate"), 26, 26));
        holdLine = Parchment.L("", "Data", HorizontalAlignment.Right);
        hold.AddChild(holdLine);
        holdGauge = new UiGauge(150, 12) { Ticks = 10, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        hold.AddChild(holdGauge);
        return header;
    }

    Control BuildTabs()
    {
        var bar = Parchment.Row(4);
        var names = new[] { "PORT_TAB_MARKET", "PORT_TAB_SHIPWRIGHT", "PORT_TAB_TAVERN", "PORT_TAB_OFFICE" };
        tabs = new Button[names.Length];
        var group = new ButtonGroup();
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            tabs[i] = Parchment.B(Text.Get(names[i]), () => ShowPage(idx), "RibbonTab");
            tabs[i].ToggleMode = true;
            tabs[i].ButtonGroup = group;
            tabs[i].CustomMinimumSize = new Vector2(170, 0);
            bar.AddChild(tabs[i]);
        }
        tabsHint = Parchment.L(Text.Get("PORT_TABS_HINT"), "Caption");
        tabsHint.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var hint = tabsHint;
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        bar.AddChild(hint);
        castOff = Parchment.B(Text.Get("PORT_TAB_CASTOFF"), () => CastOff?.Invoke(), "CastOff");
        castOff.Icon = Parchment.Tex("icon-anchor");
        castOff.ExpandIcon = true;
        castOff.CustomMinimumSize = new Vector2(190, 0);
        bar.AddChild(castOff);
        return bar;
    }

    Control BuildFooter()
    {
        var foot = Parchment.Row(16);
        storesLine = Parchment.L("", "Flavour");
        foot.AddChild(storesLine);
        message = Parchment.L("", "Warn", HorizontalAlignment.Center);
        message.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        message.AddThemeFontSizeOverride("font_size", 19);
        foot.AddChild(message);
        crewLine = Parchment.L("", "Flavour", HorizontalAlignment.Right);
        foot.AddChild(crewLine);
        return foot;
    }

    // ------------------------------------------------------------------ market

    Control BuildMarket()
    {
        var box = Parchment.Row(18);
        var left = Parchment.Column(0);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddChild(left);
        ledger = new LedgerTable();
        left.AddChild(new LedgerHeader(ledger));
        ledgerScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = false };
        left.AddChild(ledgerScroll);
        ledgerScroll.AddChild(ledger);
        ledger.Picked += g => { Audio.Ui(); Select(g); };
        ledger.Scroll = ledgerScroll;

        detail = new PanelContainer { ThemeTypeVariation = "Well", CustomMinimumSize = new Vector2(380, 0) };
        box.AddChild(detail);
        var d = Parchment.Column(6);
        detail.AddChild(d);
        // The good's particulars scroll on a short page; the quantity and the Buy/Sell buttons always stay in view.
        var infoScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        d.AddChild(infoScroll);
        var info = Parchment.Column(6);
        info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        infoScroll.AddChild(info);
        var top = Parchment.Row(12);
        info.AddChild(top);
        detailIcon = Parchment.Picture(null, 104, 104);
        top.AddChild(detailIcon);
        var names = Parchment.Column(0);
        names.Alignment = BoxContainer.AlignmentMode.Center;
        names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(names);
        detailName = Parchment.L("", "Big");
        names.AddChild(detailName);
        detailGroup = Parchment.L("", "Caption");
        detailGroup.AutowrapMode = TextServer.AutowrapMode.WordSmart;   // "Ship's consumables · compact, 4 to a slot" on a narrow pane
        names.AddChild(detailGroup);
        info.AddChild(new UiDivider(true, 18));
        var prices = new GridContainer { Columns = 2 };
        prices.AddThemeConstantOverride("h_separation", 14);
        info.AddChild(prices);
        prices.AddChild(Parchment.L(Text.Get("PORT_COL_BUY"), "Caption"));
        detailBuy = Parchment.L("", "Data");
        prices.AddChild(detailBuy);
        prices.AddChild(Parchment.L(Text.Get("PORT_COL_SELL"), "Caption"));
        detailSell = Parchment.L("", "Data");
        prices.AddChild(detailSell);
        prices.AddChild(Parchment.L(Text.Get("PORT_COL_STOCK"), "Caption"));
        var stockRow = Parchment.Row(8);
        prices.AddChild(stockRow);
        detailStock = Parchment.L("", "Data");
        stockRow.AddChild(detailStock);
        detailStockGauge = new UiGauge(90, 9) { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Fill = new Color(0.35f, 0.45f, 0.55f) };
        stockRow.AddChild(detailStockGauge);
        prices.AddChild(Parchment.L(Text.Get("PORT_COL_HELD"), "Caption"));
        detailHeld = Parchment.L("", "Data");
        prices.AddChild(detailHeld);
        detailRole = Parchment.L("", "Flavour");
        info.AddChild(detailRole);
        info.AddChild(new UiDivider(false, 6));
        info.AddChild(Parchment.L(Text.Get("PORT_ELSEWHERE"), "Caption"));
        elsewhere = new Label[4];
        for (int i = 0; i < elsewhere.Length; i++)
        {
            elsewhere[i] = Parchment.L("", "Flavour");
            elsewhere[i].TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            info.AddChild(elsewhere[i]);
        }
        detailHint = Parchment.L("", "Flavour");
        detailHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detailHint.CustomMinimumSize = new Vector2(300, 0);
        info.AddChild(detailHint);
        var qrow = Parchment.Row(6);
        d.AddChild(qrow);
        qrow.AddChild(Parchment.B("−", () => qty.Value -= 1, "Flat", 24));
        qty = new HSlider { MinValue = 1, MaxValue = 1, Step = 1, Value = 1, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, 30) };
        qty.ValueChanged += _ => RefreshDetail();
        qrow.AddChild(qty);
        MoreButton = Parchment.B("+", () => qty.Value += 1, "Flat", 24);
        qrow.AddChild(MoreButton);
        // All the way in one click: as much as the purse, the hold and the stock allow, or everything she holds (Sell
        // never sells more than she has, so the one setting serves both).
        MaxButton = Parchment.B(Text.Get("PORT_QTY_MAX"), () => qty.Value = qty.MaxValue, "Flat", 18);
        qrow.AddChild(MaxButton);
        detailQty = Parchment.L("", "Big");
        detailQty.CustomMinimumSize = new Vector2(64, 0);
        detailQty.HorizontalAlignment = HorizontalAlignment.Right;
        qrow.AddChild(detailQty);
        var buttons = Parchment.Row(8);
        d.AddChild(buttons);
        buy = Parchment.B("", () => Do(new PortCommand(PortAction.Buy, selected, (int)qty.Value)));
        buy.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        buttons.AddChild(buy);
        sell = Parchment.B("", () => Do(new PortCommand(PortAction.Sell, selected, Math.Min((int)qty.Value, world.Player.Units(selected)))));
        sell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        buttons.AddChild(sell);
        marketHint = Parchment.L(Text.Get("PORT_MARKET_HINT"), "Flavour");
        marketHint.AddThemeFontSizeOverride("font_size", 15);
        marketHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        marketHint.CustomMinimumSize = new Vector2(300, 0);
        d.AddChild(marketHint);
        return box;
    }

    // ------------------------------------------------------------------ shipwright

    Control BuildShipwright()
    {
        var box = Parchment.Row(20);
        var leftScroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.3f, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        box.AddChild(leftScroll);
        var left = Parchment.Column(8);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftScroll.AddChild(left);

        // Repairs and guns side by side (one above the other when the page is narrow).
        var top = Flow(10);
        left.AddChild(top);
        var repairCard = new PanelContainer { ThemeTypeVariation = "Card", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        top.AddChild(repairCard);
        var rc = Parchment.Row(10);
        repairCard.AddChild(rc);
        rc.AddChild(Parchment.Picture(Parchment.Tex("icon-hammer"), 44, 44));
        var rtext = Parchment.Column(3);
        rtext.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rc.AddChild(rtext);
        rtext.AddChild(Parchment.L(Text.Get("PORT_REPAIRS"), "Head"));
        repairLine = Parchment.L("", "Data");
        rtext.AddChild(repairLine);
        hullGauge = new UiGauge(180, 11) { Fill = Parchment.Sepia };
        rtext.AddChild(hullGauge);
        repairCost = Parchment.L("", "Flavour");
        rtext.AddChild(repairCost);
        repairAll = Parchment.B(Text.Get("PORT_REPAIR_ALL"), () => Do(new PortCommand(PortAction.Repair)));
        rtext.AddChild(repairAll);

        var gunCard = new PanelContainer { ThemeTypeVariation = "Card", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        top.AddChild(gunCard);
        var gc = Parchment.Row(10);
        gunCard.AddChild(gc);
        gc.AddChild(Parchment.Picture(Parchment.Tex("station-guns"), 44, 44));
        var gtext = Parchment.Column(3);
        gtext.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        gc.AddChild(gtext);
        gtext.AddChild(Parchment.L(Text.Get("PORT_GUNS"), "Head"));
        cannonLine = Parchment.L("", "Data");
        gtext.AddChild(cannonLine);
        gunPorts = new GunPorts();
        gtext.AddChild(gunPorts);
        var gb = Flow(6);
        gtext.AddChild(gb);
        buyCannon = Parchment.B(Text.Get("PORT_BUY_CANNON", World.CannonPrice), () => Do(new PortCommand(PortAction.BuyCannon)));
        sellCannon = Parchment.B(Text.Get("PORT_SELL_CANNON", World.CannonPrice / 2), () => Do(new PortCommand(PortAction.SellCannon)));
        gb.AddChild(buyCannon);
        gb.AddChild(sellCannon);

        left.AddChild(Parchment.L(Text.Get("PORT_PARTS"), "Head"));
        partGrid = new GridContainer { Columns = 3 };
        partGrid.AddThemeConstantOverride("h_separation", 10);
        partGrid.AddThemeConstantOverride("v_separation", 10);
        left.AddChild(partGrid);
        foreach (var def in PartDef.All)
        {
            var card = new PanelContainer { ThemeTypeVariation = "Card", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            partGrid.AddChild(card);
            var c = Parchment.Column(4);
            card.AddChild(c);
            var head = Parchment.Row(8);
            c.AddChild(head);
            head.AddChild(Parchment.Picture(Parchment.PartIcon(def.Id), 44, 44));
            var nt = Parchment.Column(2);
            head.AddChild(nt);
            var name = Parchment.L(Text.Get("PART_" + def.Key), "Head");
            name.AddThemeFontSizeOverride("font_size", 19);
            nt.AddChild(name);
            var pips = new UiPips();
            nt.AddChild(pips);
            var effect = Parchment.L(Text.Get("PART_" + def.Key + "_EFFECT"), "Flavour");
            effect.AddThemeFontSizeOverride("font_size", 15);
            effect.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            effect.CustomMinimumSize = new Vector2(210, 40);
            c.AddChild(effect);
            var part = def.Id;
            var b = Parchment.B("", () => Do(new PortCommand(PortAction.BuyPart, Amount: (int)part)));
            c.AddChild(b);
            partRows.Add((part, pips, b));
        }

        blackHead = Parchment.L(Text.Get("PORT_BLACK_MARKET"), "Head");
        blackHead.AddThemeColorOverride("font_color", Ink.Red);
        left.AddChild(blackHead);
        left.MoveChild(blackHead, 0);
        for (int i = 0; i < 2; i++)
        {
            var card = new PanelContainer { ThemeTypeVariation = "CardOn" };
            left.AddChild(card);
            left.MoveChild(card, 1 + i);
            var r = Parchment.Row(10);
            card.AddChild(r);
            r.AddChild(Parchment.Picture(Parchment.Tex("seal-star"), 40, 40));
            var t = Parchment.Column(0);
            t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            r.AddChild(t);
            var name = Parchment.L("", "Head");
            t.AddChild(name);
            var effect = Parchment.L("", "Flavour");
            t.AddChild(effect);
            int idx = i;
            var b = Parchment.B("", () => { var stock = world.BlackMarketHere; if (idx < stock.Count) Do(new PortCommand(PortAction.BuyUnique, Text: stock[idx].Key)); });
            b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            r.AddChild(b);
            blackRows.Add((card, name, effect, b));
        }

        wrightLeft = left;
        // Hulls: a catalogue drawn to scale, smallest first. On a wide page it has its own column; on a narrow one
        // (UI scale 125–150 %) it moves under the parts (Layout).
        hullScroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        box.AddChild(hullScroll);
        hullSection = Parchment.Column(4);
        hullSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hullScroll.AddChild(hullSection);
        var right = hullSection;
        var hh = Parchment.Row(10);
        right.AddChild(hh);
        hh.AddChild(Parchment.L(Text.Get("PORT_HULLS"), "Head"));
        tradeInLine = Parchment.L("", "Flavour");
        tradeInLine.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        tradeInLine.HorizontalAlignment = HorizontalAlignment.Right;
        hh.AddChild(tradeInLine);
        hullGrid = new GridContainer { Columns = 1, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        hullGrid.AddThemeConstantOverride("h_separation", 8);
        hullGrid.AddThemeConstantOverride("v_separation", 6);
        right.AddChild(hullGrid);
        var list = hullGrid;
        double longest = Hulls.All.Max(h => h.Length);
        foreach (var hull in Hulls.All)
        {
            var card = new PanelContainer { ThemeTypeVariation = "Card", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            list.AddChild(card);
            var r = Parchment.Row(10);
            card.AddChild(r);
            r.AddChild(new HullSketch(hull, (float)(hull.Length / longest), () => world.Player.Loadout[Cosmetics.SlotIndex("hull")]));
            var t = Parchment.Column(0);
            t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            r.AddChild(t);
            var name = Parchment.L(Text.Get("HULL_" + hull.Id), "Head");
            t.AddChild(name);
            var stats = Parchment.L(Text.Get("PORT_HULL_STATS", hull.HullHp, hull.Cargo, hull.CrewMax, hull.GunsPerSide * 2), "Caption");
            stats.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            t.AddChild(stats);
            var niche = Parchment.L(Text.Get("PORT_HULL_NICHE", hull.Speed.ToString("0.00", CultureInfo.InvariantCulture), hull.PointDeg, Text.Get("RIG_" + hull.Rig.ToString().ToLowerInvariant()), hull.OfficerSlots), "Flavour");
            niche.AutowrapMode = TextServer.AutowrapMode.WordSmart;   // wraps on a narrow page rather than losing its end
            t.AddChild(niche);
            var id = hull.Id;
            Button b = null!;
            b = Parchment.B("", () => TwoStep(b, () => Do(new PortCommand(PortAction.BuyHull, Text: id))));
            b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            b.CustomMinimumSize = new Vector2(120, 0);
            r.AddChild(b);
            hullRows.Add((hull, card, name, b));
        }
        return box;
    }

    // ------------------------------------------------------------------ tavern

    Control BuildTavern()
    {
        var box = Parchment.Row(20);
        var leftScroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        box.AddChild(leftScroll);
        var left = Parchment.Column(10);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftScroll.AddChild(left);

        var hands = Card(left, Art.Tex("people/deckhand"), 72, 90, out var ht);
        crewTavern = Parchment.L("", "Head");
        ht.AddChild(crewTavern);
        figures = new UiFigures { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Figure = 34, CustomMinimumSize = new Vector2(160, 36) };
        ht.AddChild(figures);
        ht.AddChild(Parchment.L(Text.Get("PORT_HIRE_NOTE", World.SigningFee), "Flavour"));
        var hr = Flow(6);
        ht.AddChild(hr);
        hire1 = Parchment.B(Text.Get("PORT_HIRE", 1, World.SigningFee), () => Do(new PortCommand(PortAction.Hire, Good.Provisions, 1)));
        hire5 = Parchment.B(Text.Get("PORT_HIRE", 5, World.SigningFee * 5), () => Do(new PortCommand(PortAction.Hire, Good.Provisions, Math.Min(5, world.Ship.Hull.CrewMax - world.Ship.Crew))));
        hr.AddChild(hire1);
        hr.AddChild(hire5);

        var map = Card(left, Parchment.Tex("icon-bottle"), 64, 72, out var mt);
        mt.AddChild(Parchment.L(Text.Get("PORT_MAP_HEAD"), "Head"));
        mapLine = Parchment.L("", "Flavour");
        mapLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        mapLine.CustomMinimumSize = new Vector2(300, 0);
        mt.AddChild(mapLine);
        buyMap = Parchment.B(Text.Get("PORT_MAP_BUY", World.BottleMapPrice), () => Do(new PortCommand(PortAction.BuyMap)));
        buyMap.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        mt.AddChild(buyMap);

        var rightScroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        box.AddChild(rightScroll);
        var right = Parchment.Column(8);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightScroll.AddChild(right);
        officerHead = Parchment.L("", "Head");
        right.AddChild(officerHead);
        string[] portraits = { "lookout", "marines", "quartermaster", "cartographer" };
        // The cartographer first: without him the chart stays shut, so he is the one a new captain looks for.
        foreach (var type in Enum.GetValues<OfficerType>().OrderBy(t => t == OfficerType.Cartographer ? 0 : 1))
        {
            var card = new PanelContainer { ThemeTypeVariation = "Card" };
            right.AddChild(card);
            var r = Parchment.Row(12);
            card.AddChild(r);
            r.AddChild(Parchment.Picture(Parchment.Portrait(portraits[(int)type]), 72, 90));
            var t = Parchment.Column(2);
            t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            r.AddChild(t);
            var nameRow = Parchment.Row(10);
            t.AddChild(nameRow);
            var name = Parchment.L("", "Head");
            nameRow.AddChild(name);
            var tier = new UiPips { Max = 3, Pip = 14, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            nameRow.AddChild(tier);
            var effect = Parchment.L("", "Flavour");
            effect.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            effect.CustomMinimumSize = new Vector2(240, 0);
            t.AddChild(effect);
            var terms = Parchment.L("", "Caption");
            t.AddChild(terms);
            var aboard = Parchment.L("", "Warn");
            t.AddChild(aboard);
            var bcol = Parchment.Column(4);
            bcol.Alignment = BoxContainer.AlignmentMode.Center;
            r.AddChild(bcol);
            var hire = Parchment.B("", () => { var o = world.TavernOfficers(world.Docked!).First(x => x.Type == type); Do(new PortCommand(PortAction.HireOfficer, Amount: (int)type * 10 + o.Tier)); });
            Button dismiss = null!;
            dismiss = Parchment.B(Text.Get("PORT_DISMISS"), () => TwoStep(dismiss, () => Do(new PortCommand(PortAction.DismissOfficer, Amount: (int)type))), "Flat");
            bcol.AddChild(hire);
            bcol.AddChild(dismiss);
            officerRows.Add((type, name, tier, effect, terms, aboard, hire, dismiss));
        }
        return box;
    }

    // ------------------------------------------------------------------ harbour office

    Control BuildOffice()
    {
        var box = Parchment.Row(20);
        var leftScroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.2f, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        box.AddChild(leftScroll);
        var left = Parchment.Column(8);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftScroll.AddChild(left);
        left.AddChild(Parchment.L(Text.Get("PORT_OFFICE_BOARD"), "Head"));
        var note = Parchment.L(Text.Get("PORT_OFFICE_BOARD_NOTE"), "Flavour");
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(300, 0);
        left.AddChild(note);
        for (int i = 0; i < 3; i++)
        {
            var card = new PanelContainer { ThemeTypeVariation = "Card" };
            left.AddChild(card);
            var r = Parchment.Row(12);
            card.AddChild(r);
            var icon = Parchment.Picture(null, 56, 56);
            r.AddChild(icon);
            var t = Parchment.Column(2);
            t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            r.AddChild(t);
            var title = Parchment.L("", "Head");
            t.AddChild(title);
            var cargo = Parchment.L("", "Caption");
            cargo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            cargo.CustomMinimumSize = new Vector2(220, 0);
            t.AddChild(cargo);
            var terms = Parchment.L("", "Flavour");
            terms.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            terms.CustomMinimumSize = new Vector2(220, 0);
            t.AddChild(terms);
            int slot = i;
            var sign = Parchment.B("", () => Do(new PortCommand(PortAction.SignContract, Amount: slot)));
            sign.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            sign.CustomMinimumSize = new Vector2(130, 0);
            r.AddChild(sign);
            offerRows.Add((card, icon, title, cargo, terms, sign));
        }
        boardEmpty = Parchment.L(Text.Get("PORT_OFFICE_EMPTY"), "Flavour");
        left.AddChild(boardEmpty);

        var rightScroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        box.AddChild(rightScroll);
        var right = Parchment.Column(8);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightScroll.AddChild(right);
        heldHead = Parchment.L("", "Head");
        right.AddChild(heldHead);
        for (int i = 0; i < World.MaxContracts; i++)
        {
            var card = new PanelContainer { ThemeTypeVariation = "CardOn" };
            right.AddChild(card);
            var r = Parchment.Row(10);
            card.AddChild(r);
            var t = Parchment.Column(0);
            t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            r.AddChild(t);
            var title = Parchment.L("", "Head");
            title.AddThemeFontSizeOverride("font_size", 19);
            title.AutowrapMode = TextServer.AutowrapMode.WordSmart;   // the deadline is the line's last words: never cut it
            title.CustomMinimumSize = new Vector2(220, 0);
            t.AddChild(title);
            var detail = Parchment.L("", "Flavour");
            detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            detail.CustomMinimumSize = new Vector2(220, 0);
            t.AddChild(detail);
            int index = i;
            Button abandon = null!;
            abandon = Parchment.B(Text.Get("PORT_CONTRACT_ABANDON"), () => TwoStep(abandon, () => Do(new PortCommand(PortAction.AbandonContract, Amount: index))), "Flat");
            abandon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            r.AddChild(abandon);
            contractRows.Add((card, title, detail, abandon));
        }
        heldNone = Parchment.L(Text.Get("PORT_OFFICE_NONE_HELD"), "Flavour");
        right.AddChild(heldNone);

        Card(right, Parchment.Crest(Faction.Crown, false), 48, 60, out var bt);
        bt.AddChild(Parchment.L(Text.Get("PORT_BOUNTY_HEAD"), "Head"));
        bountyLine = Parchment.L("", "Flavour");
        bountyLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        bountyLine.CustomMinimumSize = new Vector2(240, 0);
        bt.AddChild(bountyLine);


        arrivalCard = Card(right, Parchment.Tex("icon-gold"), 48, 40, out var at);
        at.AddChild(Parchment.L(Text.Get("PORT_ARRIVAL_HEAD"), "Head"));
        receiptLines = new Label[5];
        for (int i = 0; i < receiptLines.Length; i++)
        {
            receiptLines[i] = Parchment.L("", "Flavour");
            receiptLines[i].AutowrapMode = TextServer.AutowrapMode.WordSmart;
            receiptLines[i].CustomMinimumSize = new Vector2(240, 0);
            at.AddChild(receiptLines[i]);
        }
        return box;
    }

    static Texture2D? ContractIcon(ContractKind k) => Parchment.Tex(k switch { ContractKind.Dispatch => "icon-quill", ContractKind.Contraband => "icon-tankard", _ => "icon-crate" });

    /// <summary>One line of the arrival account: what the office paid (or customs took) as she came alongside.</summary>
    string ReceiptText(Receipt r) => r.Key switch
    {
        "RECEIPT_DELIVERED" or "RECEIPT_SMUGGLED" => Text.Get(r.Key, r.Gold, world.Map.Ports[r.Arg].Name),
        "RECEIPT_BOUNTY" => Text.Get(r.Key, r.Gold, r.Arg),
        "RECEIPT_CAUGHT" => Text.Get(r.Key, -r.Gold),
        _ => Text.Get(r.Key, r.Gold),
    };

    void RefreshOffice(Port port)
    {
        var player = world.Player;
        var ship = world.Ship;
        var offers = world.ContractOffers(port);
        bool any = false, full = player.Contracts.Count >= World.MaxContracts;
        for (int i = 0; i < offerRows.Count; i++)
        {
            var (card, icon, otitle, cargo, terms, sign) = offerRows[i];
            var c = i < offers.Length ? offers[i] : null;
            card.Visible = c != null;
            if (c == null) continue;
            any = true;
            var dest = world.Map.Ports[c.To];
            string key = ContractDef.Of(c.Kind).Key;
            bool smuggle = c.Kind == ContractKind.Contraband;
            icon.Texture = ContractIcon(c.Kind);
            otitle.Text = Text.Get("PORT_OFFER_TITLE", Text.Get("CONTRACT_" + key), dest.Name);
            otitle.AddThemeColorOverride("font_color", smuggle ? Ink.Red : Ink.Black);
            double days = world.SeaDistance(c.From, c.To) / World.PassageMetresPerDay;
            cargo.Text = Text.Get("PORT_OFFER_CARGO_" + key, c.Slots, Text.Get("REGION_" + RegionDef.Of(dest.Region).Key), Parchment.N(Math.Max(0.5, Math.Round(days * 2) / 2), "0.#"));
            terms.Text = smuggle ? Text.Get("PORT_OFFER_TERMS_SMUGGLE", c.Pay, Parchment.DayWatch(c.Deadline))
                : Text.Get("PORT_OFFER_TERMS", c.Pay, c.Advance, Parchment.DayWatch(c.Deadline));
            bool room = player.SlotsUsed + c.Slots <= ship.CargoCapacity + 1e-9;
            sign.Text = full ? Text.Get("PORT_OFFER_FULL") : !room ? Text.Get("PORT_OFFER_NOROOM") : Text.Get("PORT_OFFER_SIGN");
            sign.Disabled = full || !room;
        }
        boardEmpty.Visible = !any;
        heldHead.Text = Text.Get("PORT_OFFICE_HELD", player.Contracts.Count, World.MaxContracts);
        for (int i = 0; i < contractRows.Count; i++)
        {
            var (card, ctitle, detail, _) = contractRows[i];
            card.Visible = i < player.Contracts.Count;
            if (!card.Visible) continue;
            var c = player.Contracts[i];
            string key = ContractDef.Of(c.Kind).Key;
            ctitle.Text = Text.Get("PORT_CONTRACT_LINE", Text.Get("CONTRACT_" + key), world.Map.Ports[c.To].Name, Parchment.DayWatch(c.Deadline));
            ctitle.AddThemeColorOverride("font_color", c.Kind == ContractKind.Contraband ? Ink.Red : Ink.Black);
            detail.Text = Text.Get("PORT_CONTRACT_DETAIL_" + key, c.Slots, c.Pay - c.Advance)
                + (c.Advance > 0 ? " " + Text.Get("PORT_CONTRACT_FORFEIT", c.Advance) : "");
        }
        heldNone.Visible = player.Contracts.Count == 0;
        bountyLine.Text = player.BountyOwed > 0 ? Text.Get("PORT_BOUNTY_OWED", player.BountyOwed, player.BountyShips) : Text.Get("PORT_BOUNTY_NONE");
        var receipts = world.DockReceipts;
        arrivalCard.Visible = receipts.Count > 0;
        for (int i = 0; i < receiptLines.Length; i++)
        {
            receiptLines[i].Visible = i < receipts.Count;
            if (i < receipts.Count) receiptLines[i].Text = ReceiptText(receipts[i]);
        }
        int net = receipts.Sum(r => r.Gold);
        arrivalLine.Text = net > 0 ? Text.Get("PORT_ARRIVAL_GAIN", net) : net < 0 ? Text.Get("PORT_ARRIVAL_LOSS", -net) : "";
        arrivalLine.Visible = net != 0;
        arrivalLine.AddThemeColorOverride("font_color", net < 0 ? Ink.Red : Parchment.Muted);
    }

    /// <summary>A row that wraps onto a second line when there is no room (button rows, the repair and gun cards).</summary>
    static HFlowContainer Flow(int separation)
    {
        var f = new HFlowContainer();
        f.AddThemeConstantOverride("h_separation", separation);
        f.AddThemeConstantOverride("v_separation", separation);
        return f;
    }

    static PanelContainer Card(Container parent, Texture2D? picture, float w, float h, out VBoxContainer text)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        parent.AddChild(card);
        var r = Parchment.Row(12);
        card.AddChild(r);
        r.AddChild(Parchment.Picture(picture, w, h));
        text = Parchment.Column(4);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        r.AddChild(text);
        return card;
    }

    // ------------------------------------------------------------------ open, tabs, commands

    public void Open()
    {
        EnsureBuilt();
        Visible = true;
        Layout();
        message.Text = "";
        var port = world.Docked;
        sheet.Aged = port is { Secret: true };
        // At a cove the ledger opens on the first rarity it deals in (they head the list); elsewhere on provisions.
        selected = port is { Secret: true } && Goods.All.FirstOrDefault(g => Goods.IsRare(g.Id) && port.Market.Stock[(int)g.Id] >= 1) is { } rare ? rare.Id : Good.Provisions;
        qty.SetValueNoSignal(1);
        sheet.QueueRedraw();
        Show(0);
        Refresh();
        ledger.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() => Visible = false;

    public void Show(int idx)
    {
        EnsureBuilt();
        ShowPage(idx);
    }

    void ShowPage(int idx)
    {
        page = idx;
        for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == idx;
        for (int i = 0; i < tabs.Length; i++) tabs[i].SetPressedNoSignal(i == idx);
        Refresh();
        // The keyboard follows the page: the ledger on the market, the first live button elsewhere (hiding a page drops the focus).
        if (Visible) (idx == 0 ? ledger : FirstLive(pages[idx]) ?? (Control)tabs[idx]).CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>The first visible, enabled button on a page, in tree order (for the keyboard).</summary>
    static Button? FirstLive(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is Button { Disabled: false } b && b.IsVisibleInTree() && b.FocusMode != Control.FocusModeEnum.None) return b;
            if (c is Control { Visible: false }) continue;
            if (FirstLive(c) is { } found) return found;
        }
        return null;
    }

    /// <summary>The control holding the keyboard focus, for the self-test.</summary>
    public Control? FocusOwner => root.GetViewport().GuiGetFocusOwner();
    public bool FocusOnPage(int idx) => FocusOwner is { } f && pages[idx].IsAncestorOf(f);

    public override void _UnhandledKeyInput(InputEvent e)
    {
        // Q / E page through the ledger's tabs (the broadside keys have nothing to fire in port).
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        // The broadside keys (Q / E unless rebound) turn the ledger's pages: there is nothing to fire in port.
        string? action = Settings.Current.ActionOf(k.Keycode);
        if (action == "FirePort") { Audio.Ui(); ShowPage((page + pages.Length - 1) % pages.Length); GetViewport().SetInputAsHandled(); }
        else if (action == "FireStarboard") { Audio.Ui(); ShowPage((page + 1) % pages.Length); GetViewport().SetInputAsHandled(); }
    }

    public void Select(Good g)
    {
        EnsureBuilt();
        selected = g;
        qty.SetValueNoSignal(1);
        ledger.Selected = g;
        Refresh();
        ledger.KeepVisible();
    }

    Button? armed;
    string armedText = "";
    ulong armedAt;

    /// <summary>
    /// What cannot be taken back (a new hull traded against hers, a contract given up, an officer dismissed) asks once
    /// more: the first press turns the button into a red "Confirm?", a second press within four seconds acts.
    /// </summary>
    void TwoStep(Button b, Action act)
    {
        if (armed == b && Time.GetTicksMsec() - armedAt < 4000) { Disarm(); act(); return; }
        Disarm();
        armed = b;
        armedText = b.Text;
        ulong at = armedAt = Time.GetTicksMsec();
        b.Text = Text.Get("PORT_CONFIRM");
        b.AddThemeColorOverride("font_color", Ink.Red);
        b.AddThemeColorOverride("font_hover_color", Ink.Red);
        b.AddThemeColorOverride("font_focus_color", Ink.Red);
        GetTree().CreateTimer(4).Timeout += () => { if (armed == b && armedAt == at) Disarm(); };
    }

    void Disarm()
    {
        if (armed is not { } b) return;
        armed = null;
        if (!IsInstanceValid(b)) return;
        b.Text = armedText;
        b.RemoveThemeColorOverride("font_color");
        b.RemoveThemeColorOverride("font_hover_color");
        b.RemoveThemeColorOverride("font_focus_color");
    }

    /// <summary>Issues a command and shows why it was refused, if it was.</summary>
    public PortResult Do(PortCommand cmd)
    {
        EnsureBuilt();
        var r = world.Apply(cmd);
        message.Text = r == PortResult.Ok ? "" : Text.Get("PORT_RESULT_" + r.ToString().ToUpperInvariant());
        if (r == PortResult.Ok && cmd.Action is PortAction.Buy or PortAction.Sell or PortAction.BuyPart or PortAction.BuyHull or PortAction.Repair or PortAction.BuyUnique or PortAction.BuyCannon or PortAction.HireOfficer or PortAction.Hire or PortAction.SignContract)
            Audio.Instance?.Play("coins", 0.55, 0.1);
        Refresh();
        return r;
    }

    /// <summary>What the next <paramref name="n"/> units cost here: the sim's own quote (standing and the quartermaster).</summary>
    public int BuyCost(Good g, int n) => world.BuyQuote(g, n);
    /// <summary>What <paramref name="n"/> units fetch here: the sim's own quote (it also caps goods bought at this very port).</summary>
    public int SellValue(Good g, int n) => world.SellQuote(g, n);
    int UnitBuy(Port p, Good g) => (int)Math.Round(p.Market.Price(g) * world.BuyPriceMult(p));
    /// <summary>One unit's sale price: the sim's quote for a unit in the hold, else the market's price with standing.</summary>
    int UnitSell(Port p, Good g) => world.Player.Units(g) > 0 ? world.SellQuote(g, 1) : (int)Math.Round(p.Market.SellPrice(g) * world.SellPriceMult(p));

    public void Refresh()
    {
        if (!built || world.Docked is not { } port) return;
        Disarm();
        var player = world.Player;
        var ship = world.Ship;

        tabsHint.Text = Text.Get("PORT_TABS_HINT");   // key names follow the bindings

        // Header: who owns the port and how they regard her.
        title.Title = port.Name;
        double rep = player.Rep(port.Faction);
        var ink = port.Secret ? Ink.Red : Ink.Faction(port.Faction);
        crest.Texture = Parchment.Crest(port.Faction, port.Secret);
        crest.SelfModulate = ink;
        factionName.Text = port.Secret ? Text.Get("CHART_COVE") : Text.Get("FACTION_" + port.Faction.ToString().ToLowerInvariant());
        factionName.AddThemeColorOverride("font_color", ink);
        string standingWord = rep >= 20 ? Text.Get("STANDING_FRIENDLY") : rep <= -20 ? Text.Get("STANDING_HOSTILE") : Text.Get("STANDING_NEUTRAL");
        standingLine.Text = port.Secret ? Text.Get("PORT_COVE_LINE") : Text.Get("PORT_STANDING_LINE", standingWord, rep);
        title.Colour = port.Secret ? Ink.Red : Ink.Black;
        meter.Visible = !port.Secret;
        meter.Value = rep;
        warning.Text = !port.Secret && rep <= -35 ? Text.Get("PORT_SHUT_WARNING") : "";
        warning.Visible = warning.Text.Length > 0;
        goldLine.Text = Text.Get("PORT_GOLD", player.Gold);
        holdLine.Text = Text.Get("PORT_HOLD_SHORT", player.SlotsUsed.ToString("0.#", CultureInfo.InvariantCulture), ship.CargoCapacity);
        holdGauge.Max = ship.CargoCapacity;
        holdGauge.Value = player.SlotsUsed;
        storesLine.Text = Text.Get("PORT_STORES", player.Units(Good.Provisions), player.Units(Good.Munitions), player.Units(Good.Timber));
        crewLine.Text = Text.Get("PORT_CREW", ship.Crew, ship.Hull.CrewMax, world.DailyWages(), world.DailyProvisions);

        // Market ledger.
        var m = port.Market;
        var rows = new List<LedgerRow>();
        // At a cove the rarities it deals in head the ledger; elsewhere the goods keep the table's order.
        var order = port.Secret ? Goods.All.OrderBy(g => Goods.IsRare(g.Id) ? 0 : 1).ToArray() : Goods.All;
        foreach (var g in order)
        {
            int held = player.Units(g.Id);
            if (Goods.IsRare(g.Id) && !port.Secret && held == 0) continue;   // rare goods trade only at coves
            if (!Goods.IsTraded(g.Id) && held == 0) continue;   // the catch: only while she has some to sell
            int trend = 2;
            if (port.PricesLastVisit is { } last && last[(int)g.Id] is { } prev)
            {
                double pnow = m.Price(g.Id);
                trend = pnow > prev * 1.05 ? 1 : pnow < prev * 0.95 ? -1 : 0;
            }
            var (note, noteColour) = Note(port, g.Id, held);
            rows.Add(new LedgerRow(g.Id, Text.Get("GOOD_" + g.Key), UnitBuy(port, g.Id), UnitSell(port, g.Id), trend,
                Text.Get("STOCK_" + m.StockWord(g.Id)), (float)(m.Stock[(int)g.Id] / Math.Max(1, m.Target[(int)g.Id])), held, note, noteColour,
                port.Produces_(g.Id) ? 1 : port.Consumes_(g.Id) ? -1 : 0));
        }
        ledger.SetRows(rows, selected);
        RefreshDetail();

        // Shipwright.
        double missing = ship.MaxHp - ship.HullHp;
        repairLine.Text = Text.Get("PORT_HULL_HP", Math.Round(ship.HullHp), Math.Round(ship.MaxHp));
        hullGauge.Max = ship.MaxHp;
        hullGauge.Value = ship.HullHp;
        hullGauge.Fill = ship.HullHp < ship.MaxHp * 0.4 ? Ink.Red : Parchment.Sepia;
        repairCost.Text = Text.Get("PORT_REPAIR_COST", world.RepairCostPerHp.ToString("0.0", CultureInfo.InvariantCulture), (int)Math.Ceiling(missing * world.RepairCostPerHp))
            + (ship.TornSails ? "  " + Text.Get("PORT_TORN", World.TornSailRepair) : "");
        repairAll.Disabled = missing <= 0 && !ship.TornSails;
        cannonLine.Text = Text.Get("PORT_CANNONS", ship.Cannons, ship.Hull.GunsPerSide * 2, PartDef.PounderByGrade[ship.CannonGrade]);
        gunPorts.Set(ship.Cannons, ship.Hull.GunsPerSide * 2);
        foreach (var (part, pips, b) in partRows)
        {
            int g = ship.Grade(part);
            pips.Count = g;
            if (g >= PartDef.MaxGrade) { b.Text = Text.Get("PORT_PART_MAX"); b.Disabled = true; }
            else { int price = world.PartPrice(part); b.Text = Text.Get("PORT_PART_BUY", g + 1, price); b.Disabled = price > player.Gold; }
        }
        var black = world.BlackMarketHere;
        blackHead.Visible = black.Count > 0;
        for (int i = 0; i < blackRows.Count; i++)
        {
            var (card, name, effect, b) = blackRows[i];
            card.Visible = i < black.Count;
            if (!card.Visible) continue;
            var def = black[i];
            bool owned = world.HasUnique(def.Key);
            name.Text = Text.Get("UNIQUE_" + def.Key);
            effect.Text = Text.Get("UNIQUE_" + def.Key + "_EFFECT");
            b.Text = owned ? Text.Get("PORT_OWNED") : Text.Get("PORT_UNIQUE_BUY", def.Price);
            b.Disabled = owned || player.Gold < def.Price;
        }
        int tradeIn = world.TradeInValue();
        tradeInLine.Text = Text.Get("PORT_TRADE_IN", tradeIn);
        foreach (var (hull, card, name, b) in hullRows)
        {
            bool mine = hull.Id == ship.Hull.Id;
            card.ThemeTypeVariation = mine ? "CardOn" : "Card";
            if (mine) { b.Text = Text.Get("PORT_HULL_YOURS_SHORT"); b.Disabled = true; }
            else
            {
                int price = world.HullPrice(hull), refund = world.HullRefund(hull);
                bool fits = world.HullFits(hull), sold = world.HullSoldHere(hull);
                b.Text = !sold ? Text.Get("PORT_HULL_NOTSOLD") : !fits ? Text.Get("PORT_HULL_NOROOM") : refund > 0 ? Text.Get("PORT_HULL_SWAP", refund) : Text.Get("PORT_HULL_BUY_SHORT", price);
                b.Disabled = !sold || !fits || price > player.Gold;
            }
        }
        buyCannon.Text = Text.Get("PORT_BUY_CANNON", world.CannonCost);
        buyCannon.Disabled = ship.Cannons >= ship.Hull.GunsPerSide * 2 || player.Gold < world.CannonCost;
        sellCannon.Disabled = ship.Cannons <= 0;

        // Tavern.
        crewTavern.Text = Text.Get("PORT_TAVERN_CREW", ship.Crew, ship.Hull.CrewMax);
        figures.Count = ship.Crew;
        figures.Wanted = Math.Max(0, ship.Hull.CrewMax - ship.Crew);
        // The second button hires as many as the berths allow, up to five, and needs the fee for all of them.
        int berths = Math.Max(0, ship.Hull.CrewMax - ship.Crew), five = Math.Clamp(berths, 1, 5);
        hire1.Disabled = berths == 0 || player.Gold < World.SigningFee;
        hire5.Text = Text.Get("PORT_HIRE", five, World.SigningFee * five);
        hire5.Visible = five > 1;
        hire5.Disabled = berths == 0 || player.Gold < World.SigningFee * five;
        var site = world.TavernMap(port);
        if (site == null)
        {
            mapLine.Text = Text.Get("PORT_MAP_NONE");
            buyMap.Disabled = true;
        }
        else
        {
            mapLine.Text = Text.Get("PORT_MAP_LINE", Text.Get("REGION_IN_" + RegionDef.Of(world.Map.RegionAt(site.Pos).Type).Key), player.BottleMaps.Count);
            buyMap.Disabled = player.Gold < World.BottleMapPrice;
        }
        officerHead.Text = Text.Get("PORT_OFFICERS", player.SlottedOfficers, ship.Hull.OfficerSlots);
        var offers = world.TavernOfficers(port);
        foreach (var (type, name, tier, effect, terms, aboard, hire, dismiss) in officerRows)
        {
            var offer = offers.First(o => o.Type == type);
            var mine = player.OfficerOf(type);
            string key = type.ToString().ToLowerInvariant();
            name.Text = Text.Get("PORT_OFFICER_NAME", Text.Get("OFFICER_" + key), Text.Get("TIER_" + Officers.TierKey[offer.Tier]));
            tier.Count = offer.Tier + 1;
            effect.Text = Text.Get("OFFICER_" + key + "_EFFECT_" + offer.Tier);
            int price = Officers.PriceOf(type, offer.Tier);
            terms.Text = Text.Get("PORT_OFFICER_TERMS", price, Officers.WageOf(type, offer.Tier));
            aboard.Text = mine == null ? "" : Text.Get("PORT_OFFICER_HAVE", Text.Get("TIER_" + Officers.TierKey[mine.Tier]));
            aboard.Visible = mine != null;
            hire.Text = Text.Get("PORT_OFFICER_HIRE", price);
            hire.Disabled = player.Gold < price || (mine != null && mine.Tier >= offer.Tier)
                || (mine == null && Officers.UsesSlot(type) && player.SlottedOfficers >= ship.Hull.OfficerSlots);   // the cartographer berths apart
            dismiss.Disabled = mine == null;
        }

        RefreshOffice(port);
    }

    /// <summary>
    /// The ledger's last column: for goods in the hold, the margin per unit against what she paid; otherwise the best
    /// price remembered elsewhere when it beats buying here (her own ledger: prices seen in port or heard from a merchant
    /// who traded there, never hidden state).
    /// </summary>
    (string, Color) Note(Port port, Good g, int held)
    {
        var player = world.Player;
        if (held > 0 && player.CostBasis[(int)g] > 0)
        {
            double paid = player.CostBasis[(int)g] / held;
            int margin = (int)Math.Round(UnitSell(port, g) - paid);
            return (Text.Get("PORT_NOTE_MARGIN", margin > 0 ? "+" + margin : margin.ToString(CultureInfo.InvariantCulture)), margin >= 0 ? Ink.Black : Ink.Red);
        }
        var best = BestElsewhere(port, g);
        if (best != null && Math.Round(best.Price * Market.Spread) > UnitBuy(port, g))
            return (Text.Get("PORT_NOTE_ELSEWHERE", Math.Round(best.Price * Market.Spread)), Ink.Wind);
        return ("", Ink.Black);
    }

    LedgerEntry? BestElsewhere(Port port, Good g)
    {
        LedgerEntry? best = null;
        foreach (var e in world.Player.Ledger)
            if (e.Good == g && e.Port != port.Id && (best == null || e.Price > best.Price)) best = e;
        return best;
    }

    void RefreshDetail()
    {
        if (world.Docked is not { } port) return;
        var m = port.Market;
        var player = world.Player;
        var g = Goods.Of(selected);
        int held = player.Units(selected);
        double room = (world.Ship.CargoCapacity - player.SlotsUsed) / g.SlotsPerUnit;
        int canBuy = (int)Math.Floor(room + 1e-9);
        canBuy = Math.Min(canBuy, (int)Math.Floor(m.Stock[(int)selected]));
        if (Goods.IsRare(selected) && !port.Secret) canBuy = 0;
        if (!Goods.IsTraded(selected)) canBuy = 0;   // ports buy fish, they don't sell it
        // The largest n she can pay for (cost rises with n: binary search, each quote priced unit by unit).
        int lo = 0, hi = Math.Max(0, canBuy);
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (BuyCost(selected, mid) <= player.Gold) lo = mid; else hi = mid - 1;
        }
        int affordable = lo;
        int max = Math.Max(1, Math.Max(affordable, held));
        qty.MaxValue = max;
        if (qty.Value > max) qty.SetValueNoSignal(max);
        int n = (int)qty.Value;
        int nSell = Math.Min(n, held);

        detailIcon.Texture = Parchment.GoodIcon(selected);
        detailName.Text = Text.Get("GOOD_" + g.Key);
        detailGroup.Text = Text.Get("GROUP_" + g.Group.ToString().ToUpperInvariant())
            + (g.SlotsPerUnit >= 2 ? " " + Text.Get("PORT_BULKY") : g.SlotsPerUnit < 1 ? " " + Text.Get("PORT_COMPACT", Math.Round(1 / g.SlotsPerUnit)) : "");
        detailBuy.Text = Text.Get("PORT_EACH", UnitBuy(port, selected));
        detailSell.Text = Text.Get("PORT_EACH", UnitSell(port, selected));
        detailStock.Text = Text.Get("STOCK_" + m.StockWord(selected));
        detailStockGauge.Max = 2;
        detailStockGauge.Value = m.Stock[(int)selected] / Math.Max(1, m.Target[(int)selected]);
        detailHeld.Text = held > 0 && player.CostBasis[(int)selected] > 0
            ? Text.Get("PORT_HELD_PAID", held, Math.Round(player.CostBasis[(int)selected] / held))
            : Text.Get("PORT_HELD_ONLY", held);
        detailRole.Text = port.Produces_(selected) ? Text.Get("PORT_ROLE_MAKES") : port.Consumes_(selected) ? Text.Get("PORT_ROLE_WANTS") : Text.Get("PORT_ROLE_NEITHER");
        // The best prices she has on record elsewhere (seen in port, or heard from a merchant hailed at sea), dearest first.
        var known = player.Ledger.Where(e => e.Good == selected && e.Port != port.Id).OrderByDescending(e => e.Price).Take(elsewhere.Length).ToList();
        for (int i = 0; i < elsewhere.Length; i++)
        {
            elsewhere[i].Visible = i < known.Count;
            if (i >= known.Count) continue;
            var e = known[i];
            elsewhere[i].Text = e.Heard ? Text.Get("PORT_KNOWN_HEARD", world.Map.Ports[e.Port].Name, Math.Round(e.Price * Market.Spread), Math.Floor(e.Day) + 1, e.Teller)
                : Text.Get("PORT_KNOWN_SEEN", world.Map.Ports[e.Port].Name, Math.Round(e.Price * Market.Spread), Math.Floor(e.Day) + 1);
            elsewhere[i].AddThemeColorOverride("font_color", Math.Round(e.Price * Market.Spread) > UnitBuy(port, selected) ? Ink.Black : Parchment.Muted);
        }
        detailHint.Visible = known.Count == 0;
        detailHint.Text = Text.Get("PORT_HINT_NONE");
        detailQty.Text = "×" + n;
        buy.Text = n <= affordable ? Text.Get("PORT_BUY_FOR", n, BuyCost(selected, n)) : Text.Get("PORT_BUY_CANT", n);
        sell.Text = held > 0 ? Text.Get("PORT_SELL_FOR", nSell, SellValue(selected, nSell)) : Text.Get("PORT_SELL_NONE");
        buy.Disabled = n > affordable;
        sell.Disabled = held <= 0;
    }
}

/// <summary>One line of the market ledger, already priced for this port and this captain.</summary>
public readonly record struct LedgerRow(Good Good, string Name, int Buy, int Sell, int Trend, string Stock, float StockRatio, int Held, string Note, Color NoteColour, int Role);

/// <summary>
/// The market as a merchant's ledger: ruled lines, red money columns, the good's picture, prices, trend, stock and
/// what she holds. Drawn by hand in one control (a handful of batched draw calls for 27 rows); click or arrow keys pick
/// a row; the wheel scrolls its <see cref="ScrollContainer"/>.
/// </summary>
public partial class LedgerTable : Control
{
    public const float RowH = 30;
    static readonly string Makes = Text.Get("PORT_ROLE_MAKES_SHORT"), Wants = Text.Get("PORT_ROLE_WANTS_SHORT");
    List<LedgerRow> rows = new();
    int hover = -1;
    public Good Selected;
    public ScrollContainer? Scroll;
    public event Action<Good>? Picked;
    public int RowCount => rows.Count;

    public LedgerTable()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Pass;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
    }

    public void SetRows(List<LedgerRow> r, Good selected)
    {
        rows = r;
        Selected = selected;
        CustomMinimumSize = new Vector2(380, rows.Count * RowH + 4);
        QueueRedraw();
    }

    /// <summary>Column edges for a given width: icon, name, buy, sell, trend, stock, here (made/wanted), held, note.</summary>
    public static float[] Columns(float w)
    {
        float held = 56, stock = Mathf.Clamp(w * 0.15f, 90, 150), trend = 52, money = 64, icon = 34, here = Mathf.Clamp(w * 0.09f, 62, 96);
        float name = Mathf.Clamp(w * 0.26f, 120, 220);
        float note = w - icon - name - 2 * money - trend - stock - here - held;
        // A narrow ledger (UI scale 150 %) drops the notes, then "here", then tightens: the detail pane says both anyway.
        if (note < 60) { note = 0; name = Math.Max(110, w - icon - 2 * money - trend - stock - here - held); }
        if (icon + name + 2 * money + trend + stock + here + held > w) { here = 0; name = Math.Max(110, w - icon - 2 * money - trend - stock - held); }
        if (icon + name + 2 * money + trend + stock + held > w) { trend = 34; stock = 76; money = 56; name = Math.Max(96, w - icon - 2 * money - trend - stock - held); }
        float x = 0;
        var edges = new float[10];
        float[] widths = { icon, name, money, money, trend, stock, here, held, note };
        for (int i = 0; i < widths.Length; i++) { edges[i] = x; x += widths[i]; }
        edges[9] = x;
        return edges;
    }

    int IndexOf(Good g) => rows.FindIndex(r => r.Good == g);

    /// <summary>The centre of a good's row in canvas coordinates, or null when it is not listed (test hook for real clicks).</summary>
    public Vector2? RowCentre(Good g)
    {
        int i = IndexOf(g);
        return i < 0 ? null : GetGlobalRect().Position + new Vector2(Size.X * 0.3f, i * RowH + RowH / 2);
    }

    public void KeepVisible()
    {
        if (Scroll == null) return;
        int i = IndexOf(Selected);
        if (i < 0) return;
        float top = i * RowH, bottom = top + RowH;
        if (top < Scroll.ScrollVertical) Scroll.ScrollVertical = (int)top;
        else if (bottom > Scroll.ScrollVertical + Scroll.Size.Y) Scroll.ScrollVertical = (int)(bottom - Scroll.Size.Y + 2);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion mm:
                int h = (int)(mm.Position.Y / RowH);
                if (h != hover) { hover = h < rows.Count ? h : -1; QueueRedraw(); }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb:
                int i = (int)(mb.Position.Y / RowH);
                if (i >= 0 && i < rows.Count) { GrabFocus(); Picked?.Invoke(rows[i].Good); AcceptEvent(); }
                break;
            case InputEventKey { Pressed: true } when e.IsActionPressed("ui_down", true):
                Step(1); AcceptEvent(); break;
            case InputEventKey { Pressed: true } when e.IsActionPressed("ui_up", true):
                Step(-1); AcceptEvent(); break;
            case InputEventKey { Pressed: true } when e.IsActionPressed("ui_page_down", true):
                Step(8); AcceptEvent(); break;
            case InputEventKey { Pressed: true } when e.IsActionPressed("ui_page_up", true):
                Step(-8); AcceptEvent(); break;
        }
    }

    void Step(int d)
    {
        if (rows.Count == 0) return;
        int i = Math.Clamp(IndexOf(Selected) + d, 0, rows.Count - 1);
        Picked?.Invoke(rows[i].Good);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && hover >= 0) { hover = -1; QueueRedraw(); }
    }

    public override void _Draw()
    {
        float w = Size.X;
        var c = Columns(w);
        int sel = IndexOf(Selected);
        // Row washes: the selection in ochre (red edge when the ledger has focus), the pointer's row fainter.
        if (hover >= 0 && hover != sel) DrawRect(new Rect2(0, hover * RowH, w, RowH), Parchment.Wash with { A = 0.16f });
        if (sel >= 0)
        {
            DrawRect(new Rect2(0, sel * RowH, w, RowH), Parchment.Wash);
            DrawRect(new Rect2(0, sel * RowH + 3, 4, RowH - 6), HasFocus() ? Ink.Red : Ink.Black);
        }
        // Stock gauges (one rect each: rects batch).
        float gx = c[5] + 6, gw = c[6] - c[5] - 16;   // (stock column)
        for (int i = 0; i < rows.Count; i++)
        {
            float y = i * RowH + RowH - 7;
            DrawRect(new Rect2(gx, y, gw, 3), Ink.Faint);
            DrawRect(new Rect2(gx, y, gw * Math.Clamp(rows[i].StockRatio / 2f, 0, 1), 3), new Color(0.35f, 0.45f, 0.55f, 0.75f));
        }
        // Ruling: a hairline under every row, the money columns between red double rules.
        var rules = new List<Vector2>(rows.Count * 2 + 2);
        for (int i = 1; i <= rows.Count; i++) { rules.Add(new Vector2(0, i * RowH)); rules.Add(new Vector2(w, i * RowH)); }
        DrawMultiline(rules.ToArray(), new Color(0.35f, 0.45f, 0.6f, 0.28f), 1f);
        var reds = new List<Vector2>();
        float bottom = Math.Max(rows.Count * RowH, 1);
        foreach (int k in new[] { 2, 4 })
        {
            reds.Add(new Vector2(c[k] - 3, 0)); reds.Add(new Vector2(c[k] - 3, bottom));
            reds.Add(new Vector2(c[k], 0)); reds.Add(new Vector2(c[k], bottom));
        }
        DrawMultiline(reds.ToArray(), Ink.Red with { A = 0.45f }, 1f);
        // Pictures: one atlas, one batch.
        for (int i = 0; i < rows.Count; i++)
            if (Parchment.GoodIcon(rows[i].Good) is AtlasTexture at)
                DrawTextureRectRegion(at.Atlas, new Rect2(c[0] + 3, i * RowH + 2, RowH - 4, RowH - 4), at.Region);
        // Trend marks: rising in red, falling in the wind's blue (both drawn as small inked chevrons).
        var up = new List<Vector2>();
        var down = new List<Vector2>();
        var level = new List<Vector2>();
        for (int i = 0; i < rows.Count; i++)
        {
            var mid = new Vector2((c[4] + c[5]) / 2 - 3, i * RowH + RowH / 2);
            switch (rows[i].Trend)
            {
                case 1: Chevron(up, mid, -1); break;
                case -1: Chevron(down, mid, 1); break;
                case 0: level.Add(mid + new Vector2(-5, 0)); level.Add(mid + new Vector2(5, 0)); break;
            }
        }
        if (up.Count > 0) DrawMultiline(up.ToArray(), Ink.Red, 2f);
        if (down.Count > 0) DrawMultiline(down.ToArray(), Ink.Wind, 2f);
        if (level.Count > 0) DrawMultiline(level.ToArray(), Ink.Soft, 1.5f);
        // Lettering: the body face first, then the italic face (one font texture per batch).
        var body = Fonts.Body;
        const int size = 18;
        float asc = body.GetAscent(size);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            float y = i * RowH + (RowH + asc) / 2 - 3;
            DrawString(body, new Vector2(c[1] + 6, y), r.Name, HorizontalAlignment.Left, c[2] - c[1] - 10, size, Ink.Black);
            DrawString(body, new Vector2(c[2], y), r.Buy.ToString(CultureInfo.InvariantCulture), HorizontalAlignment.Right, c[3] - c[2] - 10, size, Ink.Black);
            DrawString(body, new Vector2(c[3], y), r.Sell.ToString(CultureInfo.InvariantCulture), HorizontalAlignment.Right, c[4] - c[3] - 10, size, Ink.Black);
            if (r.Held > 0) DrawString(body, new Vector2(c[7], y), r.Held.ToString(CultureInfo.InvariantCulture), HorizontalAlignment.Right, c[8] - c[7] - 10, size, Ink.Black);
        }
        var it = Fonts.Italic;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            float y = i * RowH + (RowH + asc) / 2 - 5;
            DrawString(it, new Vector2(c[5] + 6, y), r.Stock, HorizontalAlignment.Left, c[6] - c[5] - 8, 16, Parchment.Muted);
            if (r.Role != 0 && c[7] - c[6] > 20) DrawString(it, new Vector2(c[6] + 6, y), r.Role > 0 ? Makes : Wants, HorizontalAlignment.Left, c[7] - c[6] - 8, 16, r.Role > 0 ? Ink.Wind : Ink.Red);
            if (r.Note.Length > 0 && c[9] - c[8] > 20) DrawString(it, new Vector2(c[8] + 6, y + 1), r.Note, HorizontalAlignment.Left, c[9] - c[8] - 6, 16, r.NoteColour);
        }
    }

    static void Chevron(List<Vector2> pts, Vector2 at, int dir)
    {
        // dir -1: points up; +1: points down.
        pts.Add(at + new Vector2(-5, -dir * 3)); pts.Add(at + new Vector2(0, dir * 3));
        pts.Add(at + new Vector2(0, dir * 3)); pts.Add(at + new Vector2(5, -dir * 3));
    }
}

/// <summary>The ledger's column heads, in small capitals over a double rule, aligned with <see cref="LedgerTable.Columns"/>.</summary>
public partial class LedgerHeader : Control
{
    readonly LedgerTable table;
    static readonly string[] Keys = { "", "PORT_COL_GOOD", "PORT_COL_BUY", "PORT_COL_SELL", "PORT_COL_TREND", "PORT_COL_STOCK", "PORT_COL_HERE", "PORT_COL_HELD", "PORT_COL_NOTE" };
    public LedgerHeader(LedgerTable t) { table = t; CustomMinimumSize = new Vector2(0, 30); MouseFilter = MouseFilterEnum.Ignore; }
    public LedgerHeader() : this(null!) { }

    public override void _Draw()
    {
        // Line up with the table below, which is narrower by the scroll bar when one shows.
        float w = table != null && table.Size.X > 0 ? table.Size.X : Size.X;
        var c = LedgerTable.Columns(w);
        var sc = Fonts.SmallCaps;
        for (int k = 1; k < Keys.Length; k++)
        {
            if (c[k + 1] - c[k] < 20) continue;   // a column the narrow ledger dropped
            var align = k is 2 or 3 or 7 ? HorizontalAlignment.Right : k == 4 ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            float x = c[k] + (align == HorizontalAlignment.Left ? 6 : 0);
            DrawString(sc, new Vector2(x, 20), Text.Get(Keys[k]), align, c[k + 1] - c[k] - (align == HorizontalAlignment.Right ? 10 : 6), 16, Parchment.Muted);
        }
        DrawLine(new Vector2(0, Size.Y - 4), new Vector2(w, Size.Y - 4), Ink.Black, 1.4f);
        DrawLine(new Vector2(0, Size.Y - 1), new Vector2(w, Size.Y - 1), Ink.Black with { A = 0.6f }, 0.8f);
    }

    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }
}

/// <summary>Standing with the port's faction, −100…+100: shut out (hatched red), hostile at sea, neutral, friendly (blue).</summary>
public partial class StandingMeter : Control
{
    double value;
    public double Value { get => value; set { this.value = value; QueueRedraw(); } }
    public StandingMeter() { CustomMinimumSize = new Vector2(230, 16); MouseFilter = MouseFilterEnum.Ignore; }

    float X(double v) => (float)((v + 100) / 200 * Size.X);

    public override void _Draw()
    {
        float h = Size.Y - 4;
        DrawRect(new Rect2(X(-100), 2, X(-50) - X(-100), h), Ink.Red with { A = 0.35f });
        DrawRect(new Rect2(X(-50), 2, X(-20) - X(-50), h), Ink.Red with { A = 0.14f });
        DrawRect(new Rect2(X(20), 2, X(100) - X(20), h), Ink.Wind with { A = 0.18f });
        var hatch = new List<Vector2>();
        for (float x = X(-100) + 4; x < X(-50); x += 6) { hatch.Add(new Vector2(x, 2 + h)); hatch.Add(new Vector2(x + 4, 2)); }
        DrawMultiline(hatch.ToArray(), Ink.Red with { A = 0.6f }, 1f);
        var ticks = new List<Vector2>();
        foreach (var v in new[] { -50.0, -20.0, 0.0, 20.0 }) { ticks.Add(new Vector2(X(v), 0)); ticks.Add(new Vector2(X(v), Size.Y)); }
        DrawMultiline(ticks.ToArray(), Ink.Black with { A = 0.55f }, 1f);
        DrawRect(new Rect2(0, 2, Size.X, h), Ink.Black, false, 1.2f);
        float x0 = X(Math.Clamp(value, -100, 100));
        DrawColoredPolygon(new[] { new Vector2(x0, 2 + h * 0.2f), new Vector2(x0 - 6, -3), new Vector2(x0 + 6, -3) }, Ink.Black);
        DrawColoredPolygon(new[] { new Vector2(x0, 2 + h * 0.8f), new Vector2(x0 - 6, Size.Y + 3), new Vector2(x0 + 6, Size.Y + 3) }, Ink.Black);
    }
}

/// <summary>Gun ports along a hull side: filled for a cannon aboard, open for an empty port (up to 24 drawn, then a count).</summary>
public partial class GunPorts : Control
{
    int guns, ports;
    public GunPorts() { CustomMinimumSize = new Vector2(180, 18); MouseFilter = MouseFilterEnum.Ignore; }
    public void Set(int g, int p) { guns = g; ports = p; QueueRedraw(); }

    public override void _Draw()
    {
        int shown = Math.Min(ports, 24);
        float step = Math.Min(16, (Size.X - 4) / Math.Max(1, shown));
        for (int i = 0; i < shown; i++)
        {
            var r = new Rect2(2 + i * step, 3, step - 4, Size.Y - 6);
            if (i < guns) DrawRect(r, Ink.Black);
            DrawRect(r, Ink.Black, false, 1.2f);
        }
    }
}

/// <summary>
/// A hull in plan view for the shipwright's catalogue, scaled to her length against the largest hull. Uses the
/// art-ships sprite (<c>assets/art/ships/&lt;id&gt;.png</c>, bow to the right) in her own livery (and the player's
/// hull colour, if one is chosen), with her shadow under her; else an inked outline.
/// </summary>
public partial class HullSketch : Control
{
    readonly HullDef hull;
    readonly float scale;
    readonly Func<string>? hullKey;
    public HullSketch(HullDef h, float lengthFraction, Func<string>? hullCosmetic = null) { hull = h; scale = lengthFraction; hullKey = hullCosmetic; CustomMinimumSize = new Vector2(156, 64); MouseFilter = MouseFilterEnum.Ignore; TextureFilter = TextureFilterEnum.LinearWithMipmaps; }
    public HullSketch() : this(Hulls.Sloop, 0.3f) { }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) QueueRedraw();   // the hull colour may have changed since
    }

    public override void _Draw()
    {
        // Lengths keep their order, but the small hulls are drawn big enough to see their paint and deck.
        float L = Mathf.Max(34, (Size.X - 8) * Mathf.Lerp(0.42f, 1f, scale));
        float B = L * (float)(hull.Beam / hull.Length);
        var c = Size / 2;
        var tex = ShipArt.Dressed(hull.Id, ShipArt.For(hull, true, Faction.FreeTraders, hullKey?.Invoke() ?? ""));
        if (tex != null)
        {
            float h = L * tex.GetHeight() / tex.GetWidth();
            var (shadow, pad) = ShipArt.Shadow(hull.Id);
            if (shadow != null)
                DrawTextureRect(shadow, new Rect2(c.X - L / 2 - pad.X * L + h * 0.1f, c.Y - h / 2 - pad.Y * h + h * 0.13f, L * (1 + 2 * pad.X), h * (1 + 2 * pad.Y)), false, new Color(0.13f, 0.14f, 0.17f, 0.3f));
            DrawTextureRect(tex, new Rect2(c.X - L / 2, c.Y - h / 2, L, h), false);
            return;
        }
        // Inked outline: a pointed bow, rounded quarters, the deck line and the masts as dots.
        var pts = new Vector2[18];
        for (int i = 0; i < pts.Length; i++)
        {
            float t = i / (float)(pts.Length - 1);
            float x = -L / 2 + L * t;
            float half = B / 2 * Mathf.Sqrt(Mathf.Max(0, 1 - Mathf.Pow(2 * t - 1, 2))) * (t > 0.55f ? 1 - (t - 0.55f) * 0.9f : 1);
            pts[i] = c + new Vector2(x, -half);
        }
        var outline = new List<Vector2>(pts);
        for (int i = pts.Length - 1; i >= 0; i--) outline.Add(new Vector2(pts[i].X, 2 * c.Y - pts[i].Y));
        var arr = outline.ToArray();
        DrawColoredPolygon(arr, Ink.Hull with { A = 0.85f });
        DrawPolyline(Ink.Closed(arr), Ink.Black, 1.4f, true);
        DrawLine(c + new Vector2(-L * 0.42f, 0), c + new Vector2(L * 0.38f, 0), Ink.Deck, 1f);
        int masts = hull.Length > 40 ? 3 : hull.Length > 22 ? 2 : 1;
        for (int k = 0; k < masts; k++)
            DrawCircle(c + new Vector2(-L * 0.2f + k * L * 0.28f - (masts - 1) * L * 0.05f, 0), Mathf.Max(1.5f, B * 0.08f), Ink.Black);
    }
}
