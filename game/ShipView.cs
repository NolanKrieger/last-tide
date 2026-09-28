using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// A ship on the chart: the plan-view hull art (painted for cosmetics and factions), and above it the rig
/// drawn in ink — spars, masts and tops, and the sails as a separate live layer: bellied bands across the
/// yards and crescents along booms, gaffs and stays, set tier by tier with the sail level, braced and
/// sheeted to the apparent wind, swinging across on a tack and luffing in irons. A masthead fly shows the
/// apparent wind and an ensign names her side. Local +X is forward and +Y is starboard; 4 px per metre.
/// Everything above the hull is one triangle batch (<see cref="InkBatch"/>): 3–4 draw calls a ship.
/// </summary>
public partial class ShipView : Node2D
{
    Ship ship = null!;
    public Ship Ship => ship;
    public Vec2 LastPos;
    public double LastHeading;
    /// <summary>Firing arcs shown while Q/E are held (player only).</summary>
    public bool AimPort, AimStarboard;
    /// <summary>The spyglass cone while the right button is held (player only).</summary>
    public bool Spyglass;
    public double SpyglassDir, SpyglassRange;

    string[] loadout = { "", "", "", "", "" };
    float flash, snap, time;
    const float FlashTime = 0.35f, SnapTime = 0.55f;
    public const float SinkTime = 7.5f;
    /// <summary>Her own sinking is quicker: the logbook opens 2.5 s after the run ends (GDD §19), and she is gone by then.</summary>
    public const float PlayerSinkTime = 2.3f;
    float sinkT = -1, sinkTime = SinkTime;
    int listSide = 1;
    Color hullPaint = Ink.Hull, sailColor = Ink.Sail;
    bool striped;
    Texture2D? hullTex, shadowTex;
    Vector2 shadowPad;
    float artAspect = 3.2f;
    RigPlan plan = null!;
    /// <summary>Heel under sail, signed toward the lee side (+ starboard): the mastheads lean with the pressure.</summary>
    float heel;

    // The sails' own trim, eased toward the apparent wind so they swing rather than pop.
    float boomDeg, braceDeg, fill = 1f, luff, awOff = 90f, awSpeed = 8f;
    Vector2 awLocal = new(-1, 0);
    bool trimReady;

    static readonly InkBatch over = new(), rig = new(), cast = new();

    /// <summary>How strongly the sun casts shadows: 1 by day, 0 at night, faint in fog and rain (set by the weather each frame).</summary>
    public static float Sunlight = 1f;
    /// <summary>Shadows fall to the south-east, lit from the north-west as the gulls are.</summary>
    static readonly Vector2 ShadowDir = new Vector2(7, 9).Normalized();
    static readonly Color ShadowInk = new(0.13f, 0.14f, 0.17f);
    /// <summary>The shadow direction in her own frame (unit), refreshed every draw.</summary>
    Vector2 shadowLocal = new(0, 1);

    /// <summary>Every ship view in the tree (the player's and the fleet's), for the night lanterns.</summary>
    public static readonly List<ShipView> Live = new();
    public override void _EnterTree() => Live.Add(this);
    public override void _ExitTree() => Live.Remove(this);

    /// <summary>Her stern lantern in canvas coordinates, or null when it should not show (gone under, not in sight).</summary>
    public Vector2? LanternAt()
    {
        if (ship == null || sinkT >= 0 || !IsVisibleInTree()) return null;
        var (L, _) = Size();
        return GetGlobalTransform() * new Vector2(-L * 0.47f, 0);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        time += dt;
        if (flash > 0) flash -= dt;
        if (snap > 0) snap -= dt;
        if (ship != null)
        {
            if (sinkT < 0 && ship.Sunk) BeginSinking();
            if (sinkT >= 0) sinkT += dt;
            Trim(dt);
        }
        QueueRedraw();
    }

    /// <summary>A hit or a ram: the hull inks red for a moment.</summary>
    public void Flash() { if (sinkT < 0) flash = FlashTime; }
    /// <summary>A sail change: the canvas bellies and settles.</summary>
    public void SailSnap() => snap = SnapTime;
    public bool Flashing => flash > 0;
    /// <summary>She is going down: listing, settling by the stern, gone after <see cref="SinkTime"/> s.</summary>
    public bool Sinking => sinkT >= 0;
    public bool SunkFromView => sinkT >= sinkTime;

