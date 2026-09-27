using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Cannonballs, flotsam and the ink effects of combat. Broadsides ripple gun by gun: a muzzle flash and a
/// billowing ink-wash smoke blot (generated puffs, `assets/art/fx/fx.png`) that drifts downwind and fades;
/// balls fly with a short streak; misses throw up a white crown and rings; hits burst splinters; a ship
/// that sinks leaves bubbles, a spreading stain and a field of wreckage. Water-level marks draw under the
/// hulls (a child layer); smoke, flashes and splinters above them. Four draw calls at most.
/// </summary>
public partial class EffectsView : Node2D
{
    enum Kind { Smoke, Flash, Splinter, Crown, Droplet, Ring, Bubble, Stain, Whirl, Debris, Glint, Spray, Steam, InkBlot }

    struct Particle
    {
        public Kind Kind;
        public Vector2 Pos, Vel;
        public float Age, Life, Size, Spin, Rot;
        public int Sprite;
        public Color Tint;
        public bool Water;   // drawn under the hulls
    }

    World world = null!;
    readonly List<Particle> particles = new();
    readonly Random jitter = new(7);
    PaintLayer water = null!;
    Texture2D? atlas;
    public float Shake { get; private set; }
    float time;

    static readonly InkBatch ink = new(), tex = new();
    static readonly Color Foam = new(0.99f, 0.985f, 0.96f);
    static readonly Color Wood = new(0.45f, 0.31f, 0.18f);
    static readonly Rect2[] Smokes = { FxAtlas.Smoke0, FxAtlas.Smoke1, FxAtlas.Smoke2, FxAtlas.Smoke3, FxAtlas.Smoke4, FxAtlas.Smoke5, FxAtlas.Smoke6, FxAtlas.Smoke7 };
    static readonly Rect2[] Wreck = { FxAtlas.Plank, FxAtlas.Plank2, FxAtlas.Spar, FxAtlas.Grating, FxAtlas.Rope, FxAtlas.Hat, FxAtlas.Crate, FxAtlas.BarrelEnd };
    static readonly Vector2[] WreckPx = { FxAtlas.PlankPx, FxAtlas.Plank2Px, FxAtlas.SparPx, FxAtlas.GratingPx, FxAtlas.RopePx, FxAtlas.HatPx, FxAtlas.CratePx, FxAtlas.BarrelEndPx };

    public void Init(World w)
    {
        world = w;
        ZIndex = 12;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        atlas = Art.Tex("fx/fx");
        water = new PaintLayer { ZIndex = -4, Paint = PaintWater };   // z 8: on the water, under the hulls
        AddChild(water);
        fxTest = OS.GetCmdlineUserArgs().Contains("--fxtest");
    }

    // ---- `--fxtest` (debug, art review only): every 3 s the player's ship fires a broadside, the balls fall
    // 150 m off as splashes, a shot strikes 60 m to port, one strikes her and she rams, so every effect can be
    // captured on cue. Debug runs only: it appends view events to the tick's event list, never sim state.
    bool fxTest;
    double fxClock;
    readonly List<(Vector2 Pos, Vector2 Vel, float Life)> testBalls = new();

