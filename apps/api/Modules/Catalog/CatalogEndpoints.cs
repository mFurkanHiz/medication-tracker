using System.Text.Json;
using MedicationTracker.Api.Application;
using MedicationTracker.Api.Domain.Catalog;
using MedicationTracker.Api.Domain.Quantities;
using MedicationTracker.Api.Modules.Audit;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.Treatments;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Catalog;

/// <summary>
/// The reusable medication catalog. Separate from stock: creating a definition adds
/// nothing to inventory, and adding stock selects a definition rather than inventing one.
/// </summary>
public static class CatalogEndpoints
{
    public const int MaximumTags = 12;
    public const int MaximumTagLength = 40;
    public const int MaximumActiveIngredients = 12;

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/households/{householdId:guid}/medication-definitions");

        api.MapPost("/", async (
            Guid householdId,
            MedicationDefinitionRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (!TryValidate(request, out var parsed, out var field))
            {
                return ApiResults.Invalid(field, "invalid");
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;

            var definition = new MedicationDefinition(
                Guid.CreateVersion7(),
                householdId,
                request.Name,
                parsed.Form,
                parsed.Unit,
                now,
                request.Strength,
                request.Brand,
                request.Manufacturer,
                parsed.ActiveIngredients,
                parsed.DefaultPackageCapacity,
                request.Category,
                parsed.Tags,
                request.Notes,
                parsed.Cautions);

            db.MedicationDefinitions.Add(definition);

            // Keep the legacy one-per-medication row alive so ledger and count foreign
            // keys stay satisfiable. See ADR 0014.
            db.LegacyInventoryItems.Add(
                new LegacyInventoryItem(Guid.CreateVersion7(), householdId, definition.Id, now));

            db.MedicationDefinitionChangeEvents.Add(new MedicationDefinitionChangeEvent(
                Guid.CreateVersion7(), householdId, definition.Id, accountId,
                ChangeKind.Created, null, Snapshot(definition), now));

            await db.SaveChangesAsync(ct);

            return Results.Created(
                $"/api/households/{householdId}/medication-definitions/{definition.Id}",
                new { definition.Id });
        });

        api.MapPut("/{definitionId:guid}", async (
            Guid householdId,
            Guid definitionId,
            MedicationDefinitionRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (!TryValidate(request, out var parsed, out var field))
            {
                return ApiResults.Invalid(field, "invalid");
            }

            var definition = await db.MedicationDefinitions.SingleOrDefaultAsync(
                candidate => candidate.Id == definitionId && candidate.HouseholdId == householdId, ct);

            if (definition is null)
            {
                return Results.NotFound();
            }

            var before = Snapshot(definition);

            // Editing the catalog default capacity deliberately does not touch any
            // existing package: each one snapshots its own. ADR 0014 invariant 4.
            definition.UpdateDetails(
                request.Name,
                parsed.Form,
                parsed.Unit,
                request.Strength,
                request.Brand,
                request.Manufacturer,
                parsed.ActiveIngredients,
                parsed.DefaultPackageCapacity,
                request.Category,
                parsed.Tags,
                request.Notes,
                parsed.Cautions);

            db.MedicationDefinitionChangeEvents.Add(new MedicationDefinitionChangeEvent(
                Guid.CreateVersion7(), householdId, definition.Id, HouseholdAccess.RequireAccountId(context),
                ChangeKind.Updated, before, Snapshot(definition), DateTimeOffset.UtcNow));

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapDelete("/{definitionId:guid}", async (
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

            var definition = await db.MedicationDefinitions.SingleOrDefaultAsync(
                candidate => candidate.Id == definitionId && candidate.HouseholdId == householdId, ct);

            if (definition is null)
            {
                return Results.NotFound();
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;
            var before = Snapshot(definition);

            definition.Archive(now);

            // An archived medication must not leave an active plan pointing at it, but
            // historical administrations keep their links.
            //
            // This used to soft-delete those plans, and nothing could bring one back, so
            // archiving silently destroyed the dose, the times, the weekdays and the
            // instruction note. Pausing says the same thing without the loss: the plans
            // stop, the adherence replay stops counting them, and restoring the medication
            // leaves them there to be resumed one at a time.
            await PlanDeactivation.PauseAsync(
                db,
                householdId,
                plan => plan.MedicationDefinitionId == definitionId,
                accountId,
                now,
                ct);

            db.MedicationDefinitionChangeEvents.Add(new MedicationDefinitionChangeEvent(
                Guid.CreateVersion7(), householdId, definition.Id, accountId,
                ChangeKind.Archived, before, Snapshot(definition), now));

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/{definitionId:guid}/restore", async (
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

            var definition = await db.MedicationDefinitions.SingleOrDefaultAsync(
                candidate => candidate.Id == definitionId && candidate.HouseholdId == householdId, ct);

            if (definition is null)
            {
                return Results.NotFound();
            }

            var before = Snapshot(definition);
            definition.Restore();

            db.MedicationDefinitionChangeEvents.Add(new MedicationDefinitionChangeEvent(
                Guid.CreateVersion7(), householdId, definition.Id, HouseholdAccess.RequireAccountId(context),
                ChangeKind.Restored, before, Snapshot(definition), DateTimeOffset.UtcNow));

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return endpoints;
    }

    /// <summary>
    /// Validates and normalises a catalog request. Enum values arrive as names so the
    /// wire format stays readable and a renumbering cannot silently change meaning.
    /// </summary>
    internal static bool TryValidate(
        MedicationDefinitionRequest request,
        out ParsedDefinition parsed,
        out string field)
    {
        parsed = default;
        field = "name";

        if (request is null
            || string.IsNullOrWhiteSpace(request.Name)
            || request.Name.Trim().Length > 200)
        {
            return false;
        }

        field = "form";
        if (!Enum.TryParse<PharmaceuticalForm>(request.Form, ignoreCase: true, out var form))
        {
            return false;
        }

        field = "unit";
        var unit = FormDefaults.DefaultUnitFor(form);
        if (request.Unit is not null && !Enum.TryParse(request.Unit, ignoreCase: true, out unit))
        {
            return false;
        }

        field = "tags";
        var tags = Normalize(request.Tags, MaximumTags);
        if (tags is null)
        {
            return false;
        }

        field = "activeIngredients";
        var ingredients = Normalize(request.ActiveIngredients, MaximumActiveIngredients);
        if (ingredients is null)
        {
            return false;
        }

        field = "defaultPackageCapacity";
        ExactQuantity? capacity = null;
        if (request.DefaultPackageCapacityNumerator is { } numerator)
        {
            if (!ExactQuantity.TryCreatePositive(
                    numerator, request.DefaultPackageCapacityDenominator ?? 1, out var parsedCapacity))
            {
                return false;
            }

            capacity = parsedCapacity;
        }

        field = "strength";
        if (TooLong(request.Strength, 100) || TooLong(request.Brand, 200) || TooLong(request.Manufacturer, 200)
            || TooLong(request.Category, 100) || TooLong(request.Notes, 2000))
        {
            return false;
        }

        // The household's own safety notes. Checked here so an over-long one is a 400
        // naming the field rather than an exception from the entity.
        field = "cautions";
        var cautions = new CautionNotes(
            request.CautionDoNotTakeWith,
            request.CautionFoodsToAvoid,
            request.CautionThingsToDo,
            request.CautionThingsToAvoid,
            request.CautionWarning);

        if (!cautions.IsValid())
        {
            return false;
        }

        field = string.Empty;
        parsed = new ParsedDefinition(form, unit, ingredients, tags, capacity, cautions.Normalized());
        return true;
    }

    private static bool TooLong(string? value, int maximum) => value is not null && value.Trim().Length > maximum;

    private static string[]? Normalize(string[]? values, int maximumCount)
    {
        var normalized = (values ?? [])
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length <= maximumCount && normalized.All(value => value.Length <= MaximumTagLength)
            ? normalized
            : null;
    }

    internal static string Snapshot(MedicationDefinition definition) => JsonSerializer.Serialize(new
    {
        definition.Name,
        definition.Strength,
        definition.Brand,
        definition.Manufacturer,
        Form = definition.Form.ToString(),
        Unit = definition.Unit.ToString(),
        definition.ActiveIngredients,
        DefaultPackageCapacity = definition.DefaultPackageCapacity?.ToString(),
        definition.Category,
        definition.Tags,
        definition.Notes,
        definition.CautionDoNotTakeWith,
        definition.CautionFoodsToAvoid,
        definition.CautionThingsToDo,
        definition.CautionThingsToAvoid,
        definition.CautionWarning,
        definition.IsArchived,
    });

    internal readonly record struct ParsedDefinition(
        PharmaceuticalForm Form,
        MedicationUnit Unit,
        string[] ActiveIngredients,
        string[] Tags,
        ExactQuantity? DefaultPackageCapacity,
        CautionNotes Cautions);
}

/// <summary>
/// A catalog create or update. Form and unit are enum names; capacity is an exact
/// numerator/denominator pair so a default of "half a sachet" is representable.
/// </summary>
public sealed record MedicationDefinitionRequest(
    string Name,
    string Form,
    string? Unit = null,
    string? Strength = null,
    string? Brand = null,
    string? Manufacturer = null,
    string[]? ActiveIngredients = null,
    long? DefaultPackageCapacityNumerator = null,
    long? DefaultPackageCapacityDenominator = null,
    string? Category = null,
    string[]? Tags = null,
    string? Notes = null,
    string? CautionDoNotTakeWith = null,
    string? CautionFoodsToAvoid = null,
    string? CautionThingsToDo = null,
    string? CautionThingsToAvoid = null,
    string? CautionWarning = null);
