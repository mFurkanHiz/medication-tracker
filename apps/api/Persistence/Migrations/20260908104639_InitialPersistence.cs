using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "subscriptions");

            migrationBuilder.EnsureSchema(
                name: "households");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "households",
                schema: "households",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_households", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "household_memberships",
                schema: "households",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_household_memberships", x => x.id);
                    table.CheckConstraint("ck_household_memberships_valid_period", "valid_to IS NULL OR valid_to > valid_from");
                    table.ForeignKey(
                        name: "FK_household_memberships_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_household_memberships_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    household_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.id);
                    table.CheckConstraint("ck_subscriptions_exactly_one_owner", "(account_id IS NOT NULL AND household_id IS NULL) OR (account_id IS NULL AND household_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_subscriptions_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscriptions_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entitlements",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entitlements", x => x.id);
                    table.CheckConstraint("ck_entitlements_valid_period", "valid_to IS NULL OR valid_to > valid_from");
                    table.ForeignKey(
                        name: "FK_entitlements_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "subscriptions",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_normalized_email",
                schema: "identity",
                table: "accounts",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entitlements_subscription_id_code_valid_from",
                schema: "subscriptions",
                table: "entitlements",
                columns: new[] { "subscription_id", "code", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_household_memberships_account_id",
                schema: "households",
                table: "household_memberships",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_household_memberships_household_id_account_id_valid_from",
                schema: "households",
                table: "household_memberships",
                columns: new[] { "household_id", "account_id", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_account_id",
                schema: "subscriptions",
                table: "subscriptions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_household_id",
                schema: "subscriptions",
                table: "subscriptions",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_provider_external_reference",
                schema: "subscriptions",
                table: "subscriptions",
                columns: new[] { "provider", "external_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entitlements",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "household_memberships",
                schema: "households");

            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "households",
                schema: "households");
        }
    }
}
