using System.Security.Cryptography;
using System.Security.Claims;
using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Identity;

public sealed class AccountSession
{
    public string TokenHash { get; set; } = "";
    public Guid AccountId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public static class IdentityEndpoints
{
    public const string CookieName = "__Host-medication-session";
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public static async Task Authenticate(HttpContext context, RequestDelegate next)
    {
        // Legacy clients cannot choose their identity. Only validated sessions set this internal adapter header.
        context.Request.Headers.Remove("X-Account-Id");
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
                && context.Request.Headers["X-Medication-Client"] != "1")
            {
                context.Response.StatusCode = 403; return;
            }
            var token = context.Request.Cookies[CookieName];
            if (token is { Length: 64 })
            {
                var db = context.RequestServices.GetRequiredService<MedicationTrackerDbContext>();
                var hash = Hash(token);
                var session = await db.Set<AccountSession>().AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash && x.ExpiresAt > DateTimeOffset.UtcNow, context.RequestAborted);
                if (session is not null)
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, session.AccountId.ToString())], "session"));
                    context.Request.Headers["X-Account-Id"] = session.AccountId.ToString();
                }
            }
            if (!context.Request.Path.StartsWithSegments("/api/auth") && context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = 401; return;
            }
        }
        await next(context);
    }

    public static void MapIdentityEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");
        auth.MapPost("/register", async (Credentials request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!Valid(request)) return Results.BadRequest(new { error = "invalid_credentials" });
            var email = request.Email.Trim().ToUpperInvariant();
            if (await db.Accounts.AnyAsync(x => x.NormalizedEmail == email, ct)) return Results.Conflict(new { error = "account_exists" });
            var now = DateTimeOffset.UtcNow;
            var account = new Account(Guid.NewGuid(), email, now);
            account.SetPasswordHash(new PasswordHasher<Account>().HashPassword(account, request.Password));
            var household = new Household(Guid.NewGuid(), "Home", now);
            db.Accounts.Add(account);
            db.Households.Add(household);
            db.HouseholdMemberships.Add(new HouseholdMembership(Guid.NewGuid(), household.Id, account.Id, "owner", now));
            await IssueSession(context, db, account.Id, ct);
            return Results.Ok(new { accountId = account.Id, householdId = household.Id });
        });
        auth.MapPost("/login", async (Credentials request, HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!Valid(request)) return Results.Unauthorized();
            var email = request.Email.Trim().ToUpperInvariant();
            var account = await db.Accounts.SingleOrDefaultAsync(x => x.NormalizedEmail == email, ct);
            if (account?.PasswordHash is null || new PasswordHasher<Account>().VerifyHashedPassword(account, account.PasswordHash, request.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
            await IssueSession(context, db, account.Id, ct);
            return Results.Ok(new { accountId = account.Id });
        });
        auth.MapGet("/session", async (HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Results.Unauthorized();
            var now = DateTimeOffset.UtcNow;
            var households = await (from membership in db.HouseholdMemberships join household in db.Households on membership.HouseholdId equals household.Id where membership.AccountId == id && membership.ValidFrom <= now && (membership.ValidTo == null || membership.ValidTo > now) select new { household.Id, household.Name }).ToListAsync(ct);
            return Results.Ok(new { accountId = id, households });
        });
        auth.MapPost("/logout", async (HttpContext context, MedicationTrackerDbContext db, CancellationToken ct) =>
        {
            var token = context.Request.Cookies[CookieName];
            if (token is not null) { var hash = Hash(token); await db.Set<AccountSession>().Where(x => x.TokenHash == hash).ExecuteDeleteAsync(ct); }
            context.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/" });
            return Results.NoContent();
        });
    }

    private static bool Valid(Credentials request) => request.Email is { Length: > 3 and <= 320 }
        && System.Net.Mail.MailAddress.TryCreate(request.Email.Trim(), out var parsed) && parsed.Address == request.Email.Trim()
        && request.Password is { Length: >= 12 and <= 128 };

    private static async Task IssueSession(HttpContext context, MedicationTrackerDbContext db, Guid accountId, CancellationToken ct)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.AddDays(7);
        db.Set<AccountSession>().Add(new AccountSession { TokenHash = Hash(token), AccountId = accountId, ExpiresAt = expires });
        await db.SaveChangesAsync(ct);
        context.Response.Cookies.Append(CookieName, token, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", Expires = expires });
    }
}

public sealed record Credentials(string Email, string Password);
