namespace MedicationTracker.Api.Modules.Care;

public sealed class Person
{
    private Person() { }
    public Person(Guid id, Guid householdId, string name, DateTimeOffset createdAt) =>
        (Id, HouseholdId, Name, CreatedAt) = (id, householdId, name, createdAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class Medication
{
    private Medication() { }
    public Medication(Guid id, Guid householdId, Guid? personId, string name, string form, DateTimeOffset createdAt,
        string? strength = null, string? activeIngredient = null, string? notes = null,
        string? category = null, string[]? tags = null, bool isActive = true) =>
        (Id, HouseholdId, PersonId, Name, Form, CreatedAt, Strength, ActiveIngredient, Notes, Category, Tags, IsActive) =
        (id, householdId, personId, name, form, createdAt, strength?.Trim(), activeIngredient?.Trim(), notes?.Trim(),
            category?.Trim(), tags ?? [], isActive);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    // Kept nullable for backwards compatibility. Package assignment and regimens are
    // the authoritative person relationships for new records.
    public Guid? PersonId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Form { get; private set; } = string.Empty;
    public string? Strength { get; private set; }
    public string? ActiveIngredient { get; private set; }
    public string? Notes { get; private set; }
    public string? Category { get; private set; }
    public string[] Tags { get; private set; } = [];
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void UpdateMetadata(string? category, string[] tags, bool isActive) =>
        (Category, Tags, IsActive) = (category?.Trim(), tags, isActive);

    public void UpdateDetails(Guid? personId, string name, string form, string? strength,
        string? activeIngredient, string? notes, string? category, string[] tags, bool isActive) =>
        (PersonId, Name, Form, Strength, ActiveIngredient, Notes, Category, Tags, IsActive) =
        (personId, name.Trim(), form, strength?.Trim(), activeIngredient?.Trim(), notes?.Trim(),
            category?.Trim(), tags, isActive);

    public void Delete(DateTimeOffset deletedAt) =>
        (IsActive, DeletedAt) = (false, deletedAt);
}

public sealed class InventoryItem
{
    private InventoryItem() { }
    public InventoryItem(Guid id, Guid householdId, Guid medicationId, DateTimeOffset createdAt) =>
        (Id, HouseholdId, MedicationId, CreatedAt) = (id, householdId, medicationId, createdAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid MedicationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class InventoryLedgerEntry
{
    private InventoryLedgerEntry() { }
    public InventoryLedgerEntry(Guid id, Guid householdId, Guid inventoryItemId, Guid? administrationEventId,
        long quantityNumerator, long quantityDenominator, string reason, DateTimeOffset occurredAt, DateTimeOffset recordedAt,
        Guid? packageId = null) =>
        (Id, HouseholdId, InventoryItemId, AdministrationEventId, QuantityNumerator, QuantityDenominator, Reason, OccurredAt, RecordedAt, PackageId) =
        (id, householdId, inventoryItemId, administrationEventId, quantityNumerator, quantityDenominator, reason, occurredAt, recordedAt, packageId);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid InventoryItemId { get; private set; }
    public Guid? AdministrationEventId { get; private set; }
    public Guid? PackageId { get; private set; }
    public long QuantityNumerator { get; private set; }
    public long QuantityDenominator { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}

public sealed class InventoryPackage
{
    private InventoryPackage() { }
    public InventoryPackage(Guid id, Guid householdId, Guid inventoryItemId, Guid? personId,
        long capacityNumerator, long capacityDenominator, DateTimeOffset createdAt) =>
        (Id, HouseholdId, InventoryItemId, OwnerPersonId, PersonId, CapacityNumerator, CapacityDenominator, CreatedAt) =
        (id, householdId, inventoryItemId, personId, personId, capacityNumerator, capacityDenominator, createdAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid InventoryItemId { get; private set; }
    public Guid? OwnerPersonId { get; private set; }
    public Guid? PersonId { get; private set; }
    public long CapacityNumerator { get; private set; }
    public long CapacityDenominator { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public void AssignTo(Guid? personId) => PersonId = personId;
    public void AssignOwner(Guid? personId) => (OwnerPersonId, PersonId) = (personId, personId);
}

public sealed class InventoryPackageAssignmentEvent
{
    private InventoryPackageAssignmentEvent() { }
    public InventoryPackageAssignmentEvent(Guid id, Guid householdId, Guid packageId, Guid accountId,
        Guid? fromPersonId, Guid? toPersonId, DateTimeOffset recordedAt) =>
        (Id, HouseholdId, PackageId, AccountId, FromPersonId, ToPersonId, RecordedAt) =
        (id, householdId, packageId, accountId, fromPersonId, toPersonId, recordedAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid PackageId { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? FromPersonId { get; private set; }
    public Guid? ToPersonId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}

public sealed class InventoryLoan
{
    private InventoryLoan() { }
    public InventoryLoan(Guid id, Guid householdId, Guid packageId, Guid ownerPersonId, Guid borrowerPersonId,
        Guid lentByAccountId, DateTimeOffset lentAt) =>
        (Id, HouseholdId, PackageId, OwnerPersonId, BorrowerPersonId, LentByAccountId, LentAt) =
        (id, householdId, packageId, ownerPersonId, borrowerPersonId, lentByAccountId, lentAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid PackageId { get; private set; }
    public Guid OwnerPersonId { get; private set; }
    public Guid BorrowerPersonId { get; private set; }
    public Guid LentByAccountId { get; private set; }
    public DateTimeOffset LentAt { get; private set; }
    public Guid? ReturnedByAccountId { get; private set; }
    public DateTimeOffset? ReturnedAt { get; private set; }
    public void Return(Guid accountId, DateTimeOffset returnedAt) =>
        (ReturnedByAccountId, ReturnedAt) = (accountId, returnedAt);
}

public sealed class MedicationChangeEvent
{
    private MedicationChangeEvent() { }
    public MedicationChangeEvent(Guid id, Guid householdId, Guid medicationId, Guid accountId, string kind,
        string? previousValue, string? newValue, DateTimeOffset recordedAt) =>
        (Id, HouseholdId, MedicationId, AccountId, Kind, PreviousValue, NewValue, RecordedAt) =
        (id, householdId, medicationId, accountId, kind, previousValue, newValue, recordedAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid MedicationId { get; private set; }
    public Guid AccountId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string? PreviousValue { get; private set; }
    public string? NewValue { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}

public sealed class Regimen
{
    private Regimen() { }
    public Regimen(Guid id, Guid householdId, Guid personId, Guid medicationId, DateTimeOffset createdAt) =>
        (Id, HouseholdId, PersonId, MedicationId, CreatedAt) = (id, householdId, personId, medicationId, createdAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid MedicationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void UpdateAssignment(Guid personId, Guid medicationId) =>
        (PersonId, MedicationId) = (personId, medicationId);

    public void Delete(DateTimeOffset deletedAt) => DeletedAt = deletedAt;
}

public sealed class RegimenChangeEvent
{
    private RegimenChangeEvent() { }
    public RegimenChangeEvent(Guid id, Guid householdId, Guid regimenId, Guid accountId, string kind,
        string? previousValue, string? newValue, DateTimeOffset recordedAt) =>
        (Id, HouseholdId, RegimenId, AccountId, Kind, PreviousValue, NewValue, RecordedAt) =
        (id, householdId, regimenId, accountId, kind, previousValue, newValue, recordedAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid RegimenId { get; private set; }
    public Guid AccountId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string? PreviousValue { get; private set; }
    public string? NewValue { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}

public sealed class RegimenVersion
{
    private RegimenVersion() { }
    public RegimenVersion(Guid id, Guid regimenId, DateOnly? validFrom, DateOnly? validTo, long doseNumerator,
        long doseDenominator, TimeOnly? localTime, string timeZoneId, DateTimeOffset createdAt,
        string scheduleType = "scheduled", string? dayPeriod = null, string? mealRelation = null,
        int? minimumIntervalMinutes = null) =>
        (Id, RegimenId, ValidFrom, ValidTo, DoseNumerator, DoseDenominator, LocalTime, TimeZoneId, CreatedAt,
            ScheduleType, DayPeriod, MealRelation, MinimumIntervalMinutes) =
        (id, regimenId, validFrom, validTo, doseNumerator, doseDenominator, localTime, timeZoneId, createdAt,
            scheduleType, dayPeriod, mealRelation, minimumIntervalMinutes);
    public Guid Id { get; private set; }
    public Guid RegimenId { get; private set; }
    public DateOnly? ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }
    public long DoseNumerator { get; private set; }
    public long DoseDenominator { get; private set; }
    public TimeOnly? LocalTime { get; private set; }
    public string TimeZoneId { get; private set; } = string.Empty;
    public string ScheduleType { get; private set; } = "scheduled";
    public string? DayPeriod { get; private set; }
    public string? MealRelation { get; private set; }
    public int? MinimumIntervalMinutes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class AdministrationEvent
{
    private AdministrationEvent() { }
    public AdministrationEvent(Guid id, Guid householdId, Guid personId, Guid medicationId, Guid regimenVersionId,
        string outcome, DateTimeOffset scheduledFor, DateTimeOffset occurredAt, DateTimeOffset recordedAt) =>
        (Id, HouseholdId, PersonId, MedicationId, RegimenVersionId, Outcome, ScheduledFor, OccurredAt, RecordedAt) =
        (id, householdId, personId, medicationId, regimenVersionId, outcome, scheduledFor, occurredAt, recordedAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid MedicationId { get; private set; }
    public Guid RegimenVersionId { get; private set; }
    public string Outcome { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledFor { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}

public sealed class SyncCommandReceipt
{
    private SyncCommandReceipt() { }
    public SyncCommandReceipt(Guid id, Guid householdId, Guid accountId, string idempotencyKey, string kind,
        Guid resultEntityId, DateTimeOffset processedAt) =>
        (Id, HouseholdId, AccountId, IdempotencyKey, Kind, ResultEntityId, ProcessedAt) =
        (id, householdId, accountId, idempotencyKey, kind, resultEntityId, processedAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid AccountId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public Guid ResultEntityId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
}

public sealed class ProcessedAdministrationCommand
{
    private ProcessedAdministrationCommand() { }
    public ProcessedAdministrationCommand(Guid id, Guid householdId, Guid accountId, string idempotencyKey,
        Guid administrationEventId, DateTimeOffset processedAt) =>
        (Id, HouseholdId, AccountId, IdempotencyKey, AdministrationEventId, ProcessedAt) =
        (id, householdId, accountId, idempotencyKey, administrationEventId, processedAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid AccountId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid AdministrationEventId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
}
