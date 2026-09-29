using System.Globalization;
using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>One line of the ship card: its words, face, size and ink; a hull line also draws a gauge (0–1) under itself.</summary>
public readonly record struct CardLine(string Text, Font Font, int Size, Color Colour, float Gauge = -1, Color GaugeColour = default);

/// <summary>
/// The ship under the pointer at sea: a red ring round her and a card beside the cursor with her name, colours and
/// errand, how she stands toward you, her hull, guns and hands, what a merchant carries and where she is bound, the
/// Crown's bounty on a pirate, and how far off she lies. Only ships in sight answer. The view reads the sim and never
/// changes it.
/// </summary>
public partial class ShipCard : CanvasLayer
{
    /// <summary>Screen pixels of grace around a hull, so a small ship far out is still easy to point at.</summary>
    const float Slack = 8;

    World world = null!;
    CardCanvas canvas = null!;
    readonly List<CardLine> lines = new();
    long key = long.MinValue;

    /// <summary>The ship the pointer is on (her own included), or null.</summary>
    public Ship? Hovered { get; private set; }
    public IReadOnlyList<CardLine> Lines => lines;
    /// <summary>Every line of the card as one string (the self-test reads it).</summary>
    public string Text => string.Join("\n", lines.Select(l => l.Text));

    /// <summary>Self-test only: letters the card for a ship without the pointer on her.</summary>
    internal string DescribeForTest(Ship s) { Describe(s); return Text; }
    internal Vector2 Mouse, RingAt;
    internal float RingRadius;

    public void Init(World w, Font font)
    {
        world = w;
        Layer = 11;   // over the HUD, under the full pages (port, chart, crew: 12)
        canvas = new CardCanvas { Card = this, Theme = Parchment.Theme(font), MouseFilter = Control.MouseFilterEnum.Ignore };
        canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(canvas);
    }

    /// <param name="mouse">The pointer in viewport pixels (off the window: far outside it).</param>
    /// <param name="xf">The sea's canvas transform (world pixels → viewport).</param>
    /// <param name="active">At sea with nothing covering it: no page, pause, recap or title.</param>
    /// <param name="plates">The HUD's plates: a ship under one is behind it.</param>
    public void Refresh(Vector2 mouse, Transform2D xf, bool active, IReadOnlyList<Rect2> plates)
    {
        var was = Hovered;
        Hovered = active && !plates.Any(r => r.HasPoint(mouse)) ? Pick(mouse, xf) : null;
        Mouse = mouse;
        if (Hovered is { } s)
        {
            RingAt = xf * Ink.V(s.Pos);
            RingRadius = Mathf.Max(14, (xf * Ink.V(s.Pos + s.Forward * (s.Hull.Length * 0.5)) - RingAt).Length() + 6);
            long k = Key(s);
            if (k != key || was != s)
            {
                key = k;
                Describe(s);
            }
        }
        else key = long.MinValue;
        if (Hovered != null || was != null) canvas.QueueRedraw();
    }

    /// <summary>The nearest ship in sight whose hull (a capsule on screen) is within <see cref="Slack"/> of the pointer.</summary>
    Ship? Pick(Vector2 mouse, Transform2D xf)
    {
        Ship? best = null;
        float bestD = Slack;
        foreach (var s in world.AllShips)
        {
            if (s.Sunk || !s.IsPlayer && !world.PlayerSees(s.Pos, s.Hull.Length)) continue;
            double half = Math.Max(0, s.Hull.Length * 0.5 - s.Hull.Beam * 0.5);
            var centre = xf * Ink.V(s.Pos);
            var bow = xf * Ink.V(s.Pos + s.Forward * half);
            var stern = xf * Ink.V(s.Pos - s.Forward * half);
            float beam = (xf * Ink.V(s.Pos + s.Right * (s.Hull.Beam * 0.5)) - centre).Length();
            float d = Geometry2D.GetClosestPointToSegment(mouse, bow, stern).DistanceTo(mouse) - beam;
            if (d <= bestD)
            {
                bestD = d;
                best = s;
            }
        }
        return best;
    }

