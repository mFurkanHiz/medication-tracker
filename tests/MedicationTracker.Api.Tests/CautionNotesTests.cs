using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedicationTracker.Api.Domain.Catalog;
using MedicationTracker.Api.Modules.Audit;
using MedicationTracker.Api.Modules.Catalog;
using MedicationTracker.Api.Modules.Treatments;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// The household's own safety notes: what not to take this with, what to avoid eating,
/// what to do, what not to do, and anything else worth a warning.
/// </summary>
/// <remarks>
/// <para>
/// The owner asked for this plainly: "Beraberinde alınmaması gereken ilaçlar varsa, ekstra
/// bir uyarı notu varsa onlar da yazılabilsin… Beraberinde alınmaması gereken gıdalar,
/// yapılmaması gerekenler, yapılması gerekenler vs de olabilir."
/// </para>
/// <para>
/// The load-bearing part is what the feature refuses to do. These notes are prose somebody
/// copied off a box or heard from a pharmacist; the software stores and shows them and
/// never evaluates them. It does not match them against another medicine, and it does not
/// check them before a dose is recorded. A tracker that started guessing which medicines
/// clash would be making a clinical claim it cannot stand behind — and once it guessed
/// right once it would be trusted to guess always, including by its silence. So the tests
/// below pin the refusal structurally, not just the storage.
/// </para>
/// <para>All data is synthetic.</para>
/// </remarks>
public sealed class CautionNotesTests
{
    [Fact]
    public void Blank_reads_as_absent_rather_than_as_a_note_somebody_left_empty()
    {
        var notes = new CautionNotes(
            DoNotTakeWith: "  synthetic other tablet  ",
            FoodsToAvoid: "   ",
            ThingsToDo: string.Empty,
            ThingsToAvoid: null,
            Warning: "\n synthetic warning \n").Normalized();

        Assert.Equal("synthetic other tablet", notes.DoNotTakeWith);
        Assert.Equal("synthetic warning", notes.Warning);

        // A cleared textarea arrives as "" or whitespace. Storing that would show on
        // screen as a warning block with an empty line in it, which teaches people that
        // the block is noise.
        Assert.Null(notes.FoodsToAvoid);
        Assert.Null(notes.ThingsToDo);
        Assert.Null(notes.ThingsToAvoid);

        Assert.False(notes.IsEmpty);
        Assert.True(CautionNotes.None.IsEmpty);
    }

    [Fact]
    public void A_note_past_the_length_limit_is_refused_before_it_reaches_the_entity()
    {
        var tooLong = new string('x', CautionNotes.MaximumNoteLength + 1);

        Assert.False(new CautionNotes(ThingsToDo: tooLong).IsValid());
        Assert.True(new CautionNotes(ThingsToDo: new string('x', CautionNotes.MaximumNoteLength)).IsValid());

        // Trailing whitespace must not be what pushes a note over the edge.
        Assert.True(new CautionNotes(
            ThingsToDo: new string('x', CautionNotes.MaximumNoteLength) + "   ").IsValid());
    }

    [Fact]
    public void The_notes_belong_to_the_medicine_not_to_a_plan_version()
    {
        var definition = typeof(MedicationDefinition).GetProperties().Select(p => p.Name).ToArray();
        var version = typeof(TreatmentPlanVersion).GetProperties().Select(p => p.Name).ToArray();

        Assert.Contains("CautionDoNotTakeWith", definition);

        // "Do not take this with grapefruit" is a fact about the medicine. On a plan
        // version it would have to be retyped for every person in the household taking
        // the same thing, and re-entered on every dose change — and the copies would
        // drift, which is worse than not having it.
        Assert.DoesNotContain(version, name => name.StartsWith("Caution", StringComparison.Ordinal));
    }