    void FxTestTick()
    {
        var ship = world.Ship;
        double prev = fxClock;
        fxClock += Tuning.Dt;
        double c = fxClock % 3.0, p = prev % 3.0;
        bool At(double t) => p < t && c >= t;
        var right = Ink.V(ship.Right).Normalized();
        var fwd = Ink.V(ship.Forward).Normalized();
        var side = Ink.V(ship.Pos) + right * ((float)ship.Hull.Beam * 0.5f * Ink.PxPerM);
        if (At(0.3))
        {
            Broadside(new CombatEvent(CombatEventType.Fire, ship.Pos + ship.Right * (ship.Hull.Beam * 0.5), ship.Id, 4), side);
            for (int i = 0; i < 4; i++)
                testBalls.Add((side + fwd * ((i - 1.5f) * 12f), right * (float)(World.BallSpeed * Ink.PxPerM), 1.3f + i * 0.05f));
        }
        if (At(1.0)) SplashAt(side + right * 150 * Ink.PxPerM * 0.95f - fwd * 30, 1f);
        if (At(1.1)) SplashAt(side + right * 150 * Ink.PxPerM + fwd * 20, 1f);
        if (At(2.2))   // a ball strikes her own side, then she rams: both go through the sim's event list so the hull flashes too
            world.Events.Add(new CombatEvent(CombatEventType.Hit, ship.Pos - ship.Right * (ship.Hull.Beam * 0.5), ship.Id, 6));
        if (At(2.6))
            world.Events.Add(new CombatEvent(CombatEventType.Ram, ship.Pos + ship.Forward * (ship.Hull.Length * 0.5), ship.Id, 3));
        if (At(1.7))
        {
            var at = Ink.V(ship.Pos) - right * 60 * Ink.PxPerM;
            for (int i = 0; i < 12; i++) Add(Kind.Splinter, at, Rand(95), 0.55f + Rnd() * 0.5f, 2.2f + Rnd() * 2, rot: Rnd() * 6);
            Add(Kind.Smoke, at, Rand(6), 1.1f, 7, sprite: jitter.Next(8), tint: new Color(0.55f, 0.5f, 0.45f));
            Add(Kind.Flash, at, Vector2.Zero, 0.09f, 9);
        }
        for (int i = testBalls.Count - 1; i >= 0; i--)
        {
            var b = testBalls[i];
            b.Pos += b.Vel * (float)Tuning.Dt;
            b.Life -= (float)Tuning.Dt;
            if (b.Life <= 0) testBalls.RemoveAt(i); else testBalls[i] = b;
        }
    }

    float Rnd() => (float)jitter.NextDouble();
    Vector2 Rand(float r) => new Vector2(Rnd() - 0.5f, Rnd() - 0.5f) * 2 * r;
    Vector2 WindPx => Ink.V(world.Ship.LocalWind.Vector);

    void Add(Kind kind, Vector2 pos, Vector2 vel, float life, float size, float delay = 0, int sprite = 0, Color? tint = null, bool water = false, float rot = 0)
    {
        if (particles.Count > 1500) return;
        particles.Add(new Particle
        {
            Kind = kind, Pos = pos, Vel = vel, Age = -delay, Life = life, Size = size, Spin = (Rnd() - 0.5f) * 1.2f,
            Rot = rot, Sprite = sprite, Tint = tint ?? Colors.White, Water = water,
        });
    }

    Ship? ShipById(int id)
    {
        if (id == world.Ship.Id) return world.Ship;
        foreach (var s in world.Others) if (s.Id == id) return s;
        return null;
    }

    /// <summary>Turns this tick's sim events into particles. Called once per physics tick.</summary>
    public void Consume()
    {
        if (fxTest) FxTestTick();
        // Main calls this only after a tick that actually ran (game-core GC-5): once she is lost no tick runs and
        // World.Events still holds the last tick's events, which must not be replayed.
        foreach (var e in world.Events)
        {
            var at = Ink.V(e.Pos);
            switch (e.Type)
            {
                case CombatEventType.Fire: Broadside(e, at); break;
                case CombatEventType.Hit:
                    if (e.ShipId == -3)
                    {
                        // A beast struck: a burst of dark ink, not wood.
                        for (int i = 0; i < 7; i++) Add(Kind.InkBlot, at + Rand(4), Rand(40), 0.9f + Rnd() * 0.5f, 3 + Rnd() * 4);
                        Add(Kind.Ring, at, Vector2.Zero, 0.6f, 14, water: true);
                    }
                    else
                    {
                        for (int i = 0; i < 12; i++) Add(Kind.Splinter, at, Rand(95), 0.55f + Rnd() * 0.5f, 2.2f + Rnd() * 2, rot: Rnd() * 6);
                        Add(Kind.Smoke, at, Rand(6), 1.1f, 7, sprite: jitter.Next(8), tint: new Color(0.55f, 0.5f, 0.45f));
                        Add(Kind.Flash, at, Vector2.Zero, 0.09f, 9);
                    }
                    if (e.ShipId == world.Ship.Id) Shake = Mathf.Max(Shake, 6f);
                    break;
                case CombatEventType.Splash: SplashAt(at, 1f); break;
                case CombatEventType.Ram:
                    if (e.ShipId == -4)
                    {
                        // Volcanic rocks come down: a boil of white water and a roll of steam.
                        for (int i = 0; i < 6; i++) SplashAt(at + Rand((float)e.Strength * Ink.PxPerM * 0.6f), 1.5f);
                        for (int i = 0; i < 6; i++) Add(Kind.Steam, at + Rand((float)e.Strength * Ink.PxPerM * 0.5f), Rand(10), 2.8f + Rnd(), 14 + Rnd() * 10, Rnd() * 0.4f, jitter.Next(8));
                        if (e.Pos.DistanceTo(world.Ship.Pos) < 200) Shake = Mathf.Max(Shake, 5f);
                        break;
                    }
                    for (int i = 0; i < 18; i++) Add(Kind.Splinter, at, Rand(130), 0.8f + Rnd() * 0.6f, 3 + Rnd() * 2, rot: Rnd() * 6);
                    Add(Kind.Ring, at, Vector2.Zero, 1.2f, 18, water: true);
                    Add(Kind.Ring, at, Vector2.Zero, 0.8f, 10, 0.15f, water: true);
                    for (int i = 0; i < 6; i++) Add(Kind.Droplet, at, Rand(60), 0.6f, 2, water: false);
                    Shake = Mathf.Max(Shake, 4f + (float)e.Strength);
                    break;
                case CombatEventType.Sink: Sinking(e, at); break;
                case CombatEventType.Collect:
                    for (int i = 0; i < 10; i++) Add(Kind.Glint, at, Rand(55), 0.8f, 2.5f, rot: Rnd() * 6, tint: e.Strength > 0 ? Cosmetic.Gold : Foam);
                    Add(Kind.Ring, at, Vector2.Zero, 0.7f, 10, water: true);
                    break;
                case CombatEventType.Rescued:
                    Add(Kind.Ring, at, Vector2.Zero, 2f, 40, water: true);
                    Add(Kind.Ring, at, Vector2.Zero, 2f, 24, 0.3f, water: true);
                    break;
                case CombatEventType.Ring:
                    Add(Kind.Ring, at, Vector2.Zero, 1.4f, 20 + 12 * (float)e.Strength, water: true);
                    break;
            }
        }
        StormSpray();
    }