    public void BeginSinking()
    {
        if (sinkT >= 0) return;
        sinkT = 0;
        sinkTime = ship.IsPlayer ? PlayerSinkTime : SinkTime;
        listSide = (ship.Id & 1) == 0 ? 1 : -1;
    }

    public void Init(Ship s, string[]? cosmetics = null)
    {
        if (cosmetics != null && cosmetics.Length == loadout.Length) loadout = (string[])cosmetics.Clone();
        ship = s;
        LastPos = s.Pos;
        LastHeading = s.Heading;
        plan = PlanFor(s.Hull);
        var livery = ShipArt.For(s.Hull, s.IsPlayer, s.Faction, loadout[3]);
        hullPaint = livery.Topsides;
        hullTex = ShipArt.Dressed(s.Hull.Id, livery);
        (shadowTex, shadowPad) = ShipArt.Shadow(s.Hull.Id);
        heel = 0;
        artAspect = hullTex != null ? hullTex.GetWidth() / (float)Math.Max(1, hullTex.GetHeight()) : (float)(s.Hull.Length / s.Hull.Beam);
        sailColor = s.IsPlayer ? Cosmetic.Sail(loadout[1]) : s.Faction switch
        {
            Faction.Crown => new Color(0.98f, 0.97f, 0.93f),
            Faction.Brethren => new Color(0.84f, 0.80f, 0.72f),
            _ => Ink.Sail,
        };
        striped = s.IsPlayer && Cosmetic.Striped(loadout[1]);
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        sinkT = -1;
        SelfModulate = Colors.White;
        trimReady = false;
        Trim(0);
    }

    public void SetPose(Vec2 pos, double heading)
    {
        Position = Ink.V(pos);
        Rotation = (float)heading;
        QueueRedraw();
    }

    /// <summary>Hull length and drawn beam in local pixels (the art keeps its own proportions within reason).</summary>
    (float L, float B) Size()
    {
        float L = (float)ship.Hull.Length * Ink.PxPerM;
        float B = (float)ship.Hull.Beam * Ink.PxPerM;
        if (hullTex != null) B = Mathf.Lerp(B, L / artAspect, 0.5f);
        return (L, B);
    }

    /// <summary>One screen pixel in local units at the current zoom and window scale.</summary>
    float ScreenPx()
    {
        var xf = GetViewport().GetFinalTransform() * GetGlobalTransformWithCanvas();
        float s = xf.X.Length();
        return s > 1e-4f ? 1f / s : 1f;
    }

