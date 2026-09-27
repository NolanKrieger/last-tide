using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>A mast: where it stands (fraction of the hull length from midships, + forward), its rig and size.</summary>
public readonly record struct MastPlan(float X, char Kind, float Size, float Boom = 0.4f);

/// <summary>How a hull is rigged when drawn: masts (S square, G gaff, L lateen), headsails, bowsprit, extras.</summary>
public sealed record RigPlan(MastPlan[] Masts, int Jibs, float Sprit, bool Spanker = false, bool Spritsail = false, bool GaffTopsail = false);

public partial class ShipView
{
    /// <summary>The rig of every hull (view data; the sim only knows the rig family and its polar).</summary>
    static readonly Dictionary<string, RigPlan> Plans = new()
    {
        ["sloop"] = new(new[] { new MastPlan(0.12f, 'G', 1f, 0.56f) }, 2, 0.32f, GaffTopsail: true),
        ["cutter"] = new(new[] { new MastPlan(0.06f, 'G', 1.05f, 0.56f) }, 3, 0.42f, GaffTopsail: true),
        ["schooner"] = new(new[] { new MastPlan(0.24f, 'G', 0.85f, 0.27f), new MastPlan(-0.06f, 'G', 1f, 0.42f) }, 2, 0.30f, GaffTopsail: true),
        ["xebec"] = new(new[] { new MastPlan(0.36f, 'L', 0.95f, 0.46f), new MastPlan(0.04f, 'L', 1.05f, 0.5f), new MastPlan(-0.28f, 'L', 0.8f, 0.4f) }, 0, 0.14f),
        ["brigantine"] = new(new[] { new MastPlan(0.22f, 'S', 0.95f), new MastPlan(-0.10f, 'G', 1.1f, 0.40f) }, 2, 0.30f),
        ["fluyt"] = new(new[] { new MastPlan(0.27f, 'S', 0.9f), new MastPlan(0.0f, 'S', 1f), new MastPlan(-0.30f, 'L', 0.6f, 0.30f) }, 1, 0.26f, Spritsail: true),
        ["brig"] = new(new[] { new MastPlan(0.22f, 'S', 0.95f), new MastPlan(-0.08f, 'S', 1f, 0.36f) }, 2, 0.28f, Spanker: true),
        ["barque"] = new(new[] { new MastPlan(0.27f, 'S', 0.95f), new MastPlan(0.0f, 'S', 1f), new MastPlan(-0.27f, 'G', 0.85f, 0.22f) }, 2, 0.26f),
        ["corvette"] = new(new[] { new MastPlan(0.27f, 'S', 0.95f), new MastPlan(0.01f, 'S', 1f), new MastPlan(-0.25f, 'S', 0.8f, 0.22f) }, 2, 0.26f, Spanker: true),
        ["frigate"] = new(new[] { new MastPlan(0.28f, 'S', 0.95f), new MastPlan(0.02f, 'S', 1f), new MastPlan(-0.24f, 'S', 0.8f, 0.22f) }, 3, 0.27f, Spanker: true),
        ["indiaman"] = new(new[] { new MastPlan(0.28f, 'S', 0.95f), new MastPlan(0.02f, 'S', 1f), new MastPlan(-0.24f, 'S', 0.78f, 0.22f) }, 2, 0.25f, Spanker: true, Spritsail: true),
        ["heavy_frigate"] = new(new[] { new MastPlan(0.28f, 'S', 0.95f), new MastPlan(0.02f, 'S', 1f), new MastPlan(-0.24f, 'S', 0.8f, 0.22f) }, 3, 0.27f, Spanker: true),
        ["galleon"] = new(new[] { new MastPlan(0.30f, 'S', 0.9f), new MastPlan(0.05f, 'S', 1f), new MastPlan(-0.20f, 'L', 0.7f, 0.26f), new MastPlan(-0.37f, 'L', 0.5f, 0.18f) }, 0, 0.24f, Spritsail: true),
        ["man_o_war"] = new(new[] { new MastPlan(0.28f, 'S', 0.95f), new MastPlan(0.02f, 'S', 1f), new MastPlan(-0.24f, 'S', 0.82f, 0.22f) }, 3, 0.26f, Spanker: true, Spritsail: true),
    };

