using MedicationTracker.Api.Application;
using MedicationTracker.Api.Modules.People;
using MedicationTracker.Api.Modules.Sync;
using MedicationTracker.Api.Modules.Treatments;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Households;

public static class HouseholdEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost("/households", async (
            CreateHouseholdRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            var accountId = HouseholdAccess.RequireAccountId(context);

            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            {
                return ApiResults.Invalid("name", "required");
            }

            var now = DateTimeOffset.UtcNow;
            var household = new Household(Guid.CreateVersion7(), request.Name.Trim(), now);

            db.Households.Add(household);
            db.HouseholdMemberships.Add(
                new HouseholdMembership(Guid.CreateVersion7(), household.Id, accountId, "owner", now));

            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/households/{household.Id}", new { household.Id });
        });

        api.MapPost("/households/{householdId:guid}/people", async (
            Guid householdId,
            CreatePersonRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            {
                return ApiResults.Invalid("name", "required");
            }

            if (!SyncReplay.IsValidKey(request.IdempotencyKey))
            {
                return ApiResults.Invalid("idempotencyKey", "invalid");
            }

            var accountId = HouseholdAccess.RequireAccountId(context);
            var now = DateTimeOffset.UtcNow;

            // A phone creates the person offline under an id of its own and sends the
            // command later, perhaps twice. The receipt makes the second send answer with
            // the person already created; the lock keeps two sends apart.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await InventoryReader.LockHouseholdAsync(db, householdId, ct);

            if (await SyncReplay.PriorAsync(db, householdId, request.IdempotencyKey, ct) is { } prior)
            {
                return Results.Ok(new { id = prior.ResultEntityId, replayed = true });
            }

            var id = SyncReplay.ClientId(request.Id) ?? Guid.CreateVersion7();
            if (await db.People.AsNoTracking().AnyAsync(candidate => candidate.Id == id, ct))
            {
                return ApiResults.Conflict("id_in_use");
            }

            var person = new Person(id, householdId, request.Name, now);
            db.People.Add(person);
            SyncReplay.Record(db, householdId, accountId, request.IdempotencyKey, "people.create", person.Id, now);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Created($"/api/households/{householdId}/people/{person.Id}", new { person.Id, replayed = false });
        });

        api.MapPut("/households/{householdId:guid}/people/{personId:guid}", async (
            Guid householdId,
            Guid personId,
            CreatePersonRequest request,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            {
                return ApiResults.Invalid("name", "required");
            }

            var person = await db.People.SingleOrDefaultAsync(
                candidate => candidate.Id == personId && candidate.HouseholdId == householdId, ct);

            if (person is null)
            {
                return Results.NotFound();
            }

            person.Rename(request.Name);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapDelete("/households/{householdId:guid}/people/{personId:guid}", async (
            Guid householdId,
            Guid personId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var person = await db.People.SingleOrDefaultAsync(
                candidate => candidate.Id == personId && candidate.HouseholdId == householdId, ct);

            if (person is null)
            {
                return Results.NotFound();
            }

            // Archive rather than delete: packages, plans and administrations still
            // point at this person and their history must stay readable.
            var now = DateTimeOffset.UtcNow;
            person.Archive(now);

            // Archiving a person had no cascade at all, so their plans kept producing
            // doses on Today for ever — with their name on the row and nothing to say
            // they had been archived. Their plans are now set aside with them, which also
            // keeps the paused stretch out of the adherence report instead of accruing
            // missed doses nobody could have taken.
            await PlanDeactivation.PauseAsync(
                db,
                householdId,
                plan => plan.PersonId == personId,
                HouseholdAccess.RequireAccountId(context),
                now,
                ct);

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Person.Restore() existed in the domain from the beginning and no route ever
        // called it, so archiving a person was a one-way door — worse than the medication
        // one, which at least had this.
        api.MapPost("/households/{householdId:guid}/people/{personId:guid}/restore", async (
            Guid householdId,
            Guid personId,
            HttpContext context,
            MedicationTrackerDbContext db,
            CancellationToken ct) =>
        {
            if (!await HouseholdAccess.IsMemberAsync(db, householdId, context, ct))
            {
                return ApiResults.Forbidden();
            }

            var person = await db.People.SingleOrDefaultAsync(
                candidate => candidate.Id == personId && candidate.HouseholdId == householdId, ct);

            if (person is null)
            {
                return Results.NotFound();
            }

            // Their plans stay paused. Bringing somebody back into the household is not a
            // statement that they have started taking their medicines again; resuming each
            // plan is a separate decision, and it is theirs to make.
            person.Restore();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return endpoints;
    }
}

public sealed record CreateHouseholdRequest(string Name);

/// <summary>The phone may name the id and carry a key; the web sends neither.</summary>
public sealed record CreatePersonRequest(string Name, Guid? Id = null, string? IdempotencyKey = null);
