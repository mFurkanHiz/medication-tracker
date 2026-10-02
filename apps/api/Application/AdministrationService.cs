using MedicationTracker.Api.Domain.Administrations;
using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Modules.Administrations;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.Sync;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Application;

/// <summary>
/// Where the user says a dose came from.
/// </summary>
/// <remarks>
/// <see cref="Automatic"/> is the default and the only option the everyday flow shows.
/// The rest live behind "details" so that recording a dose stays one tap.
/// </remarks>
public enum DoseSourceSelection
{
    /// <summary>Let the consumption policy choose. The default.</summary>
    Automatic = 0,

    /// <summary>A package the user named.</summary>
    SpecificPackage = 1,

    /// <summary>Package-independent stock the user named.</summary>
    LooseStock = 2,

    /// <summary>Stock this household does not track. Records the dose without touching inventory.</summary>
    UntrackedExternal = 3,
}

public enum RecordDoseRefusal
{
    None = 0,

    /// <summary>Automatic selection could not cover the dose from tracked stock.</summary>
    InsufficientStock = 1,

    /// <summary>The named package does not exist in this household for this medication.</summary>
    PackageNotFound = 2,

    /// <summary>The named source exists but cannot cover the dose on its own.</summary>
    ChosenSourceInsufficient = 3,

    /// <summary>A specific package was required but none was named.</summary>
    PackageNotSpecified = 4,

    /// <summary>The named package is retired, or is held by someone else.</summary>
    PackageNotEligible = 5,
}

public sealed record RecordDoseCommand(
    Guid HouseholdId,
    Guid PersonId,
    Guid MedicationDefinitionId,
    Guid? TreatmentPlanVersionId,
    AdministrationOutcome Outcome,
    ExactQuantity? PlannedQuantity,
    ExactQuantity? ActualQuantity,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset OccurredAt,
    DoseSourceSelection Source,
    Guid? PackageId,
    Guid ActorAccountId,
    string? Note = null,
    Guid? AdministrationEventId = null);

public sealed record DoseAllocationView(Guid AllocationId, Guid? PackageId, int? PackageOrdinal, ExactQuantity Quantity);

public sealed record RecordedDose(
    Guid AdministrationEventId,
    bool Replayed,
    AdministrationStockSource StockSource,
    IReadOnlyList<DoseAllocationView> Allocations);

public enum CorrectAllocationOutcome
{
    None = 0,
    AdministrationNotFound = 1,
    PackageNotFound = 2,
}

public sealed record CorrectAllocationCommand(
    Guid HouseholdId,
    Guid AdministrationEventId,
    Guid AllocationId,
    DoseSourceSelection Target,
    Guid? TargetPackageId,
    Guid ActorAccountId,
    string? Reason = null);

