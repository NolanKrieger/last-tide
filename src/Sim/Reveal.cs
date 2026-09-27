namespace LastTide.Sim;

/// <summary>
/// The chart's ink: a bit per 12.5 m cell, set for good once the cell has passed through the
/// player's vision (GDD §5, §17). Saved with the run.
/// </summary>
public sealed class RevealMask
{
    public const double Cell = 12.5;
    public readonly int W, H;
    readonly byte[] bits;
    public int RevealedCells { get; private set; }
    /// <summary>Bumped whenever a cell changes, so the view knows when to re-upload the texture.</summary>
    public int Version { get; private set; }

    public RevealMask()
    {
        W = (int)Math.Ceiling(Map.Width / Cell);
        H = (int)Math.Ceiling(Map.Height / Cell);
        bits = new byte[(W * H + 7) / 8];
    }

    public static int ToX(double x) => (int)Math.Floor((x + Map.HalfW) / Cell);
    public static int ToY(double y) => (int)Math.Floor((y + Map.HalfH) / Cell);

    public bool Get(int x, int y)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return false;
        int i = y * W + x;
        return (bits[i >> 3] & (1 << (i & 7))) != 0;
    }

    public bool IsRevealed(Vec2 p) => Get(ToX(p.X), ToY(p.Y));

    public bool Set(int x, int y)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return false;
        int i = y * W + x;
        byte m = (byte)(1 << (i & 7));
        if ((bits[i >> 3] & m) != 0) return false;
        bits[i >> 3] |= m;
        RevealedCells++;
        Version++;
        return true;
    }

    /// <summary>Inks every cell within <paramref name="radius"/> of a point. Returns how many were new.</summary>
    public int Paint(Vec2 centre, double radius)
    {
        int cx = ToX(centre.X), cy = ToY(centre.Y);
        int r = (int)Math.Ceiling(radius / Cell);
        double r2 = radius * radius;
        int added = 0;
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                double px = -Map.HalfW + (x + 0.5) * Cell - centre.X;
                double py = -Map.HalfH + (y + 0.5) * Cell - centre.Y;
                if (px * px + py * py <= r2 && Set(x, y)) added++;
            }
        return added;
    }

    /// <summary>Inks every cell whose centre falls in the region (the home region starts charted).</summary>
    public void PaintRegion(Map map, RegionType type)
    {
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var c = new Vec2(-Map.HalfW + (x + 0.5) * Cell, -Map.HalfH + (y + 0.5) * Cell);
                if (map.RegionAt(c).Type == type) Set(x, y);
            }
    }

    public double Fraction => RevealedCells / (double)(W * H);

    public byte[] Bytes => (byte[])bits.Clone();

    public void Load(byte[] data)
    {
        Array.Clear(bits);
        Array.Copy(data, bits, Math.Min(data.Length, bits.Length));
        RevealedCells = 0;
        foreach (var b in bits) RevealedCells += System.Numerics.BitOperations.PopCount(b);
        Version++;
    }

    /// <summary>Row-major copy as one byte per cell (0/255) for a texture.</summary>
    public void FillTexture(byte[] dst)
    {
        for (int i = 0; i < W * H; i++)
            dst[i] = (bits[i >> 3] & (1 << (i & 7))) != 0 ? (byte)255 : (byte)0;
    }
}
