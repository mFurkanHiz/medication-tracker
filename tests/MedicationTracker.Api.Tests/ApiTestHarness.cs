using System.Net.Http.Json;
using System.Text.Json;
using MedicationTracker.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
    public MedicationTrackerDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<MedicationTrackerDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);
}

/// <summary>Helpers that keep the intent of a test visible instead of its plumbing.</summary>
public static class ApiTestExtensions
{
    public static async Task<JsonElement> PostOk(this HttpClient client, string path, object? body = null)
    {
        var response = await client.PostAsJsonAsync(path, body ?? new { });
        await response.EnsureSuccessOrThrow(path);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<JsonElement> PutOk(this HttpClient client, string path, object body)
    {
        var response = await client.PutAsJsonAsync(path, body);
        await response.EnsureSuccessOrThrow(path);
        return response.Content.Headers.ContentLength > 0
            ? await response.Content.ReadFromJsonAsync<JsonElement>()
            : default;
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
