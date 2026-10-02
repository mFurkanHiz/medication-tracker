namespace MedicationTracker.Api.Modules.Sync;

/// <summary>
/// Proof that one offline command has already been applied, keyed by the client's
/// idempotency key within a household.
/// </summary>
/// <remarks>
/// A mobile client that loses its network after the server committed, and retries on
/// reconnect, must not create a second record. The receipt lets the replay return the
/// original result instead.
/// </remarks>
public sealed class SyncCommandReceipt
{
    private SyncCommandReceipt() { }

    public SyncCommandReceipt(
        Guid id,
        Guid householdId,
        Guid accountId,
        string idempotencyKey,
        string kind,
        Guid resultEntityId,
        DateTimeOffset processedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        Id = id;
        HouseholdId = householdId;
        AccountId = accountId;
        IdempotencyKey = idempotencyKey;
        Kind = kind;
        ResultEntityId = resultEntityId;
        ProcessedAt = processedAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid AccountId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string Kind { get; private set; } = string.Empty;

    public Guid ResultEntityId { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }
}

/// <summary>
/// The same guarantee for a recorded dose, which is the command that must never be
/// applied twice: a duplicate would consume stock a second time.
/// </summary>
public sealed class ProcessedAdministrationCommand
{
    private ProcessedAdministrationCommand() { }

    public ProcessedAdministrationCommand(
        Guid id,
        Guid householdId,
        Guid accountId,
        string idempotencyKey,
        Guid administrationEventId,
        DateTimeOffset processedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        Id = id;
        HouseholdId = householdId;
        AccountId = accountId;
        IdempotencyKey = idempotencyKey;
        AdministrationEventId = administrationEventId;
        ProcessedAt = processedAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid AccountId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public Guid AdministrationEventId { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }
}
