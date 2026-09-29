namespace LastTide.Sim;

/// <summary>
/// News of distant markets, now that the tavern's rumours are gone (Nolan, 2026-09-28: "you only know the price of ports
/// while you are in that port ... i should also be able to interact with merchant ships to ask about the ports they have
/// recently been to"). Prices are still only ever learned in port: a merchant remembers the prices at the last ports she
/// traded in, and a captain who hails her (F within <see cref="HailRange"/>) enters them in her ledger, dated the day the
/// merchant was there.
/// </summary>
public sealed partial class World
{
    /// <summary>Metres within which a merchant answers a hail, and how many ports' prices she carries.</summary>
    public const double HailRange = 100;
    public const int MerchantNewsKept = 3;

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
}
