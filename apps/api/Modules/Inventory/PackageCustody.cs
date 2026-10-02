namespace MedicationTracker.Api.Modules.Inventory;

/// <summary>
/// An immutable record that a package changed hands or changed owner.
/// </summary>
/// <remarks>
/// Appended on assignment, lending and return. Never updated, so the full custody
/// history of a physical box is reconstructable.
/// </remarks>
public sealed class PackageAssignmentEvent
{
    private PackageAssignmentEvent() { }

    public PackageAssignmentEvent(
        Guid id,
        Guid householdId,
        Guid packageId,
        Guid accountId,
        Guid? fromPersonId,
        Guid? toPersonId,
        DateTimeOffset recordedAt)
    {
        Id = id;
        HouseholdId = householdId;
        PackageId = packageId;
        AccountId = accountId;
        FromPersonId = fromPersonId;
        ToPersonId = toPersonId;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid PackageId { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid? FromPersonId { get; private set; }

    public Guid? ToPersonId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}

/// <summary>
/// A whole package lent from its owner to another person in the household.
/// </summary>
/// <remarks>
/// Lending moves custody only. It changes no stock amount: the medication is still in
/// the household, in the same box, with the same contents. Any dose the borrower takes
/// is an ordinary package consumption against that box.
/// </remarks>
public sealed class PackageLoan
{
    private PackageLoan() { }

    public PackageLoan(
        Guid id,
        Guid householdId,
        Guid packageId,
        Guid ownerPersonId,
        Guid borrowerPersonId,
        Guid lentByAccountId,
        DateTimeOffset lentAt)
    {
        if (ownerPersonId == borrowerPersonId)
        {
            throw new ArgumentException("A package cannot be lent to its own owner.", nameof(borrowerPersonId));
        }

        Id = id;
        HouseholdId = householdId;
        PackageId = packageId;
        OwnerPersonId = ownerPersonId;
        BorrowerPersonId = borrowerPersonId;
        LentByAccountId = lentByAccountId;
        LentAt = lentAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid PackageId { get; private set; }

    public Guid OwnerPersonId { get; private set; }

    public Guid BorrowerPersonId { get; private set; }

    public Guid LentByAccountId { get; private set; }

    public DateTimeOffset LentAt { get; private set; }

    public Guid? ReturnedByAccountId { get; private set; }

    public DateTimeOffset? ReturnedAt { get; private set; }

    public bool IsOutstanding => ReturnedAt is null;

    public void Return(Guid accountId, DateTimeOffset returnedAt)
    {
        if (ReturnedAt is not null)
        {
            throw new InvalidOperationException("The loan was already returned.");
        }

        ReturnedByAccountId = accountId;
        ReturnedAt = returnedAt;
    }
}
