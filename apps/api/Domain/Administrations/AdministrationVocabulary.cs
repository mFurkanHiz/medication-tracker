namespace MedicationTracker.Api.Domain.Administrations;

/// <summary>
/// What actually happened at a dose.
/// </summary>
/// <remarks>
/// There is deliberately no <c>Late</c> member. Lateness is the difference between the
/// planned and the actual time, so storing it as a status would create a second,
/// drifting source of truth and would force the product to pick a threshold that is
/// really a clinical judgement. Callers derive it from the timestamps.
///
/// There is also no <c>Corrected</c> member: a correction changes which package paid
/// for a dose, not what the person did. Corrections are recorded against the
/// allocation.
/// </remarks>
public enum AdministrationOutcome
{
    /// <summary>Taken as planned.</summary>
    Taken = 0,

    /// <summary>Not taken. Consumes no stock.</summary>
    Skipped = 1,

    /// <summary>Taken, but less than the planned amount.</summary>
    PartialDose = 2,

    /// <summary>Taken in addition to the plan, or more than planned.</summary>
    ExtraDose = 3,
}

/// <summary>
/// Where the medication for a recorded dose came from.
/// </summary>
public enum AdministrationStockSource
{
    /// <summary>Drawn from packages or loose stock the household tracks. Creates allocations.</summary>
    TrackedInventory = 0,

    /// <summary>
    /// Really taken, but from stock this household does not track — a dose from a
    /// friend, a strip carried in a bag, a hospital dose.
    /// </summary>
    /// <remarks>
    /// The event is kept with its real amount and time, no ledger entry is written, and
    /// no package is driven negative. The medication is flagged as needing a count.
    /// This exists so the product never has to choose between losing a real health
    /// record and corrupting its inventory.
    /// </remarks>
    UntrackedExternal = 1,

    /// <summary>Nothing was consumed, because the dose was skipped.</summary>
    NotApplicable = 2,
}

public static class AdministrationOutcomes
{
    /// <summary>Outcomes that consume stock when the source is tracked inventory.</summary>
    public static bool ConsumesStock(AdministrationOutcome outcome) => outcome is
        AdministrationOutcome.Taken or
        AdministrationOutcome.PartialDose or
        AdministrationOutcome.ExtraDose;

    /// <summary>
    /// How late a dose was, or null when it was not scheduled or not taken. Positive
    /// means after the planned time; negative means early. Purely descriptive.
    /// </summary>
    public static int? LatenessMinutes(DateTimeOffset? scheduledFor, DateTimeOffset occurredAt) =>
        scheduledFor is { } planned
            ? (int)Math.Round((occurredAt - planned).TotalMinutes, MidpointRounding.AwayFromZero)
            : null;
}
