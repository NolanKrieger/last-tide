using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Hit feedback on the sea (Nolan, 2026-09-28: "make clear hit feedback"): inked figures that pop where a ball strikes
/// and rise away (black for her own hits, red for the ones she takes, merged over a broadside's ripple), "Leak!" when
/// a hit holes her, "Sunk!" over a ship her guns sent down, what a barrel or chest put in the hold, and a hull bar over
/// any ship struck in her sight that drains its lost planks a moment after the blow. Drawn in screen space under the
/// HUD's plates, from the world's events after each tick; the HUD's own hull tube shows the same chunk for her.
/// </summary>
public partial class Callouts : InkCanvas
{
    public Hud Hud = null!;

    struct Pop
    {
        public Vec2 At;
        public string Text;
        public Color Colour;
        public float Age, Life, Drop;   // Drop: screen pixels below the usual spot (the leak line under the figure)
        public int Size, Key, Amount;
        public string Format, Arg;
        public bool Big;
    }

    sealed class Bar
    {
        public float Since;        // seconds since the last blow
        public float Shown;        // the fraction drawn, easing down to the hull's own after the blow
        public bool ByHer;         // her guns struck this ship (a sinking then earns "Sunk!")
        public float Life = BarHold;
    }

    const float PopLife = 1.5f, BigLife = 2.2f, MergeWindow = 0.45f, BarHold = 4.5f;
    readonly List<Pop> pops = new();
    readonly Dictionary<int, Bar> bars = new();
    readonly Dictionary<int, int> struckBy = new();   // ship id → last tick her guns hit it, for the sinking she earned
    int lastLeaks = -1;

    /// <summary>For the self-test: the figures on the sea now, newest last.</summary>
    public IEnumerable<string> PopTexts => pops.Select(p => p.Text);
    public bool BarShown(int shipId) => bars.TryGetValue(shipId, out var b) && b.Since < b.Life;

    public void Reset()
    {
        pops.Clear();
        bars.Clear();
        struckBy.Clear();
        lastLeaks = -1;
    }

    /// <summary>Reads the tick's events (Main calls it once after every tick that ran).</summary>
    public void Consume(World world)
    {
        var ship = world.Ship;
        bool holed = false;
        foreach (var e in world.Events)
        {
            switch (e.Type)
            {
                case CombatEventType.Hit:
                    if (e.ShipId == ship.Id)
                    {
                        // Her own planks: red, merged over the ripple of one broadside, and the HUD's hull tube flashes.
                        Add(e.Pos, -1, (int)Math.Round(Math.Max(1, e.Strength)), "CALLOUT_TAKEN", "", Ink.Red, 28);
                        Hud.HullHit = 1;
                        holed = true;
                    }
                    else if (e.By == ship.Id)
                    {
                        // Her shot struck home: the figure in black ink, and the target's hull bar comes up.
                        Add(e.Pos, e.ShipId == -3 ? -3 : e.ShipId, (int)Math.Round(Math.Max(1, e.Strength)), "CALLOUT_DEALT", "", Ink.Black, 28);
                        if (e.ShipId >= 0) struckBy[e.ShipId] = (int)world.Ticks;
                    }
                    if (e.ShipId > 0 && Find(world, e.ShipId) is { } struck)
                    {
                        if (!bars.TryGetValue(e.ShipId, out var bar))
                            bars[e.ShipId] = bar = new Bar { Shown = (float)Math.Clamp((struck.HullHp + e.Strength) / struck.MaxHp, 0, 1) };
                        bar.Since = 0;
                        bar.ByHer |= e.By == ship.Id;
                    }
                    break;
                case CombatEventType.Sink:
                    if (e.ShipId != ship.Id && struckBy.TryGetValue(e.ShipId, out int at) && world.Ticks - at < 60 * Tuning.TicksPerSecond)
                        AddText(e.Pos, Text.Get("CALLOUT_SUNK"), Ink.Red, 36, big: true);
                    bars.Remove(e.ShipId);
                    struckBy.Remove(e.ShipId);
                    break;
                case CombatEventType.Collect:
                    if (e.Good >= 0) Add(e.Pos, -100 - e.Good, (int)e.Strength, "CALLOUT_GOT", Text.Get("GOOD_" + Goods.Of((Good)e.Good).Key), HudInk.HullWood.Darkened(0.25f), 22);
                    else if (e.Strength > 0) Add(e.Pos, -2, (int)e.Strength, "CALLOUT_GOLD", "", HudInk.Gold.Darkened(0.15f), 22);
                    break;
            }
        }
        // A blow that opened a leak says so under the figure, in the water's blue.
        if (holed && lastLeaks >= 0 && ship.Leaks > lastLeaks)
            AddText(ship.Pos, Text.Get("CALLOUT_LEAK"), HudInk.WaterDeep, 24, drop: -48);   // well above the figure, clear of her hull; both rise together
        lastLeaks = ship.Leaks;
    }

