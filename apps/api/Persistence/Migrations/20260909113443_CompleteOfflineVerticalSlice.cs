using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteOfflineVerticalSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "taken_at",
                schema: "administrations",
                table: "administration_events",
                newName: "occurred_at");

            migrationBuilder.AddColumn<string>(
                name: "outcome",
                schema: "administrations",
                table: "administration_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "taken");

            migrationBuilder.CreateTable(
                name: "command_receipts",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    result_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_receipts", x => x.id);
                    table.ForeignKey(
                        name: "FK_command_receipts_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_command_receipts_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_administration_events_outcome",
                schema: "administrations",
                table: "administration_events",
                sql: "outcome IN ('taken', 'skipped')");

            migrationBuilder.CreateIndex(
                name: "IX_command_receipts_account_id",
                schema: "sync",
                table: "command_receipts",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_command_receipts_household_id_idempotency_key",
                schema: "sync",
                table: "command_receipts",
                columns: new[] { "household_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "command_receipts",
                schema: "sync");

            migrationBuilder.DropCheckConstraint(
                name: "ck_administration_events_outcome",
                schema: "administrations",
                table: "administration_events");

            migrationBuilder.DropColumn(
                name: "outcome",
                schema: "administrations",
                table: "administration_events");

            migrationBuilder.RenameColumn(
                name: "occurred_at",
                schema: "administrations",
                table: "administration_events",
                newName: "taken_at");
        }
    }
}
