namespace MedicationTracker.Api.Modules.People;

/// <summary>
/// A person whose medication the household organises. Distinct from an
/// <see cref="Identity.Account"/>: a household may track medication for someone who
/// never signs in, and one account may act for several people.
/// </summary>
public sealed class Person
{
    private Person() { }

    public Person(Guid id, Guid householdId, string name, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        HouseholdId = householdId;
        Name = name.Trim();
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    /// <summary>
    /// Retires the person without deleting anything. Historical packages, plans and
    /// administrations keep pointing at them.
    /// </summary>
    public void Archive(DateTimeOffset archivedAt) => ArchivedAt = archivedAt;

    public void Restore() => ArchivedAt = null;
}
