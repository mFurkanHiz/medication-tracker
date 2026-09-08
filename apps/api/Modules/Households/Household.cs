namespace MedicationTracker.Api.Modules.Households;

public sealed class Household
{
    private Household()
    {
    }

    public Household(Guid id, string name, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}
