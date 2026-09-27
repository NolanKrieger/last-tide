namespace LastTide.Sim;

/// <summary>
/// One port's market (GDD §6): a stock per good that drifts toward its target, a price that
/// follows √(target/stock) around the base price times the port's role multiplier, and a small
/// bid/ask spread. Buying and selling move the stock, so prices move while you trade.
/// </summary>
public sealed class Market
{
    public const double ProducerMult = 0.6, NeutralMult = 1.0, ConsumerMult = 1.5;
    public const double MinFactor = 0.35, MaxFactor = 3.0;
    public const double Spread = 0.95;             // sell price as a fraction of buy price
    public const double DriftPerDay = 0.15;
    public const double NoisePerHour = 0.01;

    public readonly double[] Stock = new double[Goods.Count];
    public readonly double[] Target = new double[Goods.Count];
    readonly double[] mult = new double[Goods.Count];

    public Market(Port port, Rng rng)
    {
        double size = port.Size switch { 0 => 100, 1 => 150, _ => 200 };
        for (int i = 0; i < Goods.Count; i++)
        {
            var g = (Good)i;
            double role = port.Produces.Contains(g) ? ProducerMult : port.Consumes.Contains(g) ? ConsumerMult : NeutralMult;
            mult[i] = role;
            // Targets sit at 100–200 by port size (GDD §6): producers hold half again as much,
            // consumers exactly the base, goods nobody here trades keep a smaller float.
            double t = role == ProducerMult ? size * 1.5 : role == ConsumerMult ? size : size * 0.6;
            if (Goods.IsRare(g) && !port.Secret) t = 15;
            Target[i] = t;
            Stock[i] = Math.Max(1, t * rng.Range(0.7, 1.3));
        }
    }

    public double Mult(Good g) => mult[(int)g];

    /// <summary>Marginal price of the next unit bought.</summary>
    public double Price(Good g)
    {
        int i = (int)g;
        double factor = Math.Sqrt(Target[i] / Math.Max(Stock[i], 0.5));
        factor = Math.Clamp(factor, MinFactor, MaxFactor);
        return Goods.Of(g).BasePrice * mult[i] * factor;
    }

    public double SellPrice(Good g) => Price(g) * Spread;

    /// <summary>Total gold for buying <paramref name="units"/>, priced unit by unit as the stock falls.</summary>
    public int QuoteBuy(Good g, int units)
    {
        int i = (int)g;
        double saved = Stock[i], total = 0;
        for (int k = 0; k < units; k++)
        {
            total += Price(g);
            Stock[i] = Math.Max(0.5, Stock[i] - 1);
        }
        Stock[i] = saved;
        return (int)Math.Round(total);
    }

    /// <summary>Total gold for selling <paramref name="units"/>, priced unit by unit as the stock rises.</summary>
    public int QuoteSell(Good g, int units)
    {
        int i = (int)g;
        double saved = Stock[i], total = 0;
        for (int k = 0; k < units; k++)
        {
            total += SellPrice(g);
            Stock[i] += 1;
        }
        Stock[i] = saved;
        return (int)Math.Round(total);
    }

    public void TakeStock(Good g, int units) => Stock[(int)g] = Math.Max(0.5, Stock[(int)g] - units);
    public void AddStock(Good g, int units) => Stock[(int)g] += units;

    /// <summary>Daily drift toward the target plus production/consumption noise, applied per tick.</summary>
    public void Tick(double dt, Rng rng)
    {
        double k = DriftPerDay * dt / Tuning.SecondsPerDay;
        double n = NoisePerHour * Math.Sqrt(dt / Tuning.SecondsPerHour);
        for (int i = 0; i < Goods.Count; i++)
        {
            Stock[i] += (Target[i] - Stock[i]) * k + Target[i] * n * rng.NextGaussian();
            if (Stock[i] < 0.5) Stock[i] = 0.5;
        }
    }

    /// <summary>A word for the HUD: how the stock sits against its target.</summary>
    public string StockWord(Good g)
    {
        double r = Stock[(int)g] / Target[(int)g];
        return r < 0.5 ? "scarce" : r < 0.85 ? "short" : r < 1.25 ? "steady" : r < 2 ? "plenty" : "glut";
    }
}
