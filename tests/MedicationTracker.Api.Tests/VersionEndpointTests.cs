using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// The version a running API reports is the repository's, not a typed one (ADR 0017).
/// </summary>
public sealed class VersionEndpointTests
{
    [PostgreSqlFact]
    public async Task The_version_endpoint_reports_the_repository_VERSION_file_and_needs_no_session()
    {
        await using var harness = new ApiTestHarness();
        var client = harness.NewClient();

        var response = await client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("medication-tracker", body.GetProperty("product").GetString());

        // The project file reads VERSION at build time; this reads the same file at test
        // time, so a version typed anywhere else would fail here.
        var expected = File.ReadAllText(RepositoryFile("VERSION")).Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+$", expected);
        Assert.Equal(expected, body.GetProperty("version").GetString());

        // Null outside an image build, a commit hash inside one — never a made-up value.
        var commit = body.GetProperty("commit");
        Assert.True(
            commit.ValueKind == JsonValueKind.Null
            || Regex.IsMatch(commit.GetString()!, "^[0-9a-f]{7,40}$"),
            $"commit was {commit.GetRawText()}");
    }

    private static string RepositoryFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{name} was not found above {AppContext.BaseDirectory}.");
    }
}