/// <summary>
/// Records what a person actually took and keeps the ledger, the allocations and the
/// package states consistent with it.
/// </summary>
/// <remarks>
/// <para>
/// Every method here expects the caller to have opened a transaction and taken the
/// household advisory lock. That is deliberate: reading balances and writing
/// consumption must be one serialised unit, or two concurrent doses could each see
/// enough stock and together overdraw it.
/// </para>
/// <para>
/// Nothing in this service decides whether a medication should be taken. It records a
/// decision the household already made and keeps the arithmetic honest.
/// </para>
/// </remarks>
public static class AdministrationService
{
    /// <summary>
    /// Records a dose, allocating stock according to the chosen source.
    /// </summary>
    /// <param name="idempotencyKey">
    /// When supplied, a replay of the same key returns the original result instead of
    /// consuming stock again. ADR 0014 invariant 8 and 14.
    /// </param>
    public static async Task<(RecordDoseRefusal Refusal, RecordedDose? Result)> RecordAsync(
        MedicationTrackerDbContext db,
        RecordDoseCommand command,
        string? idempotencyKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(command);

        if (idempotencyKey is not null)
        {
            var prior = await db.ProcessedAdministrationCommands.AsNoTracking().SingleOrDefaultAsync(
                receipt => receipt.HouseholdId == command.HouseholdId
                           && receipt.IdempotencyKey == idempotencyKey,
                ct);

            if (prior is not null)
            {
                return (RecordDoseRefusal.None, await ReplayAsync(db, command.HouseholdId, prior.AdministrationEventId, ct));
            }
        }

        var now = DateTimeOffset.UtcNow;
        var administrationId = command.AdministrationEventId ?? Guid.CreateVersion7();
        var stockSource = ResolveStockSource(command.Outcome, command.Source);

        var administration = new AdministrationEvent(
            administrationId,
            command.HouseholdId,
            command.PersonId,
            command.MedicationDefinitionId,
            command.TreatmentPlanVersionId,
            command.Outcome,
            stockSource,
            command.PlannedQuantity,
            command.ActualQuantity,
            command.ScheduledFor,
            command.OccurredAt,
            now,
            command.ActorAccountId,
            command.Note);

        var allocations = new List<DoseAllocationView>();

        if (administration.DrawsFromTrackedInventory)
        {
            var stock = await InventoryReader.LoadAsync(
                db, command.HouseholdId, command.MedicationDefinitionId, tracked: true, ct);

            var (refusal, plan) = BuildPlan(command, stock);
            if (refusal != RecordDoseRefusal.None)
            {
                return (refusal, null);
            }

            var legacyItemId = await LegacyItemIdAsync(db, command.MedicationDefinitionId, ct);
            var correlationId = Guid.CreateVersion7();

            foreach (var step in plan!.Steps)
            {
                var entry = InventoryLedgerEntry.Consume(
                    command.HouseholdId,
                    command.MedicationDefinitionId,
                    legacyItemId,
                    step.PackageId,
                    step.Quantity,
                    correlationId,
                    administrationId,
                    command.ActorAccountId,
                    command.OccurredAt,
                    now);

                db.LedgerEntries.Add(entry);

                var allocation = new AdministrationAllocation(
                    Guid.CreateVersion7(),
                    command.HouseholdId,
                    administrationId,
                    step.PackageId,
                    step.Quantity,
                    entry.Id,
                    correlationId,
                    now);

                db.AdministrationAllocations.Add(allocation);

                var ordinal = OpenIfNeeded(stock, step, now);
                allocations.Add(new DoseAllocationView(allocation.Id, step.PackageId, ordinal, step.Quantity));
            }
        }

        db.AdministrationEvents.Add(administration);

        if (idempotencyKey is not null)
        {
            db.ProcessedAdministrationCommands.Add(new ProcessedAdministrationCommand(
                Guid.CreateVersion7(),
                command.HouseholdId,
                command.ActorAccountId,
                idempotencyKey,
                administrationId,
                now));
        }

        return (RecordDoseRefusal.None, new RecordedDose(administrationId, false, stockSource, allocations));
    }

