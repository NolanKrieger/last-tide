using Godot;

namespace LastTide;

/// <summary>
/// The store platform seam (GDD §17: Steam achievements and cloud saves, no leaderboards). The game talks
/// only to this interface; <see cref="NoPlatform"/> is what ships until the Steamworks App ID exists.
/// Wiring Steamworks.NET is described in docs/STEAM.md and needs no changes outside <see cref="SteamPlatform"/>.
/// </summary>
public interface IPlatform
{
    string Name { get; }
    bool Online { get; }
    /// <summary>Unlocks an achievement by its key (the same keys as <c>Achievements.All</c>); idempotent.</summary>
    void Unlock(string key);
    /// <summary>Called once per frame so the platform can pump its callbacks.</summary>
    void Pump();
    /// <summary>Called once as the game exits (Main._ExitTree, whichever way it quits); must be safe to call twice.</summary>
    void Shutdown();
}

/// <summary>No store: achievements live only in the profile. This is what every build uses today.</summary>
public sealed class NoPlatform : IPlatform
{
    public string Name => "none";
    public bool Online => false;
    public readonly List<string> Unlocked = new();
    public void Unlock(string key) { if (!Unlocked.Contains(key)) Unlocked.Add(key); }
    public void Pump() { }
    public void Shutdown() { }
}

#if STEAM
/// <summary>
/// Steamworks.NET binding. Built only with -p:DefineConstants=STEAM once the App ID is set in
/// steam_appid.txt / the store; achievement API names are the upper-cased keys (docs/STEAM.md).
/// </summary>
public sealed class SteamPlatform : IPlatform
{
    bool ok;
    public string Name => "steam";
    public bool Online => ok;
    public SteamPlatform()
    {
        try { ok = Steamworks.SteamAPI.Init(); }
        catch (Exception e) { GD.PushWarning($"Steam unavailable: {e.Message}"); ok = false; }
    }
    public void Unlock(string key)
    {
        if (!ok) return;
        Steamworks.SteamUserStats.SetAchievement(key.ToUpperInvariant());
        Steamworks.SteamUserStats.StoreStats();
    }
    public void Pump() { if (ok) Steamworks.SteamAPI.RunCallbacks(); }
    public void Shutdown() { if (ok) Steamworks.SteamAPI.Shutdown(); ok = false; }   // idempotent: every exit path reaches it (Main._ExitTree)
}
#endif

public static class Platform
{
    public static IPlatform Current { get; private set; } = new NoPlatform();

    public static void Init()
    {
#if STEAM
        Current = new SteamPlatform();
        if (!Current.Online) Current = new NoPlatform();
#else
        Current = new NoPlatform();
#endif
    }
}