    /// <summary>One muzzle flash and one smoke blot per gun, rippled like the sim's balls (0.05 s apart).</summary>
    void Broadside(CombatEvent e, Vector2 at)
    {
        int guns = Math.Max(1, (int)e.Strength);
        var ship = e.ShipId >= 0 ? ShipById(e.ShipId) : null;
        bool ghost = e.ShipId == -3;
        var tint = ghost ? new Color(0.75f, 0.92f, 0.86f) : new Color(0.97f, 0.95f, 0.9f);
        if (ship != null)
        {
            var dir = (at - Ink.V(ship.Pos)).Normalized();
            var fwd = Ink.V(ship.Forward).Normalized();
            float along = (float)ship.Hull.Length * 0.6f * Ink.PxPerM;
            float wsc = (float)Math.Clamp(ship.Hull.Beam / 8.0, 0.8, 1.6);
            for (int i = 0; i < guns; i++)
            {
                float x = guns == 1 ? 0 : -along / 2 + along * i / (guns - 1);
                var muzzle = at + fwd * x + dir * 2;
                float d = i * 0.05f;
                Add(Kind.Flash, muzzle + dir * 3, dir, 0.12f, 9 * wsc, d);
                Add(Kind.Smoke, muzzle + dir * 6, dir * (26 + Rnd() * 14) + Rand(5), 2.4f + Rnd() * 1.4f, (7 + Rnd() * 4) * wsc, d, jitter.Next(8), tint);
                if (i % 2 == 0) Add(Kind.Smoke, muzzle + dir * 14, dir * (40 + Rnd() * 10) + Rand(6), 2.0f + Rnd(), (5 + Rnd() * 3) * wsc, d + 0.05f, jitter.Next(8), tint);
            }
            if (ship.IsPlayer) Shake = Mathf.Max(Shake, 2.5f);
        }
        else
        {
            Add(Kind.Flash, at, Vector2.Zero, 0.14f, 14, tint: ghost ? new Color(0.6f, 1f, 0.8f) : (Color?)null);
            for (int i = 0; i < 3 + guns; i++)
                Add(Kind.Smoke, at + Rand(6), Rand(16), 2f + Rnd() * 1.2f, 8 + Rnd() * 8, Rnd() * 0.3f, jitter.Next(8), tint);
        }
    }