    /// <summary>
    /// Moves one allocation of an already-recorded dose to the source the user says was
    /// really used.
    /// </summary>
    /// <remarks>
    /// Appends a reversal and a re-charge rather than editing the original entry, so the
    /// medication total is unchanged and the previous answer stays visible in history.
    /// </remarks>
    public static async Task<(CorrectAllocationOutcome Outcome, AllocationCorrectionRefusal Refusal, DoseAllocationView? Result)>
        CorrectAllocationAsync(
            MedicationTrackerDbContext db,
            CorrectAllocationCommand command,
            CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(command);

        var administration = await db.AdministrationEvents.AsNoTracking().SingleOrDefaultAsync(
            e => e.Id == command.AdministrationEventId && e.HouseholdId == command.HouseholdId,
            ct);

        if (administration is null)
        {
            return (CorrectAllocationOutcome.AdministrationNotFound, AllocationCorrectionRefusal.None, null);
        }

        if (!administration.DrawsFromTrackedInventory)
        {
            return (CorrectAllocationOutcome.None, AllocationCorrectionRefusal.AdministrationIsUntracked, null);
        }

        var allocations = await db.AdministrationAllocations
            .Where(allocation => allocation.AdministrationEventId == command.AdministrationEventId
                                 && allocation.HouseholdId == command.HouseholdId)
            .ToListAsync(ct);

        var stock = await InventoryReader.LoadAsync(
            db, command.HouseholdId, administration.MedicationDefinitionId, tracked: true, ct);

        PackageCandidate? target = null;
        if (command.Target == DoseSourceSelection.SpecificPackage)
        {
            if (command.TargetPackageId is not { } targetId)
            {
                return (CorrectAllocationOutcome.PackageNotFound, AllocationCorrectionRefusal.None, null);
            }

            target = stock.CandidateFor(targetId);
            if (target is null)
            {
                return (CorrectAllocationOutcome.PackageNotFound, AllocationCorrectionRefusal.None, null);
            }
        }

        var targetBalance = target is null ? stock.LooseBalance : stock.BalanceOf(target.PackageId);

        var refusal = AllocationCorrection.TryPlan(
            allocations.Select(allocation => allocation.ToDomain()).ToList(),
            command.AllocationId,
            target,
            targetBalance,
            administration.PersonId,
            out var plan);

        if (refusal != AllocationCorrectionRefusal.None)
        {
            return (CorrectAllocationOutcome.None, refusal, null);
        }

        var superseded = allocations.Single(allocation => allocation.Id == command.AllocationId);
        var legacyItemId = await LegacyItemIdAsync(db, administration.MedicationDefinitionId, ct);
        var now = DateTimeOffset.UtcNow;
        var correlationId = Guid.CreateVersion7();

        var reversal = InventoryLedgerEntry.CorrectionReversal(
            command.HouseholdId,
            administration.MedicationDefinitionId,
            legacyItemId,
            plan!.FromPackageId,
            plan.ReversalQuantity,
            correlationId,
            administration.Id,
            superseded.LedgerEntryId,
            command.ActorAccountId,
            now,
            now,
            command.Reason);

        var recharge = InventoryLedgerEntry.CorrectionConsume(
            command.HouseholdId,
            administration.MedicationDefinitionId,
            legacyItemId,
            plan.ToPackageId,
            plan.ConsumeQuantity,
            correlationId,
            administration.Id,
            command.ActorAccountId,
            now,
            now,
            command.Reason);

        db.LedgerEntries.Add(reversal);
        db.LedgerEntries.Add(recharge);

        var replacement = new AdministrationAllocation(
            Guid.CreateVersion7(),
            command.HouseholdId,
            administration.Id,
            plan.ToPackageId,
            plan.ConsumeQuantity,
            recharge.Id,
            correlationId,
            now);

        db.AdministrationAllocations.Add(replacement);
        superseded.SupersedeBy(replacement.Id, now);

        db.AllocationCorrections.Add(new AdministrationAllocationCorrection(
            Guid.CreateVersion7(),
            command.HouseholdId,
            administration.Id,
            superseded.Id,
            replacement.Id,
            plan.FromPackageId,
            plan.ToPackageId,
            plan.ConsumeQuantity,
            correlationId,
            reversal.Id,
            recharge.Id,
            command.ActorAccountId,
            now,
            command.Reason));

        var ordinal = plan.ToPackageId is { } toPackageId
            ? OpenIfNeeded(stock, new AllocationStep(toPackageId, plan.ConsumeQuantity, plan.TargetRequiresOpening), now)
            : null;

        return (
            CorrectAllocationOutcome.None,
            AllocationCorrectionRefusal.None,
            new DoseAllocationView(replacement.Id, plan.ToPackageId, ordinal, plan.ConsumeQuantity));
    }

