using System.Runtime.CompilerServices;

namespace LastTide.Sim;

/// <summary>
/// The real-world island forms the v6 generator builds from (GDD §5, §19 "Landforms"). Each is a small group of
/// shapes whose union, roughened by noise, is traced into coastlines, so one landform can make several islands.
/// </summary>
public enum Landform
{
    GreatIsland,   // a big island kilometres across: many bays, long peninsulas, offshore islets
    HighIsland,    // one big island with coves or rias, headland peninsulas and offshore islets
    Group,         // a cluster of islands of mixed sizes around a larger one
    Chain,         // a hotspot chain: islands along an arc, shrinking from one end
    Atoll,         // a ring of reef islets around a lagoon, cut by passes
    AlmostAtoll,   // a high island inside a lagoon ringed by reef islets (Bora Bora)
    Caldera,       // a drowned volcano: a crescent round a flooded crater, an islet in the middle (Santorini)
    Barrier,       // an island fronted by long thin barrier islands across a sound, broken by inlets
    Ridges,        // drowned ridges: long thin parallel islands with channels between (Dalmatia, the Hebrides)
    Delta,         // a low landmass cut into a maze by winding channels (the mangroves)
    Cays,          // a shallow bank strewn with small sand cays
    Stack,         // a lone rock or islet
}

public enum BayStyle { Cove, Ria }

/// <summary>
/// One row of the layout table: which landforms a region builds and how. <see cref="LandShare"/> is the share of the
/// region that is land; <see cref="Groups"/> how many island groups it gathers into; <see cref="Channel"/> the open
/// water kept between landforms (m); <see cref="Scale"/> multiplies every landform size.
/// </summary>
public sealed record RegionLayout(
    RegionType Id, double LandShare, int Groups, double Channel, double Scale, BayStyle Bays, Landform Signature,
    (Landform Kind, double Weight)[] Forms)
{
    public static readonly RegionLayout[] All =
    {
        new(RegionType.TradeIsles, 0.13, 4, 150, 1.0, BayStyle.Cove, Landform.GreatIsland,
            new[] { (Landform.HighIsland, 3.0), (Landform.Group, 3.5), (Landform.AlmostAtoll, 0.6), (Landform.Chain, 1.2), (Landform.Stack, 0.5) }),
        new(RegionType.Shoals, 0.06, 5, 90, 0.85, BayStyle.Cove, Landform.Atoll,
            new[] { (Landform.Cays, 3.0), (Landform.Atoll, 0.7), (Landform.Barrier, 1.0), (Landform.Group, 1.8), (Landform.Stack, 0.4) }),
        new(RegionType.Deep, 0.04, 3, 280, 1.1, BayStyle.Cove, Landform.Atoll,
            new[] { (Landform.Atoll, 1.0), (Landform.HighIsland, 2.0), (Landform.Chain, 2.0), (Landform.Stack, 0.6) }),
        new(RegionType.FogBanks, 0.10, 4, 140, 1.0, BayStyle.Ria, Landform.Ridges,
            new[] { (Landform.Ridges, 2.0), (Landform.HighIsland, 2.0), (Landform.Group, 2.0), (Landform.Stack, 0.5) }),
        new(RegionType.StormReach, 0.11, 3, 160, 1.05, BayStyle.Ria, Landform.GreatIsland,
            new[] { (Landform.Ridges, 2.0), (Landform.HighIsland, 2.0), (Landform.Chain, 1.0), (Landform.Group, 1.0), (Landform.Stack, 0.6) }),
        new(RegionType.Mangrove, 0.18, 3, 70, 0.95, BayStyle.Cove, Landform.Delta,
            new[] { (Landform.Delta, 4.0), (Landform.Barrier, 1.0), (Landform.Group, 1.0) }),
        new(RegionType.Volcanic, 0.09, 3, 200, 1.1, BayStyle.Cove, Landform.Caldera,
            new[] { (Landform.Caldera, 0.6), (Landform.Chain, 2.0), (Landform.HighIsland, 2.2), (Landform.Stack, 0.4) }),
        new(RegionType.Sargasso, 0.025, 3, 240, 0.85, BayStyle.Cove, Landform.Atoll,
            new[] { (Landform.Atoll, 1.0), (Landform.Cays, 1.5), (Landform.Stack, 0.8) }),
        new(RegionType.SirenRuins, 0.06, 4, 110, 0.9, BayStyle.Cove, Landform.AlmostAtoll,
            new[] { (Landform.Atoll, 0.6), (Landform.AlmostAtoll, 0.8), (Landform.Group, 3.0), (Landform.Chain, 0.8), (Landform.Stack, 0.6) }),
        new(RegionType.IceReach, 0.12, 3, 150, 1.05, BayStyle.Ria, Landform.GreatIsland,
            new[] { (Landform.HighIsland, 2.5), (Landform.Ridges, 1.5), (Landform.Group, 1.0), (Landform.Stack, 0.6) }),
        new(RegionType.Maelstrom, 0.13, 3, 120, 1.0, BayStyle.Ria, Landform.Ridges,
            new[] { (Landform.Ridges, 3.0), (Landform.HighIsland, 1.5), (Landform.Chain, 1.0), (Landform.Stack, 0.4) }),
        new(RegionType.CorsairKeys, 0.06, 5, 100, 0.85, BayStyle.Cove, Landform.Barrier,
            new[] { (Landform.Cays, 3.0), (Landform.Barrier, 0.8), (Landform.AlmostAtoll, 0.4), (Landform.Group, 2.2), (Landform.Atoll, 0.3) }),
    };

    public static RegionLayout Of(RegionType t) => All[(int)t];

    /// <summary>Characteristic size range (m) of each landform before the region's scale: main radius, ring radius or half-length.</summary>
    public static (double Min, double Max) SizeOf(Landform kind) => kind switch
    {
        Landform.GreatIsland => (480, 820),
        Landform.HighIsland => (110, 380),
        Landform.Group => (60, 170),
        Landform.Chain => (80, 220),
        Landform.Atoll => (170, 420),
        Landform.AlmostAtoll => (80, 170),
        Landform.Caldera => (260, 460),
        Landform.Barrier => (110, 220),
        Landform.Ridges => (180, 560),
        Landform.Delta => (420, 900),
        Landform.Cays => (180, 420),
        _ => (22, 70),
    };
}

