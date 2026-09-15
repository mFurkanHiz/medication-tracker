using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V1CrudAuditAndStockFloor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "treatments",
                table: "regimens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "care",
                table: "medications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "previous_value",
                schema: "care",
                table: "medication_change_events",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "new_value",
                schema: "care",
                table: "medication_change_events",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "regimen_change_events",
                schema: "treatments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    regimen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    previous_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regimen_change_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_regimen_change_events_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regimen_change_events_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regimen_change_events_regimens_regimen_id",
                        column: x => x.regimen_id,
                        principalSchema: "treatments",
                        principalTable: "regimens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_regimen_change_events_account_id",
                schema: "treatments",
                table: "regimen_change_events",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_regimen_change_events_household_id_regimen_id_recorded_at",
                schema: "treatments",
                table: "regimen_change_events",
                columns: new[] { "household_id", "regimen_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_regimen_change_events_regimen_id",
                schema: "treatments",
                table: "regimen_change_events",
                column: "regimen_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "regimen_change_events",
                schema: "treatments");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "treatments",
                table: "regimens");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "care",
                table: "medications");

            migrationBuilder.AlterColumn<string>(
                name: "previous_value",
                schema: "care",
                table: "medication_change_events",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "new_value",
                schema: "care",
                table: "medication_change_events",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
