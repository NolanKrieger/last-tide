using System.Globalization;
using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Draws every other ship within sight, with its own interpolation. A ship that sank goes down in view
/// (list, settle by the stern) before her view is freed; one that slipped away fades out. Their wakes
/// are drawn by a child <see cref="WakeView"/> under the hulls.
/// `--shipsheet[=level]` lays every hull out in a grid around the player for art review (debug only).
/// </summary>
public partial class FleetView : Node2D
{
    World world = null!;
    readonly Dictionary<int, (ShipView View, Vec2 PrevPos, double PrevHeading)> views = new();
    readonly List<(ShipView View, Vec2 Pos, double Heading, float Fade)> leaving = new();
    readonly HashSet<int> seen = new(), sunkNow = new();
    readonly List<int> gone = new();
    WakeView wakes = null!;
    PaintLayer lanterns = null!;
    readonly InkBatch lamp = new();

    public void Init(World w)
    {
        world = w;
        ZIndex = 9;
        wakes = new WakeView { ZIndex = -1 };
        AddChild(wakes);
        // Stern lanterns at night, drawn above the weather wash (canvas layer 9) and under the HUD (layer 10, whose
        // sublayer = its index in Main, always > this layer's index here) so ships show as warm points in the dark.
        // The player's burns only while her lantern is lit: doused, she goes dark (GDD §9 stealth).
        var night = new CanvasLayer { Layer = 10, FollowViewportEnabled = true };
        lanterns = new PaintLayer { Paint = PaintLanterns };
        night.AddChild(lanterns);
        AddChild(night);
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--shipsheet"))
                BuildSheet(a.Length > "--shipsheet=".Length ? a["--shipsheet=".Length..] : "3");
            else if (a == "--sinktest") sinkTest = 2.0;   // debug: the nearest other ship founders and goes down (review of the sinking art)
            else if (a == "--sinktest=player") playerSinkTest = 1.0;   // debug: her own last stand, then she goes down
            else if (a.StartsWith("--loadout="))
            {
                // Debug (art review): wear cosmetics without unlocking them, e.g. --loadout=flag_gold,sails_striped,figurehead_kraken,hull_red,wake_gold.
                // Views are built after this, so the ship, wake and flag all pick it up. Cosmetics carry no rules.
                foreach (var key in a["--loadout=".Length..].Split(','))
                    if (Cosmetics.SlotOf(key) is { } slot) world.Player.Loadout[Cosmetics.SlotIndex(slot)] = key;
            }
    }

    double sinkTest = -1, playerSinkTest = -1;
    long syncedTick = -1;

    /// <summary>Called once per physics tick, after the sim stepped.</summary>
    public void Sync()
    {
        if (playerSinkTest > 0 && (playerSinkTest -= Tuning.Dt) <= 0)
        {
            world.Ship.HullHp = 0;       // the last stand starts on the next tick; the glass runs out ~20 s later
            world.Ship.Leaks = 3;
        }
        if (sinkTest > 0 && world.Others.Count > 0)
        {
            // Debug only: the nearest ship is brought into view ahead, then founders and goes down.
            var victim = world.Others.OrderBy(o => o.Pos.DistanceTo(world.Ship.Pos)).First();
            if (sinkTest >= 2.0)
            {
                victim.Pos = world.Ship.Pos + world.Ship.Forward * 45 + world.Ship.Right * 25;
                victim.Heading = world.Ship.Heading + 0.4;
                victim.Vel = Vec2.Zero;
                victim.SailTarget = 2;
                victim.HullHp = victim.MaxHp * 0.2;
            }
            if ((sinkTest -= Tuning.Dt) <= 0)
            {
                victim.HullHp = 0;
                victim.Foundering = true;
                victim.Hourglass = 0.05;
            }
        }
        seen.Clear();
        sunkNow.Clear();
        if (world.Ticks != syncedTick)   // stale events linger once the run is over (see EffectsView.Consume)
            foreach (var e in world.Events)
                if (e.Type == CombatEventType.Sink && e.ShipId > 0) sunkNow.Add(e.ShipId);
        syncedTick = world.Ticks;
        foreach (var ship in world.Others)
        {
            seen.Add(ship.Id);
            if (!views.TryGetValue(ship.Id, out var v))
            {
                var view = new ShipView();
                view.Init(ship);
                AddChild(view);
                v = (view, ship.Pos, ship.Heading);
            }
            else
                v = (v.View, v.View.LastPos, v.View.LastHeading);
            v.View.LastPos = ship.Pos;
            v.View.LastHeading = ship.Heading;
            views[ship.Id] = v;
            if (v.View.Visible) wakes.Record(ship);
        }
        gone.Clear();
        foreach (var id in views.Keys)
            if (!seen.Contains(id)) gone.Add(id);
        foreach (var id in gone)
        {
            var v = views[id];
            views.Remove(id);
            // Sunk in sight: she goes down where she was. Slipped away (or out of sight): she fades.
            if (sunkNow.Contains(id) || v.View.Ship.HullHp <= 0) v.View.BeginSinking();
            leaving.Add((v.View, v.View.Ship.Pos, v.View.Ship.Heading, 1f));
        }
    }

