namespace MedicationTracker.Api.Domain.Catalog;

/// <summary>
/// What the household was told about taking a medicine safely, in their own words:
/// what not to take it with, what to avoid eating, what to do and what not to do,
/// and anything else worth a warning.
/// </summary>
/// <remarks>
/// <para>
/// Every field is free prose somebody copied off a box, out of a leaflet, or from what
/// a doctor or pharmacist said. <strong>The software never derives any of it.</strong>
/// Nothing here is parsed, matched against a drug database, cross-referenced with
/// another medication, or checked before a dose is recorded. It is stored and shown,
/// and that is the whole contract. The one structured field that <em>is</em> matched —
/// the household's own do-not-take-with tags, by plain equality against the names and
/// ingredients they themselves typed — lives beside these notes on the definition, not
/// inside them, and claims nothing of its own (ADR 0016).
/// </para>
/// <para>
/// That restraint is the product boundary, not a shortcut. A tracker that started
/// guessing which medicines clash would be making a clinical claim it cannot stand
/// behind, and the moment it guessed once it would be trusted to guess always —
/// including by silence, which is the dangerous half. So these notes carry no
/// authority beyond the person who typed them, and the interface says so.
/// </para>
/// <para>
/// The notes belong to the <em>definition</em> rather than to a plan version. "Do not
/// take this with grapefruit" is a fact about the medicine; it does not change because
/// somebody's dose changed, and it should not have to be retyped for a second person
/// in the household taking the same thing.
/// </para>
/// </remarks>
/// <param name="DoNotTakeWith">Medicines the household was told not to combine with this one.</param>
/// <param name="FoodsToAvoid">Food and drink they were told to keep away from it.</param>
/// <param name="ThingsToDo">What to do when taking it — "drink a full glass of water".</param>
/// <param name="ThingsToAvoid">What not to do — "do not lie down for half an hour".</param>
/// <param name="Warning">Anything else they want in front of them when they open the box.</param>
public readonly record struct CautionNotes(
    string? DoNotTakeWith = null,
    string? FoodsToAvoid = null,
    string? ThingsToDo = null,
    string? ThingsToAvoid = null,
    string? Warning = null)
{
    /// <summary>
    /// How long any single note may be. Generous on purpose: this is prose, and a
    /// household that is told four things should not have to pick three.
    /// </summary>
    public const int MaximumNoteLength = 2000;

    public static CautionNotes None => default;

    /// <summary>True when the household has recorded nothing at all.</summary>
    public bool IsEmpty =>
        DoNotTakeWith is null
        && FoodsToAvoid is null
        && ThingsToDo is null
        && ThingsToAvoid is null
        && Warning is null;

    /// <summary>
    /// Trims each note and turns blank into absent, so a cleared textarea reads as "no
    /// note" rather than as an empty note somebody deliberately wrote.
    /// </summary>
    public CautionNotes Normalized() => new(
        Clean(DoNotTakeWith),
        Clean(FoodsToAvoid),
        Clean(ThingsToDo),
        Clean(ThingsToAvoid),
        Clean(Warning));

    /// <summary>True when every note is within <see cref="MaximumNoteLength"/>.</summary>
    public bool IsValid() =>
        Fits(DoNotTakeWith) && Fits(FoodsToAvoid) && Fits(ThingsToDo) && Fits(ThingsToAvoid) && Fits(Warning);

    private static bool Fits(string? value) => value is null || value.Trim().Length <= MaximumNoteLength;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
