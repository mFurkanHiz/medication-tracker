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
    public Medication(Guid id, Guid householdId, Guid personId, string name, string form, DateTimeOffset createdAt) =>
        (Id, HouseholdId, PersonId, Name, Form, CreatedAt) = (id, householdId, personId, name, form, createdAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid PersonId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Form { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
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
        long quantityNumerator, long quantityDenominator, string reason, DateTimeOffset occurredAt, DateTimeOffset recordedAt) =>
        (Id, HouseholdId, InventoryItemId, AdministrationEventId, QuantityNumerator, QuantityDenominator, Reason, OccurredAt, RecordedAt) =
        (id, householdId, inventoryItemId, administrationEventId, quantityNumerator, quantityDenominator, reason, occurredAt, recordedAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid InventoryItemId { get; private set; }
    public Guid? AdministrationEventId { get; private set; }
    public long QuantityNumerator { get; private set; }
    public long QuantityDenominator { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
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
}

public sealed class RegimenVersion
{
    private RegimenVersion() { }
    public RegimenVersion(Guid id, Guid regimenId, DateOnly validFrom, DateOnly? validTo, long doseNumerator,
        long doseDenominator, TimeOnly localTime, string timeZoneId, DateTimeOffset createdAt) =>
        (Id, RegimenId, ValidFrom, ValidTo, DoseNumerator, DoseDenominator, LocalTime, TimeZoneId, CreatedAt) =
        (id, regimenId, validFrom, validTo, doseNumerator, doseDenominator, localTime, timeZoneId, createdAt);
    public Guid Id { get; private set; }
    public Guid RegimenId { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }
    public long DoseNumerator { get; private set; }
    public long DoseDenominator { get; private set; }
    public TimeOnly LocalTime { get; private set; }
    public string TimeZoneId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class AdministrationEvent
{
    private AdministrationEvent() { }
    public AdministrationEvent(Guid id, Guid householdId, Guid personId, Guid medicationId, Guid regimenVersionId,
        DateTimeOffset scheduledFor, DateTimeOffset takenAt, DateTimeOffset recordedAt) =>
        (Id, HouseholdId, PersonId, MedicationId, RegimenVersionId, ScheduledFor, TakenAt, RecordedAt) =
        (id, householdId, personId, medicationId, regimenVersionId, scheduledFor, takenAt, recordedAt);
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid MedicationId { get; private set; }
    public Guid RegimenVersionId { get; private set; }
    public DateTimeOffset ScheduledFor { get; private set; }
    public DateTimeOffset TakenAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
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
