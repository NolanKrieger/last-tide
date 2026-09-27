using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Monsters as sea-chart marginalia come to life (GDD §12), plus eruption zones. Each beast is the
/// generated "here be monsters" drawing (`assets/art/monsters/`) bent along a live spine, so it coils,
/// swims and lunges; what hides under the water is a dark shape beneath the ships (an under-layer),
/// what breaks the surface is drawn above them. Every shootable part carries a small red gauge.
/// Procedural ink stands in for any drawing that is missing.
/// </summary>
public partial class MonsterView : Node2D
{
    World world = null!;
    float time;
    PaintLayer under = null!, glow = null!;
    long lastTick = -1;
    float hurt;                     // red flash on the beast after a hit
    int lastBites;
    float biteT = -1;               // serpent lunge clock
    float jamT = -1;                // crocodile snap clock
    double lastJam;
    readonly float[] armDeath = new float[8];
    Monster? last;                  // the beast that just left (dying or escaping)
    float leaveT = -1;

    static readonly InkBatch ink = new(), tex = new();
    static readonly Color Surf = new(0.985f, 0.975f, 0.94f);
    static readonly Color Deep = new(0.10f, 0.20f, 0.28f);

    public void Init(World w)
    {
        world = w;
        ZIndex = 11;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        under = new PaintLayer { ZIndex = -3, Paint = PaintUnder };   // z 8: under the hulls, over the sea
        AddChild(under);
        // The ghost ship lives in fog and night, so it is drawn above the weather wash (canvas layer 9) and glows
        // through the dark; layer 10 with a small sublayer (its index here) keeps it under the HUD (layer 10, index
        // in Main ≥ 10).
        var night = new CanvasLayer { Layer = 10, FollowViewportEnabled = true };
        glow = new PaintLayer { Paint = PaintGlow, TextureFilter = TextureFilterEnum.LinearWithMipmaps };
        night.AddChild(glow);
        AddChild(night);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        time += dt;
        if (hurt > 0) hurt -= dt;
        if (biteT >= 0) biteT += dt;
        if (jamT >= 0) jamT += dt;
        if (leaveT >= 0) leaveT += dt;
        if (world.Ticks != lastTick)
        {
            lastTick = world.Ticks;
            foreach (var e in world.Events)
                if (e.ShipId == -3 && e.Type == CombatEventType.Hit) hurt = 0.3f;
            var m = world.Monster;
            if (m != null)
            {
                if (m.Type == MonsterType.ReefSerpent && m.Bites > lastBites) biteT = 0;
                lastBites = m.Bites;
                if (m.Type == MonsterType.Crocodile && world.RudderJam > lastJam + 1) jamT = 0;
                if (m.Type == MonsterType.Kraken)
                    for (int i = 0; i < m.Targets.Count && i < armDeath.Length; i++)
                        armDeath[i] = m.Targets[i].Alive ? -1 : Math.Max(armDeath[i], 0);
                last = m;
                leaveT = -1;
            }
            else if (last != null && leaveT < 0) leaveT = 0;
            lastJam = world.RudderJam;
        }
        for (int i = 0; i < armDeath.Length; i++) if (armDeath[i] >= 0) armDeath[i] += dt;
        if (leaveT > 3.5f) { last = null; leaveT = -1; }
        QueueRedraw();
        under.QueueRedraw();
        glow.QueueRedraw();
    }

    void PaintGlow(PaintLayer layer)
    {
        var m = world.Monster ?? (leaveT >= 0 ? last : null);
        if (m == null || m.Type != MonsterType.GhostShip) return;
        float fade = world.Monster != null ? 1f : Mathf.Clamp(1 - leaveT / (m.Beaten ? 3f : 1.5f), 0, 1);
        if (world.Monster != null && !m.Visible(world.ConditionsAt(world.Ship.Pos))) return;
        float px = layer.ScreenPx();
        ink.Clear();
        ink.Px = px;
        Ghost(layer, m, px, fade);
        ink.Flush(layer);
    }

    float Px() => under.ScreenPx();

    // ---------------------------------------------------------------- above the ships

    public override void _Draw()
    {
        float px = Px();
        ink.Clear();
        ink.Px = px;
        DrawEruptions(px);
        var m = world.Monster;
        if (m != null)
        {
            var cond = world.ConditionsAt(world.Ship.Pos);
            bool visible = m.Visible(cond);
            switch (m.Type)
            {
                case MonsterType.ReefSerpent: if (m.Surfaced) Serpent(m, px, 1f); break;
                case MonsterType.Kraken: if (m.State == MonsterState.Grip) KrakenArms(m, px); break;
                case MonsterType.Crocodile: if (m.Surfaced) Crocodile(m, px, 1f); break;
                case MonsterType.WeedKraken: if (m.State == MonsterState.Grip) WeedFronds(m, px, 1f); break;
                case MonsterType.Siren: Siren(m, px); break;
            }
        }
        else if (last != null && leaveT >= 0)
        {
            // The beast that just left: beaten ones go under in a cloud of ink; the rest slip away.
            float fade = Mathf.Clamp(1 - leaveT / (last.Beaten ? 3f : 1.5f), 0, 1);
            switch (last.Type)
            {
                case MonsterType.ReefSerpent: Serpent(last, px, fade * 0.8f); break;
                case MonsterType.Crocodile: Crocodile(last, px, fade); break;
                case MonsterType.Siren: if (last.Beaten) SirenRock(Ink.V(last.Perch), px, fade); break;
            }
        }
        ink.Flush(this);
    }

    // ---------------------------------------------------------------- under the ships