    void SplashAt(Vector2 at, float big)
    {
        Add(Kind.Crown, at, Vector2.Zero, 0.55f, 5 * big, water: true);
        Add(Kind.Ring, at, Vector2.Zero, 1.1f, 8 * big, 0.1f, water: true);
        Add(Kind.Ring, at, Vector2.Zero, 1.3f, 5 * big, 0.35f, water: true);
        for (int i = 0; i < 9; i++)
        {
            float a = i * Mathf.Tau / 9 + Rnd() * 0.5f;
            Add(Kind.Droplet, at, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (30 + Rnd() * 30) * big, 0.45f + Rnd() * 0.2f, 1.6f + Rnd());
        }
    }

    /// <summary>She is gone: bubbles, a stain spreading on the water, a slow whirl and a field of wreckage.</summary>
    void Sinking(CombatEvent e, Vector2 at)
    {
        float len = (float)e.Strength * Ink.PxPerM;
        if (e.ShipId == -3)
        {
            for (int i = 0; i < 14; i++) Add(Kind.Bubble, at + Rand(len * 0.4f), Rand(4), 1.2f + Rnd() * 2, 1.5f + Rnd() * 2.5f, Rnd() * 3, water: true);
            return;
        }
        Add(Kind.Stain, at, Vector2.Zero, 14f, len * 0.5f, water: true);
        Add(Kind.Whirl, at, Vector2.Zero, 7f, len * 0.45f, 0.6f, water: true);
        for (int i = 0; i < 40; i++)
            Add(Kind.Bubble, at + Rand(len * 0.35f), Rand(3), 0.8f + Rnd() * 1.8f, 1.4f + Rnd() * 3f, 0.5f + Rnd() * 6.5f, water: true);
        int pieces = 5 + (int)(len / 40f);
        var drift = WindPx * 0.08f;
        bool hers = e.ShipId == world.Ship.Id;   // her own wreckage comes up before the logbook opens (2.5 s)
        for (int i = 0; i < pieces; i++)
        {
            int kind = jitter.Next(Wreck.Length);
            float size = (kind <= 2 ? 3.5f : 2.2f) * Ink.PxPerM * (0.8f + Rnd() * 0.5f);
            float delay = hers ? 0.7f + Rnd() * 1.2f : 1.5f + Rnd() * 2.5f;
            Add(Kind.Debris, at + Rand(len * 0.45f), drift + Rand(4), 22f + Rnd() * 10, size, delay, kind, water: true, rot: Rnd() * 6);
        }
        if (e.ShipId == world.Ship.Id) Shake = Mathf.Max(Shake, 5f);
    }

    /// <summary>Heavy weather: spray whips off her bow and away downwind.</summary>
    void StormSpray()
    {
        var ship = world.Ship;
        if (ship.Sunk) return;
        double wind = ship.LocalWind.Speed;
        double storm = world.Weather.StormAt(ship.Pos);
        float k = (float)Math.Clamp((wind - 11) / 6 + storm, 0, 1.5);
        if (k <= 0 || ship.Speed < 2 || Rnd() > k * 0.9f) return;
        var bow = Ink.V(ship.Pos + ship.Forward * (ship.Hull.Length * 0.42));
        var right = Ink.V(ship.Right).Normalized();
        var wv = WindPx;
        for (int i = 0; i < 2; i++)
        {
            int side = Rnd() < 0.5f ? -1 : 1;
            var p = bow + right * (side * (float)ship.Hull.Beam * Ink.PxPerM * 0.4f) + Rand(3);
            Add(Kind.Spray, p, wv * (0.35f + Rnd() * 0.3f) + right * side * (20 + Rnd() * 20), 0.55f + Rnd() * 0.3f, 1.2f + Rnd() * 1.6f);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        time += dt;
        var wv = WindPx;
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            p.Age += dt;
            if (p.Age > 0)
            {
                p.Pos += p.Vel * dt;
                p.Rot += p.Spin * dt;
                switch (p.Kind)
                {
                    case Kind.Smoke:
                    case Kind.Steam:
                        // Smoke loses its kick and rides the wind.
                        p.Vel = p.Vel * Mathf.Exp(-dt * 1.6f) + wv * (0.45f * dt * 1.6f);
                        break;
                    case Kind.Debris:
                        p.Vel = p.Vel * Mathf.Exp(-dt * 0.4f);
                        break;
                    default:
                        p.Vel *= Mathf.Exp(-dt * 3f);
                        break;
                }
            }
            if (p.Age >= p.Life) particles.RemoveAt(i);
            else particles[i] = p;
        }
        Shake = Mathf.Max(0, Shake - dt * 12);
        QueueRedraw();
        water.QueueRedraw();
    }

