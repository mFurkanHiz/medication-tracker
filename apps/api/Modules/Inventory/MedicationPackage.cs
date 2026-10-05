using MedicationTracker.Api.Domain.Catalog;
using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Modules.Inventory;

/// <summary>
/// Exactly one physical container — one box, one bottle, one blister pack.
/// </summary>
/// <remarks>
/// <para>
/// Two identical boxes of the same medication are two rows with two identifiers, not
/// one row with a doubled quantity. Adding "2 full boxes of 20" creates two packages.
/// </para>
/// <para>
/// A package holds no balance column. How much is left is the sum of its ledger
/// entries, so the two can never disagree, and emptiness is therefore derived rather
/// than stored. The only quantity stored here is the <em>nominal capacity</em>, which
/// is a snapshot of what the box was sold as.
/// </para>
/// </remarks>
public sealed class MedicationPackage
{
    private MedicationPackage() { }

    public MedicationPackage(
        Guid id,
        Guid householdId,
        Guid medicationDefinitionId,
        Guid legacyInventoryItemId,
        int ordinal,
        ExactQuantity nominalCapacity,
        MedicationUnit unit,
        bool sealedPackage,
        DateTimeOffset createdAt,
        Guid createdByAccountId,
        Guid? ownerPersonId = null,
        DateOnly? expiresOn = null,
        DateOnly? acquiredOn = null,
        string? lotNumber = null,
        string? barcode = null,
        string? source = null,
        string? storageLocation = null,
        string? note = null,
        Coverage? coverage = null)
    {
        if (!nominalCapacity.IsPositive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nominalCapacity),
                "A package must have a positive nominal capacity.");
        }

        if (ordinal < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Package ordinals start at one.");
        }

        Id = id;
        HouseholdId = householdId;
        MedicationDefinitionId = medicationDefinitionId;
        LegacyInventoryItemId = legacyInventoryItemId;
        Ordinal = ordinal;
        NominalCapacityNumerator = nominalCapacity.Numerator;
        NominalCapacityDenominator = nominalCapacity.Denominator;
        Unit = unit;
        State = sealedPackage ? PackageState.Sealed : PackageState.Opened;
        OpenedAt = sealedPackage ? null : createdAt;
        CreatedAt = createdAt;
        CreatedByAccountId = createdByAccountId;
        OwnerPersonId = ownerPersonId;
        HolderPersonId = ownerPersonId;
        ExpiresOn = expiresOn;
        AcquiredOn = acquiredOn;
        LotNumber = Clean(lotNumber);
        Barcode = Clean(barcode);
        Source = Clean(source);
        StorageLocation = Clean(storageLocation);
        Note = Clean(note);
        Coverage = coverage;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid MedicationDefinitionId { get; private set; }

    /// <summary>
    /// Foreign key to the legacy one-per-medication inventory item row. Retained so
    /// existing ledger and count history stays valid; new logic addresses stock by
    /// <see cref="MedicationDefinitionId"/>. See ADR 0014.
    /// </summary>
    public Guid LegacyInventoryItemId { get; private set; }

    /// <summary>
    /// Stable per-medication number used to render a friendly label such as "Box 3".
    /// The interface never shows the identifier; the domain never uses the ordinal as
    /// an identity.
    /// </summary>
    public int Ordinal { get; private set; }

    /// <summary>What the box was sold as. Snapshotted at creation; never follows a catalog edit.</summary>
    public long NominalCapacityNumerator { get; private set; }

    public long NominalCapacityDenominator { get; private set; }

    public ExactQuantity NominalCapacity => new(NominalCapacityNumerator, NominalCapacityDenominator);

    /// <summary>Snapshot of the counting unit, so a later catalog change cannot reinterpret this box.</summary>
    public MedicationUnit Unit { get; private set; }

    public PackageState State { get; private set; }

    public DateTimeOffset? OpenedAt { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    public DateOnly? AcquiredOn { get; private set; }

    public string? LotNumber { get; private set; }

    public string? Barcode { get; private set; }

    public string? Source { get; private set; }

    public string? StorageLocation { get; private set; }

    public string? Note { get; private set; }

    /// <summary>
    /// What the household calls this box when "Box 2" is not how they think of it: "the
    /// bedroom one", "Ayşe's travel box". Optional. The ordinal stays both the fallback
    /// name and the identity; a label is a courtesy to the reader, never a key.
    /// </summary>
    public string? Label { get; private set; }

    public const int MaximumLabelLength = 60;

    /// <summary>
    /// Who paid for this box, when that differs from the medicine's own setting. Null
    /// inherits the medicine's <see cref="Domain.Catalog.Coverage"/>.
    /// </summary>
    public Coverage? Coverage { get; private set; }

    /// <summary>Whether this box counts toward the insurance-covered supply.</summary>
    public bool IsCoveredGiven(Coverage definitionCoverage) =>
        (Coverage ?? definitionCoverage) != Domain.Catalog.Coverage.SelfPaid;

    /// <summary>Who the package belongs to. Unchanged by lending.</summary>
    public Guid? OwnerPersonId { get; private set; }

    /// <summary>
    /// Who physically has it right now. Lending moves this and leaves
    /// <see cref="OwnerPersonId"/> alone, which is what makes a loan auditable.
    /// </summary>
    public Guid? HolderPersonId { get; private set; }

    /// <summary>The package the user chose to use next, ranked first by the consumption policy.</summary>
    public bool IsPinned { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedByAccountId { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>True when the package may still be drawn from, ignoring its balance.</summary>
    public bool IsAvailable => State is PackageState.Sealed or PackageState.Opened;

    /// <summary>
    /// Records that the seal was broken. Idempotent, because consuming from a sealed
    /// package opens it and a replayed command must not move the timestamp.
    /// </summary>
    public void MarkOpened(DateTimeOffset openedAt)
    {
        if (State != PackageState.Sealed)
        {
            return;
        }

        State = PackageState.Opened;
        OpenedAt = openedAt;
    }

    /// <summary>
    /// Retires the package. Disposed, lost and archived packages are never selected
    /// for consumption; their ledger history is untouched.
    /// </summary>
    public void Retire(PackageState state, DateTimeOffset retiredAt)
    {
        if (state is not (PackageState.Disposed or PackageState.Lost or PackageState.Archived))
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Only a retiring state may be set here.");
        }

        State = state;
        RetiredAt = retiredAt;
        IsPinned = false;
    }

    /// <summary>
    /// Brings a retired package back into use, as sealed or opened depending on whether
    /// its seal had already been broken.
    /// </summary>
    /// <remarks>
    /// Takes no timestamp on purpose: the package has no reinstated-at field, and the
    /// instant belongs on the ledger entry that puts the stock back, where it sits beside
    /// the amount and the entry it undoes. Storing it here as well would be a second copy
    /// of the same fact, free to drift from the first.
    /// </remarks>
    public void Reinstate()
    {
        State = OpenedAt is null ? PackageState.Sealed : PackageState.Opened;
        RetiredAt = null;
    }

    /// <summary>Transfers ownership. Does not move custody and does not change stock.</summary>
    public void TransferOwnership(Guid? ownerPersonId) => OwnerPersonId = ownerPersonId;

    /// <summary>Moves custody for a loan or a return. Ownership and stock are unaffected.</summary>
    public void TransferCustody(Guid? holderPersonId) => HolderPersonId = holderPersonId;

    public void Pin() => IsPinned = IsAvailable;

    public void Unpin() => IsPinned = false;

    public void UpdateDetails(
        string? label,
        Coverage? coverage,
        DateOnly? expiresOn,
        DateOnly? acquiredOn,
        string? lotNumber,
        string? barcode,
        string? source,
        string? storageLocation,
        string? note)
    {
        Label = Clean(label);
        ExpiresOn = expiresOn;
        AcquiredOn = acquiredOn;
        LotNumber = Clean(lotNumber);
        Barcode = Clean(barcode);
        Source = Clean(source);
        StorageLocation = Clean(storageLocation);
        Note = Clean(note);
        Coverage = coverage;
    }

    /// <summary>
    /// Projects this package into the shape the consumption policy consumes, given a
    /// balance computed from the ledger.
    /// </summary>
    public PackageCandidate ToCandidate(ExactQuantity balance) => new(
        Id,
        Ordinal,
        State,
        balance,
        ExpiresOn,
        AcquiredOn,
        CreatedAt,
        HolderPersonId,
        IsPinned);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
