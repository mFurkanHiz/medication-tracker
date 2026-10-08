using System.Reflection;

namespace MedicationTracker.Api.Modules.Platform;

/// <summary>
/// What is running: the product version and, when the build knew it, the commit.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0017: one version line for the API and the web. The number is read from the
/// repository's <c>VERSION</c> file at build time — the project file bakes it into the
/// assembly — so what a deployment reports is what the tag was cut from, never a value
/// somebody typed into a response by hand.
/// </para>
/// <para>
/// The commit comes from the image build (<c>APP_GIT_SHA</c>), so a local run says
/// <c>null</c> rather than inventing one. Anonymous on purpose: a version is not a secret,
/// and a support conversation starts with "which one are you on". The global rate limiter
/// still applies, as it does to every <c>/api</c> path.
/// </para>
/// </remarks>
public static class VersionEndpoints
{
    public static IEndpointRouteBuilder MapVersionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/version", () => Results.Ok(new
        {
            product = "medication-tracker",
            version = ProductVersion.Current,
            commit = ProductVersion.Commit,
        }));

        return endpoints;
    }
}

public static class ProductVersion
{
    /// <summary>The semantic version the assembly was built with, without build metadata.</summary>
    public static string Current { get; } = Resolve();

    /// <summary>The git commit the image was built from, or null when the build did not say.</summary>
    public static string? Commit { get; } = ResolveCommit();

    private static string Resolve()
    {
        var assembly = typeof(ProductVersion).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3);

        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0";
        }

        // Build metadata after '+' is not part of the version somebody would quote.
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? version[..plus] : version;
    }

    private static string? ResolveCommit()
    {
        var value = Environment.GetEnvironmentVariable("APP_GIT_SHA")?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
