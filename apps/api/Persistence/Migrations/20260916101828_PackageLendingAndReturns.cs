using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PackageLendingAndReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_person_id",
                schema: "inventory",
                table: "packages",
                type: "uuid",
                nullable: true);

            // Existing assignments represented ownership before lending existed.
            migrationBuilder.Sql("UPDATE inventory.packages SET owner_person_id = person_id WHERE person_id IS NOT NULL");

            migrationBuilder.CreateTable(
                name: "package_loans",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    borrower_person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lent_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    returned_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_loans", x => x.id);
                    table.CheckConstraint("ck_package_loans_people", "owner_person_id <> borrower_person_id");
                    table.ForeignKey(
                        name: "FK_package_loans_accounts_lent_by_account_id",
                        column: x => x.lent_by_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_loans_accounts_returned_by_account_id",
                        column: x => x.returned_by_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_loans_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_loans_packages_package_id",
                        column: x => x.package_id,
                        principalSchema: "inventory",
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_loans_people_borrower_person_id",
                        column: x => x.borrower_person_id,
                        principalSchema: "care",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_loans_people_owner_person_id",
                        column: x => x.owner_person_id,
                        principalSchema: "care",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_packages_owner_person_id",
                schema: "inventory",
                table: "packages",
                column: "owner_person_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_loans_borrower_person_id",
                schema: "inventory",
                table: "package_loans",
                column: "borrower_person_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_loans_household_id_package_id",
                schema: "inventory",
                table: "package_loans",
                columns: new[] { "household_id", "package_id" },
                unique: true,
                filter: "returned_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_package_loans_lent_by_account_id",
                schema: "inventory",
                table: "package_loans",
                column: "lent_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_loans_owner_person_id",
                schema: "inventory",
                table: "package_loans",
                column: "owner_person_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_loans_package_id",
                schema: "inventory",
                table: "package_loans",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_loans_returned_by_account_id",
                schema: "inventory",
                table: "package_loans",
                column: "returned_by_account_id");

            migrationBuilder.AddForeignKey(
                name: "FK_packages_people_owner_person_id",
                schema: "inventory",
                table: "packages",
                column: "owner_person_id",
                principalSchema: "care",
                principalTable: "people",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_packages_people_owner_person_id",
                schema: "inventory",
                table: "packages");

            migrationBuilder.DropTable(
                name: "package_loans",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_packages_owner_person_id",
                schema: "inventory",
                table: "packages");

            migrationBuilder.DropColumn(
                name: "owner_person_id",
                schema: "inventory",
                table: "packages");
        }
    }
}
