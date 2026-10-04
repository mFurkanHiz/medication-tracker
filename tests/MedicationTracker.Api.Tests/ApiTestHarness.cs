using System.Net.Http.Json;
using System.Text.Json;
using MedicationTracker.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Marks a test that needs a real PostgreSQL server.
/// </summary>
/// <remarks>
/// Database behaviour — advisory-lock serialisation, check constraints, unique
/// filtered indexes, concurrent transactions — cannot be demonstrated against an
/// in-memory provider, so these tests skip rather than pretend when no server is
/// configured. CI always provides one.
/// </remarks>
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ApiTestHarness.ConnectionString))
        {
            Skip = "Set MEDICATION_TRACKER_TEST_POSTGRES to run PostgreSQL integration tests.";
        }
    }
}

/// <summary>
/// Boots the real API against PostgreSQL and drives it over HTTP, so tests exercise
/// authentication, authorisation, transactions and constraints exactly as a client does.
/// </summary>
public sealed class ApiTestHarness : WebApplicationFactory<Program>
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool _schemaApplied;

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = ConnectionString,
            }));
    }

    /// <summary>
    /// Applies migrations once per test process, before the first request reaches the
    /// application.
    /// </summary>
    /// <remarks>
    /// The application deliberately does not migrate at startup — production applies the
    /// packaged SQL from a controlled deployment step instead. Doing it here, rather than
    /// at the top of each test, means no test can accidentally run against an unmigrated
    /// database, and the gate keeps test classes running in parallel from migrating at
    /// the same time.
    /// </remarks>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        EnsureSchema();
        return host;
    }

    private void EnsureSchema()
    {
        if (_schemaApplied)
        {
            return;
        }

        SchemaGate.Wait();
        try
        {
            if (_schemaApplied)
            {
                return;
            }

            using var db = NewDbContext();
            db.Database.Migrate();
            _schemaApplied = true;
        }
        finally
        {
            SchemaGate.Release();
        }
    }

    /// <summary>
    /// A client carrying the custom header the API requires on mutations. Its absence is
    /// what stops a cross-site form post from reaching an endpoint at all.
    /// </summary>
    public HttpClient NewClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        client.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        return client;
    }

    /// <summary>
    /// Registers a fresh synthetic account and returns a signed-in client plus the
    /// household the registration created.
    /// </summary>
    public async Task<(HttpClient Client, Guid HouseholdId)> NewHouseholdAsync()
    {
        var client = NewClient();
        var email = $"synthetic-{Guid.CreateVersion7():N}@example.invalid";

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "synthetic-password-1",
            confirmPassword = "synthetic-password-1",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (client, body.GetProperty("householdId").GetGuid());
    }

    /// <summary>
    /// A context the caller owns, for asserting on what the API actually wrote.
    /// </summary>
    /// <remarks>
    /// Built from the connection string rather than resolved from the host's scope, so
    /// disposing it cannot tear down a scope the application is still using, and so each
    /// assertion reads committed state rather than a request's change tracker.
    /// </remarks>
    /// <summary>
    /// A context configured the way the application configures its own.
    /// </summary>
    /// <remarks>
    /// The migrations history table is the part that matters. The application records it
    /// as <c>infrastructure.__ef_migrations_history</c>; a bare <c>UseNpgsql</c> reads
    /// EF's default <c>public."__EFMigrationsHistory"</c> instead. With the two
    /// disagreeing, the suite passed only because CI always starts from an empty
    /// database: the harness would create the whole schema and record it in a table
    /// production never reads. The moment anything migrated with the application's own
    /// configuration — <c>dotnet ef database update</c>, or the deployment's packaged
    /// SQL — the harness saw an empty history beside a full schema and tried to create
    /// the world a second time.
    /// </remarks>
    public MedicationTrackerDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<MedicationTrackerDbContext>()
            .UseNpgsql(
                ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    PersistenceServiceCollectionExtensions.MigrationsHistorySchema))
            .Options);
}

/// <summary>Helpers that keep the intent of a test visible instead of its plumbing.</summary>
public static class ApiTestExtensions
{
    public static async Task<JsonElement> PostOk(this HttpClient client, string path, object? body = null)
    {
        var response = await client.PostAsJsonAsync(path, body ?? new { });
        await response.EnsureSuccessOrThrow(path);
        return await response.ReadJsonOrEmpty();
    }

    public static async Task<Guid> PostId(this HttpClient client, string path, object body, string property = "id")
    {
        var element = await client.PostOk(path, body);
        return element.GetProperty(property).GetGuid();
    }

    public static async Task<JsonElement> GetOk(this HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        await response.EnsureSuccessOrThrow(path);
        return await response.ReadJsonOrEmpty();
    }

    public static async Task<JsonElement> DeleteOk(this HttpClient client, string path)
    {
        var response = await client.DeleteAsync(path);
        await response.EnsureSuccessOrThrow(path);
        return await response.ReadJsonOrEmpty();
    }

    public static async Task<JsonElement> PutOk(this HttpClient client, string path, object body)
    {
        var response = await client.PutAsJsonAsync(path, body);
        await response.EnsureSuccessOrThrow(path);
        return await response.ReadJsonOrEmpty();
    }

    /// <summary>
    /// Reads a JSON body, tolerating the empty one a 204 carries.
    /// </summary>
    /// <remarks>
    /// Commands whose result is just "it worked" — pin, unpin, retire, archive — return
    /// No Content rather than an invented body, so a helper that always deserialises
    /// would fail on a correct response.
    /// </remarks>
    private static async Task<JsonElement> ReadJsonOrEmpty(this HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return default;
        }

        var payload = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(payload) ? default : JsonDocument.Parse(payload).RootElement.Clone();
    }

    /// <summary>The <c>code</c> from a refusal body, so tests assert on a stable reason.</summary>
    public static async Task<string> RefusalCode(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() ?? "" : "";
    }

    /// <summary>Renders an exact quantity response as its invariant string, e.g. "3/2".</summary>
    public static string Quantity(this JsonElement element) =>
        element.GetProperty("display").GetString() ?? "";

    private static async Task EnsureSuccessOrThrow(this HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            $"{response.RequestMessage?.Method} {path} returned {(int)response.StatusCode}: {body}");
    }
}
