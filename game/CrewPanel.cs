using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The crew panel (C): the four orders, and each station with its hands drawn as little sailors — filled for a hand at
/// the station, faded for a place still wanting one — and −/+ to move hands by hand (GDD §7).
/// </summary>
public partial class CrewPanel : CanvasLayer
{
    World world = null!;
    Font font = null!;
    Control root = null!;
    UiSheet sheet = null!;
    Label title = null!, orderLine = null!, note = null!, closeHint = null!;
    readonly Label[] names = new Label[4], counts = new Label[4];
    readonly UiFigures[] figures = new UiFigures[4];
    readonly Button[] plus = new Button[3], minus = new Button[3];
    readonly Button[] orders = new Button[4];
    static readonly string[] Keys = { "STATION_GUNS", "STATION_SAILS", "STATION_REPAIR", "STATION_PUMPS" };
    static readonly string[] Icons = { "station-guns", "station-sails", "station-repair", "station-pumps" };
    public bool IsOpen => Visible;
    public UiFigures Figures(int station) => figures[station];
    public Button Plus(int station) => plus[station];
    public Button OrderButton(int order) => orders[order];

    public void Init(World w, Font f)
    {
        world = w;
        font = f;
        Layer = 12;
        Visible = false;
        root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = Parchment.Theme(font) };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        root.AddChild(Parchment.Backdrop(0.18f, 0.6f));
        sheet = new UiSheet { CustomMinimumSize = new Vector2(760, 0) };
        root.AddChild(sheet);
        Parchment.Centre(sheet);
        var col = Parchment.Column(6);
        sheet.AddChild(col);

        var head = Parchment.Row(14);
        col.AddChild(head);
        title = Parchment.L("", "Title");
        head.AddChild(title);
        orderLine = Parchment.L("", "Flavour");
        orderLine.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        orderLine.AddThemeFontSizeOverride("font_size", 20);
        head.AddChild(orderLine);
        closeHint = Parchment.L(Text.Get("CREW_CLOSE"), "Caption");
        head.AddChild(closeHint);

        var ordersRow = Parchment.Row(6);
        col.AddChild(ordersRow);
        var group = new ButtonGroup();
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            orders[i] = Parchment.B(Text.Get("ORDER_" + ((CrewOrder)i).ToString().ToUpperInvariant()), () => { world.Apply(new PortCommand(PortAction.CrewOrder, Amount: idx)); Refresh(); });   // logged: replays see it
            orders[i].ToggleMode = true;
            orders[i].ButtonGroup = group;
            orders[i].SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            ordersRow.AddChild(orders[i]);
        }
        col.AddChild(new UiDivider(true, 20));

        for (int i = 0; i < 4; i++)
        {
            var row = Parchment.Row(12);
            col.AddChild(row);
            row.AddChild(Parchment.Picture(Parchment.Tex(Icons[i]), 52, 48));
            var text = Parchment.Column(0);
            text.CustomMinimumSize = new Vector2(150, 0);
            row.AddChild(text);
            names[i] = Parchment.L(Text.Get(Keys[i] + "_NAME"), "Head");
            text.AddChild(names[i]);
            counts[i] = Parchment.L("", "Flavour");
            text.AddChild(counts[i]);
            figures[i] = new UiFigures { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Figure = 48, CustomMinimumSize = new Vector2(300, 50), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            row.AddChild(figures[i]);
            if (i < 3)
            {
                int st = i;
                minus[i] = Parchment.B("−", () => Adjust(st, -1), "", 22);
                plus[i] = Parchment.B("+", () => Adjust(st, +1), "", 22);
                minus[i].CustomMinimumSize = plus[i].CustomMinimumSize = new Vector2(40, 36);
                minus[i].SizeFlagsVertical = plus[i].SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                row.AddChild(minus[i]);
                row.AddChild(plus[i]);
            }
            else row.AddChild(new Control { CustomMinimumSize = new Vector2(92, 0) });
        }
        col.AddChild(new UiDivider(false, 8));
        note = Parchment.L(Text.Get("CREW_NOTE"), "Flavour");
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(640, 0);
        col.AddChild(note);
    }

    /// <summary>An order key (1–4 by default) while the panel is up: the same as clicking that order.</summary>
    public void PressOrder(int order)
    {
        if (order < 0 || order > 3) return;
        Audio.Ui();
        world.Apply(new PortCommand(PortAction.CrewOrder, Amount: order));   // logged: replays see it
        Refresh();
        orders[order].GrabFocus();
    }

    void Adjust(int station, int delta)
    {
        var st = world.Ship.Stations();
        int guns = st[0], sails = st[1], repair = st[2];
        if (station == 0) guns += delta;
        if (station == 1) sails += delta;
        if (station == 2) repair += delta;
        world.Apply(World.CrewStationsCommand(guns, sails, repair));   // logged: replays see it
        Refresh();
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            Refresh();
            orders[(int)Math.Min((int)world.Ship.Order, 3)].CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    public void Close() => Visible = false;

    public void Refresh()
    {
        var ship = world.Ship;
        var st = ship.Stations();
        title.Text = Text.Get("CREW_HEAD", ship.Crew, ship.Hull.CrewMax);
        orderLine.Text = Text.Get("CREW_WAGES", world.DailyWages(), world.DailyProvisions);
        var need = new[] { ship.Cannons, ship.Hull.Riggers, Math.Max(1, (int)(ship.MaxHp / 100)), 0 };
        for (int i = 0; i < 4; i++)
        {
            counts[i].Text = i < 3 ? Text.Get("STATION_COUNT", st[i], need[i]) : Text.Get("STATION_REST", st[i]);
            counts[i].AddThemeColorOverride("font_color", i < 3 && st[i] < need[i] ? Ink.Red : Parchment.Muted);
            figures[i].Count = st[i];
            figures[i].Wanted = i < 3 ? Math.Max(0, need[i] - st[i]) : 0;
        }
        for (int i = 0; i < 4; i++)
        {
            orders[i].SetPressedNoSignal(ship.Order == (CrewOrder)i);
            orders[i].Text = Text.Get("ORDER_" + ((CrewOrder)i).ToString().ToUpperInvariant());   // key names follow the bindings
        }
        closeHint.Text = Text.Get("CREW_CLOSE");
        note.Text = Text.Get("CREW_NOTE");
        for (int i = 0; i < 3; i++)
        {
            minus[i].Disabled = st[i] <= 0;
            plus[i].Disabled = st[3] <= 0;
        }
    }
}
