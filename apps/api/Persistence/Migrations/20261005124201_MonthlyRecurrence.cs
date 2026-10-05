using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MonthlyRecurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_plan_versions_recurrence",
                schema: "treatments",
                table: "plan_versions");

            migrationBuilder.AddColumn<int>(
                name: "day_of_month",
                schema: "treatments",
                table: "plan_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "interval_months",
                schema: "treatments",
                table: "plan_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_plan_versions_recurrence",
                schema: "treatments",
                table: "plan_versions",
                sql: "(pattern = 'Daily' AND weekday_mask IS NULL AND interval_days IS NULL AND day_of_month IS NULL AND interval_months IS NULL) OR (kind = 'Scheduled' AND pattern = 'SelectedWeekdays' AND weekday_mask BETWEEN 1 AND 127 AND interval_days IS NULL AND day_of_month IS NULL AND interval_months IS NULL) OR (kind = 'Scheduled' AND pattern = 'EveryNDays' AND effective_from IS NOT NULL AND interval_days BETWEEN 1 AND 3650 AND weekday_mask IS NULL AND day_of_month IS NULL AND interval_months IS NULL) OR (kind = 'Scheduled' AND pattern = 'DayOfMonth' AND day_of_month BETWEEN 1 AND 31 AND weekday_mask IS NULL AND interval_days IS NULL AND interval_months IS NULL) OR (kind = 'Scheduled' AND pattern = 'EveryNMonths' AND effective_from IS NOT NULL AND interval_months BETWEEN 1 AND 120 AND weekday_mask IS NULL AND interval_days IS NULL AND day_of_month IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_plan_versions_recurrence",
                schema: "treatments",
                table: "plan_versions");

            migrationBuilder.DropColumn(
                name: "day_of_month",
                schema: "treatments",
                table: "plan_versions");

            migrationBuilder.DropColumn(
                name: "interval_months",
                schema: "treatments",
                table: "plan_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plan_versions_recurrence",
                schema: "treatments",
                table: "plan_versions",
                sql: "(pattern = 'Daily' AND weekday_mask IS NULL AND interval_days IS NULL) OR (kind = 'Scheduled' AND pattern = 'SelectedWeekdays' AND weekday_mask BETWEEN 1 AND 127 AND interval_days IS NULL) OR (kind = 'Scheduled' AND pattern = 'EveryNDays' AND effective_from IS NOT NULL AND interval_days BETWEEN 1 AND 3650 AND weekday_mask IS NULL)");
        }
    }
}