    // ---------------------------------------------------------------- on the water

    void PaintWater(PaintLayer layer)
    {
        float px = layer.ScreenPx();
        ink.Clear();
        ink.Px = px;
        tex.Clear();
        tex.Px = px;
        foreach (var p in particles)
        {
            if (!p.Water || p.Age < 0) continue;
            float t = p.Age / p.Life;
            switch (p.Kind)
            {
                case Kind.Crown:
                {
                    // The white crown thrown up by a ball: a ring of spikes that opens and falls back.
                    float r = p.Size * (1 + t * 2.4f);
                    int n = 12;
                    for (int k = 0; k < n; k++)
                    {
                        float a = k * Mathf.Tau / n + p.Pos.X * 0.1f;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        ink.Taper(p.Pos + d * r * 0.4f, p.Pos + d * r * (1.1f + 0.3f * Mathf.Sin(k * 2.7f)), Mathf.Max(2.2f * px, p.Size * 0.5f), 0.4f * px, Foam with { A = 0.95f * (1 - t) }, Foam with { A = 0 });
                    }
                    ink.Disc(p.Pos, r * 0.55f * (1 - t * 0.5f), Foam with { A = 0.8f * (1 - t) }, 12);
                    ink.Circle(p.Pos, r * 0.62f, 0.9f * px, Ink.Wind with { A = 0.55f * (1 - t) }, 16);
                    break;
                }
                case Kind.Ring:
                    ink.Circle(p.Pos, p.Size * (0.4f + 1.4f * t), 1.4f * px, Foam with { A = 0.8f * (1 - t) }, 28);
                    ink.Circle(p.Pos, p.Size * (0.4f + 1.4f * t) + 1.4f * px, 0.9f * px, Ink.Black with { A = 0.4f * (1 - t) }, 28);
                    break;
                case Kind.Bubble:
                {
                    // Rises, swells and pops: a small ring, then a brief flick.
                    float r = p.Size * (0.6f + 0.6f * t);
                    ink.Circle(p.Pos, r, Mathf.Max(0.9f * px, 0.8f), Ink.Wind with { A = 0.75f * (1 - t * t) }, 10);
                    ink.Disc(p.Pos - new Vector2(r * 0.3f, r * 0.3f), r * 0.28f, Foam with { A = 0.9f * (1 - t) }, 6);
                    break;
                }
                case Kind.Stain:
                {
                    // Where she went down the water is stirred and stained: a pale blue-grey slick with an inked rim.
                    float r = p.Size * (0.6f + 1.2f * Mathf.Sqrt(t));
                    float a = (1 - t) * Mathf.Min(1, p.Age * 2);
                    for (int k = 0; k < 3; k++)
                    {
                        float ang = k * 2.1f + p.Pos.Y * 0.01f;
                        ink.Disc(p.Pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r * 0.22f, r * (0.72f - 0.1f * k), new Color(0.3f, 0.42f, 0.5f, 0.1f * a), 20, 0.82f, ang);
                    }
                    ink.Circle(p.Pos, r * 0.78f, 1.1f * px, Ink.Wind with { A = 0.35f * a }, 28);
                    break;
                }
                case Kind.Whirl:
                    for (int k = 0; k < 3; k++)
                    {
                        float r = p.Size * (1 - t * 0.6f) * (1 - k * 0.25f);
                        float a0 = p.Spin * 6 + t * 12 + k * 2.1f;
                        ink.Circle(p.Pos, r, 1.6f * px, Ink.Wind with { A = 0.5f * (1 - t) }, 20, a0, a0 + 3.8f);
                        ink.Circle(p.Pos, r * 0.92f, 1.2f * px, Foam with { A = 0.7f * (1 - t) }, 20, a0 + 0.3f, a0 + 3.4f);
                    }
                    break;
                case Kind.Debris:
                    if (atlas != null)
                    {
                        float fade = Mathf.Min(1, (1 - t) * 4) * Mathf.Min(1, p.Age * 3);
                        var px2 = WreckPx[p.Sprite];
                        var size = px2 / Mathf.Max(px2.X, px2.Y) * p.Size;
                        float bob = 1 + 0.04f * Mathf.Sin(time * 2.2f + p.Rot * 5);
                        ink.Disc(p.Pos, size.X * 0.62f, Foam with { A = 0.35f * fade }, 12, size.Y / size.X, p.Rot);
                        tex.SpriteRot(p.Pos, size * bob, p.Rot, Wreck[p.Sprite], new Color(1.15f, 1.1f, 1.02f, fade));
                    }
                    break;
            }
        }
        DrawFlotsam(px);
        ink.Flush(layer);
        if (atlas != null) tex.Flush(layer, atlas);
    }

