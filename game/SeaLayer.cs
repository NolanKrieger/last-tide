using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>
/// Full-screen chart sea behind everything (assets/shaders/sea.gdshader). Once bound to a world and its
/// <see cref="CoastField"/>, the shader mirrors the sim's wind field per pixel (base wind, spatial noise, gusts, regional
/// factor, doldrums, storm swirl) on the sim's own clock, so the strokes show the wind the ships actually feel.
/// </summary>
public partial class SeaLayer : CanvasLayer
{
    ShaderMaterial material = null!;
    World? world;
    Vector2 drift;
    float lastTime = -1;
    // The sim's wind and weather sampled on a world-locked grid around the view (see UpdateGrid).
    public const int GridW = 32, GridH = 20;
    readonly byte[] windBytes = new byte[GridW * GridH * 4], condBytes = new byte[GridW * GridH * 4];
    Image windImage = null!, condImage = null!;
    ImageTexture windTex = null!, condTex = null!;
    // Toroidal addressing: world cell (cx, cy) lives in texel (cx mod W, cy mod H), so when the view moves only the
    // cells entering the window are sampled; a rolling pass refreshes a few rows every frame (no per-frame spike).
    int winX = int.MinValue, winY = int.MinValue;   // the window's first cell
    float gridStep;
    int rollRow;

    /// <summary>The chart's portolan wind roses (metres): the rhumb lines radiate from these.</summary>
    public Vector2[] Hubs { get; private set; } = Array.Empty<Vector2>();
    /// <summary>The sim clock last handed to the shader (for the self-test: gusts must use the sim's time).</summary>
    public float SimTime { get; private set; }

