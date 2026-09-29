using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// First-voyage onboarding (polish bar; GDD §19 "Onboarding"): short notes pinned to the chart beside the instrument
/// they explain, each shown once per profile when its moment comes (sails still furled, first time in irons, first
/// harbour ring, first hostile, first leak, first night…). Keys in the text are drawn as caps from the live bindings.
/// Never in debug runs.
/// </summary>
public partial class Hints : CanvasLayer
{
    /// <summary>A hint: when it fires, when it is done, and which HUD instrument its note points at (null = none).</summary>
    sealed record Hint(string Key, Func<Hints, bool> Fire, Func<Hints, bool> Done, string? Anchor = null);

    static readonly Hint[] Table =
    {
        new("first_sail", h => h.atSea > 1.5 && h.world.Ship.SailTarget == 0, h => h.world.Ship.SailTarget > 0, "helm"),
        new("wind_rose", h => h.atSea > 3 && h.world.Ship.SailTarget > 0, h => false, "rose"),
        new("irons", h => h.ironsTime > 1.5, h => !h.world.Ship.InIrons, "rose"),
        new("tack", h => h.closeHauledTime > 6, h => false, "rose"),
        // The harbour ring when she first comes into one (GDD §19 order), not the home ring she starts the voyage in.
        new("dock", h => h.world.HarborHere != null && h.outOfRing > 1, h => h.world.IsDocked, "prompt"),
        new("trade", h => h.world.Player.Ledger.Count > 0 && !h.world.IsDocked && h.atSea > 2, h => false),
        new("hostile", h => h.world.NearestThreatTo(h.world.Ship, 700) is { } t && h.world.PlayerSees(t.Pos), h => false, "guns"),
        new("leak", h => h.world.Ship.Leaks > 0 || h.world.Ship.Water > 3, h => false, "water"),
        new("night", h => h.world.IsNight, h => false, "wind"),
        new("threat", h => h.world.ThreatTier >= 1, h => false, "threat"),
        // No cartographer aboard (the voyage starts without one): say why nothing is kept, and where to find one.
        new("cartographer", h => h.atSea > 20 && !h.world.HasCartographer, h => h.world.HasCartographer, "prompt"),
        new("chart", h => h.atSea > 45 && !h.ChartOpened && h.world.HasCartographer, h => h.ChartOpened),
        new("spyglass", h => h.atSea > 90, h => false),
        new("first_fish", h => h.atSea > 60 && h.world.CanFish && h.world.Ship.Speed < 4 && !h.world.LinesOut, h => h.world.LinesOut, "prompt"),
        new("storm", h => h.world.ConditionsAt(h.world.Ship.Pos).Storm > 0.05, h => false, "wind"),
    };

    const double Hold = 9, MinHold = 4, Gap = 5;

    World world = null!;
    Profile profile = null!;
    HintNote note = null!;
    double atSea, ironsTime, closeHauledTime, shown, sinceLast = Gap, outOfRing;
    string? current;

    public bool Enabled;
    public bool ShowHints = true;
    /// <summary>Set by Main each frame (the HUD's prompt is up); the dock note reads the harbour ring itself.</summary>
    public bool DockPrompt;
    public bool ChartOpened;
    public string? Current => current;
    /// <summary>The HUD whose instruments the notes point at and whose key bindings they letter (Main sets it per voyage).</summary>
    public Hud? Hud { get; set; }
    /// <summary>The note as drawn: its text with keys resolved, and whether it is on screen (for the self-test).</summary>
    public string NoteText => note.Plain;
    public bool NoteVisible => note.Visible && note.Alpha > 0.5f;
    /// <summary>The note is on screen in any stage of its animation.</summary>
    public bool NoteShown => note.Visible;
    public Rect2 NoteRect => note.Card;

    public void Init(Profile p, Font font)
    {
        Layer = 11;
        profile = p;
        note = new HintNote { Hints = this, Visible = false };
        note.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(note);
    }

    public void Bind(World w)
    {
        world = w;
        atSea = ironsTime = closeHauledTime = shown = outOfRing = 0;
        sinceLast = Gap;
        current = null;
        ChartOpened = false;
        note.Hide(instant: true);
    }