    /// <summary>What the card shows, folded into one number: the lines are lettered again only when it changes.</summary>
    long Key(Ship s)
    {
        unchecked
        {
            long k = s.Id;
            k = k * 131 + (long)Math.Ceiling(s.HullHp);
            k = k * 131 + s.Crew;
            k = k * 131 + s.Cannons;
            k = k * 131 + s.Leaks + (s.Foundering ? 1000 : 0);
            k = k * 131 + (long)Math.Round(s.Knots);
            k = k * 131 + (long)Math.Round(s.Pos.DistanceTo(world.Ship.Pos) / 10);
            k = k * 131 + (s.Ai?.CargoUnits ?? 0) * 64 + (s.Ai?.DestPort ?? -1);
            k = k * 131 + (s.Ai?.News.Count ?? 0) + (s.PlayerHostile ? 8 : 0);
            k = k * 131 + (world.Hostile(s, world.Ship) ? 1 : 0) + (world.Hostile(world.Ship, s) ? 2 : 0);
            k = k * 131 + (long)Math.Round(world.Player.Rep(s.Faction));
            k = k * 131 + (long)Math.Round(world.Player.SlotsUsed * 10);
            if (s.IsPlayer) foreach (var c in world.Player.Contracts) k = k * 131 + c.To * 7 + (long)Math.Round(c.Deadline * 8);
            return k;
        }
    }

    static string N(double v) => v.ToString("0", CultureInfo.InvariantCulture);

    void Describe(Ship s)
    {
        lines.Clear();
        var body = Fonts.Body;
        var italic = Fonts.Italic;
        lines.Add(new CardLine(world.NameOf(s), Fonts.SmallCaps, 22, Ink.Black));
        string hull = LastTide.Text.Get("HULL_" + s.Hull.Id);
        if (s.IsPlayer)
            lines.Add(new CardLine(LastTide.Text.Get("SHIPCARD_YOURS", hull), italic, 17, Parchment.Muted));
        else
        {
            string faction = s.Faction.ToString().ToLowerInvariant();
            string role = s.Ai?.Role is { } r and not Role.Player ? r.ToString().ToLowerInvariant() : "none_" + faction;
            lines.Add(new CardLine(LastTide.Text.Get("SHIPCARD_KIND", LastTide.Text.Get("FACTION_ADJ_" + faction), LastTide.Text.Get("ROLE_" + role), hull), italic, 17, Parchment.Muted));
            // How she stands toward you: hunting, hostile, wary (a merchant runs), friendly or neutral. Asked from her
            // side of the quarrel (her faction's standing, or her own grudge): the same answer as hers for every captain.
            bool hostile = world.Hostile(world.Ship, s);
            var (stance, ink) = s.Ai?.Role is Role.Hunter or Role.Privateer ? ("SHIPCARD_HUNTING", Ink.Red)
                : hostile && s.Ai?.Role == Role.Merchant ? ("SHIPCARD_WARY", Ink.Red)
                : hostile ? ("SHIPCARD_HOSTILE", Ink.Red)
                : world.Player.Rep(s.Faction) >= 20 ? ("SHIPCARD_FRIENDLY", Ink.Wind)
                : ("SHIPCARD_NEUTRAL", Parchment.Muted);
            lines.Add(new CardLine(LastTide.Text.Get(stance), italic, 17, ink));
        }
        // Hull, with a gauge; leaks and a last stand in red.
        string state = s.Foundering ? " · " + LastTide.Text.Get("SHIPCARD_FOUNDERING") : s.Leaks > 0 ? " · " + LastTide.Text.Get("SHIPCARD_LEAKING") : "";
        float frac = (float)Math.Clamp(s.HullHp / Math.Max(1, s.MaxHp), 0, 1);
        lines.Add(new CardLine(LastTide.Text.Get("SHIPCARD_HULL", N(Math.Ceiling(s.HullHp)), N(s.MaxHp)) + state, body, 17,
            s.Foundering || s.Leaks > 0 ? Ink.Red : Ink.Black, frac, frac < 0.4f ? Ink.Red : Parchment.Sepia));
        lines.Add(new CardLine(s.Cannons > 0
            ? LastTide.Text.Get("SHIPCARD_ARMS", s.Cannons, s.Crew, PartDef.PounderByGrade[Math.Clamp(s.CannonGrade, 0, PartDef.PounderByGrade.Length - 1)])
            : LastTide.Text.Get("SHIPCARD_UNARMED", s.Crew), body, 17, Ink.Black));
        if (s.IsPlayer)
        {
            lines.Add(new CardLine(LastTide.Text.Get("SHIPCARD_OWN_HOLD", world.Player.SlotsUsed.ToString("0.#", CultureInfo.InvariantCulture), s.CargoCapacity, N(s.Knots)), body, 17, Ink.Black));
            // Her deliveries and their days, soonest first: the chart shows where, this says when without opening it.
            foreach (var c in world.Player.Contracts.OrderBy(c => c.Deadline))
                lines.Add(new CardLine(LastTide.Text.Get("PORT_CONTRACT_LINE", LastTide.Text.Get("CONTRACT_" + ContractDef.Of(c.Kind).Key), world.Map.Ports[c.To].Name,
                    Parchment.DayWatch(c.Deadline)), italic, 16, c.Deadline - world.DaysSurvived < 1 ? Ink.Red : Parchment.Muted));
            return;
        }
        // A merchant's lading and errand (a port she has not charted stays unnamed).
        if (s.Ai is { Role: Role.Merchant } ai)
        {
            string lading = ai.CargoUnits > 0
                ? LastTide.Text.Get("SHIPCARD_LADEN", ai.CargoUnits, LastTide.Text.Get("GOOD_" + Goods.Of(ai.CargoGood).Key))
                : LastTide.Text.Get("SHIPCARD_BALLAST");
            if (ai.DestPort >= 0 && ai.DestPort < world.Map.Ports.Count)
            {
                var dest = world.Map.Ports[ai.DestPort];
                lading = LastTide.Text.Get("SHIPCARD_BOUND", lading, dest.Discovered ? dest.Name : LastTide.Text.Get("SHIPCARD_UNCHARTED"));
            }
            lines.Add(new CardLine(lading, body, 17, Ink.Black));
            // Her news (Nolan, 2026-09-28): how many ports' prices she carries, and how to ask for them.
            if (ai.News.Count > 0 && !s.PlayerHostile)
                lines.Add(new CardLine(LastTide.Text.Get(ai.News.Count == 1 ? "SHIPCARD_NEWS_1" : "SHIPCARD_NEWS", ai.News.Count, N(World.HailRange)), italic, 16, Ink.Wind));
        }
        if (s.Faction == Faction.Brethren)
            lines.Add(new CardLine(LastTide.Text.Get("SHIPCARD_BOUNTY", world.Bounty(s)), body, 17, Ink.Red));
        double d = s.Pos.DistanceTo(world.Ship.Pos);
        string reach = world.Ship.Cannons > 0 && d <= world.Ship.Range ? " · " + LastTide.Text.Get("SHIPCARD_IN_REACH") : "";
        lines.Add(new CardLine(LastTide.Text.Get("SHIPCARD_RANGE", N(Math.Round(d / 10) * 10), N(s.Knots)) + reach, italic, 16, Parchment.Muted));
    }
}