    private static (RecordDoseRefusal Refusal, ConsumptionPlan? Plan) BuildPlan(
        RecordDoseCommand command,
        MedicationStock stock)
    {
        var required = command.ActualQuantity!.Value;

        switch (command.Source)
        {
            case DoseSourceSelection.Automatic:
            {
                var plan = PackageConsumptionPolicy.Plan(
                    stock.Candidates(), stock.LooseBalance, required, command.PersonId);

                return plan.IsSatisfiable
                    ? (RecordDoseRefusal.None, plan)
                    : (RecordDoseRefusal.InsufficientStock, null);
            }

            case DoseSourceSelection.SpecificPackage:
            {
                if (command.PackageId is not { } packageId)
                {
                    return (RecordDoseRefusal.PackageNotSpecified, null);
                }

                var candidate = stock.CandidateFor(packageId);
                if (candidate is null)
                {
                    return (RecordDoseRefusal.PackageNotFound, null);
                }

                // State and custody are checked against a notional positive balance so
                // that an empty-but-valid package reports an insufficiency rather than
                // an eligibility problem.
                var eligible = PackageConsumptionPolicy.IsEligible(
                    candidate with { Balance = ExactQuantity.Max(candidate.Balance, ExactQuantity.One) },
                    command.PersonId);

                if (!eligible)
                {
                    return (RecordDoseRefusal.PackageNotEligible, null);
                }

                var plan = PackageConsumptionPolicy.PlanFromChosenSource(
                    candidate, stock.BalanceOf(packageId), required);

                return plan.IsSatisfiable
                    ? (RecordDoseRefusal.None, plan)
                    : (RecordDoseRefusal.ChosenSourceInsufficient, null);
            }

            case DoseSourceSelection.LooseStock:
            {
                var plan = PackageConsumptionPolicy.PlanFromChosenSource(null, stock.LooseBalance, required);

                return plan.IsSatisfiable
                    ? (RecordDoseRefusal.None, plan)
                    : (RecordDoseRefusal.ChosenSourceInsufficient, null);
            }

            default:
                throw new InvalidOperationException(
                    "An untracked dose does not allocate stock and must not be planned.");
        }
    }

    /// <summary>
    /// Opens a sealed package the plan drew from and returns its friendly ordinal.
    /// </summary>
    private static int? OpenIfNeeded(MedicationStock stock, AllocationStep step, DateTimeOffset now)
    {
        if (step.PackageId is not { } packageId)
        {
            return null;
        }

        var package = stock.Packages.SingleOrDefault(candidate => candidate.Id == packageId);
        if (package is null)
        {
            return null;
        }

        if (step.RequiresOpening)
        {
            package.MarkOpened(now);
        }

        return package.Ordinal;
    }

    private static AdministrationStockSource ResolveStockSource(
        AdministrationOutcome outcome,
        DoseSourceSelection selection)
    {
        if (!AdministrationOutcomes.ConsumesStock(outcome))
        {
            return AdministrationStockSource.NotApplicable;
        }

        return selection == DoseSourceSelection.UntrackedExternal
            ? AdministrationStockSource.UntrackedExternal
            : AdministrationStockSource.TrackedInventory;
    }

    private static async Task<RecordedDose> ReplayAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        Guid administrationEventId,
        CancellationToken ct)
    {
        var administration = await db.AdministrationEvents.AsNoTracking()
            .SingleAsync(e => e.Id == administrationEventId, ct);

        var allocations = await db.AdministrationAllocations.AsNoTracking()
            .Where(allocation => allocation.AdministrationEventId == administrationEventId
                                 && allocation.HouseholdId == householdId
                                 && allocation.IsActive)
            .Join(
                db.Packages.AsNoTracking(),
                allocation => allocation.PackageId,
                package => (Guid?)package.Id,
                (allocation, package) => new { allocation, package.Ordinal })
            .ToListAsync(ct);

        var looseAllocations = await db.AdministrationAllocations.AsNoTracking()
            .Where(allocation => allocation.AdministrationEventId == administrationEventId
                                 && allocation.HouseholdId == householdId
                                 && allocation.IsActive
                                 && allocation.PackageId == null)
            .ToListAsync(ct);

        var views = allocations
            .Select(row => new DoseAllocationView(
                row.allocation.Id, row.allocation.PackageId, row.Ordinal, row.allocation.Quantity))
            .Concat(looseAllocations.Select(allocation =>
                new DoseAllocationView(allocation.Id, null, null, allocation.Quantity)))
            .ToList();

        return new RecordedDose(administrationEventId, true, administration.StockSource, views);
    }

    private static Task<Guid> LegacyItemIdAsync(
        MedicationTrackerDbContext db,
        Guid medicationDefinitionId,
        CancellationToken ct) =>
        db.LegacyInventoryItems.AsNoTracking()
            .Where(item => item.MedicationDefinitionId == medicationDefinitionId)
            .Select(item => item.Id)
            .SingleAsync(ct);
}