/// <summary>
/// One landform under construction: a union of <see cref="Lump"/>s with <see cref="Bites"/> taken out of it, the whole
/// warped and roughened by noise. <see cref="Field"/> is roughly the signed distance to the coast in metres (positive on
/// land); <see cref="Coastlines.Trace"/> turns it into island polygons.
/// </summary>
public sealed class LandformShape
{
    public Landform Kind;
    public RegionType Region;
    public Vec2 Centre;
    public readonly List<Lump> Lumps = new();
    public readonly List<Lump> Bites = new();
    public double WarpAmp, WarpScale = 300, LowScale = 260, HighScale = 90;
    /// <summary>Delta channels: wavelength and half-width (m); 0 = none.</summary>
    public double ChannelScale, ChannelHalf;
    public int Seed;

    /// <summary>Sample spacing for tracing (m): coarser for the kilometre-wide islands, whose coves are wide anyway.</summary>
    public double Step => Kind is Landform.GreatIsland ? 14 : Coastlines.Step;

    /// <summary>How far land can reach from <see cref="Centre"/>, noise and warp included.</summary>
    public double Reach
    {
        get
        {
            double r = 0;
            foreach (var l in Lumps) r = Math.Max(r, l.C.DistanceTo(Centre) + l.Extent + l.Low + l.High);
            return r + WarpAmp;
        }
    }