    static Ship? Find(World world, int id)
    {
        foreach (var s in world.Others) if (s.Id == id) return s;
        return null;
    }

    /// <summary>A figure; a second one for the same target inside the merge window adds to it and pops again.</summary>
    void Add(Vec2 at, int key, int amount, string format, string arg, Color colour, int size)
    {
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            var p = pops[i];
            if (p.Key != key || p.Age > MergeWindow || p.Big) continue;
            p.Amount += amount;
            p.Text = Text.Get(p.Format, p.Amount, p.Arg);
            p.Age = Math.Min(p.Age, 0.05f);
            p.At = at;
            pops[i] = p;
            return;
        }
        pops.Add(new Pop { At = at, Key = key, Amount = amount, Format = format, Arg = arg, Text = Text.Get(format, amount, arg), Colour = colour, Size = size, Life = PopLife });
    }

    void AddText(Vec2 at, string text, Color colour, int size, bool big = false, float drop = 0) =>
        pops.Add(new Pop { At = at, Key = int.MinValue, Text = text, Format = "", Arg = "", Colour = colour, Size = size, Life = big ? BigLife : PopLife, Big = big, Drop = drop });

    public override void _Process(double delta)
    {
        float dt = Math.Min((float)delta, 1 / 20f);   // a hitch must not skip the figures or the bars' drain
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            var p = pops[i];
            p.Age += dt;
            if (p.Age >= p.Life) pops.RemoveAt(i);
            else pops[i] = p;
        }
        if (Hud.World is { } world)
        {
            List<int>? gone = null;
            foreach (var (id, bar) in bars)
            {
                bar.Since += dt;
                var s = Find(world, id);
                if (s == null || s.Sunk || bar.Since > bar.Life) { (gone ??= new()).Add(id); continue; }
                float actual = (float)Math.Clamp(s.HullHp / s.MaxHp, 0, 1);
                // The planks just lost hang in the bar for a beat, then drain away.
                if (bar.Shown < actual) bar.Shown = actual;
                else if (bar.Since > 0.35f) bar.Shown = Mathf.MoveToward(bar.Shown, actual, dt * 0.5f);
            }
            if (gone != null) foreach (var id in gone) bars.Remove(id);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var world = Hud.World;
        if (world == null || world.RunOver && pops.Count == 0) return;
        var xf = GetViewport().GetCanvasTransform();
        float zoom = xf.X.Length();

        // Hull bars over the ships struck in her sight.
        foreach (var (id, bar) in bars)
        {
            var s = Find(world, id);
            if (s == null || !world.PlayerSees(s.Pos, s.Hull.Length)) continue;
            float fade = Mathf.Clamp((bar.Life - bar.Since) / 0.8f, 0, 1);
            var c = xf * Ink.V(s.Pos);
            float lift = Mathf.Max(26, (float)s.Hull.Length * Ink.PxPerM * zoom * 0.55f + 12);
            var r = new Rect2(c.X - 30, c.Y - lift, 60, 8);
            float actual = (float)Math.Clamp(s.HullHp / s.MaxHp, 0, 1);
            Box(r.Grow(2), Ink.Paper with { A = 0.92f * fade });
            Box(r, HudInk.Well with { A = fade });
            Box(new Rect2(r.Position, new Vector2(r.Size.X * actual, r.Size.Y)), (actual < 0.3f ? Ink.Red : HudInk.HullWood) with { A = fade });
            if (bar.Shown > actual + 0.002f)
                Box(new Rect2(r.Position.X + r.Size.X * actual, r.Position.Y, r.Size.X * (bar.Shown - actual), r.Size.Y), new Color(0.96f, 0.62f, 0.42f, fade));
            Frame(r.Grow(1), 1.4f, (bar.ByHer && world.Hostile(s, world.Ship) ? Ink.Red : Ink.Black) with { A = fade });
        }

        // The figures: pop in large, settle, rise and fade.
        foreach (var p in pops)
        {
            if (p.Age < 0) continue;
            float t = p.Age / p.Life;
            float alpha = t < 0.7f ? 1 : 1 - (t - 0.7f) / 0.3f;
            float pop = 1 + 0.45f * Mathf.Max(0, 1 - p.Age / 0.16f);
            var c = xf * Ink.V(p.At) + new Vector2(0, -28 + p.Drop - p.Age * (p.Big ? 18 : 30));
            var font = p.Big ? Fonts.DisplayCaps : Fonts.Body;
            SetBase(ScaleAbout(c, pop));
            TxtC(font, p.Size, c.X, c.Y, p.Text, p.Colour with { A = alpha }, p.Big ? 8 : 6, Ink.Paper with { A = 0.95f * alpha });
            SetBase(Transform2D.Identity);
        }
        FlushText();
    }
}
