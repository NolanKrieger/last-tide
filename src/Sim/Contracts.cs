namespace LastTide.Sim;

public enum ContractKind { Freight, Dispatch, Contraband }

/// <summary>
/// One row per kind of harbour-office work (GDD §6, harbour office): how much of the hold it takes, how it pays and how
/// long it allows. Pay is (base + per km of sea) — per crate for freight and contraband, flat for a dispatch — times
/// <see cref="World.DangerMoney"/>; the deadline is days of sea time, from an estimate of the passage.
/// </summary>
public sealed record ContractDef(ContractKind Kind, string Key, double HoldMin, double HoldMax, int SlotsFloor, int SlotsCap,
    double PayBase, double PayPerKm, double Advance, double DaysBase, double DaysPerPassage)
{
    public static readonly ContractDef[] All =
    {
        // Sealed crates for a merchant house: a fifth to near half the hold, a fifth of the fee paid at signing.
        new(ContractKind.Freight, "freight", 0.2, 0.45, 3, 30, 3, 10, 0.2, 1.0, 2.0),
        // Letters under seal: no hold space, a tight deadline (the cutter's trade).
        new(ContractKind.Dispatch, "dispatch", 0, 0, 0, 0, 20, 30, 0.2, 0.5, 1.3),
        // Contraband from the havens and coves: small, dear, nothing in advance, and customs may search her for it.
        new(ContractKind.Contraband, "contraband", 0.1, 0.25, 2, 12, 8, 24, 0, 1.0, 2.0),
    };

    public static ContractDef Of(ContractKind k) => All[(int)k];
}

/// <summary>Work signed for at a harbour office, carried to <see cref="To"/> by <see cref="Deadline"/> (days survived).</summary>
public sealed class Contract
{
    public ContractKind Kind { get; set; }
    public int From { get; set; }
    public int To { get; set; }
    /// <summary>Hold slots the crates take (0 for a dispatch).</summary>
    public int Slots { get; set; }
    /// <summary>The whole fee; <see cref="Advance"/> of it was paid at signing.</summary>
    public int Pay { get; set; }
    public int Advance { get; set; }
    public double Deadline { get; set; }
    /// <summary>The office's board slot it came from (<see cref="World.OfferKey"/>), so a signed offer leaves the board.</summary>
    public long Offer { get; set; }

    public Contract Clone() => (Contract)MemberwiseClone();
}

/// <summary>
/// What was paid out (or taken, negative) as she came alongside, for the port panel: survey fee, deliveries, bounties,
/// customs. <see cref="Arg"/> is the issuing port of a delivery, the searching port, or the pirates a bounty is for.
/// </summary>
public readonly record struct Receipt(string Key, int Gold, int Arg = -1);