    /// <summary>The noiseless reach: a lower bound on the real footprint, for cheap spacing checks.</summary>
    public double BaseReach
    {
        get
        {
            double r = 0;
            foreach (var l in Lumps) r = Math.Max(r, l.C.DistanceTo(Centre) + l.Extent);
            return r;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double Field(Vec2 p)
    {
        // Lumps too far away to reach this point, however noisy and warped, are skipped; if all are, it is open sea.
        double margin = 2 * WarpAmp + 20;
        Span<int> near = stackalloc int[Lumps.Count];
        int live = 0;
        double sea = double.MinValue, bound = double.MinValue, floor = double.MinValue;
        for (int i = 0; i < Lumps.Count; i++)
        {
            var l = Lumps[i];
            double dx = p.X - l.C.X, dy = p.Y - l.C.Y, r = l.Extent + l.Low + l.High + margin;
            double d2 = dx * dx + dy * dy;
            if (d2 > r * r)
            {
                sea = Math.Max(sea, r - Math.Sqrt(d2) - margin);
                continue;
            }
            near[live++] = i;
            double sd = l.Sd(p);
            bound = Math.Max(bound, sd + l.Low + l.High);
            floor = Math.Max(floor, sd - l.Low - l.High);
        }
        if (live == 0) return sea;
        if (bound + 2 * WarpAmp < -4) return bound + 2 * WarpAmp;
        // ... or still land, far inland of every bite and creek?
        floor -= 2 * WarpAmp;
        if (floor > 20 && ChannelHalf == 0)
        {
            bool clear = true;
            foreach (var b in Bites)
            {
                double reach = b.Extent + b.High + 2 * WarpAmp + 30;
                if ((p - b.C).LengthSq < reach * reach) { clear = false; break; }
            }
            if (clear) return floor;
        }
        var q = p;
        if (WarpAmp > 0)
        {
            q = new Vec2(p.X + WarpAmp * Noise.Value2D(Seed + 1, p.X / WarpScale, p.Y / WarpScale),
                p.Y + WarpAmp * Noise.Value2D(Seed + 2, p.X / WarpScale, p.Y / WarpScale));
        }

        double low = Noise.Value2D(Seed + 3, q.X / LowScale, q.Y / LowScale);
        double high = 0.62 * Noise.Value2D(Seed + 4, q.X / HighScale, q.Y / HighScale)
            + 0.38 * Noise.Value2D(Seed + 5, q.X * 2.13 / HighScale, q.Y * 2.13 / HighScale);
        double v = double.MinValue;
        for (int k = 0; k < live; k++)
        {
            var l = Lumps[near[k]];
            v = SmoothMax(v, l.Sd(q) + l.Low * low + l.High * high, 18);
        }
        if (v < -12) return v;   // bites only deepen the sea
        foreach (var b in Bites)
        {
            // A bite can reach no further than its extent: skip the ones too far away to touch this point.
            double reach = b.Extent + b.High + v + 12;
            if ((q - b.C).LengthSq > reach * reach) continue;
            v = -SmoothMax(-v, b.Sd(q) + b.High * high, 12);
        }
        if (ChannelHalf > 0 && v > -ChannelHalf)
        {
            // Zero lines of a noise field wind and branch like tidal creeks; land is whatever lies clear of them.
            double n = Noise.Value2D(Seed + 7, q.X / ChannelScale, q.Y / ChannelScale);
            double creek = Math.Abs(n) * ChannelScale / 1.15 - ChannelHalf;
            v = Math.Min(v, creek);
        }
        return v;
    }

    static double SmoothMax(double a, double b, double k)
    {
        if (a == double.MinValue) return b;
        double h = Math.Max(k - Math.Abs(a - b), 0) / k;
        return Math.Max(a, b) + h * h * k * 0.25;
    }
}

public enum LumpShape { Ellipse, Capsule, Ring, Arc }

/// <summary>
/// One primitive of a landform. Ellipse: radii A (along <see cref="Angle"/>) and B. Capsule: half-length A, half-width B.
/// Ring: an ellipse of radii A, B drawn as a band of half-width W. Arc: a circle of radius A, half-width W, spanning
/// ±<see cref="Span"/> about <see cref="Angle"/>. <see cref="Low"/> and <see cref="High"/> are the coarse and fine noise
/// amplitudes (m) that roughen its coast.
/// </summary>
public sealed class Lump
{
    public LumpShape Shape;
    public Vec2 C;
    public double A, B, W, Span, Low, High;
    double angle, cos = 1, sin;
    double ia2 = double.NaN, ib2;

    public double Angle
    {
        get => angle;
        set { angle = value; cos = Math.Cos(value); sin = Math.Sin(value); }
    }

    public double Extent => Shape switch
    {
        LumpShape.Ellipse => Math.Max(A, B),
        LumpShape.Capsule => A + B,
        _ => Math.Max(A, B) + W,
    };

    public static Lump Ellipse(Vec2 c, double a, double b, double angle, double low = 0, double high = 0) =>
        new() { Shape = LumpShape.Ellipse, C = c, A = a, B = b, Angle = angle, Low = low, High = high };

    public static Lump Capsule(Vec2 c, double halfLength, double halfWidth, double angle, double low = 0, double high = 0) =>
        new() { Shape = LumpShape.Capsule, C = c, A = halfLength, B = halfWidth, Angle = angle, Low = low, High = high };

    public static Lump Ring(Vec2 c, double a, double b, double halfWidth, double angle, double low = 0, double high = 0) =>
        new() { Shape = LumpShape.Ring, C = c, A = a, B = b, W = halfWidth, Angle = angle, Low = low, High = high };

    public static Lump Arc(Vec2 c, double radius, double halfWidth, double angle, double halfSpan, double low = 0, double high = 0) =>
        new() { Shape = LumpShape.Arc, C = c, A = radius, B = radius, W = halfWidth, Angle = angle, Span = halfSpan, Low = low, High = high };

    /// <summary>Radius of the ellipse (A, B) along the world direction <paramref name="dir"/>.</summary>
    public double RadiusToward(double dir)
    {
        double t = dir - angle;
        double c = Math.Cos(t) / A, s = Math.Sin(t) / B;
        return 1 / Math.Sqrt(c * c + s * s);
    }

    /// <summary>Signed distance (m, positive inside), exact for circles and capsules, radial for ellipses.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public double Sd(Vec2 p)
    {
        double dx = p.X - C.X, dy = p.Y - C.Y;
        double lx = dx * cos + dy * sin, ly = -dx * sin + dy * cos;
        switch (Shape)
        {
            case LumpShape.Capsule:
            {
                double ax = Math.Max(Math.Abs(lx) - A, 0);
                return B - Math.Sqrt(ax * ax + ly * ly);
            }
            case LumpShape.Arc:
            {
                double r = Math.Sqrt(lx * lx + ly * ly);
                double t = Math.Atan2(ly, lx);
                if (Math.Abs(t) <= Span) return W - Math.Abs(r - A);
                double ex = A * Math.Cos(Span), ey = A * Math.Sin(Span) * Math.Sign(t);
                return W - Math.Sqrt((lx - ex) * (lx - ex) + (ly - ey) * (ly - ey));
            }
            default:
            {
                if (double.IsNaN(ia2)) { ia2 = 1 / (A * A); ib2 = 1 / (B * B); }
                double d = Math.Sqrt(lx * lx + ly * ly);
                double k = Math.Sqrt(lx * lx * ia2 + ly * ly * ib2);
                double radial = k < 1e-9 ? Math.Min(A, B) : d / k - d;   // distance to the rim along the ray from the centre
                return Shape == LumpShape.Ring ? W - Math.Abs(radial) : radial;
            }
        }
    }
}

/// <summary>Builds each <see cref="Landform"/> from a seeded RNG. Sizes are in metres; <c>grain</c> is the region's trend.</summary>
public static class Landforms
{
    public static LandformShape Build(Landform kind, RegionType region, Vec2 centre, double size, double grain, Rng rng, int seed)
    {
        var f = new LandformShape { Kind = kind, Region = region, Centre = centre, Seed = seed };
        double rough = RegionDef.Of(region).Roughness / 0.3;
        var bays = RegionLayout.Of(region).Bays;
        switch (kind)
        {
            case Landform.GreatIsland: GreatIsland(f, rng, size, grain, rough, bays); break;
            case Landform.HighIsland: HighIsland(f, rng, size, grain, rough, bays, region == RegionType.Volcanic); break;
            case Landform.Group: Group(f, rng, size, grain, rough); break;
            case Landform.Chain: Chain(f, rng, size, grain, rough, region is RegionType.Deep or RegionType.Volcanic); break;
            case Landform.Atoll: Atoll(f, rng, size, grain); break;
            case Landform.AlmostAtoll: AlmostAtoll(f, rng, size, grain, rough); break;
            case Landform.Caldera: Caldera(f, rng, size, grain, rough); break;
            case Landform.Barrier: Barrier(f, rng, size, grain, rough); break;
            case Landform.Ridges: Ridges(f, rng, size, grain, rough); break;
            case Landform.Delta: Delta(f, rng, size, grain); break;
            case Landform.Cays: Cays(f, rng, size, grain); break;
            default: Stack(f, rng, size, grain, rough); break;
        }
        return f;
    }

    static double Tilt(Rng rng, double spread) => rng.NextGaussian() * spread;

    /// <summary>A roughened island mass; returns it so callers can hang bays and islets on it.</summary>
    static Lump Mass(LandformShape f, Vec2 c, double r, double aspect, double angle, double rough)
    {
        double sq = Math.Sqrt(aspect);
        var l = Lump.Ellipse(c, r * sq, r / sq, angle, low: 0.16 * r * rough, high: Math.Min(0.07 * r, 16) * rough);
        f.Lumps.Add(l);
        return l;
    }

    /// <summary>Carves coves (round bays with a narrower mouth) or rias (long drowned valleys) into a mass.</summary>
    static void Bays(LandformShape f, Rng rng, Lump mass, int count, BayStyle style, double scale = 1)
    {
        var used = new List<double>();
        for (int i = 0; i < count; i++)
        {
            double dir = 0;
            bool ok = false;
            for (int t = 0; t < 8 && !ok; t++)
            {
                dir = rng.Range(0, Angles.Tau);
                ok = used.All(u => Math.Abs(Angles.Wrap(u - dir)) > 2.6 / (count + 1.5));
            }
            if (!ok) continue;
            used.Add(dir);
            double rim = mass.RadiusToward(dir);
            var d = Vec2.FromAngle(dir);
            if (style == BayStyle.Ria)
            {
                // A drowned valley: in from a wide mouth in short reaches that wander and narrow toward the head,
                // with now and then a side arm.
                double len = rim * rng.Range(0.22, 0.4), half = rng.Range(42, 62) * Math.Sqrt(scale);
                var at = mass.C + d * (rim + 60);
                double heading = dir + Math.PI + Tilt(rng, 0.2);
                int reaches = Math.Max(3, (int)((len + 60) / 60));
                double step = (len + 60) / reaches;
                for (int k = 0; k < reaches; k++)
                {
                    double w = half * (1 - 0.6 * k / reaches);
                    var to = at + Vec2.FromAngle(heading) * step;
                    f.Bites.Add(Lump.Capsule((at + to) / 2, step / 2, w, heading, high: 6));
                    if (k == reaches / 2 && rng.NextDouble() < 0.5)
                    {
                        double side = heading + (rng.NextDouble() < 0.5 ? 1 : -1) * rng.Range(0.6, 1.1);
                        f.Bites.Add(Lump.Capsule(to + Vec2.FromAngle(side) * step * 0.6, step * 0.6, w * 0.6, side, high: 5));
                    }
                    at = to;
                    heading += Tilt(rng, 0.45);
                }
            }
            else
            {
                double rb = Math.Max(70, Math.Min(rim * rng.Range(0.3, 0.5), 170 * scale));
                var c = mass.C + d * (rim - rb * rng.Range(0.15, 0.55));
                f.Bites.Add(Lump.Ellipse(c, rb * rng.Range(1.0, 1.25), rb, dir, high: 8));
            }
        }
    }

    /// <summary>Islets and stacks just off a mass's coast.</summary>
    static void Islets(LandformShape f, Rng rng, Lump mass, int count, double rough)
    {
        for (int i = 0; i < count; i++)
        {
            double dir = rng.Range(0, Angles.Tau);
            double r = Math.Max(20, mass.B * rng.Range(0.08, 0.2));
            double at = mass.RadiusToward(dir) + mass.Low * 0.6 + rng.Range(45, 120) + r;
            f.Lumps.Add(Lump.Ellipse(mass.C + Vec2.FromAngle(dir) * at, r * rng.Range(1, 1.5), r, dir + Tilt(rng, 0.5),
                low: 0.1 * r * rough, high: 0.15 * r * rough));
        }
    }

    static void HighIsland(LandformShape f, Rng rng, double r, double grain, double rough, BayStyle style, bool cone)
    {
        double aspect = cone ? rng.Range(1.0, 1.25) : rng.Range(1.1, 2.2);
        var main = Mass(f, f.Centre, r, aspect, grain + Tilt(rng, 0.25), rough);
        f.WarpAmp = (cone ? 0.1 : 0.2) * r;
        f.WarpScale = 1.3 * r;
        f.LowScale = 0.9 * r;
        int peninsulas = cone ? 0 : rng.Next(3);
        for (int i = 0; i < peninsulas; i++)
        {
            double dir = rng.Range(0, Angles.Tau);
            double rim = main.RadiusToward(dir);
            double len = r * rng.Range(0.35, 0.65), half = Math.Max(22, r * rng.Range(0.08, 0.14));
            double heading = dir + Tilt(rng, 0.35);
            var root = main.C + Vec2.FromAngle(dir) * rim * 0.75;
            f.Lumps.Add(Lump.Capsule(root + Vec2.FromAngle(heading) * len / 2, len / 2, half, heading, low: 0.15 * half * rough, high: 0.25 * half * rough));
        }
        int bays = style == BayStyle.Ria ? 2 + rng.Next(3) : (cone ? rng.Next(2) : 1 + rng.Next(3));
        Bays(f, rng, main, bays, style);
        Islets(f, rng, main, rng.Next(cone ? 2 : 4), rough);
    }

    /// <summary>
    /// A big island kilometres across: the ragged coast of a real one comes from warping it hard and hanging long
    /// peninsulas off it, then biting coves or rias into every side, with islets off the headlands.
    /// </summary>
    static void GreatIsland(LandformShape f, Rng rng, double r, double grain, double rough, BayStyle style)
    {
        double angle = grain + Tilt(rng, 0.2);
        var main = Mass(f, f.Centre, r, rng.Range(1.3, 2.4), angle, rough);
        main.High = 30 * rough;
        // A second mass off-centre makes the outline lopsided rather than an ellipse.
        double side = angle + (rng.NextDouble() < 0.5 ? 0 : Math.PI) + Tilt(rng, 0.5);
        var lobe = Mass(f, f.Centre + Vec2.FromAngle(side) * main.RadiusToward(side) * rng.Range(0.45, 0.7), r * rng.Range(0.4, 0.6), rng.Range(1.0, 1.8), angle + Tilt(rng, 0.6), rough);
        lobe.High = 26 * rough;
        f.WarpAmp = 0.22 * r;
        f.WarpScale = 1.1 * r;
        f.LowScale = 0.55 * r;
        f.HighScale = 150;
        int peninsulas = 2 + rng.Next(3);
        for (int i = 0; i < peninsulas; i++)
        {
            double dir = rng.Range(0, Angles.Tau);
            double rim = main.RadiusToward(dir);
            double len = r * rng.Range(0.3, 0.55), half = r * rng.Range(0.06, 0.1);
            double heading = dir + Tilt(rng, 0.4);
            var root = main.C + Vec2.FromAngle(dir) * rim * 0.8;
            f.Lumps.Add(Lump.Capsule(root + Vec2.FromAngle(heading) * len / 2, len / 2, half, heading, low: 0.15 * half * rough, high: Math.Min(0.25 * half, 14) * rough));
        }
        Bays(f, rng, main, style == BayStyle.Ria ? 4 + rng.Next(3) : 3 + rng.Next(3), style, scale: 1.6);
        Islets(f, rng, main, 2 + rng.Next(5), rough);
    }

    static void Group(LandformShape f, Rng rng, double r, double grain, double rough)
    {
        int count = 3 + rng.Next(5);
        var placed = new List<Lump> { Mass(f, f.Centre, r, rng.Range(1.0, 1.7), grain + Tilt(rng, 0.4), rough) };
        if (rng.NextDouble() < 0.4) Bays(f, rng, placed[0], 1, BayStyle.Cove);
        for (int i = 1; i < count; i++)
        {
            double ri = Math.Max(20, r * Math.Pow(rng.Range(0.18, 0.75), 1.3));
            for (int t = 0; t < 30; t++)
            {
                var parent = placed[rng.Next(placed.Count)];
                double dir = grain + Tilt(rng, 1.1) + (rng.NextDouble() < 0.5 ? Math.PI : 0);
                double gap = rng.Range(55, 140);
                var c = parent.C + Vec2.FromAngle(dir) * (parent.Extent + parent.Low + ri * 1.3 + gap);
                if (placed.Any(p => p.C.DistanceTo(c) < p.Extent + p.Low + ri * 1.3 + 50)) continue;
                var l = Lump.Ellipse(c, ri * rng.Range(1.0, 1.8), ri, grain + Tilt(rng, 0.5), low: 0.16 * ri * rough, high: Math.Min(0.1 * ri, 12) * rough);
                f.Lumps.Add(l);
                placed.Add(l);
                if (ri > 60 && rng.NextDouble() < 0.3) Bays(f, rng, l, 1, BayStyle.Cove);
                break;
            }
        }
        f.WarpAmp = 0.12 * r;
        f.WarpScale = 2.2 * r;
        f.LowScale = 1.2 * r;
        Recentre(f);
    }

    static void Chain(LandformShape f, Rng rng, double r0, double grain, double rough, bool drowning)
    {
        int count = 3 + rng.Next(4);
        double dir = grain + Tilt(rng, 0.25);
        double bend = rng.Range(-1, 1) / rng.Range(1200, 3000);   // curvature (1/m): a gentle arc
        double decay = rng.Range(0.55, 0.8);
        double s = 0, prev = 0;
        var at = new List<(double S, double R)>();
        for (int i = 0; i < count; i++)
        {
            double ri = Math.Max(22, r0 * Math.Pow(decay, i));
            if (i > 0) s += prev * 1.25 + rng.Range(55, 170) + ri * 1.25;
            at.Add((s, ri));
            prev = ri;
        }
        double mid = s / 2;
        Vec2 Along(double t, out double heading)
        {
            // Arc-length parametrisation of a circle of curvature `bend` through the centre.
            double u = t - mid;
            heading = dir + bend * u;
            if (Math.Abs(bend) < 1e-9) return f.Centre + Vec2.FromAngle(dir) * u;
            double rad = 1 / bend;
            var n = Vec2.FromAngle(dir).Perp;
            return f.Centre + Vec2.FromAngle(dir) * (Math.Sin(bend * u) * rad) + n * ((1 - Math.Cos(bend * u)) * rad);
        }
        for (int i = 0; i < count; i++)
        {
            var (si, ri) = at[i];
            var c = Along(si, out double h);
            if (i == count - 1 && drowning && count > 3 && rng.NextDouble() < 0.45)
            {
                // The oldest end has sunk: only its reef is left, an atoll.
                double R = Math.Max(90, ri * 2.2), w = rng.Range(20, 28);
                f.Lumps.Add(Lump.Ring(c, R, R * rng.Range(0.8, 1), w, h, high: w * 0.95));
                Pass(f, rng, c, R, w, rng.Range(0, Angles.Tau));
                continue;
            }
            f.Lumps.Add(Lump.Ellipse(c, ri * rng.Range(1.0, 1.5), ri, h + Tilt(rng, 0.3), low: 0.15 * ri * rough, high: Math.Min(0.08 * ri, 14) * rough));
        }
        f.WarpAmp = 0.08 * r0;
        f.WarpScale = 2 * r0;
        f.LowScale = r0;
    }

    /// <summary>A navigable gap cut across a reef ring.</summary>
    static void Pass(LandformShape f, Rng rng, Vec2 c, double radius, double halfWidth, double dir)
    {
        var at = c + Vec2.FromAngle(dir) * radius;
        f.Bites.Add(Lump.Capsule(at, halfWidth + 45, rng.Range(46, 62), dir));
    }

    static void Atoll(LandformShape f, Rng rng, double R, double grain)
    {
        double w = rng.Range(24, 40) * Math.Pow(R / 250, 0.3);
        double aspect = rng.Range(1.0, 1.45);
        f.Lumps.Add(Lump.Ring(f.Centre, R * Math.Sqrt(aspect), R / Math.Sqrt(aspect), w, grain + Tilt(rng, 0.3), low: 0.04 * R, high: w * rng.Range(0.85, 1.1)));
        f.HighScale = rng.Range(60, 85);
        f.LowScale = R;
        int passes = 1 + rng.Next(3);
        double first = rng.Range(0, Angles.Tau);
        for (int i = 0; i < passes; i++)
        {
            double dir = first + i * Angles.Tau / passes + Tilt(rng, 0.3);
            Pass(f, rng, f.Centre, f.Lumps[0].RadiusToward(dir), w, dir);
        }
        // A sand cay or two in the lagoon.
        int cays = rng.Next(3);
        for (int i = 0; i < cays; i++)
        {
            var c = f.Centre + Vec2.FromAngle(rng.Range(0, Angles.Tau)) * R * rng.Range(0, 0.35);
            double r = rng.Range(16, 28);
            f.Lumps.Add(Lump.Ellipse(c, r * rng.Range(1, 1.8), r, grain + Tilt(rng, 0.4), high: 4));
        }
        f.WarpAmp = 0.06 * R;
        f.WarpScale = 1.5 * R;
    }

    static void AlmostAtoll(LandformShape f, Rng rng, double r, double grain, double rough)
    {
        double angle = grain + Tilt(rng, 0.3);
        var main = Mass(f, f.Centre, r, rng.Range(1.0, 1.4), angle, rough);
        Bays(f, rng, main, rng.Next(2), BayStyle.Cove);
        double lagoon = rng.Range(120, 190), w = rng.Range(18, 30);
        double a = main.A + main.Low + lagoon, b = main.B + main.Low + lagoon;
        f.Lumps.Add(Lump.Ring(f.Centre, a, b, w, angle, high: w * rng.Range(0.85, 1.05)));
        int passes = 1 + rng.Next(2);
        double first = rng.Range(0, Angles.Tau);
        for (int i = 0; i < passes; i++)
        {
            double dir = first + i * Angles.Tau / passes + Tilt(rng, 0.4);
            Pass(f, rng, f.Centre, f.Lumps[^1].RadiusToward(dir), w, dir);
        }
        f.HighScale = 70;
        f.LowScale = r;
        f.WarpAmp = 0.05 * r;
        f.WarpScale = 2 * r;
    }

    static void Caldera(LandformShape f, Rng rng, double R, double grain, double rough)
    {
        double w = R * rng.Range(0.22, 0.3);
        double aspect = rng.Range(1.0, 1.25);
        var ring = Lump.Ring(f.Centre, R * Math.Sqrt(aspect), R / Math.Sqrt(aspect), w, grain + Tilt(rng, 0.3), low: 0.06 * R, high: 14 * rough);
        f.Lumps.Add(ring);
        // The sea broke in on one side ...
        double open = rng.Range(0, Angles.Tau), half = rng.Range(0.4, 0.7);
        var at = f.Centre + Vec2.FromAngle(open) * ring.RadiusToward(open);
        f.Bites.Add(Lump.Ellipse(at, w * 1.9, ring.RadiusToward(open) * Math.Sin(half) + 20, open));
        // ... and often a second time, leaving a smaller island across the crater (Thirasia).
        if (rng.NextDouble() < 0.65)
        {
            double second = open + (rng.NextDouble() < 0.5 ? 1 : -1) * rng.Range(1.2, 2.2);
            var gap = f.Centre + Vec2.FromAngle(second) * ring.RadiusToward(second);
            f.Bites.Add(Lump.Capsule(gap, w + 50, rng.Range(46, 64), second));
        }
        // The new cone in the middle (Nea Kameni).
        if (rng.NextDouble() < 0.75)
        {
            double r = rng.Range(30, 55);
            f.Lumps.Add(Lump.Ellipse(f.Centre + Vec2.FromAngle(rng.Range(0, Angles.Tau)) * R * rng.Range(0, 0.15), r * rng.Range(1, 1.4), r, rng.Range(0, Math.PI), low: 4, high: 6 * rough));
        }
        f.LowScale = 0.8 * R;
        f.WarpAmp = 0.05 * R;
        f.WarpScale = 1.5 * R;
    }

    static void Barrier(LandformShape f, Rng rng, double r, double grain, double rough)
    {
        var main = Mass(f, f.Centre, r, rng.Range(1.0, 1.5), grain + Tilt(rng, 0.3), rough);
        if (rng.NextDouble() < 0.5) Bays(f, rng, main, 1, BayStyle.Cove);
        double side = rng.Range(0, Angles.Tau), span = rng.Range(0.8, 1.4);
        double bw = rng.Range(20, 30), sound = rng.Range(95, 150);
        double R = Math.Max(main.A, main.B) + main.Low + sound + bw;
        int segments = 2 + rng.Next(3);
        double inlet = rng.Range(75, 110) / R;   // radians
        double each = (2 * span - inlet * (segments - 1)) / segments;
        for (int i = 0; i < segments; i++)
        {
            double mid = side - span + each / 2 + i * (each + inlet);
            f.Lumps.Add(Lump.Arc(f.Centre, R, bw, mid, each / 2, high: bw * 0.35));
        }
        f.HighScale = 80;
        f.LowScale = r;
        f.WarpAmp = 0.06 * r;
        f.WarpScale = 2.5 * r;
    }

    static void Ridges(LandformShape f, Rng rng, double len, double grain, double rough)
    {
        int count = 3 + rng.Next(3);
        double dir = grain + Tilt(rng, 0.12);
        var across = Vec2.FromAngle(dir).Perp;
        var along = Vec2.FromAngle(dir);
        double y = 0, prev = 0;
        var ridges = new List<(double Y, double Half, double Len, double X)>();
        for (int i = 0; i < count; i++)
        {
            double half = rng.Range(30, 68);
            if (i > 0) y += prev + rng.Range(70, 150) + half;
            ridges.Add((y, half, len * rng.Range(0.5, 1.05), len * rng.Range(-0.4, 0.4)));
            prev = half;
        }
        double mean = y / 2;
        foreach (var (ry, half, l, x) in ridges)
        {
            var c = f.Centre + across * (ry - mean) + along * x;
            double h = dir + Tilt(rng, 0.07);
            f.Lumps.Add(Lump.Capsule(c, l, half, h, low: 0.08 * l * rough, high: Math.Min(12, half * 0.35) * rough));
            if (rng.NextDouble() < 0.45)
            {
                // A ria into one flank.
                double side = rng.NextDouble() < 0.5 ? 1 : -1;
                var mouth = c + along * (l * rng.Range(-0.6, 0.6)) + across * side * (half + 40);
                f.Bites.Add(Lump.Capsule(mouth, half * 0.7 + 40, rng.Range(30, 40), h + Math.PI / 2 + Tilt(rng, 0.2)));
            }
        }
        f.LowScale = len * 0.9;
        f.WarpAmp = 0.12 * len;
        f.WarpScale = 1.6 * len;
    }

    static void Delta(LandformShape f, Rng rng, double R, double grain)
    {
        double aspect = rng.Range(1.0, 1.5);
        var mass = Lump.Ellipse(f.Centre, R * Math.Sqrt(aspect), R / Math.Sqrt(aspect), grain + Tilt(rng, 0.3), low: 0.12 * R, high: 12);
        f.Lumps.Add(mass);
        // Distributaries wander out from an apex to the sea; the creeks between them open into them.
        var apex = f.Centre + Vec2.FromAngle(rng.Range(0, Angles.Tau)) * R * rng.Range(0, 0.25);
        int count = 3 + rng.Next(3);
        double first = rng.Range(0, Angles.Tau);
        for (int i = 0; i < count; i++)
        {
            double heading = first + i * Angles.Tau / count + Tilt(rng, 0.3);
            double half = rng.Range(30, 38);
            var at = apex;
            for (int k = 0; k < 30 && at.DistanceTo(f.Centre) < mass.Extent + mass.Low + 40; k++)
            {
                heading = Angles.LerpAngle(heading, (at - apex).Angle, k == 0 ? 0 : 0.2) + Tilt(rng, 0.4);
                var to = at + Vec2.FromAngle(heading) * 60;
                f.Bites.Add(Lump.Capsule((at + to) / 2, 30, half, heading));
                at = to;
            }
        }
        f.ChannelScale = rng.Range(170, 220);
        f.ChannelHalf = rng.Range(30, 36);
        f.LowScale = 0.8 * R;
        f.WarpAmp = 45;
        f.WarpScale = 240;
    }

    static void Cays(LandformShape f, Rng rng, double a, double grain)
    {
        double dir = grain + Tilt(rng, 0.2);
        double b = a * rng.Range(0.4, 0.65);
        int count = 5 + rng.Next(8);
        var placed = new List<(Vec2 C, double R)>();
        for (int t = 0; t < count * 12 && placed.Count < count; t++)
        {
            var local = new Vec2(rng.Range(-a, a), rng.Range(-b, b));
            if (local.X * local.X / (a * a) + local.Y * local.Y / (b * b) > 1) continue;
            var c = f.Centre + local.Rotated(dir);
            double r = 14 + 26 * Math.Pow(rng.NextDouble(), 1.6);
            double stretch = rng.Range(1.0, 2.6);
            if (placed.Any(p => p.C.DistanceTo(c) < p.R + r * stretch + 44)) continue;
            placed.Add((c, r * stretch));
            double h = dir + Tilt(rng, 0.3);
            f.Lumps.Add(stretch > 1.6
                ? Lump.Capsule(c, r * (stretch - 1), r, h, high: r * 0.2)
                : Lump.Ellipse(c, r * stretch, r, h, high: r * 0.2));
        }
        if (placed.Count == 0) f.Lumps.Add(Lump.Ellipse(f.Centre, 26, 20, dir, high: 4));
        f.HighScale = 60;
        f.LowScale = a;
    }

    static void Stack(LandformShape f, Rng rng, double r, double grain, double rough)
    {
        var main = Lump.Ellipse(f.Centre, r * rng.Range(1, 1.8), r, grain + Tilt(rng, 0.6), low: 0.12 * r * rough, high: 0.12 * r * rough);
        f.Lumps.Add(main);
        if (rng.NextDouble() < 0.35) Islets(f, rng, main, 1 + rng.Next(2), rough);
        f.HighScale = 50;
        f.LowScale = 3 * r;
    }

    /// <summary>Moves the landform's centre to the middle of its lumps' extents, so its reach is tight.</summary>
    static void Recentre(LandformShape f)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var l in f.Lumps)
        {
            minX = Math.Min(minX, l.C.X - l.Extent); maxX = Math.Max(maxX, l.C.X + l.Extent);
            minY = Math.Min(minY, l.C.Y - l.Extent); maxY = Math.Max(maxY, l.C.Y + l.Extent);
        }
        var shift = f.Centre - new Vec2((minX + maxX) / 2, (minY + maxY) / 2);
        foreach (var l in f.Lumps) l.C += shift;
        foreach (var b in f.Bites) b.C += shift;
    }
}