    public void Update(double delta, bool paused)
    {
        note.Tick((float)delta);
        if (!Enabled || !ShowHints || world == null || world.RunOver)
        {
            current = null;
            note.Hide(instant: true);   // the title, Options' switch, a lost ship: gone at once, never fading over them
            return;
        }
        if (paused && !world.IsDocked) return;
        if (!world.IsDocked)
        {
            atSea += delta;
            var ship = world.Ship;
            ironsTime = ship.InIrons && ship.SailFraction > 0.3 && ship.Speed < 1.5 ? ironsTime + delta : 0;
            if (ship.PointOfSail == PointOfSail.CloseHauled && ship.SailFraction > 0.3) closeHauledTime += delta;
            if (world.HarborHere == null) outOfRing += delta;
        }
        if (current != null)
        {
            shown += delta;
            var h = Table.First(t => t.Key == current);
            if (shown >= Hold || (shown >= MinHold && h.Done(this)))
            {
                current = null;
                note.Hide(instant: false);
                sinceLast = 0;
            }
            return;
        }
        sinceLast += delta;
        if (sinceLast < Gap) return;
        foreach (var h in Table)
        {
            if (profile.HintSeen(h.Key) || !h.Fire(this)) continue;
            current = h.Key;
            shown = 0;
            note.Show(Text.Raw("HINT_" + h.Key), h.Anchor);
            profile.MarkHint(h.Key);
            break;
        }
    }

    /// <summary>The label on the cap for an action token in a hint (the live binding), or the token itself.</summary>
    internal string KeyFor(string token) => token switch
    {
        "RMB" => Text.Get("HUD_KEY_RMB"),
        "Esc" => Text.Get("HUD_KEY_ESC"),
        _ => Hud?.Key(token) ?? token,
    };
}

/// <summary>
/// A hint's note: a scrap of chart paper pinned with a brass pin, tilted a touch, its text lettered in ink with the
/// keys as caps; an ink leader points at the instrument it explains. It drops in and settles, and fades as it leaves.
/// </summary>
public partial class HintNote : InkCanvas
{
    public Hints Hints = null!;
    public float Alpha { get; private set; }
    public string Plain { get; private set; } = "";
    public Rect2 Card { get; private set; }

    struct Tok
    {
        public string Text;
        public bool Key, Space;
        public float W;
    }

    readonly List<Tok> tokens = new();
    readonly List<(int Start, int End, float Width)> lines = new();
    string? anchor;
    bool placed;
    Vector2 placedView;
    Rect2? placedAnchor;
    float placedWidth = 440;
    float age, target;
    int tilt = 1;
    const int FontSize = 17;
    const float LineH = 27, Pad = 18, CapH = 21;

    public void Show(string raw, string? anchorKey)
    {
        anchor = anchorKey;
        placed = false;
        Parse(raw);
        age = 0;
        target = 1;
        tilt = -tilt;
        Visible = true;
        QueueRedraw();
    }

    public void Hide(bool instant)
    {
        target = 0;
        if (instant) { Alpha = 0; Visible = false; }
    }

    public void Tick(float dt)
    {
        if (!Visible) return;
        age += dt;
        Alpha = Mathf.MoveToward(Alpha, target, dt / (target > 0 ? 0.3f : 0.35f));
        if (target <= 0 && Alpha <= 0) Visible = false;
        QueueRedraw();
    }

