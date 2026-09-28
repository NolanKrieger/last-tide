using System.Globalization;

namespace LastTide.Sim;

/// <summary>
/// The harbour office (GDD §6, §13): income that needs no capital. Delivery contracts (freight, dispatches and, at the
/// havens and coves, contraband), a survey fee for the first visit to a port, and Crown bounties on pirates sunk. Customs
/// search for contraband at Crown and Free Trader docks, and Crown patrols that come alongside search her at sea.
/// </summary>
public sealed partial class World
{
    public const int MaxContracts = 3;
    /// <summary>Sea distance an office will send her, in metres: not the next cove over, not across the whole chart.</summary>
    public const double OfferMinDistance = 350, OfferMaxDistance = 2800;
    /// <summary>Metres of sea a lean sloop makes good in a day, tacks and all: the yardstick for deadlines.</summary>
    public const double PassageMetresPerDay = 700;
    public const double ContractDoneRep = 2, ContractFailRep = 6;
    /// <summary>Chance of a customs search on docking with contraband aboard: Crown ports, Free Trader ports.</summary>
    public const double CustomsCrown = 0.25, CustomsTraders = 0.1;
    /// <summary>A Crown patrol within this range hails and searches her, once per ship, at this chance.</summary>
    public const double SearchRange = 120, PatrolSearch = 0.35;
    /// <summary>Contraband found: the crates are seized, half their fee is fined and the searching faction marks her.</summary>
    public const double CaughtFine = 0.5, CaughtRep = 15;
    public const int SurveyBase = 15, SurveyPerKm = 12, SurveyCap = 75;
    public const int BountyBase = 50;
    public const double BountyHullShare = 0.03;

    /// <summary>Danger money: office work pays 15% more per +1.0 of Threat, as the sea gets meaner.</summary>
    public double DangerMoney => 1 + 0.15 * (ThreatNow - 1);

    /// <summary>What was paid out or taken as she came alongside this time (the port panel lists it). Not saved.</summary>
    public List<Receipt> DockReceipts { get; } = new();
    /// <summary>Crown patrols that have already searched her while she carries contraband (one search each).</summary>
    readonly HashSet<int> searchedBy = new();
    public IReadOnlyCollection<int> SearchedBy => searchedBy;
    readonly Dictionary<int, double[]> portDistances = new();

