using System.Runtime.CompilerServices;

namespace LastTide.Sim;

/// <summary>
/// Seeded value noise on an integer lattice with quintic interpolation, in [−1, 1]. The hash is
/// plain integer arithmetic so the sea shader (assets/shaders/sea.gdshader) computes the same field.
/// </summary>
public static class Noise
{
    static uint Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 0x8DA6B343u + (uint)y * 0xD8163841u + (uint)z * 0xCB1AB31Fu + (uint)seed * 0x9E3779B1u;
            h ^= h >> 13;
            h *= 0x5BD1E995u;
            h ^= h >> 15;
            return h;
        }
    }

    static double Lattice(int x, int y, int z, int seed) => (Hash(x, y, z, seed) & 0xFFFFFF) / 16777216.0 * 2.0 - 1.0;

    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    public static double Value3(int seed, double x, double y, double z)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
        int x0 = (int)fx, y0 = (int)fy, z0 = (int)fz;
        double ux = Fade(x - fx), uy = Fade(y - fy), uz = Fade(z - fz);

        double c000 = Lattice(x0, y0, z0, seed), c100 = Lattice(x0 + 1, y0, z0, seed);
        double c010 = Lattice(x0, y0 + 1, z0, seed), c110 = Lattice(x0 + 1, y0 + 1, z0, seed);
        double c001 = Lattice(x0, y0, z0 + 1, seed), c101 = Lattice(x0 + 1, y0, z0 + 1, seed);
        double c011 = Lattice(x0, y0 + 1, z0 + 1, seed), c111 = Lattice(x0 + 1, y0 + 1, z0 + 1, seed);

        double x00 = c000 + (c100 - c000) * ux, x10 = c010 + (c110 - c010) * ux;
        double x01 = c001 + (c101 - c001) * ux, x11 = c011 + (c111 - c011) * ux;
        double y0v = x00 + (x10 - x00) * uy, y1v = x01 + (x11 - x01) * uy;
        return y0v + (y1v - y0v) * uz;
    }

    public static double Value2(int seed, double x, double y) => Value3(seed, x, y, 0.5);

    /// <summary>True 2D value noise (four lattice points, not eight): the coastline generator's workhorse. Sim-only, no shader twin.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double Value2D(int seed, double x, double y)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        int x0 = (int)fx, y0 = (int)fy;
        double ux = Fade(x - fx), uy = Fade(y - fy);
        double c00 = Lattice(x0, y0, 0, seed), c10 = Lattice(x0 + 1, y0, 0, seed);
        double c01 = Lattice(x0, y0 + 1, 0, seed), c11 = Lattice(x0 + 1, y0 + 1, 0, seed);
        double a = c00 + (c10 - c00) * ux, b = c01 + (c11 - c01) * ux;
        return a + (b - a) * uy;
    }
}
