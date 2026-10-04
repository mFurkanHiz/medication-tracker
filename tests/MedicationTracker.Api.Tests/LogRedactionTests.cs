namespace MedicationTracker.Api.Tests;

/// <summary>
/// What the application writes to its log while handling real requests.
/// </summary>
/// <remarks>
/// <para>
/// The acceptance contract has carried "log redaction is unverified" since the threat
/// model was written: nobody had confirmed that medication names, person names, quantities
/// or credentials stay out of the application log. This file is that confirmation, as a
/// test rather than an audit, because an audit is true on the day it is run and a test is
/// true on the day somebody breaks it.
/// </para>
/// <para>
/// It matters more here than on a dedicated host. This API shares a VPS with other
/// projects behind one nginx; its log is a file and a <c>docker logs</c> away for anybody
/// who can reach the box. A health record that leaks into a log has left the database's
/// authorisation behind entirely — no household check, no session, no audit row.
/// </para>
/// <para>
/// The capture is deliberately harsher than production: every category, at
/// <see cref="Microsoft.Extensions.Logging.LogLevel.Trace"/>. A test that only inspected
/// the levels production emits today would stop being evidence the moment somebody raised
/// a level to debug something — which is exactly when a leak would happen.
/// </para>
/// <para>All data is synthetic.</para>
/// </remarks>
public sealed class LogRedactionTests
{
    private const string Password = "synthetic-password-1";

    [PostgreSqlFact]
    public async Task A_full_days_work_leaves_no_health_data_in_the_log()
    {
        await using var harness = new ApiTestHarness(captureLogs: true);

        // Distinctive enough that a match cannot be a coincidence, and shaped like the
        // real thing: a person's name, a medicine, and the household's own caution note,
        // which is the most sensitive free text the product stores.
        const string personName = "Zeynep Kayalıdere";
        const string medicationName = "Sinthetikol Retard";
        const string cautionNote = "Do not take with the blue tablet from the cardiology clinic";

        var (client, household) = await harness.NewHouseholdAsync();

        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = personName });

        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new
            {
                name = medicationName,
                form = "Tablet",
                unit = "Tablet",
                cautionDoNotTakeWith = cautionNote,
            });

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            fullPackages = 1,
            capacityNumerator = 20,
            capacityDenominator = 1,
            openedPackages = Array.Empty<object>(),
        });

        await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 2,
            timeZoneId = "UTC",
            kind = "Scheduled",
            pattern = "Daily",
            localTime = "08:00:00",
        });

        await client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "Taken",
            actualQuantityNumerator = 1,
            actualQuantityDenominator = 2,
            occurredAt = DateTimeOffset.UtcNow,
        });

        await client.GetOk($"/api/households/{household}/today");
        await client.GetOk($"/api/households/{household}/workspace");
        await client.GetOk($"/api/households/{household}/export");

        // Each of these is a separate claim, asserted separately, so a failure says which
        // kind of data escaped rather than only that something did.
        AssertAbsent(harness, personName, "a person's name");
        AssertAbsent(harness, medicationName, "a medication name");
        AssertAbsent(harness, cautionNote, "the household's own caution note");

        // Nothing was logged at all is not the same evidence as nothing sensitive was
        // logged, and would pass this test for the wrong reason. The suite would be
        // asserting against an empty list.
        Assert.NotEmpty(harness.Logs.Lines);
    }

    [PostgreSqlFact]
    public async Task Registering_and_signing_in_leaves_no_credential_in_the_log()
    {
        await using var harness = new ApiTestHarness(captureLogs: true);
        var client = harness.NewClient();
        var email = $"synthetic-{Guid.CreateVersion7():N}@example.invalid";

        await client.PostOk("/api/auth/register", new
        {
            email,
            password = Password,
            confirmPassword = Password,
            mobile = true,
        });

        await client.PostOk("/api/auth/logout", new { });

        var signIn = await client.PostOk("/api/auth/login", new { email, password = Password });
        var token = signIn.GetProperty("accessToken");

        AssertAbsent(harness, email, "the account's e-mail address");
        AssertAbsent(harness, Password, "the password as typed");

        // The bearer token is the session itself. In the log it would be a credential
        // anybody reading the log could replay for a week, which is how long a session
        // lives.
        if (token.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            AssertAbsent(harness, token.GetString()!, "the session token");
        }

        Assert.NotEmpty(harness.Logs.Lines);
    }

    [PostgreSqlFact]
    public async Task A_rejected_request_does_not_log_what_it_rejected()
    {
        await using var harness = new ApiTestHarness(captureLogs: true);
        var (client, household) = await harness.NewHouseholdAsync();

        // Over the note limit, so the request is refused. A validation failure is the
        // likeliest route by which a value reaches a log without anybody deciding to log
        // it: the message embeds what was rejected, and the framework logs the message.
        var tooLong = new string('x', 2001);
        const string marker = "SyntheticRejectedNoteMarker";

        var response = await client.PostAsync(
            $"/api/households/{household}/medication-definitions",
            System.Net.Http.Json.JsonContent.Create(new
            {
                name = "Synthetic tablet",
                form = "Tablet",
                unit = "Tablet",
                cautionWarning = marker + tooLong,
            }));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        AssertAbsent(harness, marker, "the rejected note");
    }

    private static void AssertAbsent(ApiTestHarness harness, string value, string what)
    {
        var leaked = harness.Logs.Mentioning(value);

        Assert.True(
            leaked.Count == 0,
            $"The log contains {what}. A log is read by whoever can reach the host, with no "
            + $"household check in the way. First of {leaked.Count} line(s):{Environment.NewLine}"
            + $"{leaked.FirstOrDefault()}");
    }
}
