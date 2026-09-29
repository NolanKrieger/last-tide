namespace LastTide.Sim;

/// <summary>A unique black-market part sold at secret coves (GDD §10). They move with the captain.</summary>
public sealed record BlackMarketDef(string Key, int Price)
{
    public static readonly BlackMarketDef[] All =
    {
        new("ghost_sails", 650),     // hunters detect you 30% closer
        new("smugglers_keel", 550),  // points 5% closer to the wind
        new("long_nines", 700),      // +40 m gun range
        new("hidden_hold", 600),     // +15% cargo
        new("false_colours", 450),   // reputations mend twice as fast
    };

    public static BlackMarketDef Of(string key) => All.First(d => d.Key == key);

    /// <summary>The one or two parts a given cove stocks, fixed by the run's seed.</summary>
    public static List<BlackMarketDef> Stock(int seed, int portId)
    {
        var rng = new Rng((ulong)(seed * 977 + portId * 31 + 5));
        var pool = All.ToList();
        int n = 1 + rng.Next(2);
        var list = new List<BlackMarketDef>();
        for (int i = 0; i < n && pool.Count > 0; i++)
        {
            var pick = pool[rng.Next(pool.Count)];
            pool.Remove(pick);
            list.Add(pick);
        }
        return list;
    }
}

/// <summary>A torn chart fragment (GDD §10): an island's coastline with an X. Matching it pins the X.</summary>
public sealed class BottleMap
{
    public int Treasure { get; set; }
    public bool Solved { get; set; }
    public double Rotation { get; set; }   // the sketch is drawn turned, so the shape must be read
}
