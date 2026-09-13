using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V1MedicationCatalogPackagesFlexibleRegimens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_regimen_versions_regimen_id_valid_from",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_regimen_versions_period",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_administration_event_id",
                schema: "inventory",
                table: "ledger_entries");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "valid_from",
                schema: "treatments",
                table: "regimen_versions",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "local_time",
                schema: "treatments",
                table: "regimen_versions",
                type: "time without time zone",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone");

            migrationBuilder.AddColumn<string>(
                name: "day_period",
                schema: "treatments",
                table: "regimen_versions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meal_relation",
                schema: "treatments",
                table: "regimen_versions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "minimum_interval_minutes",
                schema: "treatments",
                table: "regimen_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "schedule_type",
                schema: "treatments",
                table: "regimen_versions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "scheduled");

            migrationBuilder.AlterColumn<Guid>(
                name: "person_id",
                schema: "care",
                table: "medications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "care",
                table: "medications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "care",
                table: "medications",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string[]>(
                name: "tags",
                schema: "care",
                table: "medications",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "package_id",
                schema: "inventory",
                table: "ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "medication_change_events",
                schema: "care",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    previous_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medication_change_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_medication_change_events_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medication_change_events_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medication_change_events_medications_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "care",
                        principalTable: "medications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "packages",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capacity_numerator = table.Column<long>(type: "bigint", nullable: false),
                    capacity_denominator = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packages", x => x.id);
                    table.CheckConstraint("ck_packages_capacity", "capacity_numerator > 0 AND capacity_denominator > 0");
                    table.ForeignKey(
                        name: "FK_packages_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_packages_inventory_items_inventory_item_id",
                        column: x => x.inventory_item_id,
                        principalSchema: "inventory",
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_packages_people_person_id",
                        column: x => x.person_id,
                        principalSchema: "care",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "package_assignment_events",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_assignment_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_package_assignment_events_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_assignment_events_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_assignment_events_packages_package_id",
                        column: x => x.package_id,
                        principalSchema: "inventory",
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_assignment_events_people_from_person_id",
                        column: x => x.from_person_id,
                        principalSchema: "care",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_package_assignment_events_people_to_person_id",
                        column: x => x.to_person_id,
                        principalSchema: "care",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_regimen_versions_regimen_id_created_at",
                schema: "treatments",
                table: "regimen_versions",
                columns: new[] { "regimen_id", "created_at" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_regimen_versions_period",
                schema: "treatments",
                table: "regimen_versions",
                sql: "valid_from IS NULL OR valid_to IS NULL OR valid_to >= valid_from");

            migrationBuilder.AddCheckConstraint(
                name: "ck_regimen_versions_schedule",
                schema: "treatments",
                table: "regimen_versions",
                sql: "schedule_type = 'as_needed' OR local_time IS NOT NULL OR day_period IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_regimen_versions_schedule_type",
                schema: "treatments",
                table: "regimen_versions",
                sql: "schedule_type IN ('scheduled', 'as_needed')");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_administration_event_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "administration_event_id",
                filter: "administration_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_package_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "package_id",
                filter: "package_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_medication_change_events_account_id",
                schema: "care",
                table: "medication_change_events",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_medication_change_events_household_id_medication_id_recorde~",
                schema: "care",
                table: "medication_change_events",
                columns: new[] { "household_id", "medication_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_medication_change_events_medication_id",
                schema: "care",
                table: "medication_change_events",
                column: "medication_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_assignment_events_account_id",
                schema: "inventory",
                table: "package_assignment_events",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_assignment_events_from_person_id",
                schema: "inventory",
                table: "package_assignment_events",
                column: "from_person_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_assignment_events_household_id_package_id_recorded_~",
                schema: "inventory",
                table: "package_assignment_events",
                columns: new[] { "household_id", "package_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_package_assignment_events_package_id",
                schema: "inventory",
                table: "package_assignment_events",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_assignment_events_to_person_id",
                schema: "inventory",
                table: "package_assignment_events",
                column: "to_person_id");

            migrationBuilder.CreateIndex(
                name: "IX_packages_household_id_inventory_item_id",
                schema: "inventory",
                table: "packages",
                columns: new[] { "household_id", "inventory_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_packages_inventory_item_id",
                schema: "inventory",
                table: "packages",
                column: "inventory_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_packages_person_id",
                schema: "inventory",
                table: "packages",
                column: "person_id");

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entries_packages_package_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "package_id",
                principalSchema: "inventory",
                principalTable: "packages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ledger_entries_packages_package_id",
                schema: "inventory",
                table: "ledger_entries");

            migrationBuilder.DropTable(
                name: "medication_change_events",
                schema: "care");

            migrationBuilder.DropTable(
                name: "package_assignment_events",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "packages",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_regimen_versions_regimen_id_created_at",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_regimen_versions_period",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_regimen_versions_schedule",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_regimen_versions_schedule_type",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_administration_event_id",
                schema: "inventory",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "IX_ledger_entries_package_id",
                schema: "inventory",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "day_period",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropColumn(
                name: "meal_relation",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropColumn(
                name: "minimum_interval_minutes",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropColumn(
                name: "schedule_type",
                schema: "treatments",
                table: "regimen_versions");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "care",
                table: "medications");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "care",
                table: "medications");

            migrationBuilder.DropColumn(
                name: "tags",
                schema: "care",
                table: "medications");

            migrationBuilder.DropColumn(
                name: "package_id",
                schema: "inventory",
                table: "ledger_entries");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "valid_from",
                schema: "treatments",
                table: "regimen_versions",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "local_time",
                schema: "treatments",
                table: "regimen_versions",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0),
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "person_id",
                schema: "care",
                table: "medications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_regimen_versions_regimen_id_valid_from",
                schema: "treatments",
                table: "regimen_versions",
                columns: new[] { "regimen_id", "valid_from" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_regimen_versions_period",
                schema: "treatments",
                table: "regimen_versions",
                sql: "valid_to IS NULL OR valid_to >= valid_from");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_administration_event_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "administration_event_id",
                unique: true,
                filter: "administration_event_id IS NOT NULL");
        }
    }
}
