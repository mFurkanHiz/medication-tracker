namespace MedicationTracker.Api.Modules.Households;

public sealed class HouseholdMembership
{
    private HouseholdMembership()
    {
    }

    public HouseholdMembership(
        Guid id,
        Guid householdId,
        Guid accountId,
        string role,
        DateTimeOffset validFrom)
    {
        Id = id;
        HouseholdId = householdId;
        AccountId = accountId;
        Role = role;
        ValidFrom = validFrom;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid AccountId { get; private set; }

    public string Role { get; private set; } = string.Empty;

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset? ValidTo { get; private set; }
}
