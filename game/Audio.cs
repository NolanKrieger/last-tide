using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Pure ambience and effects (GDD §16: no music). Every sound is synthesized by <c>tools/audio/build.py</c>
/// and loaded at runtime from <c>assets/audio</c>. Loops follow the world (wind, waves, creak, rain, harbour,
/// gulls, pumps, digging, water, the siren); one-shots follow sim events and a few UI moments.
/// Buses: Master → Ambience, SFX (created here if the project has none).
/// </summary>
public partial class Audio : Node
{
    public static Audio? Instance { get; private set; }

    sealed class Loop
    {
        public AudioStreamPlayer Player = null!;
        public float Target, Current;
        public float Gain = 1f;
    }

    static readonly string[] LoopNames = { "wind_low", "wind_high", "waves", "creak", "rain", "harbour", "gulls", "pumps", "dig", "water_rush", "siren_song" };
    readonly Dictionary<string, AudioStreamWav> streams = new();
    readonly Dictionary<string, Loop> loops = new();
    readonly List<AudioStreamPlayer> pool = new();
    readonly Dictionary<string, double> lastPlayed = new();
    World? world;
    bool muted, loaded;
    int lastSail = -1, lastTier = -1;
    bool hadMonster, hadEruption, hadHostile, wasOver;
    double thunderClock = 6, windWas, gustClock;
    double time;

    public bool Loaded => loaded;
    public string? LastOneShot { get; private set; }
    public int OneShotsPlayed { get; private set; }
    readonly Dictionary<string, int> plays = new();
    /// <summary>How many times a one-shot has played this session (self-test evidence).</summary>
    public int Plays(string name) => plays.GetValueOrDefault(name);

    public void Init(bool mute)
    {
        Instance = this;
        muted = mute;
        EnsureBus("Ambience");
        EnsureBus("SFX");
        foreach (var name in LoopNames)
        {
            var stream = Load(name, loop: true);
            if (stream == null) continue;
            var p = new AudioStreamPlayer { Stream = stream, Bus = "Ambience", VolumeDb = -80, Autoplay = false };
            AddChild(p);
            loops[name] = new Loop { Player = p };
        }
        for (int i = 0; i < 14; i++)
        {
            var p = new AudioStreamPlayer { Bus = "SFX" };
            AddChild(p);
            pool.Add(p);
        }
        loaded = loops.Count == LoopNames.Length;
        ApplyMute();
    }

    /// <summary>A muted run (the self-test, --mute, debug runs) keeps the master bus muted whatever the saved volumes say.</summary>
    public void ApplyMute()
    {
        if (muted) AudioServer.SetBusMute(AudioServer.GetBusIndex("Master"), true);
    }

    /// <summary>
    /// Stops every player before the tree goes: a stream still playing at exit leaves its AudioStreamWAV and
    /// playback behind ("ObjectDB instances were leaked at exit").
    /// </summary>
    /// <summary>Silences everything for good (the game is exiting): loops stop and nothing restarts them.</summary>
    public void StopAll()
    {
        stopped = true;
        foreach (var l in loops.Values) { l.Target = l.Current = 0; l.Player.Stop(); }
        foreach (var p in pool) p.Stop();
    }

    bool stopped;

    public override void _ExitTree()
    {
        foreach (var l in loops.Values) { l.Player.Stop(); l.Player.Stream = null; }
        foreach (var p in pool) { p.Stop(); p.Stream = null; }
        streams.Clear();
        if (Instance == this) Instance = null;
    }