    public override void _Draw()
    {
        if (ship == null) return;
        var (L, B) = Size();
        float px = ScreenPx();
        float sinkK = sinkT < 0 ? 0 : Mathf.Clamp(sinkT / sinkTime, 0, 1);
        if (sinkT >= sinkTime) return;

        if (sinkT < 0)
        {
            if (AimPort) DrawArcOfFire(-1, px);
            if (AimStarboard) DrawArcOfFire(1, px);
            if (Spyglass) DrawSpyglass(px);
        }

        // Sinking: she lists (the beam foreshortens), then settles by the stern; the whole ship darkens.
        float list = 0f;
        if (ship.Foundering) list = 0.28f + 0.04f * Mathf.Sin(time * 1.3f);
        if (sinkT >= 0) list = Mathf.Min(1f, 0.3f + sinkK * 4.7f);
        float under = sinkT < 0 ? 0 : Mathf.SmoothStep(0.13f, 0.9f, sinkK);   // stern → bow
        float cut = -L / 2 + under * L * 1.05f;   // everything aft of this x is under water
        if (sinkT >= 0)
        {
            float dark = 0.5f * sinkK;
            SelfModulate = new Color(1 - dark, 1 - dark * 0.9f, 1 - dark * 0.7f, 1f - Mathf.SmoothStep(0.76f, 1f, sinkK));
        }
        float squash = 1f - 0.32f * list;
        var shift = new Vector2(0, listSide * B * 0.08f * list);
        if (list > 0) DrawSetTransform(shift, 0, new Vector2(1, squash));

        shadowLocal = ShadowDir.Rotated(-GetGlobalTransform().Rotation);
        float sun = Sunlight * (1 - under);
        DrawHull(L, B, px, cut, sun);

        // Water and hull marks first, then the shadow the rig casts across them, then the rig.
        over.Clear();
        over.Px = px / Mathf.Max(0.5f, squash);
        if (sinkT < 0) { BowWave(over, L, B); DamageMarks(over, L, B); }
        rig.Clear();
        rig.Px = over.Px;
        cast.Clear();
        cast.Px = over.Px;
        DrawRig(rig, L, B, px, cut, list);
        if (sinkT < 0 && ship.IsPlayer && loadout[2].Length > 0) Figurehead(rig, new Vector2(L * 0.5f, 0), loadout[2], B);
        if (sun > 0.01f) over.AppendShadow(cast, shadowLocal * (B * 0.5f + L * 0.03f), ShadowInk with { A = 0.15f * sun });
        if (under > 0.02f && under < 0.99f) Churn(rig, cut, B);
        over.Flush(this);
        rig.Flush(this);
        if (list > 0) DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    void DrawHull(float L, float B, float px, float cut, float sun)
    {
        var rect = new Rect2(-L / 2, -B / 2, L, B);
        if (hullTex != null)
        {
            float tw = hullTex.GetWidth(), th = hullTex.GetHeight();
            float u0 = Mathf.Clamp((cut + L / 2) / L, 0, 1);
            if (u0 >= 1) return;
            var src = new Rect2(u0 * tw, 0, (1 - u0) * tw, th);
            var dst = new Rect2(-L / 2 + u0 * L, -B / 2, (1 - u0) * L, B);
            if (shadowTex != null)
            {
                var pad = new Vector2(shadowPad.X * L, shadowPad.Y * B);
                var soft = new Rect2(-L / 2 - pad.X, -B / 2 - pad.Y, L + 2 * pad.X, B + 2 * pad.Y);
                // Her shadow on the water, cast from the north-west, and the darker water right at her sides.
                if (sun > 0.01f) DrawTextureRect(shadowTex, soft with { Position = soft.Position + shadowLocal * (B * 0.16f) }, false, ShadowInk with { A = 0.34f * sun });
                DrawTextureRect(shadowTex, soft.Grow(B * 0.04f), false, new Color(0.20f, 0.33f, 0.46f, 0.13f * (1 - Mathf.Clamp((cut + L / 2) / L, 0, 1))));
                // A lip of white water along her sides that grows with her speed, pushed out ahead by the bow.
                float k = Mathf.Clamp(((float)ship.ForwardSpeed - 1f) / 8f, 0, 1);
                if (k > 0.01f && sinkT < 0)
                {
                    var lip = soft.Grow(B * 0.03f);
                    lip = lip with { Position = lip.Position + new Vector2(L * 0.015f, -B * 0.07f * k), Size = lip.Size + new Vector2(L * 0.02f, B * 0.14f * k) };
                    DrawTextureRect(shadowTex, lip, false, new Color(0.99f, 0.98f, 0.95f, 0.55f * k));
                }
            }
            else
            {
                // The water she sits in: a soft blue-grey wash just outside the hull, so she lifts off the paper.
                var wash = new Rect2(dst.Position - new Vector2(B * 0.09f, B * 0.10f), dst.Size + new Vector2(B * 0.18f, B * 0.20f));
                DrawTextureRectRegion(hullTex, wash, src, new Color(0.22f, 0.36f, 0.5f, 0.07f));
            }
            DrawTextureRectRegion(hullTex, dst, src);
            float wet = (float)Math.Clamp((ship.Water - 5) / 95.0, 0, 1);
            if (ship.Foundering) wet = Mathf.Max(wet, 0.55f + 0.1f * Mathf.Sin(time * 2.1f));
            if (wet > 0) DrawTextureRectRegion(hullTex, dst, src, new Color(0.12f, 0.24f, 0.34f, 0.42f * wet));
            if (flash > 0)
            {
                float f = flash / FlashTime;
                DrawTextureRectRegion(hullTex, dst, src, new Color(1.25f, 0.42f, 0.36f, 0.62f * f));
            }
            return;
        }
        // Procedural fallback (no art for this hull).
        over.Clear();
        over.Px = px;
        var hull = Outline(L, B);
        var deck = Outline(L * 0.9f, B * 0.7f, -L * 0.02f);
        FillStar(over, hull, hullPaint);
        FillStar(over, deck, Ink.Deck);
        for (float x = -L * 0.42f; x < L * 0.4f; x += L / 11)
        {
            float s = (x + L * 0.02f + L * 0.45f) / (L * 0.9f);
            float w = B * 0.35f * HalfWidth(Mathf.Clamp(s, 0, 1)) * 0.95f;
            over.Line(new Vector2(x, -w), new Vector2(x, w), px, Ink.Faint);
        }
        over.Polyline(deck, px, Ink.Soft, true);
        over.Polyline(hull, Mathf.Max(1.4f * px, 1.2f), Ink.Black, true);
        if (flash > 0) over.Polyline(hull, 3.5f * px, Ink.Red with { A = flash / FlashTime }, true);
        over.Flush(this);
    }

    static void FillStar(InkBatch m, Vector2[] ring, Color c)
    {
        // The hull outline is star-shaped about its centre: a fan from the centroid is exact.
        m.Convex(ring, c, 0);
    }

    /// <summary>The ink sector a broadside covers: abeam ±25°, out to the guns' range (GDD §8).</summary>
    void DrawArcOfFire(int side, float px)
    {
        float r = (float)ship.Range * Ink.PxPerM;
        float centre = side * Mathf.Pi / 2;
        var pts = new Vector2[14];
        pts[0] = Vector2.Zero;
        for (int i = 0; i <= 12; i++)
        {
            float a = centre - Mathf.DegToRad(25) + Mathf.DegToRad(50) * i / 12f;
            pts[i + 1] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        var loaded = ship.CanFire(side > 0 ? LastTide.Sim.Side.Starboard : LastTide.Sim.Side.Port);
        var ink = loaded ? Ink.Black : Ink.Red;
        DrawColoredPolygon(pts, ink with { A = 0.09f });
        over.Clear();
        over.Px = px;
        over.Polyline(pts, 1.2f * px, ink with { A = 0.55f }, true);
        // Range ticks: the chart-maker's scale along the arc.
        for (int k = 1; k <= 3; k++)
        {
            float rr = r * k / 4f;
            over.Circle(Vector2.Zero, rr, 0.9f * px, ink with { A = 0.22f }, 16, centre - Mathf.DegToRad(25), centre + Mathf.DegToRad(25));
        }
        over.Flush(this);
    }

    void DrawSpyglass(float px)
    {
        float local = (float)(SpyglassDir - ship.Heading);
        float r = (float)SpyglassRange * Ink.PxPerM;
        var pts = new Vector2[12];
        pts[0] = Vector2.Zero;
        for (int i = 0; i <= 10; i++)
        {
            float a = local - Mathf.DegToRad(10) + Mathf.DegToRad(20) * i / 10f;
            pts[i + 1] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        DrawColoredPolygon(pts, Ink.Wind with { A = 0.08f });
        over.Clear();
        over.Px = px;
        over.Polyline(pts, 1.1f * px, Ink.Wind with { A = 0.5f }, true);
        over.Flush(this);
    }

    /// <summary>Shot holes and splintered planks as she takes damage: a hurt enemy looks hurt.</summary>
    void DamageMarks(InkBatch m, float L, float B)
    {
        double frac = 1 - ship.HullHp / Math.Max(1, ship.MaxHp);
        int n = (int)Math.Floor(frac * 9);
        Span<Vector2> ring = stackalloc Vector2[7];
        for (int k = 0; k < n; k++)
        {
            float jx = Ink.Jitter(ship.Id, k, 11), jy = Ink.Jitter(ship.Id, k, 23);
            var c = new Vector2((jx - 0.5f) * L * 0.78f, (jy - 0.5f) * B * 0.62f);
            float r = B * (0.045f + 0.035f * Ink.Jitter(ship.Id, k, 37));
            for (int q = 0; q < ring.Length; q++)
            {
                float a = q * Mathf.Tau / ring.Length;
                float rr = r * (0.6f + 0.7f * Ink.Jitter(ship.Id * 7 + k, q, 41));
                ring[q] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
            }
            m.Convex(ring, Ink.Black with { A = 0.72f });
            for (int q = 0; q < 3; q++)
            {
                float a = Ink.Jitter(ship.Id, k * 3 + q, 53) * Mathf.Tau;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                m.Line(c + d * r * 0.8f, c + d * r * 2.2f, Mathf.Max(m.Px, r * 0.18f), new Color(0.40f, 0.28f, 0.17f, 0.85f));
            }
        }
    }

    /// <summary>Foam curling off the stem and along the bows, growing with her speed.</summary>
    void BowWave(InkBatch m, float L, float B)
    {
        float v = (float)ship.ForwardSpeed;
        float k = Mathf.Clamp((v - 1.5f) / 9f, 0, 1);
        if (k <= 0.01f) return;
        var foam = new Color(0.99f, 0.98f, 0.95f, 0.9f * Mathf.Sqrt(k));
        var edge = Ink.Black with { A = 0.35f * k };
        float stem = L * 0.5f;
        Span<Vector2> curl = stackalloc Vector2[6];
        for (int s = -1; s <= 1; s += 2)
        {
            for (int i = 0; i < curl.Length; i++)
            {
                float u = i / (float)(curl.Length - 1);
                float x = stem - u * L * (0.16f + 0.12f * k);
                float y = s * (B * 0.08f + u * B * (0.48f + 0.2f * k) + Mathf.Sin(u * 3.1f + time * 5f) * B * 0.02f);
                curl[i] = new Vector2(x, y);
            }
            float w = Mathf.Max(1.4f * m.Px, B * (0.06f + 0.08f * k));
            m.PolylineWidths(curl, w, w * 0.25f, foam, foam with { A = 0 });
            for (int i = 0; i < curl.Length; i++) curl[i] += new Vector2(-B * 0.02f, s * w * 0.55f);
            m.PolylineWidths(curl, 0.9f * m.Px, 0.6f * m.Px, edge, edge with { A = 0 });
        }
    }

    /// <summary>White water where the hull meets the sea as she goes under.</summary>
    void Churn(InkBatch m, float cut, float B)
    {
        float half = B * 0.5f;
        for (int k = -3; k <= 3; k++)
        {
            float y = k / 3.5f * half;
            float wob = Mathf.Sin(time * 7 + k * 1.7f) * B * 0.05f;
            m.Line(new Vector2(cut + wob - B * 0.06f, y), new Vector2(cut + wob + B * 0.06f, y + B * 0.04f), Mathf.Max(1.4f * m.Px, B * 0.05f), new Color(0.97f, 0.96f, 0.92f, 0.85f));
        }
        m.Line(new Vector2(cut, -half * 1.1f), new Vector2(cut, half * 1.1f), 1.2f * m.Px, Ink.Wind with { A = 0.5f });
    }

    /// <summary>Half-width factor along the hull (fallback drawing): a squared-off stern, full midships, a fine bow.</summary>
    static float HalfWidth(float s)
    {
        float mid = s < 0.1f ? 0.9f * Mathf.Sqrt(s / 0.1f) : 0.9f + 0.1f * Mathf.Sin(Mathf.Pi * Mathf.Clamp((s - 0.1f) / 0.9f, 0, 1));
        if (s > 0.6f)
        {
            float u = (s - 0.6f) / 0.4f;
            mid *= Mathf.Pow(Mathf.Max(0, 1 - u * u), 0.6f);
        }
        return mid;
    }

    static Vector2[] Outline(float L, float B, float xOffset = 0)
    {
        const int n = 18;
        var starboard = new List<Vector2>();
        var port = new List<Vector2>();
        for (int i = 1; i < n; i++)
        {
            float s = i / (float)(n - 1);
            float x = -L / 2 + s * L + xOffset;
            float w = B / 2 * HalfWidth(s);
            starboard.Add(new Vector2(x, w));
            if (i < n - 1) port.Add(new Vector2(x, -w));
        }
        port.Reverse();
        return starboard.Concat(port).ToArray();
    }
}
