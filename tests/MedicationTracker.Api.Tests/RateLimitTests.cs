using System.Net;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// What the API does when somebody asks too often.
/// </summary>
/// <remarks>
/// <para>
/// The limiter used to cover <c>/api/auth</c> only, and the threat model recorded that as
/// "whether dose recording and sync need their own limits is undecided". Mapping the
/// request pipeline settled it, because the gap was concrete rather than theoretical:
/// <c>IdentityEndpoints.Authenticate</c> runs on every <c>/api</c> path and, whenever a
/// 64-character token arrives in a cookie or a bearer header, looks it up in
/// <c>identity.sessions</c> <b>before</b> any authorisation decision. An attacker with no
/// credentials could therefore drive one database round trip per request, unbounded,
/// against a PostgreSQL this project shares a host with.
/// </para>
/// <para>
/// So the limiter is global, and these tests pin the two halves that are easy to get
/// wrong in opposite directions: it has to sit in front of that lookup, and it must not be
/// tight enough to break a phone draining a fortnight of doses.
/// </para>
/// <para>All data is synthetic.</para>
/// </remarks>
public sealed class RateLimitTests
{
    /// <summary>The figures the application actually ships, so the test moves when they do.</summary>
    private const int ProductionApiPermits = 600;

    [PostgreSqlFact]
    public async Task An_unauthenticated_flood_is_refused_before_it_reaches_the_session_table()
    {
        await using var harness = new ApiTestHarness(new Dictionary<string, string?>
        {
            ["RateLimit:ApiPermitsPerMinute"] = "3",
        });

        var client = harness.NewClient();

        // A well-formed but invented token: 64 hex characters is exactly what makes the
        // middleware go to the database. This is the request an attacker would repeat.
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {new string('a', 64)}");

        var codes = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.GetAsync($"/api/households/{Guid.NewGuid()}/today");
            codes.Add(response.StatusCode);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // A 429 with no Retry-After teaches a client nothing, and the honest
                // behaviour of the offline outbox — keep trying until the dose lands —
                // then becomes hammering.
                Assert.True(
                    response.Headers.TryGetValues("Retry-After", out var retryAfter),
                    "A 429 must say when to come back.");
                Assert.Equal("60", retryAfter!.Single());
            }
        }

        // The first three are refused by authorisation, as they should be. What matters is
        // that the rest never got as far as asking.
        Assert.Equal(3, codes.Count(code => code == HttpStatusCode.Unauthorized));
        Assert.Equal(3, codes.Count(code => code == HttpStatusCode.TooManyRequests));
    }

    [PostgreSqlFact]
    public async Task A_phone_draining_a_fortnight_of_doses_is_never_refused()
    {
        // The production figure, not a convenient one. If somebody tightens it far enough
        // to break a catch-up sync, this test is where they find out — and a sync that
        // failed because of our own limiter would look to the household like lost doses.
        await using var harness = new ApiTestHarness(new Dictionary<string, string?>
        {
            ["RateLimit:ApiPermitsPerMinute"] = ProductionApiPermits.ToString(),
        });

        var (client, household) = await harness.NewHouseholdAsync();
        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic tablet", form = "Tablet", unit = "Tablet" });

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            fullPackages = 5,
            capacityNumerator = 20,
            capacityDenominator = 1,
            openedPackages = Array.Empty<object>(),
        });

        // The outbox sends one at a time and waits, which is what a real reconnect looks
        // like. Fifty is a fortnight of a few doses a day for one person.
        var refused = 0;

        for (var dose = 0; dose < 50; dose++)
        {
            var response = await client.PostAsync(
                $"/api/households/{household}/administrations",
                System.Net.Http.Json.JsonContent.Create(new
                {
                    personId = person,
                    medicationDefinitionId = definition,
                    outcome = "ExtraDose",
                    actualQuantityNumerator = 1,
                    occurredAt = DateTimeOffset.UtcNow.AddDays(-14).AddHours(dose),
                }));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                refused++;
            }
        }

        Assert.Equal(0, refused);
    }

    [PostgreSqlFact]
    public async Task The_health_checks_are_never_refused()
    {
        await using var harness = new ApiTestHarness(new Dictionary<string, string?>
        {
            ["RateLimit:ApiPermitsPerMinute"] = "1",
        });

        var client = harness.NewClient();

        // The container's own probe hits these on a timer. Throttling it would make a
        // perfectly healthy deployment report itself unhealthy and get rolled back.
        for (var probe = 0; probe < 5; probe++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
    }

    [PostgreSqlFact]
    public async Task Password_guessing_is_held_tighter_than_ordinary_traffic()
    {
        await using var harness = new ApiTestHarness(new Dictionary<string, string?>
        {
            ["RateLimit:AuthPermitsPerMinute"] = "2",
            ["RateLimit:ApiPermitsPerMinute"] = "1000",
        });

        var client = harness.NewClient();
        var codes = new List<HttpStatusCode>();

        for (var guess = 0; guess < 4; guess++)
        {
            var response = await client.PostAsync(
                "/api/auth/login",
                System.Net.Http.Json.JsonContent.Create(new
                {
                    email = "synthetic-nobody@example.invalid",
                    password = $"synthetic-guess-{guess}",
                }));

            codes.Add(response.StatusCode);
        }

        // The two policies layer: the auth one bites first even though the global budget is
        // nowhere near spent. Slow password guessing is still password guessing.
        Assert.Equal(2, codes.Count(code => code == HttpStatusCode.TooManyRequests));
    }
}
