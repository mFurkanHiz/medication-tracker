namespace MedicationTracker.Api.Modules.Audit;

/// <summary>
/// What kind of change an audit row describes. Replaces the previous free-text
/// <c>kind</c> column so the activity surface can group and translate reliably.
/// </summary>
public enum ChangeKind
{
    Created = 0,
    Updated = 1,
    Archived = 2,
    Restored = 3,
    Deleted = 4,

    /// <summary>A dependent relationship was deactivated as a side effect.</summary>
    CascadeDeactivated = 5,

    /// <summary>A new effective-dated version was appended.</summary>
    VersionAppended = 6,
}

/// <summary>
/// An actor-attributed record of a change to a medication definition, holding the
/// before and after snapshots as JSON.
/// </summary>
/// <remarks>
/// Snapshots are stored rather than field-level diffs so a reader can see exactly what
/// the record looked like on either side of the change, even after the schema evolves.
/// </remarks>
public sealed class MedicationDefinitionChangeEvent
{
    private MedicationDefinitionChangeEvent() { }

    public MedicationDefinitionChangeEvent(
        Guid id,
        Guid householdId,
        Guid medicationDefinitionId,
        Guid accountId,
        ChangeKind kind,
        string? previousValue,
        string? newValue,
        DateTimeOffset recordedAt)
    {
        Id = id;
        HouseholdId = householdId;
        MedicationDefinitionId = medicationDefinitionId;
        AccountId = accountId;
        Kind = kind;
        PreviousValue = previousValue;
        NewValue = newValue;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid MedicationDefinitionId { get; private set; }

    public Guid AccountId { get; private set; }

    public ChangeKind Kind { get; private set; }

    public string? PreviousValue { get; private set; }

    public string? NewValue { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}

/// <summary>
/// An actor-attributed record of a change to a treatment plan or one of its versions.
/// </summary>
public sealed class TreatmentPlanChangeEvent
{
    private TreatmentPlanChangeEvent() { }

    public TreatmentPlanChangeEvent(
        Guid id,
        Guid householdId,
        Guid treatmentPlanId,
        Guid accountId,
        ChangeKind kind,
        string? previousValue,
        string? newValue,
        DateTimeOffset recordedAt)
    {
        Id = id;
        HouseholdId = householdId;
        TreatmentPlanId = treatmentPlanId;
        AccountId = accountId;
        Kind = kind;
        PreviousValue = previousValue;
        NewValue = newValue;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid TreatmentPlanId { get; private set; }

    public Guid AccountId { get; private set; }

    public ChangeKind Kind { get; private set; }

    public string? PreviousValue { get; private set; }

    public string? NewValue { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}
