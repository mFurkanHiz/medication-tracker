using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BulkInventoryCountRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_count_sessions_HouseholdId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.DropIndex(
                name: "IX_count_sessions_LedgerEntryId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                schema: "inventory",
                table: "count_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "count_batches",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_count_batches", x => x.id);
                    table.CheckConstraint("ck_count_batches_revision", "revision_number > 0");
                    table.ForeignKey(
                        name: "FK_count_batches_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_batches_count_batches_previous_batch_id",
                        column: x => x.previous_batch_id,
                        principalSchema: "inventory",
                        principalTable: "count_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_batches_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_count_sessions_BatchId",
                schema: "inventory",
                table: "count_sessions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_count_sessions_HouseholdId_BatchId",
                schema: "inventory",
                table: "count_sessions",
                columns: new[] { "HouseholdId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_count_sessions_LedgerEntryId",
                schema: "inventory",
                table: "count_sessions",
                column: "LedgerEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_count_batches_account_id",
                schema: "inventory",
                table: "count_batches",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_count_batches_household_id_accepted_at",
                schema: "inventory",
                table: "count_batches",
                columns: new[] { "household_id", "accepted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_count_batches_previous_batch_id",
                schema: "inventory",
                table: "count_batches",
                column: "previous_batch_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_count_sessions_count_batches_BatchId",
                schema: "inventory",
                table: "count_sessions",
                column: "BatchId",
                principalSchema: "inventory",
                principalTable: "count_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_count_sessions_count_batches_BatchId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.DropTable(
                name: "count_batches",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_count_sessions_BatchId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.DropIndex(
                name: "IX_count_sessions_HouseholdId_BatchId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.DropIndex(
                name: "IX_count_sessions_LedgerEntryId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.DropColumn(
                name: "BatchId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.CreateIndex(
                name: "IX_count_sessions_HouseholdId",
                schema: "inventory",
                table: "count_sessions",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_count_sessions_LedgerEntryId",
                schema: "inventory",
                table: "count_sessions",
                column: "LedgerEntryId");
        }
    }
}