    static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, name);
        AudioServer.SetBusSend(idx, "Master");
    }

    readonly HashSet<string> missing = new();

    AudioStreamWav? Load(string name, bool loop)
    {
        if (streams.TryGetValue(name, out var cached)) return cached;
        if (missing.Contains(name)) return null;   // warned once, not on every cannon shot
        var path = $"res://assets/audio/{name}.wav";
        var stream = Godot.FileAccess.FileExists(path) ? AudioStreamWav.LoadFromFile(path) : null;
        if (stream == null)
        {
            GD.PushWarning($"audio missing or unreadable: {path}");
            missing.Add(name);
            return null;
        }
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = (int)(stream.GetLength() * stream.MixRate);
        }
        streams[name] = stream;
        return stream;
    }

    public void Bind(World w)
    {
        world = w;
        lastSail = w.Ship.SailTarget;
        lastTier = w.ThreatTier;
        hadMonster = w.Monster != null;
        hadEruption = w.Eruptions.Count > 0;
        hadHostile = false;
        wasOver = w.RunOver;
        windWas = w.Ship.LocalWind.Speed;
        foreach (var l in loops.Values) l.Target = 0;
    }

    /// <summary>Plays a one-shot at a linear volume (0–1); throttled so a broadside of six guns is one boom, not six.</summary>
    public void Play(string name, double volume = 1.0, double minGap = 0.05, double pitch = 1.0)
    {
        if (volume <= 0.01 || stopped) return;
        if (lastPlayed.TryGetValue(name, out var t) && time - t < minGap) return;
        lastPlayed[name] = time;
        var stream = Load(name, loop: false);
        if (stream == null) return;
        var p = pool.FirstOrDefault(x => !x.Playing) ?? pool[OneShotsPlayed % pool.Count];
        p.Stream = stream;
        p.VolumeDb = Mathf.LinearToDb((float)Math.Clamp(volume, 0, 1));
        p.PitchScale = (float)pitch;
        p.Play();
        LastOneShot = name;
        OneShotsPlayed++;
        plays[name] = plays.GetValueOrDefault(name) + 1;
    }

    public static void Ui() => Instance?.Play("click", 0.5, 0.03);

    public bool IsPlaying(string name) => pool.Any(p => p.Playing && p.Stream == (streams.TryGetValue(name, out var s) ? s : null));
    public float LoopLevel(string name) => loops.TryGetValue(name, out var l) ? l.Current : 0;

    public void Update(double delta, bool paused, bool menuOpen)
    {
        time += delta;
        if (world == null || stopped) return;
        var w = world;
        var ship = w.Ship;
        var wind = ship.LocalWind;
        var cond = w.ConditionsAt(ship.Pos);
        bool docked = w.IsDocked;
        double duck = menuOpen ? 0.35 : 1.0;
        double night = cond.Night ? 0.85 : 1.0;

        // ---- loops ----
        double windN = Math.Clamp(wind.Speed / 14.0, 0, 1);
        Set("wind_low", (0.25 + 0.6 * Math.Min(1, wind.Speed / 7.0)) * night * (docked ? 0.4 : 1) * duck);
        Set("wind_high", Math.Clamp((wind.Speed - 6) / 9.0, 0, 1) * 0.9 * (docked ? 0.2 : 1) * duck);
        Set("waves", (0.30 + 0.45 * Math.Min(1, ship.Speed / 10.0) + 0.3 * cond.Storm) * night * (docked ? 0.3 : 1) * duck);
        Set("creak", docked ? 0 : (Math.Min(1, ship.Speed / 9.0) * 0.55 + Math.Min(0.4, Math.Abs(ship.AngVel) * 0.8)) * duck);
        Set("rain", (cond.Storm > 0.05 ? Math.Min(1, cond.Storm + 0.2) : 0) * (docked ? 0.5 : 1) * duck);
        double portDist = 9999;
        foreach (var port in w.Map.Ports)
        {
            if (port.Secret && !port.Discovered) continue;   // a hidden cove must not give itself away by its bustle
            portDist = Math.Min(portDist, port.Harbor.DistanceTo(ship.Pos));
        }
        Set("harbour", docked ? 0.8 * duck : Math.Clamp(1 - portDist / 320.0, 0, 1) * 0.6 * duck);
        Set("gulls", cond.Night ? 0 : Math.Clamp(1 - portDist / 500.0, 0, 1) * 0.5 * duck);
        var st = ship.Stations();
        Set("pumps", !docked && st[3] > 0 && ship.Water > 0.5 ? 0.7 * duck : 0);
        Set("dig", w.Digging ? 0.8 * duck : 0);
        Set("water_rush", !docked && ship.Leaks > 0 ? Math.Min(1, 0.35 + 0.2 * ship.Leaks) * duck : 0);
        Set("siren_song", w.Monster is { Type: MonsterType.Siren, Surfaced: true } m ? Math.Clamp(1 - m.Pos.DistanceTo(ship.Pos) / 300.0, 0, 1) * 0.8 * duck : 0);
        if (w.RunOver) foreach (var l in loops.Values) l.Target = Math.Min(l.Target, 0.15f);

        float rate = (float)Math.Min(1, delta * 2.5);
        foreach (var l in loops.Values)
        {
            l.Current += (l.Target - l.Current) * rate;
            bool audible = l.Current > 0.01f;
            if (audible && !l.Player.Playing) l.Player.Play();
            else if (!audible && l.Player.Playing) l.Player.Stop();
            if (audible) l.Player.VolumeDb = Mathf.LinearToDb(l.Current);
        }
        // Wind pitch follows the breeze a little.
        if (loops.TryGetValue("wind_high", out var wh)) wh.Player.PitchScale = 0.9f + 0.25f * (float)windN;

        if (paused) return;

        // ---- state changes (the per-tick events are in Consume) ----
        if (ship.SailTarget != lastSail)
        {
            if (lastSail >= 0) Play(ship.SailTarget > lastSail ? "sail_up" : "sail_down", 0.8, 0.15);
            lastSail = ship.SailTarget;
        }
        if (w.ThreatTier != lastTier)
        {
            if (lastTier >= 0 && w.ThreatTier > lastTier) Play("bell_low", 0.7, 1.0);
            lastTier = w.ThreatTier;
        }
        bool monster = w.Monster != null;
        if (monster && !hadMonster && w.Monster != null) Play(w.Monster.Type switch
        {
            MonsterType.ReefSerpent => "monster_serpent",
            MonsterType.Kraken => "monster_kraken",
            MonsterType.GhostShip => "monster_ghost",
            MonsterType.Crocodile => "monster_crocodile",
            MonsterType.WeedKraken => "monster_weed",
            MonsterType.Siren => "monster_siren",   // she used to arrive with the Ghost Ship's moan
            _ => "monster_ghost",
        }, 0.9, 1.0);
        hadMonster = monster;
        bool eruption = w.Eruptions.Count > 0;
        if (eruption && !hadEruption) Play("monster_eruption", 0.9, 1.0);
        hadEruption = eruption;
        var threat = w.NearestThreatTo(ship, 800);
        bool hostile = threat != null && w.PlayerSees(threat.Pos);
        if (hostile && !hadHostile) Play("drums", 0.8, 5.0);
        hadHostile = hostile;
        if (w.RunOver && !wasOver) Play("sink", 1.0, 0.5);
        wasOver = w.RunOver;
        if (cond.Storm > 0.2)
        {
            thunderClock -= delta;
            if (thunderClock <= 0)
            {
                Play("thunder", 0.5 + 0.5 * cond.Storm, 2.0, 0.85 + 0.3 * GD.Randf());
                thunderClock = 7 + GD.Randf() * 14;
            }
        }
        gustClock -= delta;
        if (gustClock <= 0)
        {
            if (wind.Speed - windWas > 2.5 && !docked) Play("gust", 0.5, 3.0);
            windWas = wind.Speed;
            gustClock = 1.0;
        }
    }

    /// <summary>
    /// One-shots for the sim events of the tick that just ran. Called once per tick that advanced the clock: the
    /// world keeps its last event list while the clock is stopped (in port, after the loss), so reading it every
    /// frame replayed the sinking and a pre-dock bell.
    /// </summary>
    public void Consume()
    {
        if (world == null) return;
        var ship = world.Ship;
        foreach (var e in world.Events)
        {
            double d = e.Pos.DistanceTo(ship.Pos);
            double far = Math.Pow(Math.Clamp(1 - d / 950.0, 0, 1), 1.4);
            switch (e.Type)
            {
                case CombatEventType.Fire: Play("cannon", e.ShipId == ship.Id ? 1.0 : 0.8 * far, 0.12, e.ShipId == ship.Id ? 1.0 : 0.9); break;
                case CombatEventType.Hit: Play("hit", e.ShipId == ship.Id ? 1.0 : 0.7 * far, 0.08); break;
                case CombatEventType.Splash: Play("splash", 0.6 * Math.Max(far, e.ShipId == ship.Id ? 0.5 : 0), 0.1, 0.9 + 0.2 * GD.Randf()); break;
                case CombatEventType.Ram: Play("ram", Math.Max(far, 0.4), 0.3); break;
                case CombatEventType.Sink: Play("sink", e.ShipId == ship.Id ? 1.0 : 0.7 * far, 0.5); break;
                case CombatEventType.Collect: Play("coins", 0.8, 0.2); break;
                case CombatEventType.Rescued: Play("bell", 0.8, 0.5); break;
                case CombatEventType.Ring: Play("quill", 0.5, 0.3); break;
            }
        }
    }

    /// <summary>The HUD has just inked a new notice.</summary>
    public void Notice() => Play("quill", 0.5, 0.3);

    void Set(string name, double target)
    {
        if (loops.TryGetValue(name, out var l)) l.Target = (float)Math.Clamp(target, 0, 1);
    }

    public void Bell() => Play("bell", 0.9, 0.3);
}
