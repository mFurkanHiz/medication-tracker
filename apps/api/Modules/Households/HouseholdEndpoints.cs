using MedicationTracker.Api.Application;
using MedicationTracker.Api.Modules.People;
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

            var person = new Person(Guid.CreateVersion7(), householdId, request.Name, DateTimeOffset.UtcNow);
            db.People.Add(person);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/households/{householdId}/people/{person.Id}", new { person.Id });
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
            person.Archive(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return endpoints;
    }
}

public sealed record CreateHouseholdRequest(string Name);

public sealed record CreatePersonRequest(string Name);
