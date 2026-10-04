using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <summary>
    /// Adds the household's own safety notes to the medication catalog, and widens the
    /// catalog audit snapshot to hold them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The five note columns are additive and nullable, so every existing row is already
    /// correct: a household that has recorded nothing has recorded nothing.
    /// </para>
    /// <para>
    /// The <c>previous_value</c> / <c>new_value</c> widening has to happen in THIS
    /// migration rather than a later one. The audit snapshot is hand-serialised JSON of
    /// every field on the definition; five prose notes of up to 2000 characters each put
    /// it an order of magnitude past <c>varchar(4000)</c>. Ship the columns without the
    /// widening and the first household to fill in their warnings gets a write failure
    /// from the audit row — the trail breaking exactly when it finally has something
    /// worth recording. In PostgreSQL <c>varchar(n)</c> to <c>text</c> needs no table
    /// rewrite, so this stays cheap on a live table.
    /// </para>
    /// <para>
    /// <c>Down</c> is lossy by nature, and that is what EF's data-loss warning is about:
    /// going back to <c>varchar(4000)</c> truncates any snapshot that has since grown
    /// past it, and dropping the note columns discards what the household wrote. Rolling
    /// this one back means accepting that.
    /// </para>
    /// </remarks>
    public partial class CautionNotesOnMedicationDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "caution_do_not_take_with",
                schema: "catalog",
                table: "medication_definitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "caution_foods_to_avoid",
                schema: "catalog",
                table: "medication_definitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "caution_things_to_avoid",
                schema: "catalog",
                table: "medication_definitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "caution_things_to_do",
                schema: "catalog",
                table: "medication_definitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "caution_warning",
                schema: "catalog",
                table: "medication_definitions",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "previous_value",
                schema: "catalog",
                table: "medication_definition_change_events",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "new_value",
                schema: "catalog",
                table: "medication_definition_change_events",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "caution_do_not_take_with",
                schema: "catalog",
                table: "medication_definitions");

            migrationBuilder.DropColumn(
                name: "caution_foods_to_avoid",
                schema: "catalog",
                table: "medication_definitions");

            migrationBuilder.DropColumn(
                name: "caution_things_to_avoid",
                schema: "catalog",
                table: "medication_definitions");

            migrationBuilder.DropColumn(
                name: "caution_things_to_do",
                schema: "catalog",
                table: "medication_definitions");

            migrationBuilder.DropColumn(
                name: "caution_warning",
                schema: "catalog",
                table: "medication_definitions");

            migrationBuilder.AlterColumn<string>(
                name: "previous_value",
                schema: "catalog",
                table: "medication_definition_change_events",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "new_value",
                schema: "catalog",
                table: "medication_definition_change_events",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
