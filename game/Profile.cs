using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// What outlives a run (GDD §3, §14): one personal best per preset, achievements, unlocked cosmetics,
/// the last loadout and name, and which onboarding hints have been shown. Lives in <c>user://profile.json</c>;
/// the suspend save is <c>user://suspend.json</c> beside it. Writes are atomic (temp file + rename) so a
/// crash mid-write cannot eat the file.
/// </summary>
public sealed class Profile
{
    public sealed class Best
    {
        public double Days { get; set; }
        public string ShipName { get; set; } = "";
        public string Date { get; set; } = "";
        public string Cause { get; set; } = "";
    }

    public Dictionary<string, Best> Bests { get; set; } = new();
    public List<string> Achievements { get; set; } = new();
    public List<string> Cosmetics { get; set; } = new();
    public List<string> HintsSeen { get; set; } = new();
    public bool ShowHints { get; set; } = true;
    public string LastPreset { get; set; } = "RoughSeas";
    public string LastShipName { get; set; } = "";
    public List<string> LastLoadout { get; set; } = new() { "", "", "", "", "" };
    public int Voyages { get; set; }

    [JsonIgnore] public string Root { get; private set; } = "user://";
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Profile Load(string root = "user://")
    {
        var path = ProjectSettings.GlobalizePath(root + "profile.json");
        Profile p = new();
        try
        {
            if (File.Exists(path))
                p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Options) ?? new Profile();
        }
        catch (Exception e)
        {
            // The next save would overwrite the only copy of the player's bests: keep the damaged file beside it.
            var aside = $"{path}.corrupt-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
            try { File.Copy(path, aside, overwrite: true); } catch (Exception) { aside = "(could not copy it)"; }
            GD.PushWarning($"profile unreadable, starting fresh (the old file is kept as {aside}): {e.Message}");
            p = new Profile();
        }
        p.Root = root;
        p.Sanitize();
        return p;
    }

    /// <summary>A hand-edited or damaged file can hold nulls: every list and string ends up usable.</summary>
    void Sanitize()
    {
        Bests ??= new();
        foreach (var k in Bests.Where(b => b.Value == null).Select(b => b.Key).ToList()) Bests.Remove(k);
        foreach (var b in Bests.Values) { b.ShipName ??= ""; b.Date ??= ""; b.Cause ??= ""; }
        Achievements ??= new();
        Achievements.RemoveAll(a => a == null);
        Cosmetics ??= new();
        Cosmetics.RemoveAll(c => c == null);
        HintsSeen ??= new();
        HintsSeen.RemoveAll(h => h == null);
        if (!Enum.TryParse<Preset>(LastPreset, out var last) || !Enum.IsDefined(last)) LastPreset = "RoughSeas";
        foreach (var k in Bests.Keys.Where(k => !Enum.TryParse<Preset>(k, out var pr) || !Enum.IsDefined(pr)).ToList()) Bests.Remove(k);
        LastShipName ??= "";
        if (LastLoadout == null || LastLoadout.Count != Sim.Cosmetics.Slots.Length) LastLoadout = new() { "", "", "", "", "" };
        for (int i = 0; i < LastLoadout.Count; i++) LastLoadout[i] ??= "";
    }

    public bool Save() => WriteAtomic(ProjectSettings.GlobalizePath(Root + "profile.json"), JsonSerializer.Serialize(this, Options));

    public bool HasCosmetic(string key) => key.Length == 0 || Cosmetics.Contains(key);
    public bool HintSeen(string key) => HintsSeen.Contains(key);
    public void MarkHint(string key)
    {
        if (HintsSeen.Contains(key)) return;
        HintsSeen.Add(key);
        Save();
    }

    /// <summary>Folds a finished (or suspended) run into the profile. Returns what is new.</summary>
    public (bool Best, List<string> Achievements, List<string> Cosmetics) RecordRun(World w, bool finished)
    {
        var newAch = new List<string>();
        var newCos = new List<string>();
        foreach (var a in w.Player.Achievements)
        {
            if (Achievements.Contains(a)) continue;
            Achievements.Add(a);
            newAch.Add(a);
            Platform.Current.Unlock(a);   // the store mirrors the profile (GDD §14)
            var reward = Sim.Achievements.Reward(a);
            if (reward != null && !Cosmetics.Contains(reward)) { Cosmetics.Add(reward); newCos.Add(reward); }
        }
        foreach (var c in w.Player.Cosmetics)
            if (!Cosmetics.Contains(c)) { Cosmetics.Add(c); newCos.Add(c); }
        bool best = false;
        if (finished)
        {
            Voyages++;
            string key = w.Preset.ToString();
            double days = Math.Round(w.DaysSurvived, 2);
            if (!Bests.TryGetValue(key, out var old) || days > old.Days)
            {
                best = true;
                Bests[key] = new Best { Days = days, ShipName = w.Player.ShipName, Date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Cause = w.CauseOfSinking };
            }
        }
        Save();
        return (best, newAch, newCos);
    }

    // ---- suspend save (GDD §3) ----
    string SuspendPath => ProjectSettings.GlobalizePath(Root + "suspend.json");
    public bool HasSuspend => File.Exists(SuspendPath);
    public bool WriteSuspend(string json) => WriteAtomic(SuspendPath, json);
    public string? ReadSuspend()
    {
        try { return File.Exists(SuspendPath) ? File.ReadAllText(SuspendPath) : null; }
        catch (Exception e) { GD.PushWarning($"suspend save unreadable: {e.Message}"); return null; }
    }
    /// <summary>Moves an unreadable suspend save aside (suspend.json.corrupt) so the title stops offering it.</summary>
    public void SetAsideSuspend()
    {
        try { if (File.Exists(SuspendPath)) File.Move(SuspendPath, SuspendPath + ".corrupt", overwrite: true); }
        catch (Exception e) { GD.PushWarning($"could not set the suspend save aside: {e.Message}"); DeleteSuspend(); }
    }

    public void DeleteSuspend()
    {
        try { if (File.Exists(SuspendPath)) File.Delete(SuspendPath); }
        catch (Exception e) { GD.PushWarning($"could not delete the suspend save: {e.Message}"); }
    }

    /// <summary>
    /// Temp file + rename, so a crash mid-write cannot eat the file. A write that fails (read-only or full disk, a file
    /// held by a sync client) is logged and reported, never thrown: the callers are Set sail, a hint showing, the recap.
    /// </summary>
    internal static bool WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"could not write {path}: {e.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
            return false;
        }
    }
}
