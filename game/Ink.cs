using Godot;
using LastTide.Sim;

namespace LastTide;

/// <summary>The chart's palette, the metre→pixel scale, and small drawing helpers.</summary>
public static class Ink
{
    /// <summary>Pixels per metre at zoom 1.0. A 16 m sloop is 64 px long.</summary>
    public const float PxPerM = 4f;

    public static readonly Color Paper = new(0.933f, 0.886f, 0.792f);
    public static readonly Color Shade = new(0.855f, 0.792f, 0.667f);
    public static readonly Color Black = new(0.16f, 0.13f, 0.10f);
    public static readonly Color Soft = new(0.16f, 0.13f, 0.10f, 0.5f);
    public static readonly Color Faint = new(0.16f, 0.13f, 0.10f, 0.15f);
    /// <summary>The wind's own ink: a blue-black that reads apart from the sepia by hue AND by stroke weight.</summary>
    public static readonly Color Wind = new(0.20f, 0.32f, 0.45f);
    public static readonly Color Red = new(0.62f, 0.16f, 0.12f);
    public static readonly Color Land = new(0.80f, 0.72f, 0.56f);
    public static readonly Color Hull = new(0.55f, 0.42f, 0.28f);
    public static readonly Color Deck = new(0.78f, 0.68f, 0.50f);
    public static readonly Color Sail = new(0.97f, 0.95f, 0.89f);
    /// <summary>Faction inks: told apart by hue AND by glyph shape (crown, anchor, pennant), so colour is never the only cue.</summary>
    public static Color Crown { get; private set; } = new(0.58f, 0.16f, 0.12f);
    public static Color Free { get; private set; } = new(0.16f, 0.13f, 0.10f);
    public static Color Brethren { get; private set; } = new(0.08f, 0.26f, 0.32f);
    /// <summary>Bumps whenever the palette changes so cached chart chunks redraw.</summary>
    public static int PaletteVersion { get; private set; }
    public static bool Colorblind { get; private set; }

    /// <summary>The colorblind-safe palette (Okabe–Ito vermilion / blue): faction inks and the threat accent stay apart in every common deficiency.</summary>
    public static void SetPalette(bool colorblind)
    {
        if (colorblind == Colorblind && PaletteVersion > 0) return;
        Colorblind = colorblind;
        Crown = colorblind ? new Color(0.84f, 0.37f, 0.0f) : new Color(0.58f, 0.16f, 0.12f);
        Brethren = colorblind ? new Color(0.0f, 0.45f, 0.70f) : new Color(0.08f, 0.26f, 0.32f);
        Free = new Color(0.16f, 0.13f, 0.10f);
        PaletteVersion++;
    }

    public static Color Faction(LastTide.Sim.Faction f) => f switch
    {
        LastTide.Sim.Faction.Crown => Crown,
        LastTide.Sim.Faction.Brethren => Brethren,
        _ => Free,
    };

    public static Vector2 V(Vec2 v) => new((float)(v.X * PxPerM), (float)(v.Y * PxPerM));
    public static Vec2 M(Vector2 px) => new(px.X / PxPerM, px.Y / PxPerM);

    /// <summary>Stable pseudo-random number in [0,1) for decorative jitter.</summary>
    public static float Jitter(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);
        h = (h ^ (h >> 15)) * 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>Quadratic Bézier sampled into <paramref name="n"/> points (inclusive of both ends).</summary>
    public static Vector2[] Bezier(Vector2 a, Vector2 control, Vector2 b, int n)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            pts[i] = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
        }
        return pts;
    }

    public static Vector2[] Closed(Vector2[] pts)
    {
        var c = new Vector2[pts.Length + 1];
        pts.CopyTo(c, 0);
        c[^1] = pts[0];
        return c;
    }
}
