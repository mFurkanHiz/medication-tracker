using System.Text.RegularExpressions;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// The confined database role (acceptance row 37) only gets rights on the schemas named in
/// deploy/least-privilege-database-role.sql. A schema the model uses but the file does not
/// name is unreadable to the live API, and the symptom is a 500 on every query that touches
/// it — which the CI suite cannot see, because it connects as the superuser. This is the
/// check that stands in for that: the two lists must agree.
/// </summary>
public sealed class LeastPrivilegeSchemaCoverageTests
{
    [Fact]
    public void Every_schema_the_model_maps_is_granted_to_the_confined_role()
    {
        using var dbContext = new MedicationTrackerDbContext(
            new DbContextOptionsBuilder<MedicationTrackerDbContext>()
                .UseNpgsql("Host=localhost;Database=model_validation")
                .Options);

        var used = dbContext.Model.GetEntityTypes()
            .Select(entity => entity.GetSchema())
            .Where(schema => !string.IsNullOrEmpty(schema))
            .Select(schema => schema!)
            .Distinct()
            .OrderBy(schema => schema, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(used);

        var sql = File.ReadAllText(RepositoryFile("deploy/least-privilege-database-role.sql"));
        var array = Regex.Match(sql, @"FOREACH target IN ARRAY ARRAY\[(?<list>[^\]]*)\]", RegexOptions.Singleline);
        Assert.True(array.Success, "The schema list was not found in least-privilege-database-role.sql.");

        var granted = Regex.Matches(array.Groups["list"].Value, @"'(?<name>[a-z_]+)'")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = used.Where(schema => !granted.Contains(schema)).ToList();

        Assert.True(
            missing.Count == 0,
            $"Schemas used by the model but not granted to medication_tracker_app: {string.Join(", ", missing)}.");
    }

    private static string RepositoryFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{relative} was not found above {AppContext.BaseDirectory}.");
    }
}