    /// <summary>Words and key caps: "[[SailUp]]" becomes a cap with the bound key; punctuation after it stays attached.</summary>
    void Parse(string raw)
    {
        tokens.Clear();
        var plain = new System.Text.StringBuilder();
        int i = 0;
        bool space = false;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c == ' ') { space = true; i++; continue; }
            if (space && plain.Length > 0) plain.Append(' ');
            if (c == '[' && i + 1 < raw.Length && raw[i + 1] == '[')
            {
                int end = raw.IndexOf("]]", i, StringComparison.Ordinal);
                if (end > i)
                {
                    string label = Hints.KeyFor(raw[(i + 2)..end]);
                    tokens.Add(new Tok { Text = label, Key = true, Space = space && tokens.Count > 0, W = KeyCapW(label, CapH) });
                    plain.Append(label);
                    space = false;
                    i = end + 2;
                    continue;
                }
            }
            int j = i + 1;
            while (j < raw.Length && raw[j] != ' ' && !(raw[j] == '[' && j + 1 < raw.Length && raw[j + 1] == '[')) j++;
            string word = raw[i..j];
            tokens.Add(new Tok { Text = word, Space = space && tokens.Count > 0, W = TextW(Fonts.Body, FontSize, word) });
            plain.Append(word);
            space = false;
            i = j;
        }
        Plain = plain.ToString();
    }

    void Wrap(float maxW)
    {
        lines.Clear();
        float spaceW = TextW(Fonts.Body, FontSize, "a a") - TextW(Fonts.Body, FontSize, "aa");
        int start = 0;
        float w = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            float add = tokens[i].W + (tokens[i].Space && i > start ? spaceW : 0);
            if (i > start && w + add > maxW)
            {
                lines.Add((start, i, w));
                start = i;
                w = tokens[i].W;
            }
            else w += add;
        }
        if (start < tokens.Count) lines.Add((start, tokens.Count, w));
    }

    public override void _Draw()
    {
        if (!Visible || Alpha <= 0.001f || tokens.Count == 0) return;
        var hud = Hints.Hud;
        var view = Size;
        Rect2? a = anchor != null ? hud?.AnchorOf(anchor) : null;
        // Placed once per note (and again only if the screen or the instrument moves), so it never hops about.
        if (!placed || view != placedView || a != placedAnchor)
        {
            placedView = view;
            placedAnchor = a;
            placed = true;
            (Card, placedWidth) = Place(a, view, hud);
        }
        Wrap(placedWidth - 2 * Pad);
        var card = Card;

        // Drops in from a little above and settles with a slight tilt; leaves by fading and lifting.
        float settle = Mathf.Clamp(age / 0.35f, 0, 1);
        float ease = 1 - Mathf.Pow(1 - settle, 3);
        float drop = target > 0 ? (1 - ease) * -14 : (1 - Alpha) * -8;
        float rot = Mathf.DegToRad(tilt * (1.1f + 2.4f * (1 - ease)));
        var centre = card.GetCenter();
        var x = new Transform2D(rot, centre + new Vector2(0, drop)) * new Transform2D(0, -centre);
        var ink = Ink.Black with { A = Alpha };

        // The leader first (under the paper), from the nearest point of the card to the instrument.
        if (a is { } ar)
        {
            var (from, to) = Leader(card, ar, anchor!, hud!);
            var dir = to - from;
            if (dir.Length() > 24 && dir.Length() < 320 && !Crosses(from, to, ar, hud!))
            {
                var d = dir.Normalized();
                to -= d * 5;
                Stroke(from, to - d * 7, 1.6f, Ink.Black with { A = 0.75f * Alpha });
                SpriteAt(HudArt.Tri, to - d * 3, new Vector2(12, 11), d.Angle(), ink);
            }
        }
        SetBase(x);
        Sprite(HudArt.Glow, card.Grow(16), Ink.Black with { A = 0.12f * Alpha });
        Plate(card, tint: Colors.White with { A = Alpha });
        // The pin that holds it to the chart.
        Icon("o_pin", new Rect2(new Vector2(centre.X - 10, card.Position.Y - 17), new Vector2(20, 36)), Colors.White with { A = Alpha });

        float y = card.Position.Y + Pad - 4;
        float spaceW = TextW(Fonts.Body, FontSize, "a a") - TextW(Fonts.Body, FontSize, "aa");
        foreach (var (s, e, lw) in lines)
        {
            float lx = card.Position.X + (card.Size.X - lw) / 2;
            for (int i = s; i < e; i++)
            {
                var t = tokens[i];
                if (t.Space && i > s) lx += spaceW;
                if (t.Key)
                    KeyCapA(new Vector2(lx, y + 2), t.Text, Alpha);
                else
                    Txt(Fonts.Body, FontSize, new Vector2(lx, y + 19), t.Text, ink);
                lx += t.W;
            }
            y += LineH;
        }
        FlushText();
    }

    void KeyCapA(Vector2 at, string label, float alpha)
    {
        float tw = TextW(Fonts.Body, 15, label);
        float w = Math.Max(CapH, tw + 11);
        var r = new Rect2(at, new Vector2(w, CapH));
        KeyFace(r, alpha, false);
        Txt(Fonts.Body, 15, new Vector2(r.Position.X + (w - tw) / 2, r.Position.Y + CapH * 0.5f + 4.5f), label, Ink.Black with { A = alpha });
    }

    /// <summary>The note's size when wrapped to a given width.</summary>
    Vector2 SizeAt(float width)
    {
        Wrap(width - 2 * Pad);
        float widest = 0;
        foreach (var l in lines) widest = Math.Max(widest, l.Width);
        return new Vector2(widest + 2 * Pad, lines.Count * LineH + 2 * Pad - 6);
    }

    /// <summary>
    /// Beside its instrument: the nearest clear spot around it (wrapping narrower if that is what fits), clear of the
    /// HUD's plates, the notice ribbon's slot and the screen edge; otherwise the hint slot above the prompt.
    /// </summary>
    (Rect2, float) Place(Rect2? anchor, Vector2 view, Hud? hud)
    {
        float full = Math.Min(440, view.X - 80);
        var size0 = SizeAt(full);
        var slot = hud?.HintSlot ?? new Rect2(view.X / 2 - 300, view.Y - 240, 600, 96);
        var fallback = new Rect2(slot.GetCenter().X - size0.X / 2, slot.End.Y - size0.Y, size0.X, size0.Y);
        if (anchor is not { } a || hud == null) return (fallback, full);
        var screen = new Rect2(Vector2.One * 8, view - Vector2.One * 16);
        var notices = hud.NoticeZone;
        var ac = a.GetCenter();
        const float gap = 26;
        Rect2 best = fallback;
        float bestW = full, bestScore = float.MaxValue;
        foreach (float width in new[] { full, 360f, 300f })
        {
            if (width > full) continue;
            var size = SizeAt(width);
            for (int side = 0; side < 4; side++)
                for (int away = 0; away < 4; away++)
                    for (int k = -6; k <= 6; k++)
                    {
                        float off = k * 30, far = gap + away * 45;
                        var r = side switch
                        {
                            0 => new Rect2(a.End.X + far, ac.Y - size.Y / 2 + off, size.X, size.Y),
                            1 => new Rect2(ac.X - size.X / 2 + off, a.End.Y + far, size.X, size.Y),
                            2 => new Rect2(ac.X - size.X / 2 + off, a.Position.Y - far - size.Y, size.X, size.Y),
                            _ => new Rect2(a.Position.X - far - size.X, ac.Y - size.Y / 2 + off, size.X, size.Y),
                        };
                        if (!screen.Encloses(r) || r.Grow(4).Intersects(a) || r.Intersects(notices)) continue;
                        bool blocked = false;
                        foreach (var kr in hud.KeepOut) if (kr.Intersects(r)) { blocked = true; break; }
                        if (blocked) continue;
                        // Near the instrument first; a narrower note costs a little; a leader that would have to cross
                        // another plate costs a lot (the note then goes without one).
                        var (from, to) = Leader(r, a, this.anchor!, hud);
                        float score = from.DistanceTo(to) + (full - width) * 0.4f;
                        if (Crosses(from, to, a, hud)) score += 1000;
                        if (score < bestScore) { bestScore = score; best = r; bestW = width; }
                    }
        }
        return (best, bestW);
    }

    /// <summary>True when the leader would run across another of the HUD's plates (then the note goes without one).</summary>
    static bool Crosses(Vector2 a, Vector2 b, Rect2 anchor, Hud hud)
    {
        foreach (var plate in hud.Plates)
        {
            if (plate.Intersects(anchor.Grow(2))) continue;
            for (int i = 1; i < 16; i++)
                if (plate.HasPoint(a.Lerp(b, i / 16f))) return true;
        }
        return false;
    }

    /// <summary>The leader's ends: the point on the instrument facing the card, and the card's nearest point to it.</summary>
    static (Vector2 From, Vector2 To) Leader(Rect2 card, Rect2 anchorRect, string anchorKey, Hud hud)
    {
        var to = hud.LeaderTarget(anchorKey, card.GetCenter()) ?? NearestOn(anchorRect, card.GetCenter());
        var from = NearestOn(card, to);
        to = hud.LeaderTarget(anchorKey, from) ?? NearestOn(anchorRect, from);
        return (from, to);
    }

    static Vector2 EdgeToward(Rect2 r, Vector2 p)
    {
        var c = r.GetCenter();
        var d = p - c;
        if (Math.Abs(d.X) * r.Size.Y > Math.Abs(d.Y) * r.Size.X)
            return new Vector2(d.X > 0 ? r.End.X : r.Position.X, c.Y);
        return new Vector2(c.X, d.Y > 0 ? r.End.Y : r.Position.Y);
    }

    static Vector2 NearestOn(Rect2 r, Vector2 p) =>
        new(Math.Clamp(p.X, r.Position.X, r.End.X), Math.Clamp(p.Y, r.Position.Y, r.End.Y));
}