/// <summary>Draws the ring and the card (screen space, over the HUD).</summary>
public partial class CardCanvas : Control
{
    public ShipCard Card = null!;
    const float GaugeW = 170, GaugeH = 7;

    public override void _Draw()
    {
        if (Card.Hovered == null) return;
        DrawArc(Card.RingAt, Card.RingRadius, 0, Mathf.Tau, 48, Ink.Red, 2f, true);
        DrawArc(Card.RingAt, Card.RingRadius + 4, 0, Mathf.Tau, 52, Ink.Red with { A = 0.45f }, 1.2f, true);
        var lines = Card.Lines;
        float width = GaugeW, height = 18;
        foreach (var l in lines)
        {
            width = Mathf.Max(width, l.Font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, l.Size).X);
            height += l.Size + 9 + (l.Gauge >= 0 ? GaugeH + 5 : 0);
        }
        // Below and right of the pointer; left of it near the right edge; always wholly on the screen.
        var box = new Rect2(Card.Mouse + new Vector2(22, 16), new Vector2(width + 30, height));
        if (box.End.X > Size.X - 12) box.Position = new Vector2(Card.Mouse.X - 22 - box.Size.X, box.Position.Y);
        box.Position = new Vector2(Mathf.Clamp(box.Position.X, 12, Mathf.Max(12, Size.X - 12 - box.Size.X)),
            Mathf.Clamp(box.Position.Y, 12, Mathf.Max(12, Size.Y - 12 - box.Size.Y)));
        DrawRect(box, Ink.Paper);
        DrawStyleBox(GetThemeStylebox("panel", "TooltipPanel"), box);
        float x = box.Position.X + 15, y = box.Position.Y + 12;
        foreach (var l in lines)
        {
            y += l.Size + 2;
            DrawString(l.Font, new Vector2(x, y), l.Text, HorizontalAlignment.Left, -1, l.Size, l.Colour);
            y += 7;
            if (l.Gauge < 0) continue;
            var bar = new Rect2(x, y - 2, GaugeW, GaugeH);
            DrawRect(bar, Ink.Faint);
            DrawRect(new Rect2(bar.Position, new Vector2(GaugeW * l.Gauge, GaugeH)), l.GaugeColour);
            DrawRect(bar, Ink.Black with { A = 0.7f }, false, 1f);
            y += GaugeH + 5;
        }
    }
}