    void PaintUnder(PaintLayer layer)
    {
        float px = layer.ScreenPx();
        ink.Clear();
        ink.Px = px;
        var m = world.Monster;
        if (m != null)
        {
            var pos = Ink.V(m.Pos);
            switch (m.Type)
            {
                case MonsterType.ReefSerpent:
                    if (!m.Surfaced) { Shadow(layer, "serpent", pos, (float)m.Heading, 42, 0.3f, px, true); Ripples(pos, 0.7f, px); }
                    break;
                case MonsterType.Kraken:
                    if (m.State == MonsterState.Grip) KrakenBody(layer, m, px, 1f);
                    else
                    {
                        float a = m.State == MonsterState.Retreat ? Mathf.Clamp((float)m.Timer / 6f, 0, 1) : 1f;
                        KrakenShadow(layer, m, pos, a, px);
                    }
                    break;
                case MonsterType.Crocodile:
                    if (!m.Surfaced) { Shadow(layer, "crocodile", pos, (float)m.Heading, CrocLen, 0.4f, px, false); Eyes(pos, (float)m.Heading, px); Vee(pos, (float)m.Heading, CrocLen, px); }
                    break;
                case MonsterType.WeedKraken:
                    if (m.State == MonsterState.Grip) WeedMass(layer, m, px, 1f);
                    else if (m.State == MonsterState.Approach) { WeedPatch(layer, pos, px, 0.8f); Ripples(pos, 0.6f, px); }
                    else WeedMass(layer, m, px, Mathf.Clamp((float)m.Timer / 4f, 0, 1));
                    break;
            }
        }
        else if (last != null && leaveT >= 0 && last.Beaten)
        {
            float k = Mathf.Clamp(leaveT / 3f, 0, 1);
            var p = Ink.V(last.Pos);
            InkCloud(p, 20 + 50 * k, (1 - k) * 0.5f, px);
            if (last.Type == MonsterType.Kraken) KrakenBody(layer, last, px, 1 - k);
        }
        ink.Flush(layer);
    }

    // ---------------------------------------------------------------- serpent

    /// <summary>The serpent's spine: a travelling wave along a body lying parallel to her ship when up.</summary>
    void SerpentSpine(Monster m, Span<Vector2> spine, Span<float> alpha, out Vector2 head)
    {
        float len = 42 * Ink.PxPerM;
        float h = m.Surfaced || m.State == MonsterState.Dive ? (float)world.Ship.Heading : (float)m.Heading;
        var dir = new Vector2(Mathf.Cos(h), Mathf.Sin(h));
        var side = new Vector2(-dir.Y, dir.X);
        var c = Ink.V(m.Pos);
        float lunge = biteT is >= 0 and < 0.7f ? Mathf.Sin(biteT / 0.7f * Mathf.Pi) : 0f;
        var toShip = (Ink.V(world.Ship.Pos) - c).Normalized();
        int n = spine.Length;
        for (int i = 0; i < n; i++)
        {
            float s = i / (float)(n - 1);            // 0 tail … 1 head
            float env = Mathf.Sin(Mathf.Pi * Mathf.Clamp(s * 1.1f, 0, 1)) * (1 - 0.6f * s);
            float wave = Mathf.Sin(s * Mathf.Tau * 1.3f - time * 3.2f) * 3.2f * Ink.PxPerM * env;
            var p = c + dir * ((s - 0.55f) * len) + side * wave;
            p += toShip * (lunge * 9 * Ink.PxPerM * s * s);
            spine[i] = p;
            // Humps: the body breaks the surface in arches; the head is always up.
            float arch = Mathf.SmoothStep(-0.25f, 0.35f, Mathf.Sin(s * Mathf.Tau * 1.6f - time * 1.6f));
            alpha[i] = s > 0.78f ? 1f : Mathf.Lerp(0.28f, 1f, arch);
        }
        head = spine[n - 1];
    }

    void Serpent(Monster m, float px, float a)
    {
        var t = Art.Tex("monsters/serpent");
        Span<Vector2> spine = stackalloc Vector2[24];
        Span<float> alpha = stackalloc float[24];
        Span<float> hw = stackalloc float[24];
        SerpentSpine(m, spine, alpha, out var head);
        float half = 42 * Ink.PxPerM / (t != null ? t.GetWidth() / (float)t.GetHeight() : 6f) * 0.5f;
        hw.Fill(half);
        // Foam where each arch leaves and enters the water.
        for (int i = 1; i < spine.Length; i++)
        {
            bool edge = (alpha[i] > 0.6f) != (alpha[i - 1] > 0.6f);
            if (!edge) continue;
            float r = half * 0.75f;
            float ang = (spine[i] - spine[i - 1]).Angle();
            ink.Circle(spine[i], r, Mathf.Max(1.6f * px, r * 0.22f), Surf with { A = 0.9f * a }, 10, ang + 0.6f, ang + Mathf.Pi - 0.6f);
            ink.Circle(spine[i], r, Mathf.Max(1.6f * px, r * 0.22f), Surf with { A = 0.9f * a }, 10, ang + Mathf.Pi + 0.6f, ang + Mathf.Tau - 0.6f);
            ink.Circle(spine[i], r * 1.2f, 0.9f * px, Ink.Black with { A = 0.35f * a }, 10, ang + 0.7f, ang + Mathf.Pi - 0.7f);
        }
        ink.Flush(this);
        var col = Colors.White.Lerp(new Color(1, 0.4f, 0.35f), hurt > 0 ? hurt / 0.3f : 0) with { A = a };
        if (t != null)
        {
            tex.Clear();
            tex.Px = px;
            tex.Strip(spine, hw, new Rect2(0, 0, 1, 1), col, alpha);
            tex.Flush(this, t);
        }
        else
        {
            ink.Polyline(spine, half * 0.8f, Ink.Brethren with { A = a });
            ink.Disc(head, half, Ink.Black with { A = a });
        }
        if (m.Surfaced && a >= 1 && m.Targets.Count > 0) Target(Ink.V(m.Targets[0].Pos), (float)m.Targets[0].Radius, (float)(m.Hp / m.Def.Hp), px);
        if (biteT is >= 0 and < 0.4f) Splash(head, 18 * (biteT / 0.4f), 1 - biteT / 0.4f, px);
    }

