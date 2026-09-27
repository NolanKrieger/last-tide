using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// The weather on the chart (GDD §9): storm cells drawn in the world as ink swirls over a dark watercolour wash
/// (assets/shaders/storm.gdshader, one quad per cell, under the ships), lightning bolts that strike with the thunder,
/// and the screen wash for night, dusk and dawn, the lantern's pool, fog banks, rain and ash
/// (assets/shaders/weather.gdshader), fed by the sim's own weather fields through the sea's condition grid.
/// </summary>
public partial class WeatherView : Node2D
{
    World world = null!;
    SeaLayer? sea;
    CanvasLayer washLayer = null!;
    ShaderMaterial wash = null!;
    readonly StormQuad[] quads = new StormQuad[Weather.MaxStorms];
    float time, spin;
    public Conditions Now { get; private set; }

    // Lightning
    float flash, flashAge = 99;
    readonly Vector2[] bolt = new Vector2[20];
    readonly Vector2[] branch = new Vector2[9];
    int boltLen, branchLen;
    float boltAge = 99, distantClock = 5;
    int shotsSeen = -1;
    uint strikes;
    /// <summary>Strikes so far (the self-test checks a storm throws lightning).</summary>
    public int Strikes => (int)strikes;

    public void Init(World w, SeaLayer? seaLayer = null, CoastField? field = null)
    {
        world = w;
        sea = seaLayer;
        ZIndex = 6;
        for (int i = 0; i < quads.Length; i++)
        {
            quads[i] = new StormQuad();
            AddChild(quads[i]);
        }
        washLayer = new CanvasLayer { Layer = 9 };
        var rect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Color = Colors.White };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        wash = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/weather.gdshader") };
        if (Art.Tex("paper/noise") is { } noise) wash.SetShaderParameter("noise_tex", noise);
        if (Art.Tex("paper/paper") is { } paper) wash.SetShaderParameter("paper_tex", paper);
        wash.SetShaderParameter("map_half", new Vector2((float)Map.HalfW, (float)Map.HalfH));
        if (field != null)
        {
            wash.SetShaderParameter("sdf", field.Sdf);
            wash.SetShaderParameter("has_sdf", true);
        }
        if (sea != null)
        {
            wash.SetShaderParameter("cond_grid", sea.CondTexture);
            wash.SetShaderParameter("grid_texels", new Vector2(SeaLayer.GridW, SeaLayer.GridH));
            wash.SetShaderParameter("has_grid", true);
        }
        rect.Material = wash;
        washLayer.AddChild(rect);
        AddChild(washLayer);
    }

    float founder;
    public float Founder => founder;

    /// <summary>Feeds the wash and the storm quads: where the ship is on screen, how dark, how foggy, where it rains.</summary>
    public void Update(Vector2 shipScreenPx, float zoom, double delta, bool paused)
    {
        float dt = paused ? 0 : (float)delta;
        time += dt;
        spin += dt * 0.12f;
        var c = world.ConditionsAt(world.Ship.Pos);
        Now = c;
        // Twilight is continuous: Dusk runs 0→1 through the evening hour (21–22 h) and 1→0 through the morning hour
        // (6–7 h), so night fades in and out with it instead of jumping (it used to be Dusk × 0.5, a pop at 22:00 and 06:00):
        // golden (evening) or rose (morning) light nearest the day, the blue hour nearest the night.
        float tw = (float)c.Dusk;
        float night = c.Night ? 1f : Mathf.SmoothStep(0.45f, 1f, tw);
        // The title's backdrop is a fresh world held still at 06:00, the last instant of the night: show it by day
        // (only a world that has never ticked and is held still; a voyage ticks on its first frame).
        bool backdrop = paused && world.Ticks == 0;
        if (backdrop) { night = 0; tw = 0; }
        var window = (Vector2)DisplayServer.WindowGetSize();
        var visible = GetViewport().GetVisibleRect().Size;
        var scale = window / visible;
        float pxPerM = Ink.PxPerM * zoom * scale.X;
        var inv = GetCanvasTransform().AffineInverse();
        var viewMetres = (inv * visible - inv * Vector2.Zero) / Ink.PxPerM;
        var camMetres = (inv * (visible * 0.5f)) / Ink.PxPerM;
        wash.SetShaderParameter("cam_pos", camMetres);
        wash.SetShaderParameter("view_world", viewMetres);
        wash.SetShaderParameter("screen_px", window);
        wash.SetShaderParameter("night", night);
        wash.SetShaderParameter("dusk", c.Night || tw >= 0.8f || backdrop ? 0f : Mathf.Sin(tw / 0.8f * Mathf.Pi));
        wash.SetShaderParameter("dawn", world.HourOfDay < 12 ? 1f : 0f);
        wash.SetShaderParameter("pool_centre", shipScreenPx * scale);
        wash.SetShaderParameter("pool_radius", 75f * pxPerM);   // the lamp's warm light on the water around her
        wash.SetShaderParameter("lantern", world.Lantern ? 1f : 0f);
        wash.SetShaderParameter("vision_px", (float)world.VisionRadius * pxPerM);
        wash.SetShaderParameter("fog_here", (float)c.Fog);
        wash.SetShaderParameter("rain_here", (float)c.Rain);
        wash.SetShaderParameter("ash_here", (float)c.Ash);
        wash.SetShaderParameter("time", time);
        wash.SetShaderParameter("wind_dir", Ink.V(Vec2.FromAngle(world.Ship.LocalWind.Direction)).Normalized());
        if (sea != null)
        {
            wash.SetShaderParameter("drift", sea.Drift);
            wash.SetShaderParameter("grid_step", sea.GridStep);
        }
        var ship = world.Ship;
        float founderWant = world.RunOver ? 0.85f : ship.Foundering ? 0.35f + 0.4f * (1f - (float)Math.Clamp(ship.Hourglass / 35.0, 0, 1)) : ship.Water > 40 ? (float)((ship.Water - 40) / 60.0) * 0.3f : 0f;
        founder += (founderWant - founder) * (float)Math.Min(1, delta * (world.RunOver ? 0.8 : 2.0));
        wash.SetShaderParameter("founder", founder);

        // Storm cells in the world.
        for (int i = 0; i < quads.Length; i++)
        {
            if (i < world.Weather.Storms.Count)
            {
                var cell = world.Weather.Storms[i];
                float fade = (float)Math.Min(1, Math.Min(cell.Age / 10, cell.Life / 15));
                quads[i].Set(Ink.V(cell.Pos), (float)cell.Radius * Ink.PxPerM, (float)cell.Strength, fade, spin + i * 1.7f);
            }
            else quads[i].Visible = false;
        }
        Lightning(dt, c);
        wash.SetShaderParameter("flash", flash);
        QueueRedraw();
    }

    /// <summary>A strike with every thunderclap the ambience plays (it only thunders inside a storm), and now and then
    /// a silent distant one inside a storm cell in view.</summary>
    void Lightning(float dt, Conditions c)
    {
        boltAge += dt;
        flashAge += dt;
        // a double flicker: bright, a dip, a second pulse, then gone
        flash = flashAge < 0.07f ? 1f : flashAge < 0.12f ? 0.25f : flashAge < 0.2f ? 0.7f : Mathf.Max(0, 0.7f - (flashAge - 0.2f) * 3f);
        if (flashAge > 0.45f) flash = 0;
        var audio = (GetParent() as Main)?.Sound;
        if (audio != null)
        {
            int thunder = audio.Plays("thunder");   // counted by name: another one-shot may land in the same frame
            if (shotsSeen >= 0 && thunder > shotsSeen) Strike(world.Ship.Pos, full: true);
            shotsSeen = thunder;
        }
        if (dt <= 0) return;
        distantClock -= dt;
        if (distantClock <= 0)
        {
            distantClock = 5 + (strikes * 2654435761u % 1000) / 1000f * 8;
            foreach (var cell in world.Weather.Storms)
            {
                if (cell.Inside(world.Ship.Pos) > 0.05) continue;   // inside, the thunder brings the strikes
                if (cell.Pos.DistanceTo(world.Ship.Pos) > 1100) continue;
                Strike(cell.Pos, full: false);
                break;
            }
        }
    }

    void Strike(Vec2 near, bool full)
    {
        strikes++;
        uint h = strikes * 2654435761u + 12345;
        float Rnd() { h ^= h << 13; h ^= h >> 17; h ^= h << 5; return (h & 0xFFFFFF) / 16777216f; }
        // A forked bolt seen from above: a hard zig-zag (alternate kinks about one heading), a fork off the middle,
        // and the strike where it ends. Near the ship inside the storm, or inside a distant cell.
        var start = Ink.V(near) + new Vector2(Rnd() - 0.5f, Rnd() - 0.5f) * 110 * Ink.PxPerM;
        float heading = Rnd() * Mathf.Tau;
        boltLen = bolt.Length;
        var p = start;
        float side = 1;
        for (int i = 0; i < boltLen; i++)
        {
            bolt[i] = p;
            // irregular kinks: mostly alternating, now and then a run the same way; short and long legs mixed
            if (Rnd() < 0.72f) side = -side;
            float kink = side * (0.15f + 0.95f * Rnd() * Rnd());
            float step = (1.8f + 7f * Rnd() * Rnd()) * Ink.PxPerM;
            p += new Vector2(Mathf.Cos(heading + kink), Mathf.Sin(heading + kink)) * step;
            heading += (Rnd() - 0.5f) * 0.35f;
        }
        int from = 5 + (int)(Rnd() * 6);
        branchLen = branch.Length;
        p = bolt[from];
        float bh = heading + (Rnd() < 0.5f ? 1 : -1) * 0.7f;
        side = 1;
        for (int i = 0; i < branchLen; i++)
        {
            branch[i] = p;
            if (Rnd() < 0.72f) side = -side;
            float kink = side * (0.15f + 0.9f * Rnd() * Rnd());
            p += new Vector2(Mathf.Cos(bh + kink), Mathf.Sin(bh + kink)) * (1.5f + 5f * Rnd() * Rnd()) * Ink.PxPerM;
        }
        impact = bolt[boltLen - 1];
        boltAge = 0;
        if (full) flashAge = 0;
        else if (flashAge > 0.45f) { flashAge = 0.2f; }   // a distant strike only pales the sea a little
    }

    Vector2 impact;

    public override void _Draw()
    {
        if (boltAge > 0.6f || boltLen == 0) return;
        // bright for a blink, a flicker, a second stroke, then it fades like a retinal after-image
        float a = boltAge < 0.07f ? 1f : boltAge < 0.12f ? 0.3f : boltAge < 0.2f ? 0.95f : Mathf.Max(0, 0.95f - (boltAge - 0.2f) * 2.4f);
        if (a <= 0) return;
        var glow = new Color(0.72f, 0.80f, 1f, 0.28f * a);
        var inkCol = Ink.Black with { A = 0.85f * a };
        var core = new Color(1f, 0.98f, 0.85f, a);
        DrawPolyline(bolt, glow, 16f, true);
        DrawPolyline(branch, glow, 10f, true);
        DrawPolyline(bolt, inkCol, 6f, true);          // inked outline, so it reads on the pale storm wash
        DrawPolyline(branch, inkCol, 4f, true);
        DrawPolyline(bolt, core, 3.2f, true);
        DrawPolyline(branch, core, 2f, true);
        // where it strikes the sea: a burst ring and a splash of light
        float t = Mathf.Clamp(boltAge / 0.6f, 0, 1);
        DrawArc(impact, (4 + 22 * t) * Ink.PxPerM, 0, Mathf.Tau, 28, Ink.Black with { A = 0.5f * (1 - t) }, 2f, true);
        DrawCircle(impact, (2.5f + 3 * (1 - t)) * Ink.PxPerM, core with { A = 0.8f * a });
    }
}

/// <summary>One storm cell's quad (assets/shaders/storm.gdshader).</summary>
public partial class StormQuad : Node2D
{
    ShaderMaterial mat = null!;
    Rect2 rect;

    public override void _Ready()
    {
        mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/storm.gdshader") };
        if (Art.Tex("paper/noise") is { } noise) mat.SetShaderParameter("noise_tex", noise);
        if (Art.Tex("paper/paper") is { } paper) mat.SetShaderParameter("paper_tex", paper);
        mat.SetShaderParameter("px_per_m", Ink.PxPerM);
        Material = mat;
        Visible = false;
    }

    public void Set(Vector2 centre, float radius, float strength, float fade, float spin)
    {
        Visible = true;
        mat.SetShaderParameter("centre", centre);
        mat.SetShaderParameter("radius", radius);
        mat.SetShaderParameter("strength", strength);
        mat.SetShaderParameter("fade", fade);
        mat.SetShaderParameter("spin", spin);
        var r = new Rect2(centre - Vector2.One * radius, Vector2.One * radius * 2);
        if (r != rect) { rect = r; QueueRedraw(); }
    }

    public override void _Draw() => DrawRect(rect, Colors.White);
}
