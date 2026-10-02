using MedicationTracker.Api.Domain.Administrations;

namespace MedicationTracker.Api.Domain.Reports;

/// <summary>
/// One recorded dose, reduced to the facts the adherence tally needs.
/// </summary>
/// <remarks>
/// A dose and the slot it answers are placed in the period separately, because they can
/// land on different sides of a boundary. Someone who takes a Monday-evening dose after
/// midnight has answered Monday's slot with a dose that happened on Tuesday; counting
/// the dose in Tuesday's totals is right, and calling Monday's slot missed is not. The
/// caller decides both, so this type carries no window of its own.
/// </remarks>
/// <param name="Outcome">What the person did about the dose.</param>
/// <param name="AnsweredSlot">
/// The planned slot this record answers, when that slot falls inside the period. Null
/// for a dose that belonged to no slot, and for one whose slot is outside the period.
/// </param>
/// <param name="OccurredInPeriod">
/// Whether the dose itself happened inside the period, which is what the outcome counts
/// describe.
/// </param>
public sealed record AdherenceRecord(
    AdministrationOutcome Outcome,
    DateTimeOffset? AnsweredSlot,
    bool OccurredInPeriod);

/// <summary>
/// What happened to one person's doses of one medication over a period.
/// </summary>
/// <remarks>
/// <para>
/// This is a description of recorded behaviour, not an assessment of it. The product
/// reports what was planned and what was recorded and leaves every judgement to the
/// household and their prescriber: there is no threshold above which adherence is
/// called good, no warning for a low ratio, and no suggestion about what to do next.
/// </para>
/// <para>
/// Counts and the ratio are kept separate on purpose. A household can take more doses
/// than were planned — an extra dose is a real event with no slot behind it — so the
/// counts do not have to sum to <paramref name="ScheduledDoses"/> and the ratio is
/// never clamped to hide that.
/// </para>
/// </remarks>
/// <param name="ScheduledDoses">
/// Slots the effective-dated plan versions placed inside the period. Zero for a
/// medication that is only taken as needed, whose future use is unknown by design.
/// </param>
/// <param name="RecordedSlots">
/// Distinct scheduled slots that have any record against them, including a skip. A
/// deliberate skip is answered, not missing.
/// </param>
/// <param name="OnScheduleDoses">
/// Scheduled slots answered by a dose that was actually taken, in full or in part.
/// </param>
public sealed record AdherenceTally(
    int ScheduledDoses,
    int RecordedSlots,
    int OnScheduleDoses,
    int Taken,
    int Skipped,
    int PartialDoses,
    int ExtraDoses)
{
    public static AdherenceTally Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Scheduled slots with nothing recorded against them at all.</summary>
    public int MissedDoses => Math.Max(0, ScheduledDoses - RecordedSlots);

    /// <summary>Every record in the period, whatever the outcome.</summary>
    public int RecordedDoses => Taken + Skipped + PartialDoses + ExtraDoses;

    /// <summary>
    /// Scheduled slots answered by a dose, over slots planned, as an exact pair so the
    /// client can render a percentage without this layer choosing a rounding. Null when
    /// nothing was scheduled, because a ratio with no denominator is not a zero.
    /// </summary>
    public (int Numerator, int Denominator)? OnScheduleRatio =>
        ScheduledDoses > 0 ? (OnScheduleDoses, ScheduledDoses) : null;

    public bool IsEmpty => ScheduledDoses == 0 && RecordedDoses == 0;
}

/// <summary>
/// Turns planned slots and recorded doses into a tally, with no clock, database or
/// culture of its own.
/// </summary>
public static class AdherenceReport
{
    /// <summary>
    /// The longest period the report will aggregate, so one request cannot walk an
    /// unbounded number of days.
    /// </summary>
    public const int MaximumPeriodDays = 366;

    /// <summary>Default period when the caller names neither end.</summary>
    public const int DefaultPeriodDays = 30;

    public static AdherenceTally Tally(int scheduledDoses, IEnumerable<AdherenceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentOutOfRangeException.ThrowIfNegative(scheduledDoses);

        var taken = 0;
        var skipped = 0;
        var partial = 0;
        var extra = 0;

        // Distinct, because two devices may each record the same slot, and a replayed
        // command must not make a household look twice as adherent.
        var answeredSlots = new HashSet<DateTimeOffset>();
        var dosedSlots = new HashSet<DateTimeOffset>();

        foreach (var record in records)
        {
            if (record.OccurredInPeriod)
            {
                switch (record.Outcome)
                {
                    case AdministrationOutcome.Taken:
                        taken++;
                        break;
                    case AdministrationOutcome.Skipped:
                        skipped++;
                        break;
                    case AdministrationOutcome.PartialDose:
                        partial++;
                        break;
                    case AdministrationOutcome.ExtraDose:
                        extra++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(records), record.Outcome, "Unknown administration outcome.");
                }
            }

            if (record.AnsweredSlot is not { } slot)
            {
                continue;
            }

            answeredSlots.Add(slot);

            // An extra dose against a slot is not that slot being taken as planned; it
            // is an additional dose, and counting it here would overstate the ratio.
            if (record.Outcome is AdministrationOutcome.Taken or AdministrationOutcome.PartialDose)
            {
                dosedSlots.Add(slot);
            }
        }

        return new AdherenceTally(
            scheduledDoses,
            // A slot recorded but never planned — a plan edited after the fact — must not
            // push the answered count past what was scheduled and show a negative miss.
            RecordedSlots: Math.Min(answeredSlots.Count, scheduledDoses),
            OnScheduleDoses: Math.Min(dosedSlots.Count, scheduledDoses),
            taken,
            skipped,
            partial,
            extra);
    }

    /// <summary>
    /// Adds two tallies, for rolling per-pair rows up to a person, a medication or the
    /// whole household.
    /// </summary>
    public static AdherenceTally Add(AdherenceTally left, AdherenceTally right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new AdherenceTally(
            left.ScheduledDoses + right.ScheduledDoses,
            left.RecordedSlots + right.RecordedSlots,
            left.OnScheduleDoses + right.OnScheduleDoses,
            left.Taken + right.Taken,
            left.Skipped + right.Skipped,
            left.PartialDoses + right.PartialDoses,
            left.ExtraDoses + right.ExtraDoses);
    }

    public static AdherenceTally Total(IEnumerable<AdherenceTally> tallies)
    {
        ArgumentNullException.ThrowIfNull(tallies);
        return tallies.Aggregate(AdherenceTally.Empty, Add);
    }
}
