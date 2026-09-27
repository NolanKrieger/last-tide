using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace LastTide;

/// <summary>
/// Options (polish bar): key bindings (keyboard + mouse only), volumes, resolution / fullscreen / UI scale,
/// the colorblind-safe palette and the onboarding-hints switch. Lives in <c>user://settings.json</c>.
/// </summary>
public sealed class Settings
{
    public static readonly string[] Actions = { "SailUp", "SailDown", "Port", "Starboard", "FirePort", "FireStarboard", "Crew", "Lantern", "Dock", "Chart", "Order1", "Order2", "Order3", "Order4" };
    public static readonly Dictionary<string, string> Defaults = new()
    {
        ["SailUp"] = "W", ["SailDown"] = "S", ["Port"] = "A", ["Starboard"] = "D", ["FirePort"] = "Q", ["FireStarboard"] = "E",
        ["Crew"] = "C", ["Lantern"] = "L", ["Dock"] = "F", ["Chart"] = "M", ["Order1"] = "Key1", ["Order2"] = "Key2", ["Order3"] = "Key3", ["Order4"] = "Key4",
    };
    public static readonly (int W, int H)[] Resolutions = { (1280, 720), (1600, 900), (1920, 1080), (2560, 1440) };

    public double Master { get; set; } = 1.0;
    public double Ambience { get; set; } = 0.8;
    public double Sfx { get; set; } = 0.9;
    public bool Fullscreen { get; set; }
    public int Width { get; set; } = 1600;
    public int Height { get; set; } = 900;
    public double UiScale { get; set; } = 1.0;
    public bool Colorblind { get; set; }
    public bool ShowHints { get; set; } = true;
    public Dictionary<string, string> Keys { get; set; } = new(Defaults);

    [JsonIgnore] public string Root { get; private set; } = "user://";
    /// <summary>The settings in force: the string table names keys through it (<c>[[Action]]</c> tokens).</summary>
    [JsonIgnore] public static Settings Current { get; set; } = new();
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Settings Load(string root = "user://")
    {
        var path = ProjectSettings.GlobalizePath(root + "settings.json");
        Settings s = new();
        try
        {
            if (File.Exists(path)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options) ?? new Settings();
        }
        catch (Exception e)
        {
            GD.PushWarning($"settings unreadable, using defaults: {e.Message}");
            s = new Settings();
        }
        s.Root = root;
        s.Sanitize();
        return s;
    }

    /// <summary>A hand-edited or damaged file can hold nulls and nonsense; every value ends up usable.</summary>
    void Sanitize()
    {
        Keys ??= new Dictionary<string, string>(Defaults);
        foreach (var k in Keys.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) Keys[k] = Defaults.GetValueOrDefault(k, "None");
        foreach (var (k, v) in Defaults) Keys.TryAdd(k, v);
        UiScale = double.IsFinite(UiScale) ? Math.Clamp(UiScale, 0.75, 1.5) : 1.0;
        Master = Volume(Master, 1.0);
        Ambience = Volume(Ambience, 0.8);
        Sfx = Volume(Sfx, 0.9);
        if (Width < 640 || Height < 360 || Width > 7680 || Height > 4320) (Width, Height) = (1600, 900);
        static double Volume(double v, double fallback) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : fallback;
    }

    public bool Save() => Profile.WriteAtomic(ProjectSettings.GlobalizePath(Root + "settings.json"), JsonSerializer.Serialize(this, Options));

    public Key KeyFor(string action) => Enum.TryParse<Key>(Keys.TryGetValue(action, out var k) ? k : Defaults[action], out var key) ? key : Enum.Parse<Key>(Defaults[action]);

    /// <summary>The action a key is bound to, or null.</summary>
    public string? ActionOf(Key key)
    {
        foreach (var a in Actions)
            if (KeyFor(a) == key) return a;
        return null;
    }

    public void Bind(string action, Key key)
    {
        // A key holds one action. The action that had it takes this action's old key in exchange, so no action is
        // ever left without a key (binding Dock's F to the chart used to leave the ship unable to dock at all).
        var old = KeyFor(action);
        foreach (var a in Actions)
            if (a != action && KeyFor(a) == key) Keys[a] = old.ToString();
        Keys[action] = key.ToString();
    }

    public void ResetKeys() => Keys = new Dictionary<string, string>(Defaults);

    /// <summary>A short human label for a key: "W", "Space", "1".</summary>
    public static string Label(Key key) => key switch
    {
        Key.None => "—",
        >= Key.Key0 and <= Key.Key9 => ((int)key - (int)Key.Key0).ToString(),
        _ => OS.GetKeycodeString(key),
    };

    /// <summary>Applies everything that has an immediate effect: window, scale, palette, buses.</summary>
    public void Apply(Window window)
    {
        var mode = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
        if (!Fullscreen)
        {
            // A window size bigger than the screen (2560×1440 on a 1080p monitor) would hang its title bar off the
            // top: fit the largest listed size that the screen's usable area holds.
            int screenId = DisplayServer.WindowGetCurrentScreen();
            var usable = DisplayServer.ScreenGetUsableRect(screenId);
            var size = new Vector2I(Width, Height);
            if (usable.Size.X > 0 && usable.Size.Y > 0 && (size.X > usable.Size.X || size.Y > usable.Size.Y))
            {
                var fit = Resolutions.LastOrDefault(r => r.W <= usable.Size.X && r.H <= usable.Size.Y, Resolutions[0]);
                size = new Vector2I(fit.W, fit.H);
            }
            if (DisplayServer.WindowGetSize() != size)
            {
                DisplayServer.WindowSetSize(size);
                var origin = usable.Size.X > 0 ? usable.Position : DisplayServer.ScreenGetPosition(screenId);
                var room = usable.Size.X > 0 ? usable.Size : DisplayServer.ScreenGetSize(screenId);
                DisplayServer.WindowSetPosition(origin + (room - size) / 2);
            }
        }
        window.ContentScaleFactor = (float)UiScale;
        Ink.SetPalette(Colorblind);
        Bus("Master", Master);
        Bus("Ambience", Ambience);
        Bus("SFX", Sfx);
    }

    static void Bus(string name, double linear)
    {
        int idx = AudioServer.GetBusIndex(name);
        if (idx < 0) return;
        AudioServer.SetBusVolumeDb(idx, Mathf.LinearToDb((float)Math.Clamp(linear, 0, 1)));
        AudioServer.SetBusMute(idx, linear <= 0.001);
    }
}
