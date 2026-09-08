using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string MigrationsHistorySchema = "infrastructure";

    public static IServiceCollection AddMedicationTrackerPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Database must be supplied through runtime configuration.");
        }

        services.AddDbContext<MedicationTrackerDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    MigrationsHistorySchema)));

        return services;
    }
}
