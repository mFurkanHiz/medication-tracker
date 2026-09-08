namespace MedicationTracker.Api.Modules.Subscriptions;

public sealed class Entitlement
{
    private Entitlement()
    {
    }

    public Guid Id { get; private set; }

    public Guid SubscriptionId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset? ValidTo { get; private set; }
}
