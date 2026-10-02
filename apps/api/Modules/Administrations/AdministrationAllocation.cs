using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Modules.Administrations;

/// <summary>
/// Which physical package paid for a dose, and how much of it.
/// </summary>
/// <remarks>
/// <para>
/// This is the first-class answer to "where did that tablet come from". The previous
/// model left it implicit in ledger rows that happened to share an administration id,
/// which meant the question could not be displayed, queried, or corrected.
/// </para>
/// <para>
/// One dose may have several allocations when it spans packages. The active allocations
/// of a tracked dose always sum to exactly the amount administered — ADR 0014
/// invariant 6.
/// </para>
/// </remarks>
public sealed class AdministrationAllocation
{
    private AdministrationAllocation() { }

    public AdministrationAllocation(
        Guid id,
        Guid householdId,
        Guid administrationEventId,
        Guid? packageId,
        ExactQuantity quantity,
        Guid ledgerEntryId,
        Guid correlationId,
        DateTimeOffset createdAt)
    {
        if (!quantity.IsPositive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "An allocation records a positive amount drawn from one source.");
        }

        Id = id;
        HouseholdId = householdId;
        AdministrationEventId = administrationEventId;
        PackageId = packageId;
        QuantityNumerator = quantity.Numerator;
        QuantityDenominator = quantity.Denominator;
        LedgerEntryId = ledgerEntryId;
        CorrelationId = correlationId;
        CreatedAt = createdAt;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid AdministrationEventId { get; private set; }

    /// <summary>The package drawn from, or null for package-independent stock.</summary>
    public Guid? PackageId { get; private set; }

    public long QuantityNumerator { get; private set; }

    public long QuantityDenominator { get; private set; }

    /// <summary>Always positive: the amount taken out of this one source.</summary>
    public ExactQuantity Quantity => new(QuantityNumerator, QuantityDenominator);

    /// <summary>The consuming ledger entry this allocation accounts for.</summary>
    public Guid LedgerEntryId { get; private set; }

    /// <summary>Groups the allocations written together for one dose or one correction.</summary>
    public Guid CorrelationId { get; private set; }

    /// <summary>
    /// False once a correction moved this draw to a different source. The row stays so
    /// the previous answer remains visible in history.
    /// </summary>
    public bool IsActive { get; private set; }

    public Guid? SupersededByAllocationId { get; private set; }

    public DateTimeOffset? SupersededAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Retires this allocation in favour of a corrected one. Never deletes it: the
    /// history of what the system previously believed is part of the audit trail.
    /// </summary>
    public void SupersedeBy(Guid replacementAllocationId, DateTimeOffset supersededAt)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("The allocation was already superseded.");
        }

        IsActive = false;
        SupersededByAllocationId = replacementAllocationId;
        SupersededAt = supersededAt;
    }

    /// <summary>Projects the allocation into the shape the correction planner consumes.</summary>
    public ActiveAllocation ToDomain() => new(Id, PackageId, Quantity, IsActive);
}