    public override void _Ready()
    {
        Layer = -10;
        var rect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Color = Colors.White };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/sea.gdshader") };
        if (Art.Tex("paper/paper") is { } paper) material.SetShaderParameter("paper_tex", paper);
        if (Art.Tex("paper/noise") is { } noise) material.SetShaderParameter("noise_tex", noise);
        windImage = Image.CreateFromData(GridW, GridH, false, Image.Format.Rgba8, windBytes);
        condImage = Image.CreateFromData(GridW, GridH, false, Image.Format.Rgba8, condBytes);
        windTex = ImageTexture.CreateFromImage(windImage);
        condTex = ImageTexture.CreateFromImage(condImage);
        material.SetShaderParameter("wind_grid", windTex);
        material.SetShaderParameter("cond_grid", condTex);
        material.SetShaderParameter("grid_texels", new Vector2(GridW, GridH));
        material.SetShaderParameter("map_half", new Vector2((float)Map.HalfW, (float)Map.HalfH));
        rect.Material = material;
        AddChild(rect);
    }

    /// <summary>Binds the world: coast field textures, regional weather rules and the rose positions.</summary>
    public void Init(World w, CoastField field)
    {
        world = w;
        material.SetShaderParameter("sdf", field.Sdf);
        material.SetShaderParameter("regions", field.Regions);
        material.SetShaderParameter("water_tint", field.WaterTint);
        material.SetShaderParameter("land_tint", field.LandTint);
        Hubs = PlaceHubs(field);
        var hubs = new Vector2[9];
        Array.Copy(Hubs, hubs, Math.Min(9, Hubs.Length));
        material.SetShaderParameter("hubs", hubs);
        material.SetShaderParameter("hub_count", Hubs.Length);
        material.SetShaderParameter("has_field", true);
    }

    /// <summary>The chart's reveal mask (FogView's texture): the sea skips its work under blank, unexplored paper.</summary>
    public void BindReveal(Texture2D reveal) => material.SetShaderParameter("reveal", reveal);

    /// <summary>
    /// A portolan layout: one rose near the middle of the chart and four on a ring around it, each nudged to the
    /// nearest open water (well clear of any coast) so the rose drawn there never sits on an island.
    /// </summary>
    public static Vector2[] PlaceHubs(CoastField field)
    {
        var nominal = new List<Vector2> { Vector2.Zero };
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.Tau / 4 + 0.62f;
            nominal.Add(new Vector2(Mathf.Cos(a) * 1900, Mathf.Sin(a) * 1450));
        }
        var hubs = new List<Vector2>();
        foreach (var p in nominal)
        {
            Vector2? found = null;
            for (float r = 0; r <= 600 && found == null; r += 25)
            {
                int n = Math.Max(1, (int)(r / 12));
                for (int k = 0; k < n && found == null; k++)
                {
                    float a = k * Mathf.Tau / n;
                    var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (Mathf.Abs(q.X) > Map.HalfW - 250 || Mathf.Abs(q.Y) > Map.HalfH - 250) continue;
                    if (field.DistanceAt(q) > 120) found = q;
                }
            }
            if (found is { } f) hubs.Add(f);
        }
        return hubs.ToArray();
    }

    public void Update(Vector2 camMetres, Vector2 viewMetres, Vector2 screenPx, Vector2 windDir, float windSpeed, float time, int seed, bool fixedWind)
    {
        float dt = lastTime < 0 ? 0 : Math.Max(0, time - lastTime);
        lastTime = time;
        // The strokes drift downwind at a third of the wind, accumulated here so a change of wind never makes the
        // whole pattern jump (it used to be time × speed, which slid the sea by metres whenever the wind freshened).
        drift += windDir * windSpeed * 0.35f * dt;
        material.SetShaderParameter("cam_pos", camMetres);
        material.SetShaderParameter("view_world", viewMetres);
        material.SetShaderParameter("screen_px", screenPx);
        material.SetShaderParameter("ship_wind_dir", windDir);
        material.SetShaderParameter("ship_wind_speed", windSpeed);
        material.SetShaderParameter("time", time);
        material.SetShaderParameter("drift", drift);
        if (world == null) return;
        SimTime = (float)world.Time;
        UpdateGrid(camMetres, viewMetres);
    }

    /// <summary>
    /// Samples World.WindAt / ConditionsAt (the sim's own functions, so the strokes can never disagree with the wind the
    /// ships feel) on a 32×20 world-locked window around the view: 30 m cells, 60 m when zoomed far out. Cells entering
    /// the window are sampled at once; four rows are refreshed each frame, so every cell is fresh within five frames.
    /// </summary>
    void UpdateGrid(Vector2 camMetres, Vector2 viewMetres)
    {
        float step = viewMetres.X * 1.15f > GridW * 30 || viewMetres.Y * 1.15f > GridH * 30 ? 60 : 30;
        int wx = (int)Mathf.Floor(camMetres.X / step) - GridW / 2, wy = (int)Mathf.Floor(camMetres.Y / step) - GridH / 2;
        if (step != gridStep || Math.Abs(wx - winX) >= GridW || Math.Abs(wy - winY) >= GridH)
        {
            gridStep = step;
            winX = wx;
            winY = wy;
            for (int y = 0; y < GridH; y++) SampleRow(wy + y);
        }
        else
        {
            // columns and rows that entered the window
            for (int x = winX + GridW; x < wx + GridW; x++) SampleColumn(x, wy);
            for (int x = wx; x < winX; x++) SampleColumn(x, wy);
            winX = wx;
            for (int y = winY + GridH; y < wy + GridH; y++) SampleRow(y);
            for (int y = wy; y < winY; y++) SampleRow(y);
            winY = wy;
            for (int k = 0; k < 4; k++) SampleRow(winY + (rollRow++ % GridH));
        }
        windImage.SetData(GridW, GridH, false, Image.Format.Rgba8, windBytes);
        condImage.SetData(GridW, GridH, false, Image.Format.Rgba8, condBytes);
        windTex.Update(windImage);
        condTex.Update(condImage);
        material.SetShaderParameter("grid_step", step);
    }

    void SampleRow(int cy)
    {
        for (int cx = winX; cx < winX + GridW; cx++) Sample(cx, cy);
    }

    void SampleColumn(int cx, int wy)
    {
        for (int cy = wy; cy < wy + GridH; cy++) Sample(cx, cy);
    }

    void Sample(int cx, int cy)
    {
        var p = new Vec2(cx * (double)gridStep, cy * (double)gridStep);
        var w = world!.WindAt(p);
        var c = world.ConditionsAt(p);
        double gust = world.Wind.Gust(p, world.Time);
        int k = 4 * (Mod(cy, GridH) * GridW + Mod(cx, GridW));
        windBytes[k] = B(Math.Cos(w.Direction) * 0.5 + 0.5);
        windBytes[k + 1] = B(Math.Sin(w.Direction) * 0.5 + 0.5);
        windBytes[k + 2] = B(w.Speed / 30.0);
        windBytes[k + 3] = B(gust);
        condBytes[k] = B(c.Doldrums);
        condBytes[k + 1] = B(c.Fog);
        condBytes[k + 2] = B(c.Storm);
        condBytes[k + 3] = B(c.Ash);
        Samples++;
    }

    static int Mod(int a, int m) => ((a % m) + m) % m;

    /// <summary>Resamples the whole window now (the self-test compares it with World.WindAt at the same instant).</summary>
    public void RefreshGrid()
    {
        for (int y = 0; y < GridH; y++) SampleRow(winY + y);
        windImage.SetData(GridW, GridH, false, Image.Format.Rgba8, windBytes);
        windTex.Update(windImage);
    }

    /// <summary>Cells sampled so far (diagnostics: about 128 a frame at sea).</summary>
    public long Samples { get; private set; }

    static byte B(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    /// <summary>The condition grid (r doldrums, g fog, b storm, a ash) for the weather wash, and where it lies.</summary>
    public ImageTexture CondTexture => condTex;
    public float GridStep => gridStep;
    /// <summary>The accumulated downwind drift (m): mist and ash ride it too.</summary>
    public Vector2 Drift => drift;

    /// <summary>The grid's wind at a point as the shader reads it (bilinear, wrapped), for the self-test's contract check.</summary>
    public (Vector2 Dir, float Speed) GridWindAt(Vec2 p)
    {
        float fx = (float)(p.X / gridStep), fy = (float)(p.Y / gridStep);
        int x0 = (int)Mathf.Floor(fx), y0 = (int)Mathf.Floor(fy);
        float tx = fx - x0, ty = fy - y0;
        Vector4 Texel(int x, int y)
        {
            int k = 4 * (Mod(y, GridH) * GridW + Mod(x, GridW));
            return new Vector4(windBytes[k], windBytes[k + 1], windBytes[k + 2], windBytes[k + 3]) / 255f;
        }
        var v = Texel(x0, y0).Lerp(Texel(x0 + 1, y0), tx).Lerp(Texel(x0, y0 + 1).Lerp(Texel(x0 + 1, y0 + 1), tx), ty);
        return (new Vector2(v.X * 2 - 1, v.Y * 2 - 1).Normalized(), v.Z * 30);
    }
}
