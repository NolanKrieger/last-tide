namespace LastTide.Sim;

/// <summary>
/// News of distant markets, now that the tavern's rumours are gone (Nolan, 2026-09-28: "you only know the price of ports
/// while you are in that port ... i should also be able to interact with merchant ships to ask about the ports they have
/// recently been to"). Prices are still only ever learned in port: a merchant remembers the prices at the last ports she
/// traded in, and a captain who hails her (F within <see cref="HailRange"/>) enters them in her ledger, dated the day the
/// merchant was there. And the harbour office posts which nearby ports are short of what they want, in words, no prices.
/// </summary>
public sealed partial class World
{
    /// <summary>Metres within which a merchant answers a hail, and how many ports' prices she carries.</summary>
    public const double HailRange = 150;
    public const int MerchantNewsKept = 3;
    /// <summary>At most this many "wanted" notices on an office's board.</summary>
    public const int WantedShown = 4;

    /// <summary>A merchant trades at a port: she remembers its prices today (what it makes and what it wants).</summary>
    void RecordNews(Ship ship, Port port)
    {
        if (ship.Ai is not { } ai || port.Secret) return;
        var news = new PortNews { Port = port.Id, Day = DaysSurvived };
        foreach (var g in port.Produces.Concat(port.Consumes).Distinct())
        {
            news.Goods.Add(g);
            news.Prices.Add(port.Market.Price(g));
        }
        ai.News.RemoveAll(n => n.Port == port.Id);
        ai.News.Add(news);
        if (ai.News.Count > MerchantNewsKept) ai.News.RemoveAt(0);
    }

    /// <summary>The nearest merchant in her sight and within hail that will answer (one she has struck runs instead), or null.</summary>
    public Ship? HailableMerchant
    {
        get
        {
            Ship? best = null;
            double bestD = HailRange;
            foreach (var o in Others)
            {
                if (o.Sunk || o.Ai is not { Role: Role.Merchant } || o.PlayerHostile) continue;
                double d = o.Pos.DistanceTo(Ship.Pos);
                if (d <= bestD && PlayerSees(o.Pos, o.Hull.Length)) { bestD = d; best = o; }
            }
            return best;
        }
    }

    /// <summary>
    /// She hails a merchant: its master's prices go into her ledger (dated his visits; a fresher price she holds is kept),
    /// the ports he names are marked on her chart, and the HUD says what came of it. Returns the ledger entries changed.
    /// </summary>
    public int Hail(Ship merchant)
    {
        var ai = merchant.Ai!;
        string name = NameOf(merchant);
        int learned = 0;
        var ports = new List<string>();
        foreach (var n in ai.News)
        {
            int before = learned;
            for (int i = 0; i < n.Goods.Count; i++)
            {
                var known = Player.Remembered(n.Port, n.Goods[i]);
                if (known != null && known.Day >= n.Day) continue;
                Player.Remember(n.Port, n.Goods[i], n.Prices[i], n.Day, name);
                learned++;
            }
            if (learned > before) ports.Add(Map.Ports[n.Port].Name);
            Map.Ports[n.Port].Discovered = true;   // the master names the port: it goes on her chart, as a contract's does
        }
        Notices.Enqueue(ai.News.Count == 0 ? Note("NOTICE_HAIL_NONE", name)
            : learned == 0 ? Note("NOTICE_HAIL_STALE", name)
            : Note("NOTICE_HAIL", name, string.Join(", ", ports)));
        Events.Add(new CombatEvent(CombatEventType.Ring, merchant.Pos, merchant.Id, 1));
        return learned;
    }

    /// <summary>A harbour office's notice: a port within a few days' sail short of something it wants.</summary>
    public readonly record struct WantedNotice(int Port, Good Good, bool Badly, double Days);

    /// <summary>
    /// The "wanted" notices at <paramref name="here"/>'s office: open ports within the office's reach whose stock of a good
    /// they want runs short (scarce: badly wanted), shortest first, then nearest; rare goods only while she carries some.
    /// Words only: no price is given.
    /// </summary>
    public List<WantedNotice> WantedNotices(Port here)
    {
        var list = new List<(WantedNotice N, double Ratio)>();
        foreach (var p in Map.Ports)
        {
            if (p == here || p.Secret || !IsOpen(p)) continue;
            double d = SeaDistance(here.Id, p.Id);
            if (d < OfferMinDistance || d > OfferMaxDistance) continue;
            foreach (var g in p.Consumes)
            {
                if (Goods.IsRare(g) && Player.Units(g) == 0) continue;   // a cove's rarity is news only to one who carries it
                double r = p.Market.Stock[(int)g] / p.Market.Target[(int)g];
                if (r >= 0.85) continue;   // "short" or worse (Market.StockWord)
                list.Add((new WantedNotice(p.Id, g, r < 0.5, d / PassageMetresPerDay), r));
            }
        }
        return list.OrderBy(x => x.Ratio).ThenBy(x => x.N.Days).Take(WantedShown).Select(x => x.N).ToList();
    }
}