    public void Render(float alpha)
    {
        foreach (var (id, v) in views)
        {
            var ship = v.View.Ship;
            var pos = Vec2.Lerp(v.PrevPos, ship.Pos, alpha);
            double heading = Angles.LerpAngle(v.PrevHeading, ship.Heading, alpha);
            v.View.SetPose(pos, heading);
            v.View.Visible = world.PlayerSees(pos, ship.Hull.Length);
        }
    }

    void PaintLanterns(PaintLayer layer)
    {
        var c = world.ConditionsAt(world.Ship.Pos);
        float k = c.Night ? 1f : (float)c.Dusk * 0.5f;   // the same curve as the night wash (WeatherView)
        if (k <= 0.02f) return;
        float px = layer.ScreenPx();
        lamp.Clear();
        lamp.Px = px;
        float flick = 0.9f + 0.1f * Mathf.Sin((float)Time.GetTicksMsec() * 0.013f);
        var warm = new Color(1f, 0.78f, 0.38f);
        foreach (var v in ShipView.Live)
        {
            if (v.Ship.IsPlayer && !world.Lantern) continue;
            if (v.LanternAt() is not { } p) continue;
            float r = Mathf.Max(5.5f * Ink.PxPerM, 14f * px);
            lamp.Glow(p, r, warm with { A = 0.42f * k * flick }, warm with { A = 0 }, 20);
            lamp.Disc(p, Mathf.Max(0.7f * Ink.PxPerM, 1.8f * px), new Color(1f, 0.95f, 0.8f, k), 8);
        }
        lamp.Flush(layer);
    }

    public override void _Process(double delta)
    {
        lanterns.QueueRedraw();
        for (int i = leaving.Count - 1; i >= 0; i--)
        {
            var (view, pos, heading, fade) = leaving[i];
            if (!view.Sinking) fade -= (float)delta / 0.6f;
            if (!view.Sinking) view.Modulate = new Color(1, 1, 1, Mathf.Max(0, fade));
            view.SetPose(pos, heading);
            view.Visible = world.PlayerSees(pos, view.Ship.Hull.Length);
            if (fade <= 0 || view.SunkFromView)
            {
                view.QueueFree();
                leaving.RemoveAt(i);
            }
            else leaving[i] = (view, pos, heading, fade);
        }
    }

    /// <summary>
    /// Hulls on show around the player (review only). `--shipsheet=level[:id,id,…]`: all 14 by default, laid
    /// out in a grid that skips the centre (hers), spaced for the biggest hull shown.
    /// </summary>
    void BuildSheet(string spec)
    {
        var parts = spec.Split(':');
        int level = Math.Clamp(int.Parse(parts[0], CultureInfo.InvariantCulture), 0, 3);
        // "hull@N" = N copies of one hull turned through the wind in equal steps (a point-of-sail sweep).
        var list = new List<(HullDef Hull, double? Off)>();
        foreach (var id in parts.Length > 1 ? parts[1].Split(',') : Hulls.All.Select(h => h.Id).ToArray())
        {
            var bits = id.Split('@');
            if (!Hulls.Exists(bits[0])) continue;
            int copies = bits.Length > 1 ? int.Parse(bits[1], CultureInfo.InvariantCulture) : 1;
            for (int c = 0; c < copies; c++) list.Add((Hulls.Get(bits[0]), copies > 1 ? Math.Tau * c / copies : null));
        }
        var hulls = list.Select(e => e.Hull).ToArray();
        if (hulls.Length == 0) return;
        double big = hulls.Max(h => h.Length);
        int cols = hulls.Length <= 2 ? 3 : hulls.Length <= 4 ? 3 : 5;
        int rows = (hulls.Length + 1 + cols - 1) / cols;
        double dx = big * 1.45 + 12, dy = big * 0.95 + 14;
        var centre = world.Ship.Pos;
        int k = 0, cr = rows / 2, cc = cols / 2;
        for (int row = 0; row < rows && k < hulls.Length; row++)
            for (int col = 0; col < cols && k < hulls.Length; col++)
            {
                if (row == cr && col == cc) continue;
                var h = hulls[k];
                var pos = centre + new Vec2((col - cc) * dx, (row - cr) * dy);
                var faction = (k % 3) switch { 0 => Faction.Brethren, 1 => Faction.FreeTraders, _ => Faction.Crown };
                double heading = list[k].Off is { } off ? world.WindAt(pos).From + off : world.Ship.Heading;
                var s = new Ship(h, pos, heading) { Id = 1000 + k, Faction = faction };
                s.SailTarget = level;
                s.SailFraction = Tuning.SailFraction[level];
                if (k % 4 == 3) s.HullHp = h.HullHp * 0.45;
                s.Step(Tuning.Dt, world.WindAt(pos), new ShipInput(0, 0), Array.Empty<Island>());
                s.Vel = Vec2.Zero;
                var view = new ShipView();
                view.Init(s);
                AddChild(view);
                view.SetPose(pos, s.Heading);
                k++;
            }
    }
}
