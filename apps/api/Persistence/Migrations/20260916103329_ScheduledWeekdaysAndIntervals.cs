using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledWeekdaysAndIntervals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "interval_days",
                schema: "treatments",
                table: "regimen_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recurrence_kind",
                schema: "treatments",
                table: "regimen_versions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "daily");

            migrationBuilder.AddColumn<int>(
                name: "weekday_mask",
                schema: "treatments",
                table: "regimen_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_regimen_versions_recurrence",
                schema: "treatments",
                table: "regimen_versions",
                sql: "(recurrence_kind = 'daily' AND weekday_mask IS NULL AND interval_days IS NULL) OR (schedule_type = 'scheduled' AND recurrence_kind = 'weekdays' AND weekday_mask BETWEEN 1 AND 127 AND interval_days IS NULL) OR (schedule_type = 'scheduled' AND recurrence_kind = 'interval' AND valid_from IS NOT NULL AND interval_days BETWEEN 1 AND 3650 AND weekday_mask IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_regimen_versions_recurrence",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropColumn(
                name: "interval_days",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropColumn(
                name: "recurrence_kind",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropColumn(
                name: "weekday_mask",
                schema: "treatments",
                table: "regimen_versions");
        }
    }
}
