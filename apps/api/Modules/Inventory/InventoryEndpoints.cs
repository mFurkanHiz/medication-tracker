using MedicationTracker.Api.Application;
using MedicationTracker.Api.Domain.Inventory;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Inventory;

/// <summary>
/// Physical stock. Adding stock creates one row per real container, which is what makes
/// "which box did this come from" answerable at all.
/// </summary>
public static class InventoryEndpoints
{
    /// <summary>Guard against a single request creating an unreasonable number of rows.</summary>
    public const int MaximumPackagesPerRequest = 100;

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/households/{householdId:guid}/inventory");

        api.MapPost("/{definitionId:guid}/stock", async (
            Guid householdId,
            Guid definitionId,
            AddStockRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var definition = await db.MedicationDefinitions.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == definitionId && candidate.HouseholdId == householdId, ct);

            if (definition is null)
            {
                return Results.NotFound();
            }

            if (request.OwnerPersonId is { } ownerId
                && !await db.People.AsNoTracking().AnyAsync(
                    person => person.Id == ownerId && person.HouseholdId == householdId, ct))
            {
                return ApiResults.Invalid("ownerPersonId", "unknown_person");
            }

            if (!TryResolveCapacity(request, definition.DefaultPackageCapacity, out var capacity))
            {
                return ApiResults.Invalid("capacity", "invalid");
            }

            var opened = request.OpenedPackages ?? [];
            if (request.FullPackages < 0
                || request.FullPackages + opened.Count > MaximumPackagesPerRequest
                || request.FullPackages + opened.Count == 0 && request.LooseNumerator is null)
            {
                return ApiResults.Invalid("packages", "invalid");
            }

            var openedAmounts = new List<(ExactQuantity Capacity, ExactQuantity Remaining)>(opened.Count);
            foreach (var input in opened)
            {
                var packageCapacity = capacity;
                if (input.CapacityNumerator is { } numerator
                    && !ExactQuantity.TryCreatePositive(numerator, input.CapacityDenominator ?? 1, out packageCapacity))
                {
                    return ApiResults.Invalid("openedPackages", "invalid_capacity");
                }

                if (!ExactQuantity.TryCreateNonNegative(
                        input.RemainingNumerator, input.RemainingDenominator, out var remaining))
                {
                    return ApiResults.Invalid("openedPackages", "invalid_remaining");
                }

                if (remaining > packageCapacity)
                {
                    return ApiResults.Invalid("openedPackages", "remaining_exceeds_capacity");
                }

                openedAmounts.Add((packageCapacity, remaining));
            }

            ExactQuantity? loose = null;
            if (request.LooseNumerator is { } looseNumerator)
            {
                if (!ExactQuantity.TryCreatePositive(
                        looseNumerator, request.LooseDenominator ?? 1, out var parsedLoose))
                {
                    return ApiResults.Invalid("loose", "invalid");
                }

                loose = parsedLoose;
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;
            if (!TryParseCoverage(request.Coverage, out var coverage))
            {
                return ApiResults.Invalid("coverage", "invalid");
            }

            var correlationId = Guid.CreateVersion7();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Ordinals are allocated under the household lock so two simultaneous
            // additions cannot claim the same "Box 3".
            await InventoryReader.LockHouseholdAsync(db, householdId, ct);

            var legacyItemId = await db.LegacyInventoryItems.AsNoTracking()
                .Where(item => item.MedicationDefinitionId == definitionId)
                .Select(item => item.Id)
                .SingleAsync(ct);

            var ordinal = await InventoryReader.NextOrdinalAsync(db, definitionId, ct);
            var created = new List<object>();

            // Each full package is its own row: "2 full boxes" is two containers, not one
            // row of forty. ADR 0014 invariant 2.
            for (var index = 0; index < request.FullPackages; index++)
            {
                created.Add(AddPackage(db, householdId, definition.Id, legacyItemId, ordinal++, capacity, capacity,
                    definition.Unit, sealedPackage: true, request, accountId, now, correlationId, coverage));
            }

            foreach (var (packageCapacity, remaining) in openedAmounts)
            {
                created.Add(AddPackage(db, householdId, definition.Id, legacyItemId, ordinal++, packageCapacity,
                    remaining, definition.Unit, sealedPackage: false, request, accountId, now, correlationId, coverage));
            }

            if (loose is { } looseAmount)
            {
                db.LedgerEntries.Add(InventoryLedgerEntry.Acquire(
                    householdId, definition.Id, legacyItemId, null, looseAmount,
                    correlationId, accountId, now, now, request.Note));
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Created(
                $"/api/households/{householdId}/inventory/{definitionId}",
                new { packages = created, looseAdded = loose?.ToString() });
        });

        api.MapGet("/{definitionId:guid}", async (
            Guid householdId,
            Guid definitionId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (!await db.MedicationDefinitions.AsNoTracking().AnyAsync(
                    candidate => candidate.Id == definitionId && candidate.HouseholdId == householdId, ct))
            {
                return Results.NotFound();
            }

            var stock = await InventoryReader.LoadAsync(db, householdId, definitionId, tracked: false, ct);

            return Results.Ok(new
            {
                medicationDefinitionId = definitionId,
                total = Quantity(stock.Total),
                packageCount = stock.Packages.Count(package => package.IsAvailable),
                packages = stock.Packages.Select(package => PackageView(package, stock.BalanceOf(package.Id))),
                loose = Quantity(stock.LooseBalance),
            });
        });

        api.MapPost("/packages/{packageId:guid}/pin", async (
            Guid householdId,
            Guid packageId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var package = await db.Packages.SingleOrDefaultAsync(
                candidate => candidate.Id == packageId && candidate.HouseholdId == householdId, ct);

            if (package is null)
            {
                return Results.NotFound();
            }

            if (!package.IsAvailable)
            {
                return ApiResults.Conflict("package_not_available");
            }

            // Only one package per medication may be pinned, and the filtered unique index
            // ix_packages_single_pinned enforces that per row, immediately. Releasing the old
            // pin and taking the new one in a single SaveChanges is therefore not enough: EF
            // orders the two UPDATEs by primary key, not by which one frees the slot, so
            // whenever the box being pinned sorted first PostgreSQL rejected it (23505)
            // before the unpin ran. Which box sorts first is decided by their ids, so one
            // pair of boxes failed every time while another never would — the owner met the
            // failing pair on the live site as "something went wrong". Two saves inside one
            // transaction make the order explicit: release, then take.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var others = await db.Packages
                .Where(candidate => candidate.MedicationDefinitionId == package.MedicationDefinitionId
                                    && candidate.IsPinned
                                    && candidate.Id != packageId)
                .ToListAsync(ct);

            if (others.Count > 0)
            {
                foreach (var other in others)
                {
                    other.Unpin();
                }

                await db.SaveChangesAsync(ct);
            }

            package.Pin();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        });

        api.MapDelete("/packages/{packageId:guid}/pin", async (
            Guid householdId,
            Guid packageId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var package = await db.Packages.SingleOrDefaultAsync(
                candidate => candidate.Id == packageId && candidate.HouseholdId == householdId, ct);

            if (package is null)
            {
                return Results.NotFound();
            }

            package.Unpin();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/packages/{packageId:guid}/retire", async (
            Guid householdId,
            Guid packageId,
            RetirePackageRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (!Enum.TryParse<PackageState>(request.State, ignoreCase: true, out var state)
                || state is not (PackageState.Disposed or PackageState.Lost or PackageState.Archived))
            {
                return ApiResults.Invalid("state", "invalid");
            }

            var package = await db.Packages.SingleOrDefaultAsync(
                candidate => candidate.Id == packageId && candidate.HouseholdId == householdId, ct);

            if (package is null)
            {
                return Results.NotFound();
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await InventoryReader.LockHouseholdAsync(db, householdId, ct);

            var stock = await InventoryReader.LoadAsync(
                db, householdId, package.MedicationDefinitionId, tracked: true, ct);

            var remaining = stock.BalanceOf(packageId);

            // Retiring a package that still holds stock must remove that stock from the
            // household total, or the total would keep counting medication that has been
            // thrown away or lost. The write is a ledger entry, not a silent reset.
            if (remaining.IsPositive)
            {
                var legacyItemId = await db.LegacyInventoryItems.AsNoTracking()
                    .Where(item => item.MedicationDefinitionId == package.MedicationDefinitionId)
                    .Select(item => item.Id)
                    .SingleAsync(ct);

                var entryType = state == PackageState.Lost ? LedgerEntryType.Loss : LedgerEntryType.Dispose;

                db.LedgerEntries.Add(InventoryLedgerEntry.Adjustment(
                    householdId, package.MedicationDefinitionId, legacyItemId, packageId,
                    -remaining, entryType, Guid.CreateVersion7(), accountId, now, now, request.Reason));
            }

            var tracked = stock.Packages.Single(candidate => candidate.Id == packageId);
            tracked.Retire(state, now);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.NoContent();
        });

        // Marking a box lost was a one-way door: Reinstate() sat in the domain with no
        // endpoint calling it, so "I found it again" had no answer anywhere in the product.
        api.MapPost("/packages/{packageId:guid}/reinstate", async (
            Guid householdId,
            Guid packageId,
            ReinstatePackageRequest? request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var package = await db.Packages.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == packageId && candidate.HouseholdId == householdId, ct);

            if (package is null)
            {
                return Results.NotFound();
            }

            if (package.RetiredAt is not { } retiredAt)
            {
                return ApiResults.Conflict("package_not_retired");
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await InventoryReader.LockHouseholdAsync(db, householdId, ct);

            var stock = await InventoryReader.LoadAsync(
                db, householdId, package.MedicationDefinitionId, tracked: true, ct);

            // The entry this retirement wrote, if it wrote one. Filtering from the
            // package's own RetiredAt is what makes a box that has been lost, found and
            // lost again come out right: only the current retirement's entry is at or
            // after that instant, so an older pair can never be undone twice.
            var retirement = await db.LedgerEntries.AsNoTracking()
                .Where(entry => entry.HouseholdId == householdId
                                && entry.PackageId == packageId
                                && entry.RecordedAt >= retiredAt
                                && (entry.EntryType == LedgerEntryType.Loss
                                    || entry.EntryType == LedgerEntryType.Dispose))
                .OrderByDescending(entry => entry.RecordedAt)
                .FirstOrDefaultAsync(ct);

            // A package retired while empty took nothing out of the total, so putting it
            // back puts nothing in. Writing a zero entry would also be refused by
            // ck_ledger_entries_non_zero, and rightly: a stock change that changes no
            // stock is a bug, not a record.
            if (retirement is not null)
            {
                var legacyItemId = await db.LegacyInventoryItems.AsNoTracking()
                    .Where(item => item.MedicationDefinitionId == package.MedicationDefinitionId)
                    .Select(item => item.Id)
                    .SingleAsync(ct);

                db.LedgerEntries.Add(InventoryLedgerEntry.Reinstatement(
                    householdId, package.MedicationDefinitionId, legacyItemId, packageId,
                    -retirement.Quantity, Guid.CreateVersion7(), retirement.Id, accountId, now, now,
                    request?.Reason));
            }

            var tracked = stock.Packages.Single(candidate => candidate.Id == packageId);
            tracked.Reinstate();

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.NoContent();
        });

        api.MapPut("/packages/{packageId:guid}", async (
            Guid householdId,
            Guid packageId,
            UpdatePackageRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var package = await db.Packages.SingleOrDefaultAsync(
                candidate => candidate.Id == packageId && candidate.HouseholdId == householdId, ct);

            if (package is null)
            {
                return Results.NotFound();
            }

            if (!TryParseCoverage(request.Coverage, out var coverage))
            {
                return ApiResults.Invalid("coverage", "invalid");
            }

            if (request.Label is { } label && label.Trim().Length > MedicationPackage.MaximumLabelLength)
            {
                return ApiResults.Invalid("label", "label_too_long");
            }

            package.UpdateDetails(
                request.Label,
                coverage,
                request.ExpiresOn,
                request.AcquiredOn,
                request.LotNumber,
                request.Barcode,
                request.Source,
                request.StorageLocation,
                request.Note);

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/packages/{packageId:guid}/owner", async (
            Guid householdId,
            Guid packageId,
            AssignPackageRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var package = await db.Packages.SingleOrDefaultAsync(
                candidate => candidate.Id == packageId && candidate.HouseholdId == householdId, ct);

            if (package is null)
            {
                return Results.NotFound();
            }

            if (request.PersonId is { } personId
                && !await db.People.AsNoTracking().AnyAsync(
                    person => person.Id == personId && person.HouseholdId == householdId, ct))
            {
                return ApiResults.Invalid("personId", "unknown_person");
            }

            if (await db.PackageLoans.AsNoTracking().AnyAsync(
                    loan => loan.PackageId == packageId && loan.ReturnedAt == null, ct))
            {
                return ApiResults.Conflict("package_on_loan");
            }

            var now = DateTimeOffset.UtcNow;
            var previousOwner = package.OwnerPersonId;

            package.TransferOwnership(request.PersonId);
            package.TransferCustody(request.PersonId);

            db.PackageAssignmentEvents.Add(new PackageAssignmentEvent(
                Guid.CreateVersion7(), householdId, packageId, HouseholdAccess.RequireAccountId(context),
                previousOwner, request.PersonId, now));

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/packages/{packageId:guid}/loans", async (
            Guid householdId,
            Guid packageId,
            CreatePackageLoanRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var package = await db.Packages.SingleOrDefaultAsync(
                candidate => candidate.Id == packageId && candidate.HouseholdId == householdId, ct);

            if (package is null || package.OwnerPersonId is null)
            {
                return Results.NotFound();
            }

            if (package.OwnerPersonId == request.BorrowerPersonId)
            {
                return ApiResults.Invalid("borrowerPersonId", "same_person");
            }

            if (!await db.People.AsNoTracking().AnyAsync(
                    person => person.Id == request.BorrowerPersonId && person.HouseholdId == householdId, ct))
            {
                return ApiResults.Invalid("borrowerPersonId", "unknown_person");
            }

            if (await db.PackageLoans.AsNoTracking().AnyAsync(
                    loan => loan.PackageId == packageId && loan.ReturnedAt == null, ct))
            {
                return ApiResults.Conflict("already_on_loan");
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;

            var loan = new PackageLoan(
                Guid.CreateVersion7(), householdId, packageId, package.OwnerPersonId.Value,
                request.BorrowerPersonId, accountId, now);

            db.PackageLoans.Add(loan);

            // Custody moves; ownership and stock do not.
            package.TransferCustody(request.BorrowerPersonId);

            db.PackageAssignmentEvents.Add(new PackageAssignmentEvent(
                Guid.CreateVersion7(), householdId, packageId, accountId,
                package.OwnerPersonId, request.BorrowerPersonId, now));

            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/households/{householdId}/inventory/loans/{loan.Id}", new { loan.Id });
        });

        api.MapPost("/loans/{loanId:guid}/return", async (
            Guid householdId,
            Guid loanId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var loan = await db.PackageLoans.SingleOrDefaultAsync(
                candidate => candidate.Id == loanId && candidate.HouseholdId == householdId, ct);

            if (loan is null)
            {
                return Results.NotFound();
            }

            if (!loan.IsOutstanding)
            {
                return ApiResults.Conflict("already_returned");
            }

            var package = await db.Packages.SingleAsync(candidate => candidate.Id == loan.PackageId, ct);
            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;

            loan.Return(accountId, now);
            package.TransferCustody(loan.OwnerPersonId);

            db.PackageAssignmentEvents.Add(new PackageAssignmentEvent(
                Guid.CreateVersion7(), householdId, loan.PackageId, accountId,
                loan.BorrowerPersonId, loan.OwnerPersonId, now));

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return endpoints;
    }

    internal static bool TryResolveCapacity(
        AddStockRequest request,
        ExactQuantity? catalogDefault,
        out ExactQuantity capacity)
    {
        if (request.CapacityNumerator is { } numerator)
        {
            return ExactQuantity.TryCreatePositive(numerator, request.CapacityDenominator ?? 1, out capacity);
        }

        capacity = catalogDefault ?? ExactQuantity.Zero;
        return catalogDefault is not null;
    }

    private static object AddPackage(
        MedicationTrackerDbContext db,
        Guid householdId,
        Guid definitionId,
        Guid legacyItemId,
        int ordinal,
        ExactQuantity capacity,
        ExactQuantity initialAmount,
        Domain.Catalog.MedicationUnit unit,
        bool sealedPackage,
        AddStockRequest request,
        Guid accountId,
        DateTimeOffset now,
        Guid correlationId,
        Domain.Catalog.Coverage? coverage)
    {
        var package = new MedicationPackage(
            Guid.CreateVersion7(),
            householdId,
            definitionId,
            legacyItemId,
            ordinal,
            capacity,
            unit,
            sealedPackage,
            now,
            accountId,
            request.OwnerPersonId,
            request.ExpiresOn,
            request.AcquiredOn,
            request.LotNumber,
            request.Barcode,
            request.Source,
            request.StorageLocation,
            request.Note,
            coverage);

        db.Packages.Add(package);

        // An opened package may legitimately arrive empty, in which case there is no
        // acquisition to record.
        if (initialAmount.IsPositive)
        {
            db.LedgerEntries.Add(InventoryLedgerEntry.Acquire(
                householdId, definitionId, legacyItemId, package.Id, initialAmount,
                correlationId, accountId, now, now, request.Note));
        }

        return new
        {
            id = package.Id,
            ordinal = package.Ordinal,
            state = package.State.ToString(),
            nominalCapacity = Quantity(capacity),
            remaining = Quantity(initialAmount),
        };
    }

    /// <summary>Null and blank mean "not given"; anything else has to name a Coverage.</summary>
    internal static bool TryParseCoverage(string? value, out Domain.Catalog.Coverage? coverage)
    {
        coverage = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<Domain.Catalog.Coverage>(value, ignoreCase: true, out var parsed))
        {
            return false;
        }

        coverage = parsed;
        return true;
    }

    internal static object PackageView(MedicationPackage package, ExactQuantity balance) => new
    {
        id = package.Id,
        ordinal = package.Ordinal,
        label = package.Label,
        coverage = package.Coverage?.ToString(),
        state = package.State.ToString(),

        // Emptiness is derived from the ledger, never stored. ADR 0014.
        isEmpty = !balance.IsPositive,
        nominalCapacity = Quantity(package.NominalCapacity),
        remaining = Quantity(balance),
        unit = package.Unit.ToString(),
        openedAt = package.OpenedAt,
        expiresOn = package.ExpiresOn,
        acquiredOn = package.AcquiredOn,
        lotNumber = package.LotNumber,
        barcode = package.Barcode,
        source = package.Source,
        storageLocation = package.StorageLocation,
        note = package.Note,
        ownerPersonId = package.OwnerPersonId,
        holderPersonId = package.HolderPersonId,
        isPinned = package.IsPinned,
    };

    internal static object Quantity(ExactQuantity value) => new
    {
        numerator = value.Numerator,
        denominator = value.Denominator,
        display = value.ToString(),
    };
}

/// <summary>
/// Adding stock. The everyday call is a capacity plus a number of full packages; every
/// other field is an advanced option.
/// </summary>
public sealed record AddStockRequest(
    long? CapacityNumerator = null,
    long? CapacityDenominator = null,
    int FullPackages = 0,
    List<OpenedPackageInput>? OpenedPackages = null,
    long? LooseNumerator = null,
    long? LooseDenominator = null,
    Guid? OwnerPersonId = null,
    DateOnly? ExpiresOn = null,
    DateOnly? AcquiredOn = null,
    string? LotNumber = null,
    string? Barcode = null,
    string? Source = null,
    string? StorageLocation = null,
    string? Note = null,
    string? Coverage = null);

/// <summary>
/// A partly-used package. Capacity defaults to the request's capacity, so the common
/// case is just "8 left".
/// </summary>
public sealed record OpenedPackageInput(
    long RemainingNumerator,
    long RemainingDenominator = 1,
    long? CapacityNumerator = null,
    long? CapacityDenominator = null);

public sealed record RetirePackageRequest(string State, string? Reason = null);

/// <summary>Bringing a retired package back. The reason rides onto the ledger entry.</summary>
public sealed record ReinstatePackageRequest(string? Reason = null);

public sealed record UpdatePackageRequest(
    DateOnly? ExpiresOn = null,
    DateOnly? AcquiredOn = null,
    string? LotNumber = null,
    string? Barcode = null,
    string? Source = null,
    string? StorageLocation = null,
    string? Note = null,
    string? Label = null,
    string? Coverage = null);

public sealed record AssignPackageRequest(Guid? PersonId);

public sealed record CreatePackageLoanRequest(Guid BorrowerPersonId);
