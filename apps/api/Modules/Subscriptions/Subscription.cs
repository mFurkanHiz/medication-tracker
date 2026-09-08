namespace MedicationTracker.Api.Modules.Subscriptions;

public sealed class Subscription
{
    private Subscription()
    {
    }

    public Guid Id { get; private set; }

    public Guid? AccountId { get; private set; }

    public Guid? HouseholdId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string ExternalReference { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}