    static RigPlan PlanFor(HullDef h)
    {
        if (Plans.TryGetValue(h.Id, out var p)) return p;
        return h.Rig switch
        {
            Rig.Square => Plans["frigate"],
            Rig.Mixed => Plans["brigantine"],
            Rig.Lateen => Plans["xebec"],
            _ => Plans["sloop"],
        };
    }

    // Sail levels in the sim are fractions 0, 0.4, 0.75, 1 (Tuning.SailFraction): each tier of canvas comes in
    // over one step, so the number of sails set reads the level at a glance.
    const float L1 = 0.4f, L2 = 0.75f;
    static float Ramp(float x, float a, float b) => Mathf.SmoothStep(a, b, x);

    static readonly Color Wood = new(0.36f, 0.25f, 0.15f);
    static readonly Color Spar = new(0.20f, 0.15f, 0.10f);

    /// <summary>Eases the sails toward the apparent wind: booms swing, yards brace, bellies fill or luff.</summary>
    void Trim(float dt)
    {
        var aw = ship.ApparentWind;
        float spd = (float)aw.Length;
        if (spd > 0.05f)
        {
            float local = (float)(aw.Angle - ship.Heading);
            var toward = new Vector2(Mathf.Cos(local), Mathf.Sin(local));   // where the wind goes, ship frame
            float k0 = trimReady ? 1 - Mathf.Exp(-dt * 4f) : 1f;
            awLocal = awLocal.Lerp(toward, k0).Normalized();
            if (awLocal == Vector2.Zero) awLocal = toward;
        }
        awSpeed = trimReady ? Mathf.Lerp(awSpeed, spd, 1 - Mathf.Exp(-dt * 2f)) : spd;
        var from = -awLocal;
        awOff = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(from.X, -1, 1)));
        int windSide = from.Y >= 0 ? 1 : -1;   // +1: the wind comes over the starboard side
        int lee = -windSide;
        float point = (float)ship.PointDeg;
        // Drawing when off the wind; aback (slightly) when head to wind, luffing between.
        float drawing = Mathf.SmoothStep(point - 14, point + 4, awOff);
        float fillT = Mathf.Lerp(-0.35f, 1f, drawing);
        float boomT = lee * Mathf.Clamp((awOff - 22f) * 0.55f, 4f, 82f) * Mathf.Lerp(0.25f, 1f, drawing);
        float braceT = windSide * Mathf.Clamp((180f - awOff) * 0.4f, 0f, 45f);
        if (sinkT >= 0 || ship.Sunk) { fillT = 0.1f; }
        if (!trimReady)
        {
            boomDeg = boomT; braceDeg = braceT; fill = fillT;
            trimReady = true;
            return;
        }
        float k = 1 - Mathf.Exp(-dt * 2.6f);
        boomDeg = Mathf.Lerp(boomDeg, boomT, k);
        braceDeg = Mathf.Lerp(braceDeg, braceT, k);
        fill = Mathf.Lerp(fill, fillT, 1 - Mathf.Exp(-dt * 4.5f));
        luff = Mathf.Clamp(1 - Mathf.Abs(fill) * 1.6f, 0, 1);
    }

    /// <summary>Belly pulse after a sail change: a snap out and a settle.</summary>
    float Pulse()
    {
        if (snap <= 0) return 1f;
        float s = 1 - snap / SnapTime;
        return 1f + 0.55f * Mathf.Sin(s * Mathf.Pi * 1.6f) * (1 - s) * (1 - s);
    }

    float WindFill() => Mathf.Clamp(0.62f + 0.42f * awSpeed / 9f, 0.7f, 1.3f);

    /// <summary>Canvas swells a little when she is small on screen, so a far ship still reads as a ship under sail.</summary>
    float exag = 1f;
    float Exag() => exag;

    /// <summary>The whole rig above the hull, in draw order (lowest canvas first).</summary>
    void DrawRig(InkBatch m, float L, float B, float px, float cut, float list)
    {
        float frac = (float)ship.SailFraction;
        exag = Mathf.Clamp(1f + (140f - L / px) / 160f, 1f, 1.6f);
        if (list > 0.2f) frac *= Mathf.Clamp(1 - (list - 0.2f) * 1.6f, 0, 1);   // canvas gone slack as she goes
        float lean = listSide * B * 0.55f * list;                                 // mastheads fall toward the list
        float bow = L * 0.49f;
        float spritLen = plan.Sprit * L;
        float detail = Mathf.Clamp((B / px - 14f) / 20f, 0f, 1f);                 // fine lines only when she is big on screen
        float ink = Mathf.Max(1.3f * px, 0.9f);
        float sparW = Mathf.Max(1.5f * px, B * 0.045f);

        // Bowsprit and jib-boom.
        if (spritLen > 0 && bow > cut)
        {
            var root = new Vector2(bow - L * 0.04f, 0);
            var tip = new Vector2(bow + spritLen, 0);
            m.Taper(root, tip, sparW * 1.3f, sparW * 0.6f, Spar, Spar);
        }

        // Shrouds: the lower rigging fans from each mast to the rails.
        if (detail > 0.05f)
            foreach (var mp in plan.Masts)
            {
                float mx = mp.X * L;
                if (mx < cut) continue;
                var head = new Vector2(mx, lean * 0.5f);
                for (int s = -1; s <= 1; s += 2)
                    for (int q = 0; q < 3; q++)
                    {
                        var foot = new Vector2(mx - L * 0.02f - q * B * 0.07f, s * B * 0.47f);
                        m.Line(head, foot, 0.8f * px, Ink.Black with { A = 0.32f * detail });
                    }
            }

        // Headsails on their stays, from the bowsprit to the foremast.
        var fore = plan.Masts[0];
        float foreX = fore.X * L;
        if (bow > cut)
        {
            if (plan.Spritsail) Spritsail(m, new Vector2(bow + spritLen * 0.62f, 0), B * 0.9f, Ramp(frac, L2, 1f), ink, detail, lean);
            for (int j = plan.Jibs - 1; j >= 0; j--)
            {
                float set = j == 0 ? Ramp(frac, L1 * 0.4f, L1 + 0.1f) : j == 1 ? Ramp(frac, L1, L2) : Ramp(frac, L2, 1f);
                float tx = j == 0 ? bow + spritLen * 0.15f : j == 1 ? bow + spritLen * 0.72f : bow + spritLen;
                var tack = new Vector2(tx, 0);
                var head = new Vector2(foreX + (fore.Kind == 'S' ? B * 0.1f : L * 0.015f) + j * L * 0.01f, lean * 0.7f);
                Jib(m, tack, head, set, j, ink, detail, px);
            }
        }

        // Masts, aftmost first so forward canvas overlaps, as seen from ahead-above.
        for (int i = plan.Masts.Length - 1; i >= 0; i--)
        {
            var mp = plan.Masts[i];
            float mx = mp.X * L;
            if (mx < cut) continue;
            bool aftmost = i == plan.Masts.Length - 1;
            switch (mp.Kind)
            {
                case 'S':
                    SquareMast(m, mx, mp.Size, B, frac, ink, sparW, detail, lean, px);
                    if (aftmost && plan.Spanker)
                        Gaff(m, new Vector2(mx, lean * 0.3f), mp.Boom * L, Ramp(frac, L1, L2), 1f, ink, sparW, detail, false, px);
                    break;
                case 'G':
                {
                    bool main = aftmost;
                    float set = main ? Ramp(frac, 0.02f, L1) : Ramp(frac, L1, L2);
                    float size = main ? Mathf.Lerp(0.72f, 1f, Ramp(frac, L1, 1f)) : 1f;
                    Gaff(m, new Vector2(mx, lean * 0.3f), mp.Boom * L * mp.Size, set, size, ink, sparW, detail, plan.GaffTopsail && main, px, Ramp(frac, L2, 1f));
                    MastCap(m, new Vector2(mx, lean), B * 0.055f * mp.Size, false, px);
                    break;
                }
                case 'L':
                {
                    int n = plan.Masts.Length;
                    // Lateen masts come in main first, then fore, then the mizzens.
                    float set = i == 1 || n == 1 ? Ramp(frac, 0.02f, L1) : i == 0 ? Ramp(frac, L1, L2) : Ramp(frac, L2, 1f);
                    if (plan.Masts[0].Kind == 'S') set = i == n - 1 ? Ramp(frac, L2, 1f) : Ramp(frac, L1, L2);
                    Lateen(m, new Vector2(mx, lean * 0.4f), mp.Boom * L, set, ink, sparW, detail, px);
                    MastCap(m, new Vector2(mx, lean), B * 0.05f * mp.Size, false, px);
                    break;
                }
            }
        }

        // The masthead fly on the mainmast: the apparent wind, read off the ship herself.
        var mainMast = plan.Masts[plan.Masts.Length > 1 && plan.Masts[0].Kind != 'G' ? 1 : plan.Masts.Length - 1];
        if (plan.Masts.Length == 2 && plan.Masts[1].Kind == 'G' && plan.Masts[0].Kind == 'S') mainMast = plan.Masts[0];
        float mmx = mainMast.X * L;
        if (mmx > cut && sinkT < 0) Pennant(m, new Vector2(mmx, lean), px, L);

        // The ensign at the stern: her side at a glance (the player's is her chosen flag).
        if (sinkT < 0 || -L * 0.48f > cut)
        {
            var staff = new Vector2(-L * 0.5f + B * 0.04f, 0);
            if (!ship.IsPlayer) Ensign(m, staff, px, L, Ink.Faction(ship.Faction), ship.Faction, false);
            else if (loadout[0].Length > 0) Ensign(m, staff, px, L, Cosmetic.Flag(loadout[0]), null, true);
        }
    }

    // ---- square rig ----

    void SquareMast(InkBatch m, float mx, float size, float B, float frac, float ink, float sparW, float detail, float lean, float px)
    {
        float course = B * 1.95f * size;
        // Tiers bottom-up: course (in over the second step), topsail (first step), topgallant (last step).
        Span<float> set = stackalloc float[3] { Ramp(frac, L1, L2), Ramp(frac, 0.02f, L1), Ramp(frac, L2, 1f) };
        Span<float> scale = stackalloc float[3] { 1f, 0.8f, 0.6f };
        for (int t = 0; t < 3; t++)
        {
            float W = course * scale[t];
            float h = t / 2f;                                   // height up the mast, 0..1
            var yc = new Vector2(mx - B * 0.11f * t, lean * (0.45f + 0.55f * h));
            SquareSail(m, yc, W, set[t], t, ink, Mathf.Max(1.2f * px, B * 0.03f) * (1f - 0.15f * t), detail, px);
            if (t == 0) MastTop(m, new Vector2(mx, lean * 0.5f), B * 0.3f * size, px);
        }
        MastCap(m, new Vector2(mx - B * 0.14f, lean), B * 0.045f * size, true, px);
    }

    void SquareSail(InkBatch m, Vector2 yc, float W, float set, int tier, float ink, float sparW, float detail, float px)
    {
        float br = Mathf.DegToRad(braceDeg);
        var d = new Vector2(Mathf.Sin(br), Mathf.Cos(br));       // yard: port arm → starboard arm
        var fwd = new Vector2(Mathf.Cos(br), -Mathf.Sin(br));    // the face the wind fills (forward)
        var a = yc - d * W * 0.5f;
        var b = yc + d * W * 0.5f;
        if (set > 0.03f)
        {
            float f = fill + luff * 0.22f * Mathf.Sin(time * 21f + tier * 1.9f + yc.X * 0.07f);
            float depth = W * 0.21f * WindFill() * Exag() * set * Mathf.Abs(f) * Pulse();
            var dir = f >= 0 ? fwd : -fwd;
            int windSide = braceDeg >= 0 ? 1 : -1;
            float peak = 0.5f - 0.07f * windSide * Mathf.Clamp(Mathf.Abs(braceDeg) / 30f, 0, 1);
            int panels = Math.Clamp((int)(W / (px * 7f)), 3, 9);
            SailBand(m, a, b, dir, Mathf.Max(depth, px * 3.2f * set * Mathf.Abs(f)), peak, panels, ink, detail, tier == 0, set);
        }
        // The yard, with whatever canvas is still furled on it.
        m.Taper(a, b, sparW, sparW, Spar, Spar);
        if (set < 0.97f) Furl(m, a, b, sparW * (1.9f - 1.3f * set), px, 1 - set);
    }

    void MastTop(InkBatch m, Vector2 c, float w, float px)
    {
        Span<Vector2> ring = stackalloc Vector2[8];
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.Tau / 8;
            ring[k] = c + new Vector2(Mathf.Cos(a) * w * 0.22f - w * 0.06f, Mathf.Sin(a) * w * 0.5f);
        }
        m.Convex(ring, new Color(0.47f, 0.34f, 0.21f));
        m.Polyline(ring, Mathf.Max(0.9f * px, w * 0.04f), Ink.Black with { A = 0.85f }, true);
    }

    // ---- fore-and-aft rig ----

    void Gaff(InkBatch m, Vector2 mast, float boomLen, float set, float size, float ink, float sparW, float detail, bool topsail, float px, float topSet = 0)
    {
        float ang = Mathf.DegToRad(boomDeg);
        int s = boomDeg >= 0 ? 1 : -1;
        float th = Mathf.Abs(ang);
        var boomDir = new Vector2(-Mathf.Cos(th), s * Mathf.Sin(th));
        var belly = new Vector2(Mathf.Sin(th), s * Mathf.Cos(th));
        var clew = mast + boomDir * boomLen;
        // Boom first (lowest), then the canvas, then the gaff over it.
        m.Taper(mast, clew + boomDir * boomLen * 0.04f, sparW * 1.05f, sparW * 0.75f, Spar, Spar);
        float depth = 0;
        if (set > 0.03f)
        {
            float f = fill + luff * 0.3f * Mathf.Sin(time * 19f + mast.X * 0.05f);
            depth = boomLen * 0.46f * WindFill() * Exag() * set * size * Mathf.Abs(f) * Pulse();
            var dir = f >= 0 ? belly : -belly;
            int panels = Math.Clamp((int)(boomLen / (px * 7f)), 3, 8);
            SailBand(m, mast, clew, dir, Mathf.Max(depth, px * 4f * set * Mathf.Abs(f)), 0.42f, panels, ink, detail, true, set);
            if (topsail && topSet > 0.03f)
            {
                var peakEnd = mast + boomDir * boomLen * 0.62f + dir * depth * 0.75f;
                float td = boomLen * 0.62f * 0.3f * topSet * WindFill() * Mathf.Abs(f);
                SailBand(m, mast + dir * depth * 0.1f, peakEnd, dir, Mathf.Max(td, px * 0.5f), 0.4f, 3, ink, detail, false, topSet);
            }
            // The gaff: from the throat to the peak, which sags off to leeward over the leech.
            var peak = mast + boomDir * boomLen * 0.66f + dir * depth * 0.62f;
            m.Taper(mast, peak, sparW * 0.95f, sparW * 0.65f, Spar, Spar);
        }
        float reef = set < 0.03f ? 1f : 1f - size * Mathf.Min(1, set * 1.5f);
        if (reef > 0.04f) Furl(m, mast + boomDir * boomLen * 0.04f, clew, sparW * (1f + 1.1f * reef), px, reef);
    }

    void Lateen(InkBatch m, Vector2 mast, float yardLen, float set, float ink, float sparW, float detail, float px)
    {
        float th = Mathf.DegToRad(Mathf.Clamp(Mathf.Abs(boomDeg), 12f, 62f));
        int s = boomDeg >= 0 ? 1 : -1;
        var dir = new Vector2(-Mathf.Cos(th), s * Mathf.Sin(th));
        var belly = new Vector2(Mathf.Sin(th), s * Mathf.Cos(th));
        var tack = mast - dir * yardLen * 0.34f;
        var peak = mast + dir * yardLen * 0.66f;
        if (set > 0.03f)
        {
            float f = fill + luff * 0.3f * Mathf.Sin(time * 18f + mast.X * 0.05f);
            float depth = yardLen * 0.36f * WindFill() * Exag() * set * Mathf.Abs(f) * Pulse();
            int panels = Math.Clamp((int)(yardLen / (px * 7f)), 3, 8);
            SailBand(m, tack, peak, f >= 0 ? belly : -belly, Mathf.Max(depth, px * 4f * set * Mathf.Abs(f)), 0.55f, panels, ink, detail, true, set);
        }
        m.Taper(tack, peak, sparW * 1.1f, sparW * 0.55f, Spar, Spar);
        if (set < 0.97f) Furl(m, tack + dir * yardLen * 0.05f, peak - dir * yardLen * 0.05f, sparW * (1.8f - 1.2f * set), px, 1 - set);
    }

    void Jib(InkBatch m, Vector2 tack, Vector2 head, float set, int j, float ink, float detail, float px)
    {
        // The stay is always there; the headsail fills to leeward along it.
        m.Line(tack, head, Mathf.Max(0.9f * px, 0.5f), Ink.Black with { A = 0.7f });
        if (set <= 0.03f) return;
        int lee = boomDeg >= 0 ? 1 : -1;
        float len = tack.DistanceTo(head);
        float reach = Mathf.Clamp((awOff - 40f) / 90f, 0f, 1f);
        float blanket = awOff > 150f ? Mathf.Lerp(1f, 0.55f, (awOff - 150f) / 30f) : 1f;   // blanketed by the main when running
        float f = fill + luff * 0.35f * Mathf.Sin(time * 23f + j * 2.1f);
        float depth = len * (0.22f - 0.03f * j) * Exag() * Mathf.Lerp(0.75f, 1.1f, reach) * blanket * WindFill() * set * Mathf.Abs(f) * Pulse();
        var d = (head - tack).Normalized();
        var normal = new Vector2(-d.Y, d.X);
        if (normal.Y * lee < 0) normal = -normal;
        if (f < 0) normal = -normal;
        SailBand(m, tack, head, normal, Mathf.Max(depth, px * 2.5f * set * Mathf.Abs(f)), 0.62f, 3, ink, detail, false, set);
    }

    void Spritsail(InkBatch m, Vector2 yc, float W, float set, float ink, float detail, float lean)
    {
        var a = yc + new Vector2(0, -W * 0.5f);
        var b = yc + new Vector2(0, W * 0.5f);
        if (set > 0.03f)
            SailBand(m, a, b, new Vector2(1, 0), Mathf.Max(W * 0.3f * set * Mathf.Abs(fill) * WindFill(), m.Px * 3f * set), 0.5f, 4, ink, detail, false, set);
        m.Taper(a, b, ink * 1.2f, ink * 1.2f, Spar, Spar);
        if (set < 0.97f) Furl(m, a, b, ink * 1.8f, m.Px, 1 - set);
    }

    // ---- canvas ----

    /// <summary>
    /// One sail seen from above: a band from the spar (a→b) bulging along <paramref name="dir"/> to a
    /// belly edge, washed in watercolour that pools at the edge, cloths seamed, outlined in ink.
    /// </summary>
    void SailBand(InkBatch m, Vector2 a, Vector2 b, Vector2 dir, float depth, float peak, int panels, float ink, float detail, bool reefBand, float set)
    {
        int n = panels + 1;
        Span<Vector2> p = stackalloc Vector2[n * 3];
        Span<Color> c = stackalloc Color[n * 3];
        var cloth = sailColor;
        if (ship.TornSails) cloth = cloth.Darkened(0.08f);
        var shade = cloth.Darkened(0.12f);
        var pool = cloth.Lerp(new Color(0.62f, 0.55f, 0.45f), 0.42f) with { A = 1 };
        var lit = cloth.Lightened(0.06f);
        float slack = 1 - set;
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1);
            float sh = u < peak ? Mathf.Sin(Mathf.Pi * 0.5f * u / peak) : Mathf.Sin(Mathf.Pi * 0.5f * (1 - u) / (1 - peak));
            sh = Mathf.Pow(Mathf.Max(sh, 0), 0.85f);
            var baseP = a.Lerp(b, u);
            var off = dir * depth * sh;
            p[i] = baseP;
            p[n + i] = baseP + off * 0.5f;
            p[2 * n + i] = baseP + off;
            c[i] = shade;
            c[n + i] = lit.Lerp(shade, slack * 0.5f);
            c[2 * n + i] = pool;
        }
        m.Grid(p, c, 3, n);
        if (striped)
            for (int i = 0; i < n - 1; i += 2)
                m.Quad(p[i], p[i + 1], p[2 * n + i + 1], p[2 * n + i], Ink.Red with { A = 0.62f });
        // Seams run from the spar to the belly; a reef band just below the spar on the big sails.
        if (detail > 0.02f)
        {
            var seam = Ink.Black with { A = 0.2f * detail };
            for (int i = 1; i < n - 1; i++)
                m.Line(p[i], p[2 * n + i], 0.8f * m.Px, seam);
            if (reefBand && depth > m.Px * 8)
            {
                Span<Vector2> reefLine = stackalloc Vector2[n];
                for (int i = 0; i < n; i++) reefLine[i] = p[i].Lerp(p[2 * n + i], 0.28f);
                m.Polyline(reefLine, 0.8f * m.Px, Ink.Black with { A = 0.22f * detail });
            }
        }
        if (ship.TornSails)
            for (int k = 0; k < 2; k++)
            {
                int i = 1 + (int)(Ink.Jitter(ship.Id, k, (int)(a.X * 3)) * (n - 2));
                var q0 = p[i].Lerp(p[2 * n + i], 0.35f);
                var q1 = p[Math.Min(i + 1, n - 1)].Lerp(p[2 * n + Math.Min(i + 1, n - 1)], 0.7f);
                m.Line(q0, q1, Mathf.Max(1.4f * m.Px, depth * 0.08f), Ink.Black with { A = 0.75f });
            }
        // The belly edge carries the weight of the ink; the head is under the spar.
        m.Polyline(p.Slice(2 * n, n), ink, Ink.Black);
    }

    /// <summary>Canvas furled along a spar: a soft roll with gasket ties.</summary>
    void Furl(InkBatch m, Vector2 a, Vector2 b, float w, float px, float amount)
    {
        if (amount < 0.04f) return;
        var d = b - a;
        float len = d.Length();
        if (len < 1e-3f) return;
        var t = d / len;
        var roll = sailColor.Darkened(0.06f);
        m.Taper(a + t * len * 0.04f, b - t * len * 0.04f, w, w, roll, roll);
        var nrm = new Vector2(-t.Y, t.X);
        float half = w * 0.5f;
        m.Line(a + t * len * 0.04f + nrm * half, b - t * len * 0.04f + nrm * half, 0.8f * px, Ink.Black with { A = 0.55f });
        m.Line(a + t * len * 0.04f - nrm * half, b - t * len * 0.04f - nrm * half, 0.8f * px, Ink.Black with { A = 0.55f });
        int ties = Math.Clamp((int)(len / (px * 10f)), 2, 7);
        for (int k = 1; k <= ties; k++)
        {
            var q = a.Lerp(b, k / (ties + 1f));
            m.Line(q - nrm * half * 1.1f, q + nrm * half * 1.1f, 0.8f * px, Ink.Black with { A = 0.6f });
        }
    }

    void MastCap(InkBatch m, Vector2 c, float r, bool square, float px)
    {
        r = Mathf.Max(r, 1.6f * px);
        m.Disc(c, r, Wood, 10);
        m.Circle(c, r, Mathf.Max(0.9f * px, r * 0.25f), Ink.Black, 12);
        if (square) m.Disc(c, r * 0.35f, new Color(0.62f, 0.5f, 0.34f), 6);
    }

    // ---- flags ----

    /// <summary>The masthead fly: a long streamer that lies where the apparent wind goes.</summary>
    void Pennant(InkBatch m, Vector2 mast, float px, float L)
    {
        var aw = ship.ApparentWind;
        if (aw.Length < 0.3) return;
        var dir = awLocal;
        var perp = new Vector2(-dir.Y, dir.X);
        float len = Mathf.Max(L * 0.16f + 7f * Mathf.Clamp((float)aw.Length / 12f, 0, 1), 22f * px);
        const int n = 7;
        Span<Vector2> spine = stackalloc Vector2[n];
        for (int k = 0; k < n; k++)
        {
            float u = k / (float)(n - 1);
            float wave = Mathf.Sin(time * 11f - u * 7f) * len * 0.05f * u;
            spine[k] = mast + dir * (u * len) + perp * wave;
        }
        m.PolylineWidths(spine, Mathf.Max(3.2f * px, len * 0.14f), 0.6f * px, Ink.Wind, Ink.Wind);
    }

    /// <summary>A flag streaming from the stern staff: faction field and device, or the player's colours.</summary>
    void Ensign(InkBatch m, Vector2 staff, float px, float L, Color field, Faction? faction, bool swallowtail)
    {
        var dir = awLocal;
        if (ship.ApparentWind.Length < 0.3) dir = new Vector2(-1, 0);
        var perp = new Vector2(-dir.Y, dir.X);
        float fl = Mathf.Max(L * 0.13f, 17f * px);
        float fh = fl * 0.62f;
        Vector2 F(float u, float v) => staff + dir * (u * fl) + perp * ((v - 0.5f) * fh + Mathf.Sin(time * 9f - u * 5.5f) * fh * 0.14f * u);
        void Rect(float u0, float u1, float v0, float v1, Color col)
        {
            const int cols = 4;
            for (int k = 0; k < cols; k++)
            {
                float ua = Mathf.Lerp(u0, u1, k / (float)cols), ub = Mathf.Lerp(u0, u1, (k + 1) / (float)cols);
                m.Quad(F(ua, v0), F(ub, v0), F(ub, v1), F(ua, v1), col);
            }
        }
        // Staff.
        m.Line(staff - dir * fl * 0.05f, staff + perp * 0, Mathf.Max(1.2f * px, 0.8f), Spar);
        if (swallowtail)
        {
            Rect(0, 0.7f, 0, 1, field);
            m.Quad(F(0.7f, 0), F(1f, 0), F(0.85f, 0.5f), F(0.7f, 0.5f), field);
            m.Quad(F(0.7f, 0.5f), F(0.85f, 0.5f), F(1f, 1), F(0.7f, 1), field);
        }
        else Rect(0, 1, 0, 1, field);
        switch (faction)
        {
            case Faction.Crown:
                // A paper cross on the red: the navy's colours.
                Rect(0.28f, 0.42f, 0, 1, Ink.Paper);
                Rect(0, 1, 0.4f, 0.6f, Ink.Paper);
                break;
            case Faction.FreeTraders:
                // A merchant house flag: paper field, an ink band.
                Rect(0, 1, 0, 1, Ink.Paper);
                Rect(0, 1, 0.36f, 0.64f, field);
                break;
            case Faction.Brethren:
                // A pale skull over crossed bones on the dark field.
                {
                    var sk = F(0.46f, 0.42f);
                    float r = fh * 0.17f;
                    m.Disc(sk, r, Ink.Paper, 8);
                    m.Line(F(0.3f, 0.66f), F(0.62f, 0.84f), Mathf.Max(0.9f * px, r * 0.4f), Ink.Paper);
                    m.Line(F(0.3f, 0.84f), F(0.62f, 0.66f), Mathf.Max(0.9f * px, r * 0.4f), Ink.Paper);
                }
                break;
        }
        // Ink outline round the fly.
        Span<Vector2> edge = stackalloc Vector2[swallowtail ? 11 : 10];
        int e = 0;
        for (int k = 0; k <= 4; k++) edge[e++] = F(k / 4f, 0);
        if (swallowtail) { edge[e++] = F(0.85f, 0.5f); }
        for (int k = 4; k >= 0; k--) edge[e++] = F(k / 4f, 1);
        m.Polyline(edge[..e], Mathf.Max(0.9f * px, 0.6f), Ink.Black with { A = 0.85f }, true);
    }

    /// <summary>The player's carved figurehead at the stem, gilded.</summary>
    void Figurehead(InkBatch m, Vector2 bow, string key, float B)
    {
        var gold = Cosmetic.Gold;
        float s = Mathf.Max(B * 0.34f, 7f * m.Px);
        float w = Mathf.Max(1.3f * m.Px, s * 0.2f);
        switch (key)
        {
            case "figurehead_serpent":
            {
                Span<Vector2> body = stackalloc Vector2[6];
                for (int k = 0; k < 6; k++) body[k] = bow + new Vector2(k * s * 0.3f, Mathf.Sin(k * 1.4f) * s * 0.28f);
                m.Polyline(body, w * 1.6f, Ink.Black);
                m.Polyline(body, w, gold);
                m.Disc(body[5] + new Vector2(s * 0.15f, 0), s * 0.2f, gold, 8);
                break;
            }
            case "figurehead_siren":
                m.Disc(bow + new Vector2(s * 0.95f, 0), s * 0.26f, gold, 10);
                m.Taper(bow, bow + new Vector2(s * 0.8f, 0), s * 0.5f, s * 0.3f, gold, gold);
                m.Line(bow + new Vector2(s * 0.55f, -s * 0.35f), bow + new Vector2(s * 1.2f, -s * 0.5f), w, gold);
                m.Line(bow + new Vector2(s * 0.55f, s * 0.35f), bow + new Vector2(s * 1.2f, s * 0.5f), w, gold);
                m.Circle(bow + new Vector2(s * 0.95f, 0), s * 0.26f, 0.9f * m.Px, Ink.Black, 10);
                break;
            case "figurehead_kraken":
                Span<Vector2> arm = stackalloc Vector2[5];
                for (int i = -1; i <= 1; i++)
                {
                    for (int k = 0; k < 5; k++)
                    {
                        float u = k / 4f;
                        arm[k] = bow + new Vector2(u * s * 1.2f, i * s * 0.35f * (0.4f + u) + Mathf.Sin(u * 5 + i) * s * 0.12f);
                    }
                    m.PolylineWidths(arm, w * 1.8f, w * 0.6f, Ink.Black, Ink.Black);
                    m.PolylineWidths(arm, w * 1.1f, w * 0.3f, gold, gold);
                }
                break;
        }
    }
}
