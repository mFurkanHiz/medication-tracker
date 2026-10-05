using MedicationTracker.Api.Domain.Catalog;
using MedicationTracker.Api.Domain.Quantities;

namespace MedicationTracker.Api.Modules.Catalog;

/// <summary>
/// A reusable household catalog entry describing what a medication <em>is</em> — for
/// example "Parol 500 mg Tablet".
/// </summary>
/// <remarks>
/// <para>
/// A definition is never owned by a person and never holds stock. Physical containers
/// live in <see cref="Inventory.MedicationPackage"/>; who takes what lives in
/// <see cref="Treatments.TreatmentPlan"/>. This separation is the point: the previous
/// model's single <c>Medication</c> entity meant "kind of drug" in one place and
/// "that person's supply" in another.
/// </para>
/// <para>
/// Every descriptive field here is a label the user typed. None of it is interpreted
/// clinically, and none of it drives dose arithmetic or unit conversion.
/// </para>
/// </remarks>
public sealed class MedicationDefinition
{
    private MedicationDefinition() { }

    public MedicationDefinition(
        Guid id,
        Guid householdId,
        string name,
        PharmaceuticalForm form,
        MedicationUnit unit,
        DateTimeOffset createdAt,
        string? strength = null,
        string? brand = null,
        string? manufacturer = null,
        string[]? activeIngredients = null,
        ExactQuantity? defaultPackageCapacity = null,
        string? category = null,
        string[]? tags = null,
        string? notes = null,
        CautionNotes cautions = default,
        Coverage coverage = Coverage.Unspecified,
        string[]? doNotTakeWithTags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        HouseholdId = householdId;
        Name = name.Trim();
        Form = form;
        Unit = unit;
        CreatedAt = createdAt;
        Strength = Clean(strength);
        Brand = Clean(brand);
        Manufacturer = Clean(manufacturer);
        ActiveIngredients = activeIngredients ?? [];
        Category = Clean(category);
        Tags = tags ?? [];
        Notes = Clean(notes);
        SetDefaultPackageCapacity(defaultPackageCapacity);
        SetCautions(cautions);
        Coverage = coverage;
        DoNotTakeWithTags = doNotTakeWithTags ?? [];
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Free text as written on the box, for example "500 mg". Never parsed.</summary>
    public string? Strength { get; private set; }

    public string? Brand { get; private set; }

    public string? Manufacturer { get; private set; }

    public PharmaceuticalForm Form { get; private set; }

    /// <summary>
    /// The unit every capacity, dose and ledger amount for this medication is counted
    /// in. One medication has exactly one unit, so no amount is ever converted.
    /// </summary>
    public MedicationUnit Unit { get; private set; }

    public string[] ActiveIngredients { get; private set; } = [];

    /// <summary>
    /// How much a new package of this medication usually holds, used to pre-fill the
    /// add-stock form.
    /// </summary>
    /// <remarks>
    /// Changing this must never alter an existing package: each package snapshots its
    /// own nominal capacity at creation. See ADR 0014 invariant 4.
    /// </remarks>
    public long? DefaultPackageCapacityNumerator { get; private set; }

    public long? DefaultPackageCapacityDenominator { get; private set; }

    public ExactQuantity? DefaultPackageCapacity =>
        DefaultPackageCapacityNumerator is { } numerator && DefaultPackageCapacityDenominator is { } denominator
            ? new ExactQuantity(numerator, denominator)
            : null;

    public string? Category { get; private set; }

    public string[] Tags { get; private set; } = [];

    /// <summary>
    /// Who paid for this medicine by default. A box may say otherwise for itself;
    /// <see cref="Domain.Catalog.Coverage.Unspecified"/> behaves as covered.
    /// </summary>
    public Coverage Coverage { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>
    /// The household's own safety notes, stored one column per note.
    /// </summary>
    /// <remarks>
    /// Flat columns rather than an owned entity, matching
    /// <see cref="DefaultPackageCapacity"/>: an optional owned type whose every column
    /// is null is the exact shape EF Core warns about, and a medicine with nothing
    /// recorded is the common case here, not the edge one.
    /// </remarks>
    public string? CautionDoNotTakeWith { get; private set; }

    public string? CautionFoodsToAvoid { get; private set; }

    public string? CautionThingsToDo { get; private set; }

    public string? CautionThingsToAvoid { get; private set; }

    public string? CautionWarning { get; private set; }

    /// <summary>
    /// Medicine names and active ingredients the household was told not to combine with
    /// this one, as short tags: <c>ligone, parol, paracetamol, cvitamine</c>. Unlike the
    /// prose in <see cref="CautionDoNotTakeWith"/>, these are matched — by plain equality
    /// on the household's own words — against the other medicines the same person has
    /// on the same day, and the Today row says so in red. ADR 0016.
    /// </summary>
    public string[] DoNotTakeWithTags { get; private set; } = [];

    /// <summary>
    /// What the household was told about taking this safely, as one value so a caller
    /// cannot pick up four of the five notes by accident. Never derived by the
    /// software — <see cref="CautionNotes"/> says why that line is not crossed.
    /// </summary>
    public CautionNotes Cautions => new(
        CautionDoNotTakeWith,
        CautionFoodsToAvoid,
        CautionThingsToDo,
        CautionThingsToAvoid,
        CautionWarning);

    /// <summary>
    /// Reserved for future external identifiers (barcode, GTIN, ATC, national code)
    /// as a JSON document, so adding one later needs no schema change. Not read by
    /// any V1 behaviour.
    /// </summary>
    public string? ExternalCodes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    /// <summary>
    /// Legacy column retained from the superseded model, where a medication could be
    /// tied to a person. Preserved so no information is destroyed by the rebuild; the
    /// domain never reads it. See ADR 0013.
    /// </summary>
    public Guid? LegacyPersonId { get; private set; }

    public void UpdateDetails(
        string name,
        PharmaceuticalForm form,
        MedicationUnit unit,
        string? strength,
        string? brand,
        string? manufacturer,
        string[] activeIngredients,
        ExactQuantity? defaultPackageCapacity,
        string? category,
        string[] tags,
        string? notes,
        CautionNotes cautions,
        Coverage coverage = Coverage.Unspecified,
        string[]? doNotTakeWithTags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(activeIngredients);
        ArgumentNullException.ThrowIfNull(tags);

        Name = name.Trim();
        Form = form;
        Unit = unit;
        Strength = Clean(strength);
        Brand = Clean(brand);
        Manufacturer = Clean(manufacturer);
        ActiveIngredients = activeIngredients;
        Category = Clean(category);
        Tags = tags;
        Notes = Clean(notes);
        SetDefaultPackageCapacity(defaultPackageCapacity);
        SetCautions(cautions);
        Coverage = coverage;
        DoNotTakeWithTags = doNotTakeWithTags ?? [];
    }

    /// <summary>
    /// Retires the definition from pickers while leaving every historical package,
    /// plan and administration intact. See ADR 0014 invariant 10.
    /// </summary>
    public void Archive(DateTimeOffset archivedAt) => ArchivedAt = archivedAt;

    public void Restore() => ArchivedAt = null;

    private void SetDefaultPackageCapacity(ExactQuantity? capacity)
    {
        if (capacity is null)
        {
            DefaultPackageCapacityNumerator = null;
            DefaultPackageCapacityDenominator = null;
            return;
        }

        if (!capacity.Value.IsPositive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "A default package capacity must be a positive amount.");
        }

        DefaultPackageCapacityNumerator = capacity.Value.Numerator;
        DefaultPackageCapacityDenominator = capacity.Value.Denominator;
    }

    private void SetCautions(CautionNotes cautions)
    {
        if (!cautions.IsValid())
        {
            throw new ArgumentOutOfRangeException(
                nameof(cautions),
                $"A caution note may be at most {CautionNotes.MaximumNoteLength} characters.");
        }

        var normalized = cautions.Normalized();

        CautionDoNotTakeWith = normalized.DoNotTakeWith;
        CautionFoodsToAvoid = normalized.FoodsToAvoid;
        CautionThingsToDo = normalized.ThingsToDo;
        CautionThingsToAvoid = normalized.ThingsToAvoid;
        CautionWarning = normalized.Warning;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
