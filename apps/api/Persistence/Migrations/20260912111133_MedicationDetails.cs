using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MedicationDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "active_ingredient",
                schema: "care",
                table: "medications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "care",
                table: "medications",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "strength",
                schema: "care",
                table: "medications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "active_ingredient",
                schema: "care",
                table: "medications");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "care",
                table: "medications");

            migrationBuilder.DropColumn(
                name: "strength",
                schema: "care",
                table: "medications");
        }
    }
}
