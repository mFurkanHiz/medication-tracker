using MedicationTracker.Api.Domain.Administrations;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Modules.Administrations;

/// <summary>
/// A record of what a person actually did about a dose, as distinct from what was
/// planned.
/// </summary>
/// <remarks>
/// <para>
/// The plan version is optional: an extra or unplanned dose is a real event with no
/// scheduled slot behind it, and refusing to record it would lose health information.
/// </para>
/// <para>
/// This row is never rewritten. A change of mind about which package paid for the dose
/// is recorded against the allocation; a change of mind about what happened is a new
/// event.
/// </para>
/// </remarks>
public sealed class AdministrationEvent
{
    private AdministrationEvent() { }

    public AdministrationEvent(
        Guid id,
        Guid householdId,
        Guid personId,
        Guid medicationDefinitionId,
        Guid? treatmentPlanVersionId,
        AdministrationOutcome outcome,
        AdministrationStockSource stockSource,
        ExactQuantity? plannedQuantity,
        ExactQuantity? actualQuantity,
        DateTimeOffset? scheduledFor,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        Guid actorAccountId,
        string? note = null)
    {
        var consumes = AdministrationOutcomes.ConsumesStock(outcome);

        if (consumes && actualQuantity is null)
        {
            throw new ArgumentNullException(
                nameof(actualQuantity),
                "A dose that was taken must record how much was taken.");
        }

        if (consumes && !actualQuantity!.Value.IsPositive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actualQuantity),
                "A dose that was taken must record a positive amount.");
        }

        if (!consumes && stockSource != AdministrationStockSource.NotApplicable)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stockSource),
                "A skipped dose consumes nothing, so it has no stock source.");
        }

        if (consumes && stockSource == AdministrationStockSource.NotApplicable)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stockSource),
                "A dose that was taken must declare whether its stock was tracked.");
        }

        Id = id;
        HouseholdId = householdId;
        PersonId = personId;
        MedicationDefinitionId = medicationDefinitionId;
        TreatmentPlanVersionId = treatmentPlanVersionId;
        Outcome = outcome;
        StockSource = stockSource;
        PlannedQuantityNumerator = plannedQuantity?.Numerator;
        PlannedQuantityDenominator = plannedQuantity?.Denominator;
        ActualQuantityNumerator = actualQuantity?.Numerator;
        ActualQuantityDenominator = actualQuantity?.Denominator;
        ScheduledFor = scheduledFor;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
        ActorAccountId = actorAccountId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid PersonId { get; private set; }

    public Guid MedicationDefinitionId { get; private set; }

    /// <summary>Null for an unplanned or extra dose that belongs to no scheduled slot.</summary>
    public Guid? TreatmentPlanVersionId { get; private set; }

    public AdministrationOutcome Outcome { get; private set; }

    public AdministrationStockSource StockSource { get; private set; }

    public long? PlannedQuantityNumerator { get; private set; }

    public long? PlannedQuantityDenominator { get; private set; }

    /// <summary>What the plan asked for, kept so an under-dose stays explainable.</summary>
    public ExactQuantity? PlannedQuantity =>
        PlannedQuantityNumerator is { } numerator && PlannedQuantityDenominator is { } denominator
            ? new ExactQuantity(numerator, denominator)
            : null;

    public long? ActualQuantityNumerator { get; private set; }

    public long? ActualQuantityDenominator { get; private set; }

    /// <summary>What was really taken. Allocations must sum to exactly this amount.</summary>
    public ExactQuantity? ActualQuantity =>
        ActualQuantityNumerator is { } numerator && ActualQuantityDenominator is { } denominator
            ? new ExactQuantity(numerator, denominator)
            : null;

    public DateTimeOffset? ScheduledFor { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public Guid ActorAccountId { get; private set; }

    public string? Note { get; private set; }

    public bool ConsumesStock => AdministrationOutcomes.ConsumesStock(Outcome);

    /// <summary>True when the dose drew from stock the household tracks.</summary>
    public bool DrawsFromTrackedInventory =>
        ConsumesStock && StockSource == AdministrationStockSource.TrackedInventory;

    /// <summary>Minutes between the planned and actual time; null when unscheduled.</summary>
    public int? LatenessMinutes => AdministrationOutcomes.LatenessMinutes(ScheduledFor, OccurredAt);
}
