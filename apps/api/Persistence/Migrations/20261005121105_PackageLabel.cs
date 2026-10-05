using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PackageLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "label",
                schema: "inventory",
                table: "packages",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "label",
                schema: "inventory",
                table: "packages");
        }
    }
}
