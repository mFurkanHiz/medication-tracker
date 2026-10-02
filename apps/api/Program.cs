using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MedicationTracker.Api.Persistence;
using MedicationTracker.Api.Modules.Administrations;
using MedicationTracker.Api.Modules.Catalog;
using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.Refill;
using MedicationTracker.Api.Modules.Treatments;
using MedicationTracker.Api.Modules.Workspace;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMedicationTrackerPersistence();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
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

app.Run();

public partial class Program;
