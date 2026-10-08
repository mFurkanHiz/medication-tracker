using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MedicationTracker.Api.Persistence;
using MedicationTracker.Api.Modules.Administrations;
using MedicationTracker.Api.Modules.Catalog;
using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.Refill;
using MedicationTracker.Api.Modules.Reports;
using MedicationTracker.Api.Modules.Treatments;
using MedicationTracker.Api.Modules.Workspace;
using MedicationTracker.Api.Modules.Platform;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMedicationTrackerPersistence();

// Rate limiting
// -------------
// The limiter used to cover /api/auth only, which left a real hole rather than a
// theoretical one: IdentityEndpoints.Authenticate runs on every /api path and, whenever a
// 64-character token is presented in a cookie or a bearer header, looks that token up in
// identity.sessions BEFORE any authorisation decision. An attacker with no credentials
// could therefore drive one database round trip per request, unbounded, against a
// PostgreSQL this project shares a host with.
//
// The partition is the remote address, deliberately, even though a session would be more
// precise for an authenticated household. A token-derived key can be rotated on every
// request, which defeats the limit entirely; an address cannot be. The cost is that a
// household behind one NAT shares a budget, which the limit below is generous enough to
// absorb.
//
// Both limits are configuration, not constants. A rate limit that can only be changed by
// a deployment is a rate limit nobody tunes, and the test suite needs to set its own.
//
// They are read per partition rather than here, which looks like the long way round and
// is not. builder.Configuration is already built by this line, so an eager read silently
// ignores any layer added later — a WebApplicationFactory override, or a provider a future
// deployment adds. It would not fail; it would quietly use the default and look correct.
var rateLimitWindow = TimeSpan.FromMinutes(1);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    // Without this a 429 tells a client nothing, and the honest behaviour of the offline
    // outbox — keep retrying until the dose lands — becomes hammering.
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter =
            ((int)rateLimitWindow.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };

    // Stricter, and layered on top of the global limiter below: this one is about
    // guessing a password, where even a slow attempt rate is an attack.
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        ClientPartition(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Permits(context, "AuthPermitsPerMinute", 30),
            Window = rateLimitWindow,
            QueueLimit = 0,
        }));

    // Global rather than per-endpoint on purpose. Remembering to add a limit to each new
    // endpoint is exactly the thing that gets forgotten, and the endpoint somebody forgets
    // is the one that was not reviewed. Health checks stay unlimited: the container's own
    // probe hits them, and throttling it would make the deployment report itself unhealthy.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetFixedWindowLimiter(
                ClientPartition(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Permits(context, "ApiPermitsPerMinute", 600),
                    Window = rateLimitWindow,
                    QueueLimit = 0,
                })
            : RateLimitPartition.GetNoLimiter<string>("not-the-api"));
});

static string ClientPartition(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

// Resolved from the request's services, so the value comes from the configuration the
// application is actually running with. The factory runs once per partition, not per
// request, so this costs a lookup per distinct caller rather than per call.
static int Permits(HttpContext context, string key, int fallback) =>
    context.RequestServices.GetRequiredService<IConfiguration>()
        .GetValue($"RateLimit:{key}", fallback);

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddDbContextCheck<MedicationTrackerDbContext>(
        "database",
        tags: ["ready"]);

var app = builder.Build();

app.UseRateLimiter();
app.Use(IdentityEndpoints.Authenticate);

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapHealthChecks("/health");

// One registration per module boundary. See ADR 0013 for the boundaries themselves.
app.MapIdentityEndpoints();
app.MapHouseholdEndpoints();
app.MapCatalogEndpoints();
app.MapInventoryEndpoints();
app.MapTreatmentEndpoints();
app.MapAdministrationEndpoints();
app.MapRefillEndpoints();
app.MapWorkspaceEndpoints();
app.MapReportEndpoints();
app.MapExportEndpoints();
app.MapVersionEndpoints();

app.Run();

public partial class Program;