    /// <summary>Barrels, crates and chests afloat: they bob in a ring of foam and settle as they sink.</summary>
    void DrawFlotsam(float px)
    {
        foreach (var f in world.Flotsam)
        {
            var p = Ink.V(f.Pos);
            float bob = Mathf.Sin((float)f.Bob * 2.2f);
            float fade = f.Life < 8 ? (float)(f.Life / 8) : 1;
            bool chest = f.Gold > 0;
            int h = (int)(f.Pos.X * 13 + f.Pos.Y * 7);
            Rect2 uv; Vector2 dims;
            if (chest) { uv = FxAtlas.Chest; dims = FxAtlas.ChestPx; }
            else if ((h & 3) == 0) { uv = FxAtlas.Crate; dims = FxAtlas.CratePx; }
            else if ((h & 3) == 1) { uv = FxAtlas.BarrelEnd; dims = FxAtlas.BarrelEndPx; }
            else { uv = FxAtlas.Barrel; dims = FxAtlas.BarrelPx; }
            float size = (chest ? 3.6f : 3.2f) * Ink.PxPerM * (0.85f + 0.15f * fade);
            var sz = dims / Mathf.Max(dims.X, dims.Y) * size * (1 + 0.04f * bob);
            float rot = (float)f.Bob * 0.3f + h * 0.7f;
            ink.Disc(p, size * 0.62f, Ink.Wind with { A = 0.12f * fade }, 14);
            ink.Circle(p, size * (0.6f + 0.05f * bob), 1.1f * px, Foam with { A = 0.85f * fade }, 16);
            if (atlas != null)
            {
                var col = new Color(1, 1, 1, fade).Lerp(new Color(0.5f, 0.6f, 0.7f, fade), 1 - fade);
                tex.SpriteRot(p, sz, rot, uv, col);
                if (chest && ((int)(time * 0.8f + h) % 3 == 0))
                {
                    float g = Mathf.Sin((time * 0.8f + h) % 1f * Mathf.Pi);
                    Star(p + new Vector2(size * 0.2f, -size * 0.25f), 3.5f * px + g * 3.5f * px, g * fade, px);
                }
            }
            else
            {
                ink.Disc(p, size * 0.4f, Ink.Hull with { A = fade }, 10);
            }
        }
    }

    void Star(Vector2 c, float r, float a, float px)
    {
        if (a <= 0.02f) return;
        var gold = Cosmetic.Gold.Lightened(0.35f) with { A = a };
        ink.Taper(c - new Vector2(r, 0), c + new Vector2(r, 0), 1.4f * px, 1.4f * px, gold, gold);
        ink.Taper(c - new Vector2(0, r), c + new Vector2(0, r), 1.4f * px, 1.4f * px, gold, gold);
    }

    // ---------------------------------------------------------------- above the ships

