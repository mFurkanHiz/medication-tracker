using System.Net;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedicationTracker.Api.Tests;

public sealed class PostgreSqlIntegrationTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Migrations_health_check_and_transaction_rollback_work()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "MEDICATION_TRACKER_TEST_POSTGRES")!;

        await using var factory = new PostgreSqlApiFactory(connectionString);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MedicationTrackerDbContext>();
        await dbContext.Database.MigrateAsync();

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var accountId = Guid.NewGuid();
        dbContext.Accounts.Add(new Account(
            accountId,
            $"INTEGRATION-{accountId:N}@EXAMPLE.INVALID",
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
        Assert.True(await dbContext.Accounts.AnyAsync(account => account.Id == accountId));
        await transaction.RollbackAsync();
    }

    private sealed class PostgreSqlApiFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = connectionString
                });
            });
        }
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES")))
        {
            Skip = "Set MEDICATION_TRACKER_TEST_POSTGRES to run PostgreSQL integration tests.";
        }
    }
}
