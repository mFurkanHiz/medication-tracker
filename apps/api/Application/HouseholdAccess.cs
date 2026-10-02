using System.Security.Claims;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Application;

/// <summary>
/// Resolves who is calling and whether they may act on a household.
/// </summary>
/// <remarks>
/// Identity is read from <see cref="HttpContext.User"/>, which the session middleware
/// populates. The previous design re-injected a validated account id into the
/// <c>X-Account-Id</c> request header and bound it with <c>[FromHeader]</c>; that was
/// safe because the middleware stripped the client's own copy first, but it made
/// request-header rewriting load-bearing for authorisation. Reading the principal
/// removes that dependency — see ADR 0013, finding 9.
/// </remarks>
public static class HouseholdAccess
{
    /// <summary>
    /// The authenticated account, or null when the request is unauthenticated.
    /// </summary>
    public static Guid? AccountId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var claim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>
    /// The authenticated account, for endpoints the middleware has already rejected
    /// anonymous access to.
    /// </summary>
    public static Guid RequireAccountId(HttpContext context) =>
        AccountId(context) ?? throw new InvalidOperationException(
            "The endpoint ran without an authenticated principal.");

    /// <summary>
    /// Whether the caller currently holds membership of the household.
    /// </summary>
    /// <remarks>
    /// Membership is effective-dated, so a revoked membership stops granting access
    /// without its history being deleted. Every household-scoped endpoint must call
    /// this before reading or writing anything.
    /// </remarks>
    public static async Task<bool> IsMemberAsync(
        MedicationTrackerDbContext db,
        Guid householdId,
        HttpContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (AccountId(context) is not { } accountId)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        return await db.HouseholdMemberships.AsNoTracking().AnyAsync(
            membership => membership.HouseholdId == householdId
                          && membership.AccountId == accountId
                          && membership.ValidFrom <= now
                          && (membership.ValidTo == null || membership.ValidTo > now),
            ct);
    }
}

/// <summary>
/// Problem-details helpers so every endpoint reports failures the same way and clients
/// can translate a stable code rather than parsing prose.
/// </summary>
public static class ApiResults
{
    public static IResult Forbidden() => Results.StatusCode(StatusCodes.Status403Forbidden);

    public static IResult Invalid(string field, string code) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [code] });

    public static IResult Conflict(string code) => Results.Conflict(new { code });
}