    public override void _Draw()
    {
        float px = Px();
        ink.Clear();
        ink.Px = px;
        tex.Clear();
        tex.Px = px;

        // Smoke first (it hangs over the water), then balls, flashes, splinters, spray on top.
        foreach (var p in particles)
        {
            if (p.Water || p.Age < 0) continue;
            float t = p.Age / p.Life;
            if (p.Kind is Kind.Smoke or Kind.Steam && atlas != null)
            {
                var rect = Smokes[p.Sprite & 7];
                float grow = p.Size * (0.55f + 2.1f * Mathf.Sqrt(t));
                float a = Mathf.Min(1, p.Age / 0.06f) * Mathf.Clamp((1 - t) * 1.6f, 0, 1) * (p.Kind == Kind.Steam ? 0.75f : 1f);
                var tint = p.Kind == Kind.Steam ? new Color(1, 1, 1) : p.Tint.Lerp(new Color(1, 1, 1), 0.5f + t * 0.5f);
                tex.SpriteRot(p.Pos, new Vector2(grow * 2, grow * 2), p.Rot, rect, tint with { A = a });
            }
        }
        if (atlas != null) tex.Flush(this, atlas);

        foreach (var b in world.Balls)
            if (b.Delay <= 0) Ball(Ink.V(b.Pos), Ink.V(b.Vel), px);
        foreach (var b in testBalls) Ball(b.Pos, b.Vel, px);

        foreach (var p in particles)
        {
            if (p.Water || p.Age < 0) continue;
            float t = p.Age / p.Life;
            switch (p.Kind)
            {
                case Kind.Flash:
                {
                    // A short orange starburst with a white-hot heart, pointing out of the gunport.
                    float s = p.Size * (0.7f + 0.8f * t);
                    var dir = p.Vel.LengthSquared() > 0.01f ? p.Vel.Normalized() : Vector2.Right;
                    var perp = new Vector2(-dir.Y, dir.X);
                    var hot = new Color(1f, 0.95f, 0.75f, 1 - t);
                    var orange = new Color(0.98f, 0.55f, 0.18f, 0.9f * (1 - t));
                    ink.Disc(p.Pos + dir * s * 0.3f, s * 0.9f, orange with { A = 0.35f * (1 - t) }, 14);
                    for (int k = -2; k <= 2; k++)
                    {
                        var d = (dir + perp * (k * 0.45f)).Normalized();
                        ink.Taper(p.Pos, p.Pos + d * s * (k == 0 ? 1.9f : 1.2f), s * 0.35f, 0.3f * px, orange, orange with { A = 0 });
                    }
                    ink.Disc(p.Pos, s * 0.32f * (1 - t), hot, 10);
                    break;
                }
                case Kind.Splinter:
                {
                    var d = new Vector2(Mathf.Cos(p.Rot), Mathf.Sin(p.Rot));
                    float l = p.Size * 1.6f;
                    ink.Taper(p.Pos - d * l, p.Pos + d * l, Mathf.Max(1.4f * px, p.Size * 0.55f), 0.4f * px, Wood with { A = 1 - t }, Wood with { A = 1 - t });
                    break;
                }
                case Kind.Droplet:
                case Kind.Spray:
                {
                    var tail = p.Vel * 0.03f;
                    ink.Taper(p.Pos - tail, p.Pos, 0.3f * px, Mathf.Max(1.4f * px, p.Size), Foam with { A = 0 }, Foam with { A = 0.95f * (1 - t) });
                    ink.Circle(p.Pos, Mathf.Max(1.0f * px, p.Size * 0.6f), 0.8f * px, Ink.Wind with { A = 0.5f * (1 - t) }, 6);
                    break;
                }
                case Kind.Glint:
                {
                    float s = p.Size * (1 - t) * 1.6f;
                    ink.Taper(p.Pos - new Vector2(s, 0), p.Pos + new Vector2(s, 0), 1.3f * px, 1.3f * px, p.Tint with { A = 1 - t }, p.Tint with { A = 1 - t });
                    ink.Taper(p.Pos - new Vector2(0, s), p.Pos + new Vector2(0, s), 1.3f * px, 1.3f * px, p.Tint with { A = 1 - t }, p.Tint with { A = 1 - t });
                    break;
                }
                case Kind.InkBlot:
                    ink.Disc(p.Pos, p.Size * (0.6f + t), new Color(0.14f, 0.1f, 0.16f, 0.7f * (1 - t)), 9, 0.8f, p.Rot);
                    break;
            }
        }
        ink.Flush(this);
    }

    float Px() => water.ScreenPx();

    /// <summary>A round shot in flight: a black ball with a short fading streak behind it.</summary>
    void Ball(Vector2 p, Vector2 vel, float px)
    {
        var v = vel.Normalized();
        float r = Mathf.Max(2.3f, 1.8f * px);
        ink.Taper(p - v * 26, p, 0.2f * px, r * 1.4f, Ink.Black with { A = 0 }, Ink.Black with { A = 0.4f });
        ink.Disc(p, r, Ink.Black, 8);
        ink.Disc(p - new Vector2(r * 0.3f, r * 0.3f), r * 0.3f, new Color(0.55f, 0.52f, 0.48f), 5);
    }
}
