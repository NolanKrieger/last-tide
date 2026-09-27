using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>Esc at sea: resume, save and quit to the title (the suspend save, GDD §3), options, or quit the game.</summary>
public partial class PauseMenu : CanvasLayer
{
    public event Action? ResumePressed, SaveQuitPressed, QuitPressed, OptionsPressed;
    Control root = null!;
    UiSheet card = null!;
    Label where = null!;
    Button resume = null!, save = null!;
    public bool IsOpen => root.Visible;
    public string WhereText => where.Text;

    public void Init(Font font)
    {
        Layer = 13;
        root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = Parchment.Theme(font), Visible = false };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        root.AddChild(Parchment.Backdrop(0.28f, 0.72f));
        card = new UiSheet { CustomMinimumSize = new Vector2(470, 0) };
        root.AddChild(card);
        Parchment.Centre(card);
        var col = Parchment.Column(4);
        card.AddChild(col);
        col.AddChild(new UiCartouche { Title = Text.Get("PAUSE_TITLE"), PlateHeight = 88, FontSize = 38, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter });
        where = Parchment.L("", "Flavour", HorizontalAlignment.Center);
        where.AddThemeFontSizeOverride("font_size", 18);
        col.AddChild(where);
        col.AddChild(new UiDivider(true, 22));
        resume = Line(col, "PAUSE_RESUME", () => ResumePressed?.Invoke());
        save = Line(col, "PAUSE_SAVE_QUIT", () => SaveQuitPressed?.Invoke());
        Line(col, "PAUSE_OPTIONS", () => OptionsPressed?.Invoke());
        Line(col, "PAUSE_QUIT", () => QuitPressed?.Invoke());
    }

    static Button Line(Container parent, string key, Action a)
    {
        var b = Parchment.B(Text.Get(key), a, "MenuItem", 26);
        parent.AddChild(b);
        return b;
    }

    /// <summary>Opens the menu; with the world it also says where she is ("the Kestrel · Day 3 · forenoon watch · Rough Seas").</summary>
    public void Open(bool canSave, World? world = null)
    {
        save.Visible = canSave;
        where.Visible = world != null;
        if (world != null)
            where.Text = Text.Get("PAUSE_WHERE", world.Player.ShipName, Parchment.DayWatch(world.DaysSurvived), Text.Get("PRESET_" + world.Preset.ToString().ToUpperInvariant()));
        root.Visible = true;
        resume.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() => root.Visible = false;
    public void PressSaveQuit() => SaveQuitPressed?.Invoke();
    public void PressResume() => ResumePressed?.Invoke();
}
