using System.Net.Http.Json;
using System.Text.Json;
using MedicationTracker.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    private readonly Dictionary<string, string?> _settings;
    private readonly CapturedLogs? _logs;

    /// <param name="settings">
    /// Configuration to layer over the application's own, for the few tests that need to
    /// change how the application behaves rather than what it stores — the rate limits,
    /// for instance, which are set absurdly high for every other test so the suite's own
    /// traffic never trips them.
    /// </param>
    /// <param name="captureLogs">
    /// Collects everything the application logs, at <see cref="LogLevel.Trace"/> and
    /// across every category, into <see cref="Logs"/>.
    /// </param>
    public ApiTestHarness(
        IReadOnlyDictionary<string, string?>? settings = null,
        bool captureLogs = false)
    {
        // A fixed window of a minute means a shared partition: the suite runs hundreds of
        // requests in seconds, all from TestServer with no remote address, so the
        // production figure would make unrelated tests fail each other. Raised here rather
        // than in the application, where the real figure belongs.
        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = ConnectionString,
            ["RateLimit:ApiPermitsPerMinute"] = "1000000",
            ["RateLimit:AuthPermitsPerMinute"] = "1000000",
        };

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }

        _logs = captureLogs ? new CapturedLogs() : null;

        if (captureLogs)
        {
            // SetMinimumLevel is not enough on its own: the category rules the application
            // ships in appsettings.json take precedence over it, so a capture that only
            // called SetMinimumLevel would silently miss every category the application
            // filters — including the EF command category, which is the one that would
            // carry parameter values. This layer is added last, so it wins, and the capture
            // really is everything at Trace.
            _settings["Logging:LogLevel:Default"] = "Trace";
            _settings["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace";
            _settings["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Trace";
            _settings["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Trace";
        }
    }

    /// <summary>Everything the application logged, when the harness was asked to capture.</summary>
    public CapturedLogs Logs =>
        _logs ?? throw new InvalidOperationException("Construct the harness with captureLogs: true.");

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable("MEDICATION_TRACKER_TEST_POSTGRES");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(_settings));

        if (_logs is not null)
        {
            builder.ConfigureLogging(logging =>
            {
                // Trace, and no category filtered out. A redaction test that only looked at
                // the levels production happens to emit today would stop being evidence the
                // moment somebody raised a level to debug something.
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(new CapturingLoggerProvider(_logs));
            });
        }
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

/// <summary>
/// Everything the application logged during a test, as plain text.
/// </summary>
/// <remarks>
/// Flat and stringly-typed on purpose. A log leak is a leak of the rendered line — the
/// thing that lands in the container log on a shared host and gets read by whoever can
/// read that host — so the assertion has to be made against the rendered line, not against
/// a structured event the test reassembles to its own liking.
/// </remarks>
public sealed class CapturedLogs
{
    private readonly List<string> _lines = [];
    private readonly Lock _gate = new();

    internal void Add(string line)
    {
        lock (_gate)
        {
            _lines.Add(line);
        }
    }

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }

    /// <summary>Every logged line containing <paramref name="value"/>, case-insensitively.</summary>
    public IReadOnlyList<string> Mentioning(string value) =>
        [.. Lines.Where(line => line.Contains(value, StringComparison.OrdinalIgnoreCase))];
}

internal sealed class CapturingLoggerProvider(CapturedLogs logs) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, logs);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, CapturedLogs logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // The exception is appended because an exception message is the likeliest way a
            // value reaches a log without anybody deciding to log it: a validation failure
            // that embeds what was rejected, a database error that quotes the row.
            var line = $"{logLevel} {category}: {formatter(state, exception)}";

            if (exception is not null)
            {
                line += $" | {exception}";
            }

            logs.Add(line);
        }
    }
}