    [PostgreSqlFact]
    public async Task All_five_notes_are_stored_and_handed_back_to_the_screen()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new
            {
                name = "Synthetic tablet",
                form = "Tablet",
                unit = "Tablet",
                cautionDoNotTakeWith = "Synthetic blood thinner",
                cautionFoodsToAvoid = "Synthetic citrus",
                cautionThingsToDo = "Drink a full glass of water",
                cautionThingsToAvoid = "Do not lie down for half an hour",
                cautionWarning = "Synthetic pharmacist warning",
            });

        var cautions = (await DefinitionAsync(client, household, definition)).GetProperty("cautions");

        Assert.Equal("Synthetic blood thinner", cautions.GetProperty("doNotTakeWith").GetString());
        Assert.Equal("Synthetic citrus", cautions.GetProperty("foodsToAvoid").GetString());
        Assert.Equal("Drink a full glass of water", cautions.GetProperty("thingsToDo").GetString());
        Assert.Equal("Do not lie down for half an hour", cautions.GetProperty("thingsToAvoid").GetString());
        Assert.Equal("Synthetic pharmacist warning", cautions.GetProperty("warning").GetString());
    }

    [PostgreSqlFact]
    public async Task A_medicine_with_no_notes_says_so_rather_than_sending_five_blanks()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic tablet", form = "Tablet", unit = "Tablet" });

        // Null, so "does this medicine carry a warning" is one check on the client. The
        // screen needs that answer to decide whether to show the block at all.
        Assert.Equal(
            JsonValueKind.Null,
            (await DefinitionAsync(client, household, definition)).GetProperty("cautions").ValueKind);
    }

    [PostgreSqlFact]
    public async Task An_over_long_note_is_refused_and_the_refusal_names_the_field()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var refusal = await client.PostAsJsonAsync(
            $"/api/households/{household}/medication-definitions",
            new
            {
                name = "Synthetic tablet",
                form = "Tablet",
                unit = "Tablet",
                cautionWarning = new string('x', CautionNotes.MaximumNoteLength + 1),
            });

        Assert.Equal(HttpStatusCode.BadRequest, refusal.StatusCode);

        var problem = JsonSerializer.Deserialize<JsonElement>(await refusal.Content.ReadAsStringAsync());
        Assert.True(problem.GetProperty("errors").TryGetProperty("cautions", out _));
    }

    [PostgreSqlFact]
    public async Task Editing_a_warning_is_audited_rather_than_recorded_as_a_no_op()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new
            {
                name = "Synthetic tablet",
                form = "Tablet",
                unit = "Tablet",
                cautionWarning = "Synthetic first warning",
            });

        await client.PutOk($"/api/households/{household}/medication-definitions/{definition}", new
        {
            name = "Synthetic tablet",
            form = "Tablet",
            unit = "Tablet",
            cautionWarning = "Synthetic second warning",
        });

        // The plan slice found the same hole one table over: a snapshot that leaves a
        // field out audits an edit to it as a change from nothing to nothing. A safety
        // warning is the last field that should go missing from the trail.
        await using var db = harness.NewDbContext();
        var edit = await db.MedicationDefinitionChangeEvents.AsNoTracking()
            .Where(e => e.MedicationDefinitionId == definition && e.Kind == ChangeKind.Updated)
            .SingleAsync();

        Assert.Contains("Synthetic first warning", edit.PreviousValue);
        Assert.Contains("Synthetic second warning", edit.NewValue);
    }

    [PostgreSqlFact]
    public async Task A_warning_long_enough_to_overflow_the_old_audit_column_still_writes()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        // Five notes at the full limit serialise to roughly 10,000 characters, well past
        // the varchar(4000) the audit columns used to be. Migration 14 widened them in the
        // same migration that added the notes, for exactly this write.
        var note = new string('y', CautionNotes.MaximumNoteLength);

        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new
            {
                name = "Synthetic tablet",
                form = "Tablet",
                unit = "Tablet",
                cautionDoNotTakeWith = note,
                cautionFoodsToAvoid = note,
                cautionThingsToDo = note,
                cautionThingsToAvoid = note,
                cautionWarning = note,
            });

        await using var db = harness.NewDbContext();
        var created = await db.MedicationDefinitionChangeEvents.AsNoTracking()
            .Where(e => e.MedicationDefinitionId == definition && e.Kind == ChangeKind.Created)
            .SingleAsync();

        Assert.True(created.NewValue!.Length > 4000, $"snapshot was only {created.NewValue!.Length} characters");
    }

    [PostgreSqlFact]
    public async Task Archiving_and_restoring_a_medicine_keeps_its_notes()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new
            {
                name = "Synthetic tablet",
                form = "Tablet",
                unit = "Tablet",
                cautionFoodsToAvoid = "Synthetic citrus",
            });

        await client.DeleteOk($"/api/households/{household}/medication-definitions/{definition}");
        await client.PostOk($"/api/households/{household}/medication-definitions/{definition}/restore");

        var cautions = (await DefinitionAsync(client, household, definition)).GetProperty("cautions");
        Assert.Equal("Synthetic citrus", cautions.GetProperty("foodsToAvoid").GetString());
    }

    [PostgreSqlFact]
    public async Task A_caution_note_never_stands_between_somebody_and_recording_a_dose()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new
            {
                name = "Synthetic tablet",
                form = "Tablet",
                unit = "Tablet",
                cautionDoNotTakeWith = "Synthetic blood thinner",
                cautionWarning = "Do not take this at all",
            });

        await client.PostOk($"/api/households/{household}/inventory/{definition}/stock", new
        {
            fullPackages = 1,
            capacityNumerator = 20,
            capacityDenominator = 1,
            openedPackages = Array.Empty<object>(),
        });

        // This is the product boundary as a test. Even a note that reads like a refusal
        // is somebody's prose, not an instruction to the software: the app records what
        // actually happened. A tracker that argued with the person holding the box would
        // teach them to stop telling it the truth, and the truth is all it has.
        var dose = await client.PostOk($"/api/households/{household}/administrations", new
        {
            personId = person,
            medicationDefinitionId = definition,
            outcome = "ExtraDose",
            actualQuantityNumerator = 1,
        });

        Assert.NotEqual(Guid.Empty, dose.GetProperty("administrationEventId").GetGuid());
    }

    [PostgreSqlFact]
    public async Task The_dose_row_carries_the_warning_so_it_arrives_before_the_dose()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new
            {
                name = "Synthetic tablet",
                form = "Tablet",
                unit = "Tablet",
                cautionFoodsToAvoid = "Synthetic citrus",
            });

        await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "Scheduled",
            pattern = "Daily",
            localTime = "08:00:00",
        });

        var today = await client.GetOk($"/api/households/{household}/today");
        var due = today.GetProperty("due").EnumerateArray().Single();

        // Today is the screen somebody reads with the box in their hand. A warning that
        // lives only on the medicine's own page is a warning that arrives after the dose.
        Assert.Equal(
            "Synthetic citrus",
            due.GetProperty("cautions").GetProperty("foodsToAvoid").GetString());
    }

    [PostgreSqlFact]
    public async Task A_dose_row_for_a_medicine_with_no_notes_carries_no_empty_block()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var person = await client.PostId(
            $"/api/households/{household}/people", new { name = "Synthetic person" });
        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new { name = "Synthetic tablet", form = "Tablet", unit = "Tablet" });

        await client.PostOk($"/api/households/{household}/plans", new
        {
            personId = person,
            medicationDefinitionId = definition,
            doseNumerator = 1,
            doseDenominator = 1,
            timeZoneId = "UTC",
            kind = "Scheduled",
            pattern = "Daily",
            localTime = "08:00:00",
        });

        var today = await client.GetOk($"/api/households/{household}/today");
        var due = today.GetProperty("due").EnumerateArray().Single();

        // All three readers agree on this shape. A client that met an object of five
        // nulls here would render a warning panel with nothing in it, and a panel that
        // is sometimes empty is a panel people stop reading.
        Assert.Equal(JsonValueKind.Null, due.GetProperty("cautions").ValueKind);
    }

    [PostgreSqlFact]
    public async Task A_household_taking_its_data_elsewhere_takes_the_warnings_with_it()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        await client.PostOk($"/api/households/{household}/medication-definitions", new
        {
            name = "Synthetic tablet",
            form = "Tablet",
            unit = "Tablet",
            cautionThingsToAvoid = "Do not lie down for half an hour",
        });

        var export = await client.GetOk($"/api/households/{household}/export");

        // The export shape changed, so its declared version has to change with it, or a
        // reader cannot tell a file without warnings from one written before warnings
        // existed.
        Assert.Equal(3, export.GetProperty("schemaVersion").GetInt32());

        var exported = export.GetProperty("medicationDefinitions").EnumerateArray().Single();
        Assert.Equal(
            "Do not lie down for half an hour",
            exported.GetProperty("cautions").GetProperty("thingsToAvoid").GetString());
    }

    [PostgreSqlFact]
    public async Task A_household_cannot_read_or_write_another_households_notes()
    {
        await using var harness = new ApiTestHarness();
        var (client, household) = await harness.NewHouseholdAsync();

        var definition = await client.PostId(
            $"/api/households/{household}/medication-definitions",
            new
            {
                name = "Synthetic tablet",
                form = "Tablet",
                unit = "Tablet",
                cautionWarning = "Synthetic private warning",
            });

        var (outsider, _) = await harness.NewHouseholdAsync();

        var read = await outsider.GetAsync($"/api/households/{household}/workspace");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        var write = await outsider.PutAsJsonAsync(
            $"/api/households/{household}/medication-definitions/{definition}",
            new { name = "Synthetic tablet", form = "Tablet", cautionWarning = "Injected" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    private static async Task<JsonElement> DefinitionAsync(HttpClient client, Guid household, Guid definitionId)
    {
        var workspace = await client.GetOk($"/api/households/{household}/workspace");

        return workspace.GetProperty("medications").EnumerateArray()
            .Single(medication => medication.GetProperty("id").GetGuid() == definitionId);
    }
}
