using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryCountSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "count_sessions",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeforeNumerator = table.Column<long>(type: "bigint", nullable: false),
                    BeforeDenominator = table.Column<long>(type: "bigint", nullable: false),
                    ObservedNumerator = table.Column<long>(type: "bigint", nullable: false),
                    ObservedDenominator = table.Column<long>(type: "bigint", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_count_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_count_sessions_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_sessions_households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_sessions_inventory_items_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalSchema: "inventory",
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_sessions_ledger_entries_LedgerEntryId",
                        column: x => x.LedgerEntryId,
                        principalSchema: "inventory",
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_count_sessions_AccountId",
                schema: "inventory",
                table: "count_sessions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_count_sessions_HouseholdId",
                schema: "inventory",
                table: "count_sessions",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_count_sessions_InventoryItemId",
                schema: "inventory",
                table: "count_sessions",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_count_sessions_LedgerEntryId",
                schema: "inventory",
                table: "count_sessions",
                column: "LedgerEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "count_sessions",
                schema: "inventory");
        }
    }
}