    // ---------------------------------------------------------------- kraken

    void KrakenBody(PaintLayer layer, Monster m, float px, float a)
    {
        var t = Art.Tex("monsters/kraken-body");
        var ship = world.Ship;
        var c = Ink.V(ship.Pos + ship.Forward * (ship.Hull.Length * 0.3 + 4));
        float h = (float)ship.Heading;
        float breathe = 1 + 0.035f * Mathf.Sin(time * 1.7f);
        float w = Mathf.Max(26f, (float)ship.Hull.Beam * 2.2f) * Ink.PxPerM * breathe;
        // The mantle lies under her, its eyes and arms toward the bow.
        var size = t != null ? new Vector2(w, w * t.GetHeight() / (float)t.GetWidth()) : new Vector2(w, w);
        ink.Glow(c, w * 0.75f, Deep with { A = 0.22f * a }, Deep with { A = 0 }, 28, 0.85f, h);
        ink.Flush(layer);
        if (t != null)
        {
            tex.Clear();
            tex.SpriteRot(c, size, h - Mathf.Pi / 2, new Rect2(0, 0, 1, 1), new Color(0.62f, 0.7f, 0.84f, 0.62f * a));
            tex.Flush(layer, t);
        }
        else ink.Disc(c, w * 0.45f, Ink.Brethren with { A = 0.7f * a }, 20);
    }

    /// <summary>The kraken under way: a vast dark shape just under the water, arms streaming behind it.</summary>
    void KrakenShadow(PaintLayer layer, Monster m, Vector2 pos, float a, float px)
    {
        float h = (float)m.Heading;
        var dir = new Vector2(Mathf.Cos(h), Mathf.Sin(h));
        var side = new Vector2(-dir.Y, dir.X);
        ink.Glow(pos, 26 * Ink.PxPerM, Deep with { A = 0.3f * a }, Deep with { A = 0 }, 24, 0.8f, h);
        var armTex = Art.Tex("monsters/kraken-arm");
        tex.Clear();
        tex.Px = px;
        if (armTex != null)
        {
            Span<Vector2> spine = stackalloc Vector2[10];
            Span<float> hw = stackalloc float[10];
            float aspect = armTex.GetWidth() / (float)armTex.GetHeight();
            var root = pos - dir * (9 * Ink.PxPerM);   // the arm stumps, at the back: it jets mantle-first
            for (int k = 0; k < 4; k++)
            {
                float off = (k - 1.5f) * 2.6f * Ink.PxPerM;
                for (int i = 0; i < spine.Length; i++)
                {
                    float u = i / (float)(spine.Length - 1);
                    // Root at the arm stumps, tip streaming astern and waving.
                    spine[i] = root - dir * (u * 24 * Ink.PxPerM) + side * (off * (1 + 0.8f * u) + Mathf.Sin(time * 3f + k * 1.3f - u * 4f) * 2.2f * Ink.PxPerM * u);
                    hw[i] = 22 * Ink.PxPerM / aspect * 0.4f;
                }
                // The strip runs root→tip along the spine, as the drawing does left→right (thick root, thin tip).
                tex.Strip(spine, hw, new Rect2(0.07f, 0, 0.93f, 1), new Color(0.25f, 0.4f, 0.5f, 0.3f * a));
            }
            tex.Flush(layer, armTex);
        }
        Shadow(layer, "kraken-body", pos, h + Mathf.Pi / 2, 22, 0.42f * a, px, false);
        // The sea heaves over it: broken water ahead and rings spreading from the hump.
        Vee(pos + dir * 10 * Ink.PxPerM, h, 22, px);
        Ripples(pos, 1.3f * a, px);
    }

