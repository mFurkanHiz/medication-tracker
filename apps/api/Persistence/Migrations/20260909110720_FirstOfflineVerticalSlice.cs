using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirstOfflineVerticalSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "administrations");

            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.EnsureSchema(
                name: "care");

            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.EnsureSchema(
                name: "treatments");

            migrationBuilder.CreateTable(
                name: "people",
                schema: "care",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_people", x => x.id);
                    table.ForeignKey(
                        name: "FK_people_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medications",
                schema: "care",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    form = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medications", x => x.id);
                    table.CheckConstraint("ck_medications_tablet_form", "form = 'tablet'");
                    table.ForeignKey(
                        name: "FK_medications_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medications_people_person_id",
                        column: x => x.person_id,
                        principalSchema: "care",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_inventory_items_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_items_medications_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "care",
                        principalTable: "medications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regimens",
                schema: "treatments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regimens", x => x.id);
                    table.ForeignKey(
                        name: "FK_regimens_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regimens_medications_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "care",
                        principalTable: "medications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_regimens_people_person_id",
                        column: x => x.person_id,
                        principalSchema: "care",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regimen_versions",
                schema: "treatments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    regimen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    dose_numerator = table.Column<long>(type: "bigint", nullable: false),
                    dose_denominator = table.Column<long>(type: "bigint", nullable: false),
                    local_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regimen_versions", x => x.id);
                    table.CheckConstraint("ck_regimen_versions_denominator", "dose_denominator > 0");
                    table.CheckConstraint("ck_regimen_versions_period", "valid_to IS NULL OR valid_to >= valid_from");
                    table.ForeignKey(
                        name: "FK_regimen_versions_regimens_regimen_id",
                        column: x => x.regimen_id,
                        principalSchema: "treatments",
                        principalTable: "regimens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "administration_events",
                schema: "administrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    regimen_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_administration_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_administration_events_regimen_versions_regimen_version_id",
                        column: x => x.regimen_version_id,
                        principalSchema: "treatments",
                        principalTable: "regimen_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administration_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_numerator = table.Column<long>(type: "bigint", nullable: false),
                    quantity_denominator = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_denominator", "quantity_denominator > 0");
                    table.ForeignKey(
                        name: "FK_ledger_entries_administration_events_administration_event_id",
                        column: x => x.administration_event_id,
                        principalSchema: "administrations",
                        principalTable: "administration_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ledger_entries_inventory_items_inventory_item_id",
                        column: x => x.inventory_item_id,
                        principalSchema: "inventory",
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processed_administration_commands",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    administration_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_administration_commands", x => x.id);
                    table.ForeignKey(
                        name: "FK_processed_administration_commands_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_processed_administration_commands_administration_events_adm~",
                        column: x => x.administration_event_id,
                        principalSchema: "administrations",
                        principalTable: "administration_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_processed_administration_commands_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_administration_events_household_id_regimen_version_id_sched~",
                schema: "administrations",
                table: "administration_events",
                columns: new[] { "household_id", "regimen_version_id", "scheduled_for" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_administration_events_regimen_version_id",
                schema: "administrations",
                table: "administration_events",
                column: "regimen_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_household_id",
                schema: "inventory",
                table: "inventory_items",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_medication_id",
                schema: "inventory",
                table: "inventory_items",
                column: "medication_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_administration_event_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "administration_event_id",
                unique: true,
                filter: "administration_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_inventory_item_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "inventory_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_medications_household_id",
                schema: "care",
                table: "medications",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "IX_medications_person_id",
                schema: "care",
                table: "medications",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "IX_people_household_id_id",
                schema: "care",
                table: "people",
                columns: new[] { "household_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processed_administration_commands_account_id",
                schema: "sync",
                table: "processed_administration_commands",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_processed_administration_commands_administration_event_id",
                schema: "sync",
                table: "processed_administration_commands",
                column: "administration_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_processed_administration_commands_household_id_idempotency_~",
                schema: "sync",
                table: "processed_administration_commands",
                columns: new[] { "household_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_regimen_versions_regimen_id_valid_from",
                schema: "treatments",
                table: "regimen_versions",
                columns: new[] { "regimen_id", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_regimens_household_id",
                schema: "treatments",
                table: "regimens",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "IX_regimens_medication_id",
                schema: "treatments",
                table: "regimens",
                column: "medication_id");

            migrationBuilder.CreateIndex(
                name: "IX_regimens_person_id",
                schema: "treatments",
                table: "regimens",
                column: "person_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "processed_administration_commands",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "inventory_items",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "administration_events",
                schema: "administrations");

            migrationBuilder.DropTable(
                name: "regimen_versions",
                schema: "treatments");

            migrationBuilder.DropTable(
                name: "regimens",
                schema: "treatments");

            migrationBuilder.DropTable(
                name: "medications",
                schema: "care");

            migrationBuilder.DropTable(
                name: "people",
                schema: "care");
        }
    }
}
