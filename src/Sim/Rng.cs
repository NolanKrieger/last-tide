namespace LastTide.Sim;

/// <summary>xoshiro256** seeded from splitmix64. Deterministic; state is four ulongs so it saves and replays.</summary>
public sealed class Rng
{
    ulong s0, s1, s2, s3;

    public Rng(ulong seed)
    {
        ulong x = seed;
        s0 = Split(ref x);
        s1 = Split(ref x);
        s2 = Split(ref x);
        s3 = Split(ref x);
    }

    static ulong Split(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextULong()
    {
        ulong result = RotL(s1 * 5, 7) * 9;
        ulong t = s1 << 17;
        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;
        s2 ^= t;
        s3 = RotL(s3, 45);
        return result;
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    public double Range(double min, double max) => min + (max - min) * NextDouble();

    public int Next(int maxExclusive) => (int)(NextDouble() * maxExclusive);

    /// <summary>Standard normal (Box–Muller, no cached second value so the state stays four words).</summary>
    public double NextGaussian()
    {
        double u1 = 1.0 - NextDouble();
        double u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public ulong[] State
    {
        get => new[] { s0, s1, s2, s3 };
        set
        {
            if (value.Length != 4) throw new ArgumentException("rng state has four words");
            (s0, s1, s2, s3) = (value[0], value[1], value[2], value[3]);
        }
    }
}