    void KrakenArms(Monster m, float px)
    {
        var t = Art.Tex("monsters/kraken-arm");
        var ship = world.Ship;
        var centre = Ink.V(ship.Pos);
        var fwd = Ink.V(ship.Forward).Normalized();
        float shipLen = (float)ship.Hull.Length * Ink.PxPerM;
        Span<Vector2> spine = stackalloc Vector2[14];
        Span<float> hw = stackalloc float[14];
        float aspect = t != null ? t.GetWidth() / (float)t.GetHeight() : 5.6f;
        tex.Clear();
        tex.Px = px;
        for (int k = 0; k < m.Targets.Count; k++)
        {
            var target = m.Targets[k];
            var b = Ink.V(target.Pos);
            float along = (k % 2 == 0 ? 0.18f : -0.18f) * shipLen;
            var grip = centre + fwd * along;
            var toGrip = grip - b;
            float len = toGrip.Length() * 1.25f;
            var perp = new Vector2(-toGrip.Y, toGrip.X).Normalized();
            float dead = k < armDeath.Length ? armDeath[k] : -1;
            bool alive = target.Alive;
            float reach = alive ? 1f : Mathf.Clamp(1 - dead / 1.2f, 0, 1);
            if (!alive && reach <= 0) { InkCloud(b, 14 + 10 * Mathf.Min(dead, 3), Mathf.Clamp(0.5f - dead * 0.12f, 0, 0.5f), px); continue; }
            for (int i = 0; i < spine.Length; i++)
            {
                float u = i / (float)(spine.Length - 1) * reach;
                // Rises at the target, arches over the rail, curls onto the deck; the tip squeezes and sways.
                var p = b + toGrip * (u * 1.05f);
                p += perp * (Mathf.Sin(u * Mathf.Pi) * len * 0.16f * (k < 2 ? 1 : -1));
                p += perp * (Mathf.Sin(time * 2.3f + k * 1.7f + u * 4f) * 1.6f * Ink.PxPerM * u);
                spine[i] = p;
                hw[i] = len / aspect * 0.36f;
            }
            var col = (alive ? Colors.White : new Color(0.7f, 0.7f, 0.75f)).Lerp(new Color(1, 0.45f, 0.4f), hurt > 0 && alive ? hurt / 0.3f : 0);
            if (!alive) col.A = reach;
            if (t != null) tex.Strip(spine, hw, new Rect2(0.07f, 0, 0.93f * reach, 1), col);
            else ink.PolylineWidths(spine, hw[0] * 1.4f, hw[0] * 0.3f, Ink.Brethren, Ink.Brethren);
        }
        ink.Flush(this);
        if (t != null) tex.Flush(this, t);
        // Water boils where each arm comes up, over its root; a gauge on every arm still gripping.
        for (int k = 0; k < m.Targets.Count; k++)
        {
            var target = m.Targets[k];
            var b = Ink.V(target.Pos);
            float dead = k < armDeath.Length ? armDeath[k] : -1;
            if (target.Alive)
            {
                float pulse = 1 + 0.1f * Mathf.Sin(time * 5 + k);
                Foam(b, 9 * Ink.PxPerM * 0.36f * pulse, px, 1f);
                Target(b, (float)target.Radius, (float)(target.Hp / 12.0), px);
            }
            else InkCloud(b, 14 + 10 * Mathf.Min(dead, 3), Mathf.Clamp(0.45f - dead * 0.1f, 0, 0.45f), px);
        }
    }

    /// <summary>A ring of broken white water with an ink edge, where something breaks the surface.</summary>
    void Foam(Vector2 c, float r, float px, float a)
    {
        int n = 11;
        ink.Glow(c, r * 1.2f, Surf with { A = 0.35f * a }, Surf with { A = 0 }, 16);
        for (int i = 0; i < n; i++)
        {
            float ang = i * Mathf.Tau / n + time * 0.4f;
            float rr = r * (0.9f + 0.25f * Mathf.Sin(i * 2.3f + time * 3));
            ink.Disc(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr, r * 0.17f, Surf with { A = 0.85f * a }, 6);
        }
        ink.Circle(c, r * 1.25f, 0.9f * px, Ink.Black with { A = 0.35f * a }, 20);
    }

    // ---------------------------------------------------------------- ghost ship