    /// <summary>A notice with arguments for the HUD: "KEY|arg|arg" (the HUD formats the key's line with them).</summary>
    static string Note(string key, params object[] args) =>
        args.Length == 0 ? key : key + "|" + string.Join("|", args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)));

    /// <summary>Sea distance in metres between two harbours (breadth-first over the nav grid, cached per origin); −1 if none.</summary>
    public double SeaDistance(int from, int to)
    {
        if (!portDistances.TryGetValue(from, out var row))
        {
            var field = Map.Nav.Distances(Map.Ports[from].Harbor);
            row = Map.Ports.Select(p => Map.Nav.DistanceAt(field, p.Harbor)).ToArray();
            portDistances[from] = row;
        }
        return row[to];
    }

    // ---- The board ----

    /// <summary>The key of one board slot: a port's offers are drawn per day, slot by slot.</summary>
    public static long OfferKey(int port, int day, int slot) => ((long)day << 24) | ((long)port << 4) | (long)slot;

    /// <summary>Two slots on the board, three at a large port.</summary>
    public static int BoardSize(Port port) => !port.Secret && port.Size == 2 ? 3 : 2;

    /// <summary>
    /// Today's board at a port. Each slot is drawn on its own from (seed, port, day, slot), so signing one offer or
    /// buying a new hull never reshuffles the others. Null where a slot is empty or already signed.
    /// </summary>
    public Contract?[] ContractOffers(Port port)
    {
        var offers = new Contract?[BoardSize(port)];
        for (int i = 0; i < offers.Length; i++)
        {
            long key = OfferKey(port.Id, Day, i);
            if (!Player.TakenOffers.Contains(key)) offers[i] = DrawOffer(port, i, key);
        }
        return offers;
    }

    Contract? DrawOffer(Port port, int slot, long key)
    {
        var rng = new Rng((ulong)key * 0x9E3779B97F4A7C15UL + (ulong)(uint)Seed * 0xBF58476D1CE4E5B9UL + 29);
        // Colonies and free ports ship freight and dispatches; a haven ships freight to its own kind and smuggles; a cove only smuggles.
        ContractKind kind = port.Secret ? ContractKind.Contraband
            : port.Faction == Faction.Brethren ? (slot == 0 ? ContractKind.Freight : ContractKind.Contraband)
            : rng.NextDouble() < 0.3 ? ContractKind.Dispatch : ContractKind.Freight;
        if (port.Secret && slot == 1 && rng.NextDouble() < 0.5) return null;
        bool bigHull = !RegionDef.MangroveHulls.Contains(Ship.Hull.Id);
        var candidates = new List<(Port Port, double Dist)>();
        foreach (var p in Map.Ports)
        {
            if (p.Id == port.Id || p.Secret || !IsOpen(p) || bigHull && p.Region == RegionType.Mangrove) continue;
            bool wanted = kind == ContractKind.Contraband ? p.Faction != Faction.Brethren
                : port.Faction == Faction.Brethren ? p.Faction != Faction.Crown : p.Faction != Faction.Brethren;
            if (!wanted) continue;
            double d = SeaDistance(port.Id, p.Id);
            if (d >= OfferMinDistance && d <= OfferMaxDistance) candidates.Add((p, d));
        }
        if (candidates.Count == 0) return null;
        var (dest, dist) = candidates[rng.Next(candidates.Count)];
        var def = ContractDef.Of(kind);
        int slots = def.SlotsCap == 0 ? 0 : Math.Clamp((int)Math.Round(Ship.CargoCapacity * rng.Range(def.HoldMin, def.HoldMax)), def.SlotsFloor, def.SlotsCap);
        double each = def.PayBase + def.PayPerKm * dist / 1000;
        int pay = (int)Math.Round((slots > 0 ? slots * each : each) * DangerMoney);
        double days = def.DaysBase + def.DaysPerPassage * dist / PassageMetresPerDay;
        return new Contract
        {
            Kind = kind, From = port.Id, To = dest.Id, Slots = slots, Pay = pay, Advance = (int)Math.Round(pay * def.Advance),
            Deadline = DaysSurvived + days, Offer = key,
        };
    }

    // ---- Signing, delivering, failing ----

    PortResult SignContract(int slot)
    {
        if (Docked == null) return PortResult.NotDocked;
        var offers = ContractOffers(Docked);
        if (slot < 0 || slot >= offers.Length || offers[slot] is not { } c) return PortResult.Nothing;
        if (Player.Contracts.Count >= MaxContracts) return PortResult.Busy;
        if (Player.SlotsUsed + c.Slots > Ship.CargoCapacity + 1e-9) return PortResult.NoRoom;
        Player.Contracts.Add(c);
        Player.TakenOffers.Add(c.Offer);
        Player.Gold += c.Advance;
        Map.Ports[c.To].Discovered = true;   // the clerk marks the port on her chart
        return PortResult.Ok;
    }

    PortResult AbandonContract(int index)
    {
        if (index < 0 || index >= Player.Contracts.Count) return PortResult.Nothing;
        Forfeit(Player.Contracts[index]);
        Player.Contracts.RemoveAt(index);
        return PortResult.Ok;
    }

    /// <summary>Who hired her: the issuing port's faction (a cove's smugglers are Brethren).</summary>
    public Faction Issuer(Contract c) => Map.Ports[c.From] is { Secret: true } ? Faction.Brethren : Map.Ports[c.From].Faction;

    /// <summary>A contract given up or missed: the advance goes back (as far as the purse allows) and the issuer remembers.</summary>
    void Forfeit(Contract c)
    {
        Player.Gold -= Math.Min(Player.Gold, c.Advance);
        var f = (int)Issuer(c);
        Player.Reputation[f] = Math.Max(-100, Player.Reputation[f] - ContractFailRep);
    }

    public bool CarriesContraband
    {
        get
        {
            foreach (var c in Player.Contracts) if (c.Kind == ContractKind.Contraband) return true;
            return false;
        }
    }

    /// <summary>Hidden hold (black market): searchers find contraband half as often.</summary>
    double SearchMult => HasUnique("hidden_hold") ? 0.5 : 1;

    /// <summary>Contraband found: seized, half its fee fined (what the purse holds), and the searching faction's standing falls.</summary>
    int Caught(Faction by)
    {
        int fee = 0;
        foreach (var c in Player.Contracts) if (c.Kind == ContractKind.Contraband) fee += c.Pay;
        int fine = Math.Min(Player.Gold, (int)Math.Round(fee * CaughtFine));
        Player.Gold -= fine;
        Player.Contracts.RemoveAll(c => c.Kind == ContractKind.Contraband);
        Player.Reputation[(int)by] = Math.Max(-100, Player.Reputation[(int)by] - CaughtRep);
        return fine;
    }

    /// <summary>First visit to a port: the harbour master buys her soundings of the approaches. Farther from home pays more.</summary>
    public int SurveyFee(Port port) => port.Secret || port.Id == Map.StartPortId ? 0
        : Math.Min(SurveyCap, (int)Math.Round(SurveyBase + SurveyPerKm * port.Harbor.DistanceTo(Map.StartPort.Harbor) / 1000));

    /// <summary>What the Crown pays for sinking a pirate: a base plus a share of her hull, rising with the Threat.</summary>
    public int Bounty(Ship pirate) => (int)Math.Round((BountyBase + BountyHullShare * pirate.Hull.Cost) * Threat.EnemyScale(ThreatNow));

    /// <summary>Coming alongside: customs, deliveries, bounties, the survey fee. Called from the dock, before the ledger opens.</summary>
    void Arrive(Port port, bool firstVisit)
    {
        DockReceipts.Clear();
        if (!port.Secret && port.Faction != Faction.Brethren && CarriesContraband)
        {
            double chance = (port.Faction == Faction.Crown ? CustomsCrown : CustomsTraders) * SearchMult;
            if (Rng.NextDouble() < chance) DockReceipts.Add(new Receipt("RECEIPT_CAUGHT", -Caught(port.Faction), port.Id));
        }
        for (int i = Player.Contracts.Count - 1; i >= 0; i--)
        {
            var c = Player.Contracts[i];
            if (c.To != port.Id) continue;
            Player.Contracts.RemoveAt(i);
            int due = c.Pay - c.Advance;
            Player.Gold += due;
            Stats.GoldEarned += c.Pay;
            var f = (int)Issuer(c);
            Player.Reputation[f] = Math.Min(100, Player.Reputation[f] + ContractDoneRep);
            DockReceipts.Add(new Receipt(c.Kind == ContractKind.Contraband ? "RECEIPT_SMUGGLED" : "RECEIPT_DELIVERED", due, c.From));
        }
        if (Player.BountyOwed > 0 && !port.Secret && port.Faction == Faction.Crown)
        {
            Player.Gold += Player.BountyOwed;
            Stats.GoldEarned += Player.BountyOwed;
            DockReceipts.Add(new Receipt("RECEIPT_BOUNTY", Player.BountyOwed, Player.BountyShips));
            Player.BountyOwed = 0;
            Player.BountyShips = 0;
        }
        if (firstVisit && SurveyFee(port) is > 0 and var fee)
        {
            Player.Gold += fee;
            Stats.GoldEarned += fee;
            DockReceipts.Add(new Receipt("RECEIPT_SURVEY", fee));
        }
    }

    /// <summary>A pirate sunk by her guns or her bow: the Crown owes a bounty, claimed at any Crown port.</summary>
    void EarnBounty(Ship pirate)
    {
        int b = Bounty(pirate);
        Player.BountyOwed += b;
        Player.BountyShips++;
        Notices.Enqueue(Note("NOTICE_BOUNTY", b));
    }

    /// <summary>At sea: deadlines, and Crown patrols that come alongside while contraband is aboard.</summary>
    void OfficeTick()
    {
        for (int i = Player.Contracts.Count - 1; i >= 0; i--)
        {
            var c = Player.Contracts[i];
            if (DaysSurvived <= c.Deadline) continue;
            Player.Contracts.RemoveAt(i);
            Forfeit(c);
            Notices.Enqueue(Note("NOTICE_CONTRACT_LATE", Map.Ports[c.To].Name));
        }
        if (!CarriesContraband)
        {
            if (searchedBy.Count > 0) searchedBy.Clear();
            return;
        }
        foreach (var o in Others)
        {
            if (o.Sunk || o.Faction != Faction.Crown || o.Ai?.Role != Role.Patrol || Hostile(o, Ship)) continue;
            if (o.Pos.DistanceTo(Ship.Pos) > SearchRange || !searchedBy.Add(o.Id)) continue;
            if (Rng.NextDouble() < PatrolSearch * SearchMult)
            {
                Notices.Enqueue(Note("NOTICE_SEARCH_CAUGHT", Caught(Faction.Crown)));
                searchedBy.Clear();
                return;
            }
            Notices.Enqueue("NOTICE_SEARCH_CLEAR");
        }
    }
}
