namespace MedicationTracker.Api.Modules.Identity;

public sealed class Account
{
    private Account()
    {
    }

    public Account(Guid id, string normalizedEmail, DateTimeOffset createdAt)
    {
        Id = id;
        NormalizedEmail = normalizedEmail;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string NormalizedEmail { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}
