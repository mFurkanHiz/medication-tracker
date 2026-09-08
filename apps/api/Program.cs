using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MedicationTracker.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMedicationTrackerPersistence(builder.Configuration);
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddDbContextCheck<MedicationTrackerDbContext>(
        "database",
        tags: ["ready"]);

var app = builder.Build();

app.UseHttpsRedirection();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