    void Ghost(PaintLayer layer, Monster m, float px, float fade)
    {
        var t = Art.Tex("monsters/ghost");
        var p = Ink.V(m.Pos);
        float h = (float)m.Heading;
        bool flare = m.FlareTimer > 0;
        float solid = flare ? Mathf.Clamp((3f - (float)m.FlareTimer) / 0.25f, 0, 1) * Mathf.Clamp((float)m.FlareTimer / 0.4f, 0, 1) : 0f;
        float shimmer = 0.42f + 0.08f * Mathf.Sin(time * 3.1f) + 0.05f * Mathf.Sin(time * 7.7f);
        float a = Mathf.Lerp(shimmer, 1f, solid) * fade;
        float L = 40 * Ink.PxPerM;
        float aspect = t != null ? t.GetWidth() / (float)t.GetHeight() : 3.8f;
        var size = new Vector2(L, L / aspect);
        var dir = new Vector2(Mathf.Cos(h), Mathf.Sin(h));
        var side = new Vector2(-dir.Y, dir.X);
        // A cold halo in the fog around her, warm while her lanterns flare.
        var halo = new Color(0.72f, 0.9f, 0.95f).Lerp(new Color(1f, 0.85f, 0.55f), solid);
        ink.Glow(p, L * 0.75f, halo with { A = (0.18f + 0.14f * solid) * fade }, halo with { A = 0 }, 32, 0.5f, h);
        ink.Flush(layer);
        tex.Clear();
        tex.Px = px;
        var tint = new Color(0.82f, 0.95f, 1f, a).Lerp(new Color(1, 0.55f, 0.5f, a), hurt > 0 ? hurt / 0.3f : 0);
        if (t != null)
        {
            // A second, drifting image makes her waver like something half there.
            var drift = side * (Mathf.Sin(time * 1.3f) * 2.2f * Ink.PxPerM * (1 - solid));
            tex.SpriteRot(p + drift, size, h, new Rect2(0, 0, 1, 1), tint with { A = a * 0.35f * (1 - solid) });
            tex.SpriteRot(p, size, h, new Rect2(0, 0, 1, 1), tint);
            tex.Flush(layer, t);
        }
        else
        {
            ink.Disc(p, L * 0.5f, tint, 16, 0.25f, h);
        }
        // Tattered canvas on two masts, flapping, holed.
        Span<Vector2> top = stackalloc Vector2[7];
        Span<Vector2> bot = stackalloc Vector2[7];
        for (int k = 0; k < 2; k++)
        {
            var mast = p + dir * (k == 0 ? L * 0.18f : -L * 0.14f);
            float W = size.Y * 1.7f;
            var yard = side * (W * 0.5f);
            var belly = dir * (W * 0.16f);
            for (int i = 0; i < 7; i++)
            {
                float u = i / 6f;
                var yp = mast - yard + yard * 2 * u;
                float rag = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(u * 17 + k * 3 + time * 2.5f));
                top[i] = yp;
                bot[i] = yp + belly * (Mathf.Sin(u * Mathf.Pi) * rag) + side * Mathf.Sin(time * 4 + u * 6) * 0.6f * Ink.PxPerM;
            }
            ink.Band(top, bot, new Color(0.9f, 0.96f, 1f, 0.55f * a), new Color(0.75f, 0.86f, 0.92f, 0.25f * a));
            ink.Polyline(bot, 0.9f * px, new Color(0.35f, 0.5f, 0.58f, 0.8f * a));
            ink.Line(mast - yard, mast + yard, Mathf.Max(1.2f * px, 1.2f), new Color(0.3f, 0.42f, 0.48f, 0.9f * a));
            ink.Disc(mast, Mathf.Max(1.5f * px, 2f), new Color(0.3f, 0.42f, 0.48f, a), 8);
        }
        // Lanterns: pale ghost-lights, blazing gold while she flares (the only time shot can touch her).
        foreach (float f in new[] { -0.46f, 0.44f })
        {
            var l = p + dir * (L * f);
            float glow = Mathf.Lerp(0.35f, 1f, solid) * fade;
            var warm = new Color(1f, 0.8f, 0.35f).Lerp(new Color(0.7f, 1f, 0.85f), 1 - solid);
            ink.Glow(l, (7 + 12 * solid) * Ink.PxPerM * 0.5f, warm with { A = 0.55f * glow }, warm with { A = 0 }, 20);
            ink.Disc(l, 1.4f * Ink.PxPerM, warm.Lightened(0.4f) with { A = 0.95f * glow }, 10);
        }
        if (fade >= 1) Target(m.Targets.Count > 0 ? Ink.V(m.Targets[0].Pos) : p, m.Targets.Count > 0 ? (float)m.Targets[0].Radius : 12f, (float)(m.Hp / m.Def.Hp), px, flare);
    }

    // ---------------------------------------------------------------- crocodile

    const float CrocLen = 19f;   // metres, snout to tail: a giant among crocodiles, drawn bigger than the 8 m bite circle

    void CrocSpine(Vector2 c, float h, float len, Span<Vector2> spine, float swing)
    {
        var dir = new Vector2(Mathf.Cos(h), Mathf.Sin(h));
        var side = new Vector2(-dir.Y, dir.X);
        int n = spine.Length;
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1);          // 0 tail tip … 1 snout
            float tail = Mathf.Clamp((0.46f - u) / 0.46f, 0, 1);
            float sway = Mathf.Sin(time * (2.2f + 3f * swing) - u * 5f) * tail * tail * (1.4f + 2.4f * swing) * Ink.PxPerM;
            spine[i] = c + dir * ((u - 0.5f) * len) + side * sway;
        }
    }

    void Crocodile(Monster m, float px, float fade)
    {
        var t = Art.Tex("monsters/crocodile");
        var c = Ink.V(m.Pos);
        // Basking on the bank she watches the ship; sliding home she faces where she goes.
        float h = m.State == MonsterState.Perched ? (Ink.V(world.Ship.Pos) - c).Angle() : (float)m.Heading;
        if (m.State == MonsterState.Retreat) h = (Ink.V(m.Perch) - c).Angle() + Mathf.Pi;   // backs off, jaws to the ship
        float len = CrocLen * Ink.PxPerM;
        float snap = jamT is >= 0 and < 0.5f ? Mathf.Sin(jamT / 0.5f * Mathf.Pi) : 0;
        len *= 1 + 0.08f * snap;
        Span<Vector2> spine = stackalloc Vector2[12];
        Span<float> hw = stackalloc float[12];
        CrocSpine(c, h, len, spine, m.State == MonsterState.Perched ? 0f : 1f);
        float aspect = t != null ? t.GetWidth() / (float)t.GetHeight() : 3f;
        hw.Fill(len / aspect * 0.5f);
        // Mud and ripples where she lies half in the water.
        ink.Glow(c, len * 0.5f, Deep with { A = 0.16f * fade }, Deep with { A = 0 }, 20, 0.45f, h);
        ink.Flush(this);
        var col = Colors.White.Lerp(new Color(1, 0.45f, 0.4f), hurt > 0 ? hurt / 0.3f : 0) with { A = fade };
        if (t != null)
        {
            tex.Clear();
            tex.Px = px;
            tex.Strip(spine, hw, new Rect2(0, 0, 1, 1), col);
            tex.Flush(this, t);
        }
        else ink.PolylineWidths(spine, hw[0] * 0.3f, hw[0] * 0.9f, Ink.Brethren with { A = fade }, Ink.Brethren with { A = fade });
        if (fade >= 1 && m.Targets.Count > 0) Target(Ink.V(m.Targets[0].Pos), (float)m.Targets[0].Radius, (float)(m.Hp / m.Def.Hp), px);
        if (snap > 0) Splash(spine[^1], 14 * snap, snap, px);
    }

    void Eyes(Vector2 c, float h, float px)
    {
        var dir = new Vector2(Mathf.Cos(h), Mathf.Sin(h));
        var side = new Vector2(-dir.Y, dir.X);
        var head = c + dir * (CrocLen * Ink.PxPerM * 0.3f);
        for (int s = -1; s <= 1; s += 2)
        {
            var e = head + side * (s * 1.1f * Ink.PxPerM);
            ink.Disc(e, Mathf.Max(1.6f * px, 1.8f), new Color(0.92f, 0.78f, 0.3f), 8);
            ink.Disc(e, Mathf.Max(0.8f * px, 0.8f), Ink.Black, 6);
        }
    }

    /// <summary>A V of disturbed water off something moving just under the surface.</summary>
    void Vee(Vector2 c, float h, float lenM, float px)
    {
        var dir = new Vector2(Mathf.Cos(h), Mathf.Sin(h));
        var side = new Vector2(-dir.Y, dir.X);
        var nose = c + dir * (lenM * Ink.PxPerM * 0.45f);
        Span<Vector2> arm = stackalloc Vector2[4];
        for (int s = -1; s <= 1; s += 2)
        {
            for (int i = 0; i < 4; i++)
            {
                float u = i / 3f;
                arm[i] = nose - dir * (u * lenM * Ink.PxPerM * 0.9f) + side * (s * u * lenM * Ink.PxPerM * 0.35f);
            }
            ink.PolylineWidths(arm, 2.2f * px, 0.6f * px, Surf with { A = 0.9f }, Surf with { A = 0 });
            ink.PolylineWidths(arm, 0.9f * px, 0.5f * px, Ink.Black with { A = 0.45f }, Ink.Black with { A = 0 });
        }
    }

    // ---------------------------------------------------------------- weed-kraken

    void WeedPatch(PaintLayer layer, Vector2 c, float px, float a)
    {
        var t = Art.Tex("monsters/weed-mass");
        float r = 13 * Ink.PxPerM;
        if (t == null) { ink.Disc(c, r * 0.6f, new Color(0.45f, 0.4f, 0.2f, 0.5f * a), 14); return; }
        tex.Clear();
        tex.Px = px;
        tex.SpriteRot(c, new Vector2(r * 2, r * 2 * t.GetHeight() / (float)t.GetWidth()), time * 0.15f, new Rect2(0, 0, 1, 1), new Color(0.9f, 0.9f, 0.85f, 0.55f * a));
        tex.Flush(layer, t);
    }

    void WeedMass(PaintLayer layer, Monster m, float px, float a)
    {
        var t = Art.Tex("monsters/weed-mass");
        var c = Ink.V(m.Pos);
        float r = 10.5f * Ink.PxPerM * (1 + 0.05f * Mathf.Sin(time * 2.2f));
        ink.Glow(c, r * 1.3f, new Color(0.22f, 0.24f, 0.12f, 0.25f * a), new Color(0.22f, 0.24f, 0.12f, 0), 24);
        ink.Flush(layer);
        if (t != null)
        {
            tex.Clear();
            tex.Px = px;
            var col = Colors.White.Lerp(new Color(1, 0.5f, 0.45f), hurt > 0 ? hurt / 0.3f : 0) with { A = a };
            tex.SpriteRot(c, new Vector2(r * 2, r * 2 * t.GetHeight() / (float)t.GetWidth()), time * 0.25f, new Rect2(0, 0, 1, 1), col);
            tex.Flush(layer, t);
        }
        else ink.Disc(c, r * 0.7f, new Color(0.35f, 0.4f, 0.2f, a), 14);
        if (m.State == MonsterState.Grip && m.Targets.Count > 0)
            Target(Ink.V(m.Targets[0].Pos), (float)m.Targets[0].Radius, (float)(m.Targets[0].Hp / m.Def.Hp), px);
    }

    /// <summary>Fronds from the stern mass up both sides of the hull, their tips curling in over the rails onto her deck.</summary>
    void WeedFronds(Monster m, float px, float a)
    {
        var ship = world.Ship;
        var stern = Ink.V(m.Pos);
        var fwd = Ink.V(ship.Forward).Normalized();
        var right = new Vector2(-fwd.Y, fwd.X);
        float L = (float)ship.Hull.Length * Ink.PxPerM, B = (float)ship.Hull.Beam * Ink.PxPerM;
        Span<Vector2> spine = stackalloc Vector2[12];
        Span<float> hw = stackalloc float[12];
        for (int k = 0; k < 6; k++)
        {
            var t = Art.Tex("monsters/weed-frond" + (k % 3));
            if (t == null) continue;
            int sd = k % 2 == 0 ? 1 : -1;
            float reach = L * (0.42f + 0.13f * (k / 2));
            float out0 = B * (0.52f + 0.06f * (k % 3));
            float grow = Mathf.Clamp((float)m.Age * 0.6f - k * 0.08f, 0.15f, 1f);   // they creep up her sides
            for (int i = 0; i < spine.Length; i++)
            {
                float u = i / (float)(spine.Length - 1) * grow;
                float curl = Mathf.SmoothStep(0.68f, 1f, u);
                float lateral = sd * out0 * (1 - 0.85f * curl);
                var q = stern + fwd * (u * reach) + right * lateral;
                q += right * (Mathf.Sin(time * 2.1f + k * 1.7f + u * 6f) * B * 0.06f * u);
                spine[i] = q;
                hw[i] = B * 0.15f;
            }
            tex.Clear();
            tex.Px = px;
            tex.Strip(spine, hw, new Rect2(0, 0, grow, 1), Colors.White with { A = a });
            tex.Flush(this, t);
        }
    }

    // ---------------------------------------------------------------- siren

    void Siren(Monster m, float px)
    {
        var p = Ink.V(m.Perch);
        if (world.SirenPull != 0)
        {
            // Her song: ribbons of sound from her rock to the ship (the way the helm is pulled), notes riding
            // them, and the edge of earshot (250 m, where the pull ends) as a dotted chart circle.
            var song = new Color(0.58f, 0.24f, 0.5f);
            float ear = 250 * Ink.PxPerM;
            int dots = 90;
            for (int i = 0; i < dots; i += 2)
                ink.Circle(p, ear, Mathf.Max(1.4f * px, 1.1f), song with { A = 0.45f }, 2, i * Mathf.Tau / dots, (i + 0.8f) * Mathf.Tau / dots);
            var ship = Ink.V(world.Ship.Pos);
            var to = ship - p;
            float dist = to.Length();
            var side = new Vector2(-to.Y, to.X) / Mathf.Max(dist, 1);
            Span<Vector2> rib = stackalloc Vector2[32];
            for (int k = -1; k <= 1; k++)
            {
                for (int i = 0; i < rib.Length; i++)
                {
                    float u = i / (float)(rib.Length - 1);
                    float env = Mathf.Sin(u * Mathf.Pi);
                    rib[i] = p + to * u + side * (k * 7 * Ink.PxPerM * env + Mathf.Sin(u * dist / (9 * Ink.PxPerM) - time * 4f + k) * 2.5f * Ink.PxPerM * env);
                }
                ink.PolylineWidths(rib, Mathf.Max(2f * px, 1.6f), Mathf.Max(1f * px, 0.8f), song with { A = 0.5f }, song with { A = 0.15f });
            }
            for (int k = 0; k < 6; k++)
            {
                float t = (time * 0.18f + k / 6f) % 1f;
                var q = p + to * t + side * (Mathf.Sin(t * 9 + k * 2) * 6 * Ink.PxPerM * Mathf.Sin(t * Mathf.Pi));
                Note(q, Mathf.Max(8f * px, 3.2f * Ink.PxPerM), song with { A = Mathf.Sin(t * Mathf.Pi) * 0.95f }, px, (k & 1) == 0);
            }
        }
        SirenRock(p, px, 1f);
        if (m.Targets.Count > 0) Target(Ink.V(m.Targets[0].Pos), (float)m.Targets[0].Radius, (float)(m.Hp / m.Def.Hp), px);
    }

    /// <summary>A quaver or a pair of beamed notes, the way a chart-maker letters a mermaid's song.</summary>
    void Note(Vector2 c, float h, Color col, float px, bool pair)
    {
        float w = Mathf.Max(1.2f * px, h * 0.12f);
        ink.Disc(c, h * 0.3f, col, 10, 0.72f, -0.4f);
        ink.Line(c + new Vector2(h * 0.26f, 0), c + new Vector2(h * 0.26f, -h * 1.1f), w, col);
        if (pair)
        {
            var c2 = c + new Vector2(h * 0.8f, -h * 0.15f);
            ink.Disc(c2, h * 0.3f, col, 10, 0.72f, -0.4f);
            ink.Line(c2 + new Vector2(h * 0.26f, 0), c2 + new Vector2(h * 0.26f, -h * 1.25f), w, col);
            ink.Taper(c + new Vector2(h * 0.26f, -h * 1.1f), c2 + new Vector2(h * 0.26f, -h * 1.25f), w * 2.2f, w * 2.2f, col, col);
        }
        else ink.Taper(c + new Vector2(h * 0.26f, -h * 1.1f), c + new Vector2(h * 0.62f, -h * 0.72f), w * 1.8f, w * 0.6f, col, col);
    }

    void SirenRock(Vector2 p, float px, float a)
    {
        var t = Art.Tex("monsters/siren");
        float w = 26 * Ink.PxPerM * (1 + 0.02f * Mathf.Sin(time * 1.4f));
        if (t == null)
        {
            ink.Disc(p, 12, Ink.Land with { A = a }, 16);
            ink.Circle(p, 12, 1.5f, Ink.Black with { A = a }, 20);
            ink.Line(p + new Vector2(0, -2), p + new Vector2(0, -18), 2f, Ink.Black with { A = a });
            ink.Disc(p + new Vector2(0, -21), 3.5f, Ink.Black with { A = a }, 8);
            return;
        }
        ink.Flush(this);
        tex.Clear();
        tex.Px = px;
        var col = Colors.White.Lerp(new Color(1, 0.5f, 0.45f), hurt > 0 ? hurt / 0.3f : 0) with { A = a };
        tex.SpriteRot(p, new Vector2(w, w * t.GetHeight() / (float)t.GetWidth()), 0, new Rect2(0, 0, 1, 1), col);
        tex.Flush(this, t);
    }

    // ---------------------------------------------------------------- eruptions

    void DrawEruptions(float px)
    {
        var rocks = Art.Tex("monsters/rock0");
        foreach (var e in world.Eruptions)
        {
            var c = Ink.V(e.Pos);
            float r = (float)e.Radius * Ink.PxPerM;
            int seed = (int)(e.Pos.X * 7 + e.Pos.Y * 13);
            if (!e.Landed)
            {
                // The marked zone: a dashed red ring that tightens, a faint hatch, and the shadows of the rocks
                // growing where they will fall.
                float k = 1 - Mathf.Clamp((float)e.Warning / 5f, 0, 1);
                float pulse = 0.9f + 0.1f * Mathf.Sin(time * 9);
                int n = 40;
                for (int i = 0; i < n; i += 2)
                    ink.Circle(c, r * pulse, 2f * px + 0.6f, Ink.Red with { A = 0.85f }, 3, i * Mathf.Tau / n + time * 0.3f, (i + 1) * Mathf.Tau / n + time * 0.3f);
                ink.Disc(c, r * pulse, Ink.Red with { A = 0.06f + 0.06f * k }, 32);
                for (int i = 0; i < 7; i++)
                {
                    var q = c + RockSpot(seed, i) * r;
                    ink.Disc(q, (1.5f + 3f * k) * Ink.PxPerM * (0.6f + 0.4f * Ink.Jitter(seed, i, 5)), Ink.Black with { A = 0.12f + 0.3f * k }, 10, 0.8f);
                }
                DrawString(Main.Fell, c + new Vector2(-30, -r - 8), Text.Get("WX_ERUPTION_WARN", Math.Ceiling(e.Warning)), HorizontalAlignment.Center, -1, 16, Ink.Red);
            }
            else
            {
                float t = (float)Math.Min(1, -e.Warning / 3);
                ink.Flush(this);
                tex.Clear();
                tex.Px = px;
                for (int i = 0; i < 7; i++)
                {
                    var q = c + RockSpot(seed, i) * r;
                    float size = (2.5f + 3f * Ink.Jitter(seed, i, 5)) * Ink.PxPerM;
                    ink.Circle(q, size * (0.8f + 2.2f * t), 1.4f * px, Surf with { A = 0.8f * (1 - t) }, 16);
                    if (rocks != null)
                    {
                        var rt = Art.Tex("monsters/rock" + (i % 4)) ?? rocks;
                        float sink = 1 - t;
                        tex.SpriteRot(q, new Vector2(size, size) * (0.6f + 0.4f * sink), Ink.Jitter(seed, i, 9) * 6, new Rect2(0, 0, 1, 1), new Color(1, 1, 1, sink));
                        tex.Flush(this, rt);
                    }
                    else ink.Disc(q, size * 0.4f, Ink.Black with { A = 0.8f * (1 - t) }, 8);
                }
            }
        }
    }

    static Vector2 RockSpot(int seed, int i)
    {
        float a = Ink.Jitter(seed, i, 1) * Mathf.Tau;
        float d = Mathf.Sqrt(Ink.Jitter(seed, i, 2)) * 0.8f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
    }

    // ---------------------------------------------------------------- shared marks

    /// <summary>A dark shape under the water: the drawing, tinted deep and faint.</summary>
    void Shadow(PaintLayer layer, string sprite, Vector2 c, float h, float lenM, float a, float px, bool swim)
    {
        var t = Art.Tex("monsters/" + sprite);
        float len = lenM * Ink.PxPerM;
        if (t == null) { ink.Disc(c, len * 0.3f, Deep with { A = a }, 16, 0.3f, h); return; }
        float aspect = t.GetWidth() / (float)t.GetHeight();
        tex.Clear();
        tex.Px = px;
        var col = new Color(0.25f, 0.4f, 0.5f, a);
        if (swim)
        {
            Span<Vector2> spine = stackalloc Vector2[16];
            Span<float> hw = stackalloc float[16];
            var dir = new Vector2(Mathf.Cos(h), Mathf.Sin(h));
            var side = new Vector2(-dir.Y, dir.X);
            for (int i = 0; i < 16; i++)
            {
                float s = i / 15f;
                spine[i] = c + dir * ((s - 0.6f) * len) + side * (Mathf.Sin(s * Mathf.Tau * 1.2f - time * 4f) * 2.5f * Ink.PxPerM * (1 - s));
            }
            hw.Fill(len / aspect * 0.5f);
            tex.Strip(spine, hw, new Rect2(0, 0, 1, 1), col);
        }
        else tex.SpriteRot(c, new Vector2(len, len / aspect), h, new Rect2(0, 0, 1, 1), col);
        tex.Flush(layer, t);
    }

    void Ripples(Vector2 p, float size, float px)
    {
        for (int k = 0; k < 3; k++)
        {
            float t = (time * 0.6f + k / 3f) % 1f;
            float r = (10 + 40 * t) * size * Ink.PxPerM * 0.25f + 6;
            ink.Circle(p, r, 1.4f * px, Surf with { A = 0.7f * (1 - t) }, 28);
            ink.Circle(p, r + 1.5f * px, 0.9f * px, Ink.Black with { A = 0.35f * (1 - t) }, 28);
        }
    }

    void Splash(Vector2 p, float r, float a, float px)
    {
        ink.Circle(p, r, 1.6f * px, Surf with { A = a }, 18);
        for (int i = 0; i < 8; i++)
        {
            float ang = i * Mathf.Tau / 8 + 0.3f;
            ink.Disc(p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r * 1.2f, Mathf.Max(1.2f * px, 1.4f), Surf with { A = a }, 6);
        }
    }

    void InkCloud(Vector2 p, float r, float a, float px)
    {
        if (a <= 0.01f) return;
        for (int i = 0; i < 5; i++)
        {
            float ang = i * 1.3f;
            var q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r * 0.35f;
            ink.Disc(q, r * (0.5f + 0.1f * i), new Color(0.12f, 0.1f, 0.14f, a * 0.5f), 14);
        }
    }

    /// <summary>
    /// A shootable part: its hit zone as a faint dotted chart circle (radius from the sim) and, at the top of it,
    /// a small dial whose red arc is what it has left. <paramref name="live"/> false = it cannot be hurt right now.
    /// </summary>
    void Target(Vector2 c, float radiusM, float frac, float px, bool live = true)
    {
        float r = radiusM * Ink.PxPerM;
        int n = Math.Clamp((int)(r / (px * 9f)), 12, 64);
        var col = (live ? Ink.Red : Ink.Black) with { A = live ? 0.5f : 0.25f };
        for (int i = 0; i < n; i += 2)
            ink.Circle(c, r, Mathf.Max(1.2f * px, 0.8f), col, 3, i * Mathf.Tau / n + time * 0.15f, (i + 1) * Mathf.Tau / n + time * 0.15f);
        Gauge(c + new Vector2(0, -r), frac, px, live);
    }

    /// <summary>How much a shootable part has left: a small ink ring with a red arc, like a chart's scale.</summary>
    void Gauge(Vector2 c, float frac, float px, bool live = true)
    {
        frac = Mathf.Clamp(frac, 0, 1);
        float r = Mathf.Max(7f * px, 5f);
        ink.Disc(c, r * 1.25f, Ink.Paper with { A = 0.85f }, 16);
        ink.Circle(c, r, Mathf.Max(1f * px, 0.9f), Ink.Black with { A = 0.55f }, 20);
        if (frac > 0.01f)
            ink.Circle(c, r, Mathf.Max(2.6f * px, 2f), live ? Ink.Red : Ink.Red with { A = 0.4f }, 20, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * frac);
    }
}
