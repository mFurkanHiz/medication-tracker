using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoverageAndExpectedDepletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "coverage",
                schema: "inventory",
                table: "packages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expected_depletion_on",
                schema: "refill",
                table: "medication_refill_policies",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "coverage",
                schema: "catalog",
                table: "medication_definitions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValueSql: "'Unspecified'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "coverage",
                schema: "inventory",
                table: "packages");

            migrationBuilder.DropColumn(
                name: "expected_depletion_on",
                schema: "refill",
                table: "medication_refill_policies");

            migrationBuilder.DropColumn(
                name: "coverage",
                schema: "catalog",
                table: "medication_definitions");
        }
    }
}
