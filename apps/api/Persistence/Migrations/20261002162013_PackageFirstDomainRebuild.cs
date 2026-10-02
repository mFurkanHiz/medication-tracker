using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicationTracker.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PackageFirstDomainRebuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "refill");

            migrationBuilder.DropForeignKey(
                name: "FK_administration_events_regimen_versions_regimen_version_id",
                schema: "administrations",
                table: "administration_events");

            migrationBuilder.DropForeignKey(
                name: "FK_count_sessions_accounts_AccountId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_count_sessions_count_batches_BatchId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_count_sessions_households_HouseholdId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_count_sessions_inventory_items_InventoryItemId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_count_sessions_ledger_entries_LedgerEntryId",
                schema: "inventory",
                table: "count_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_items_medications_medication_id",
                schema: "inventory",
                table: "inventory_items");

            migrationBuilder.DropForeignKey(
                name: "FK_packages_people_person_id",
                schema: "inventory",
                table: "packages");

            migrationBuilder.DropIndex(
                name: "IX_packages_household_id_inventory_item_id",
                schema: "inventory",
                table: "packages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_packages_capacity",
                schema: "inventory",
                table: "packages");

            migrationBuilder.DropIndex(
                name: "IX_administration_events_household_id_regimen_version_id_sched~",
                schema: "administrations",
                table: "administration_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_administration_events_outcome",
                schema: "administrations",
                table: "administration_events");

            migrationBuilder.CreateTable(
                name: "medication_definitions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    strength = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    brand = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    manufacturer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    form = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    active_ingredients = table.Column<string[]>(type: "text[]", nullable: false),
                    default_package_capacity_numerator = table.Column<long>(type: "bigint", nullable: true),
                    default_package_capacity_denominator = table.Column<long>(type: "bigint", nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    external_codes = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    legacy_person_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medication_definitions", x => x.id);
                    table.CheckConstraint("ck_medication_definitions_default_capacity", "(default_package_capacity_numerator IS NULL AND default_package_capacity_denominator IS NULL) OR (default_package_capacity_numerator > 0 AND default_package_capacity_denominator > 0)");
                    table.ForeignKey(
                        name: "FK_medication_definitions_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medication_definitions_people_legacy_person_id",
                        column: x => x.legacy_person_id,
                        principalSchema: "care",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medication_definition_change_events",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    previous_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medication_definition_change_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_medication_definition_change_events_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medication_definition_change_events_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medication_definition_change_events_medication_definitions_~",
                        column: x => x.medication_definition_id,
                        principalSchema: "catalog",
                        principalTable: "medication_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "treatments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_plans_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plans_medication_definitions_medication_definition_id",
                        column: x => x.medication_definition_id,
                        principalSchema: "catalog",
                        principalTable: "medication_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plans_people_person_id",
                        column: x => x.person_id,
                        principalSchema: "care",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_change_events",
                schema: "treatments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    previous_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_change_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_change_events_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plan_change_events_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plan_change_events_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "treatments",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_versions",
                schema: "treatments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    dose_numerator = table.Column<long>(type: "bigint", nullable: false),
                    dose_denominator = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pattern = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    weekday_mask = table.Column<int>(type: "integer", nullable: true),
                    interval_days = table.Column<int>(type: "integer", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    local_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    day_period = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    meal_relation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    minimum_interval_minutes = table.Column<int>(type: "integer", nullable: true),
                    instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_versions", x => x.id);
                    table.CheckConstraint("ck_plan_versions_dose", "dose_numerator > 0 AND dose_denominator > 0");
                    table.CheckConstraint("ck_plan_versions_kind", "kind IN ('Scheduled', 'AsNeeded')");
                    table.CheckConstraint("ck_plan_versions_period", "effective_from IS NULL OR effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_plan_versions_recurrence", "(pattern = 'Daily' AND weekday_mask IS NULL AND interval_days IS NULL) OR (kind = 'Scheduled' AND pattern = 'SelectedWeekdays' AND weekday_mask BETWEEN 1 AND 127 AND interval_days IS NULL) OR (kind = 'Scheduled' AND pattern = 'EveryNDays' AND effective_from IS NOT NULL AND interval_days BETWEEN 1 AND 3650 AND weekday_mask IS NULL)");
                    table.CheckConstraint("ck_plan_versions_schedule", "kind = 'AsNeeded' OR local_time IS NOT NULL OR day_period IS NOT NULL");
                    table.CheckConstraint("ck_plan_versions_version_number", "version_number > 0");
                    table.ForeignKey(
                        name: "FK_plan_versions_accounts_created_by_account_id",
                        column: x => x.created_by_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_plan_versions_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "treatments",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Every legacy medication was a tablet: the superseded schema enforced
            // form = 'tablet' with a check constraint, and its single free-text active
            // ingredient becomes a one-element array.
            // 
            // A medication that was inactive without being deleted is genuinely archived
            // but has no recorded archive time, so the migration's own timestamp is used
            // rather than inventing a historical one. The former person_id is preserved as
            // legacy_person_id rather than discarded; the domain stops reading it.
            migrationBuilder.Sql(@"
                INSERT INTO catalog.medication_definitions (
                                id, household_id, name, strength, brand, manufacturer, form, unit,
                                active_ingredients, default_package_capacity_numerator,
                                default_package_capacity_denominator, category, tags, notes,
                                external_codes, created_at, archived_at, legacy_person_id)
                            SELECT m.id, m.household_id, m.name, m.strength, NULL, NULL, 'Tablet', 'Tablet',
                                   CASE WHEN m.active_ingredient IS NULL THEN '{}'::text[]
                                        ELSE ARRAY[m.active_ingredient] END,
                                   NULL, NULL, m.category, COALESCE(m.tags, '{}'::text[]), m.notes,
                                   NULL, m.created_at,
                                   COALESCE(m.deleted_at, CASE WHEN m.is_active THEN NULL ELSE now() END),
                                   m.person_id
                              FROM care.medications m;
            ");

            // Audit kinds become enum names. An unrecognised legacy kind maps to
            // Updated rather than failing the migration, because losing the deployment is
            // worse than losing one label's precision; the snapshots either side of the
            // change are preserved verbatim.
            migrationBuilder.Sql(@"
                INSERT INTO catalog.medication_definition_change_events (
                                id, household_id, medication_definition_id, account_id, kind,
                                previous_value, new_value, recorded_at)
                            SELECT e.id, e.household_id, e.medication_id, e.account_id,
                                   CASE e.kind
                                       WHEN 'created' THEN 'Created'
                                       WHEN 'updated' THEN 'Updated'
                                       WHEN 'deleted' THEN 'Deleted'
                                       WHEN 'archived' THEN 'Archived'
                                       WHEN 'restored' THEN 'Restored'
                                       ELSE 'Updated'
                                   END,
                                   e.previous_value, e.new_value, e.recorded_at
                              FROM care.medication_change_events e;
            ");

            // A regimen is a treatment plan under its proper name; every column carries over.
            migrationBuilder.Sql(@"
                INSERT INTO treatments.plans (
                                id, household_id, person_id, medication_definition_id, created_at, deleted_at)
                            SELECT r.id, r.household_id, r.person_id, r.medication_id, r.created_at, r.deleted_at
                              FROM treatments.regimens r;
            ");

            // Version numbers are assigned by creation order, which the superseded
            // schema already enforced as unique per regimen, so the sequence is stable and
            // matches the history it describes.
            // 
            // Legacy versions recorded no author. They are attributed to the household's
            // owner account, which is the only account known to have existed for the whole
            // period, rather than to a fabricated identifier.
            migrationBuilder.Sql(@"
                INSERT INTO treatments.plan_versions (
                                id, plan_id, version_number, dose_numerator, dose_denominator, kind, pattern,
                                weekday_mask, interval_days, effective_from, effective_to, local_time,
                                time_zone_id, day_period, meal_relation, minimum_interval_minutes,
                                instructions, created_at, created_by_account_id)
                            SELECT v.id, v.regimen_id,
                                   ROW_NUMBER() OVER (PARTITION BY v.regimen_id ORDER BY v.created_at, v.id),
                                   v.dose_numerator, v.dose_denominator,
                                   CASE v.schedule_type WHEN 'as_needed' THEN 'AsNeeded' ELSE 'Scheduled' END,
                                   CASE v.recurrence_kind
                                       WHEN 'weekdays' THEN 'SelectedWeekdays'
                                       WHEN 'interval' THEN 'EveryNDays'
                                       ELSE 'Daily'
                                   END,
                                   v.weekday_mask, v.interval_days, v.valid_from, v.valid_to, v.local_time,
                                   v.time_zone_id,
                                   CASE v.day_period
                                       WHEN 'morning' THEN 'Morning'
                                       WHEN 'noon' THEN 'Noon'
                                       WHEN 'evening' THEN 'Evening'
                                       WHEN 'night' THEN 'Night'
                                       WHEN 'bedtime' THEN 'Bedtime'
                                       ELSE NULL
                                   END,
                                   CASE v.meal_relation
                                       WHEN 'fasting' THEN 'Fasting'
                                       WHEN 'before_food' THEN 'BeforeFood'
                                       WHEN 'with_food' THEN 'WithFood'
                                       WHEN 'after_food' THEN 'AfterFood'
                                       ELSE NULL
                                   END,
                                   v.minimum_interval_minutes, NULL, v.created_at,
                                   (SELECT m.account_id
                               FROM households.household_memberships m
                              WHERE m.household_id = r.household_id
                                AND m.role = 'owner'
                              ORDER BY m.valid_from
                              LIMIT 1)
                              FROM treatments.regimen_versions v
                              JOIN treatments.regimens r ON r.id = v.regimen_id;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO treatments.plan_change_events (
                                id, household_id, plan_id, account_id, kind, previous_value, new_value, recorded_at)
                            SELECT e.id, e.household_id, e.regimen_id, e.account_id,
                                   CASE e.kind
                                       WHEN 'created' THEN 'Created'
                                       WHEN 'updated' THEN 'Updated'
                                       WHEN 'deleted' THEN 'Deleted'
                                       WHEN 'cascade_deactivated' THEN 'CascadeDeactivated'
                                       ELSE 'Updated'
                                   END,
                                   e.previous_value, e.new_value, e.recorded_at
                              FROM treatments.regimen_change_events e;
            ");

            migrationBuilder.DropTable(
                name: "medication_change_events",
                schema: "care");

            migrationBuilder.DropTable(
                name: "regimen_change_events",
                schema: "treatments");

            migrationBuilder.DropTable(
                name: "regimen_versions",
                schema: "treatments");

            migrationBuilder.DropTable(
                name: "regimens",
                schema: "treatments");

            migrationBuilder.DropTable(
                name: "medications",
                schema: "care");

            migrationBuilder.RenameColumn(
                name: "person_id",
                schema: "inventory",
                table: "packages",
                newName: "holder_person_id");

            migrationBuilder.RenameColumn(
                name: "capacity_numerator",
                schema: "inventory",
                table: "packages",
                newName: "nominal_capacity_numerator");

            migrationBuilder.RenameColumn(
                name: "capacity_denominator",
                schema: "inventory",
                table: "packages",
                newName: "nominal_capacity_denominator");

            migrationBuilder.RenameIndex(
                name: "IX_packages_person_id",
                schema: "inventory",
                table: "packages",
                newName: "IX_packages_holder_person_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "inventory",
                table: "count_sessions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "ObservedNumerator",
                schema: "inventory",
                table: "count_sessions",
                newName: "observed_numerator");

            migrationBuilder.RenameColumn(
                name: "ObservedDenominator",
                schema: "inventory",
                table: "count_sessions",
                newName: "observed_denominator");

            migrationBuilder.RenameColumn(
                name: "LedgerEntryId",
                schema: "inventory",
                table: "count_sessions",
                newName: "ledger_entry_id");

            migrationBuilder.RenameColumn(
                name: "HouseholdId",
                schema: "inventory",
                table: "count_sessions",
                newName: "household_id");

            migrationBuilder.RenameColumn(
                name: "BeforeNumerator",
                schema: "inventory",
                table: "count_sessions",
                newName: "before_numerator");

            migrationBuilder.RenameColumn(
                name: "BeforeDenominator",
                schema: "inventory",
                table: "count_sessions",
                newName: "before_denominator");

            migrationBuilder.RenameColumn(
                name: "BatchId",
                schema: "inventory",
                table: "count_sessions",
                newName: "batch_id");

            migrationBuilder.RenameColumn(
                name: "AccountId",
                schema: "inventory",
                table: "count_sessions",
                newName: "account_id");

            migrationBuilder.RenameColumn(
                name: "AcceptedAt",
                schema: "inventory",
                table: "count_sessions",
                newName: "accepted_at");

            migrationBuilder.RenameColumn(
                name: "InventoryItemId",
                schema: "inventory",
                table: "count_sessions",
                newName: "inventory_item_id");

            migrationBuilder.RenameIndex(
                name: "IX_count_sessions_LedgerEntryId",
                schema: "inventory",
                table: "count_sessions",
                newName: "IX_count_sessions_ledger_entry_id");

            migrationBuilder.RenameIndex(
                name: "IX_count_sessions_InventoryItemId",
                schema: "inventory",
                table: "count_sessions",
                newName: "IX_count_sessions_inventory_item_id");

            migrationBuilder.RenameIndex(
                name: "IX_count_sessions_HouseholdId_BatchId",
                schema: "inventory",
                table: "count_sessions",
                newName: "IX_count_sessions_household_id_batch_id");

            migrationBuilder.RenameIndex(
                name: "IX_count_sessions_BatchId",
                schema: "inventory",
                table: "count_sessions",
                newName: "IX_count_sessions_batch_id");

            migrationBuilder.RenameIndex(
                name: "IX_count_sessions_AccountId",
                schema: "inventory",
                table: "count_sessions",
                newName: "IX_count_sessions_account_id");

            migrationBuilder.RenameColumn(
                name: "regimen_version_id",
                schema: "administrations",
                table: "administration_events",
                newName: "plan_version_id");

            migrationBuilder.RenameColumn(
                name: "medication_id",
                schema: "administrations",
                table: "administration_events",
                newName: "medication_definition_id");

            migrationBuilder.DropIndex(
                name: "IX_administration_events_regimen_version_id",
                schema: "administrations",
                table: "administration_events");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "care",
                table: "people",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "acquired_on",
                schema: "inventory",
                table: "packages",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "barcode",
                schema: "inventory",
                table: "packages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_account_id",
                schema: "inventory",
                table: "packages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateOnly>(
                name: "expires_on",
                schema: "inventory",
                table: "packages",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_pinned",
                schema: "inventory",
                table: "packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "lot_number",
                schema: "inventory",
                table: "packages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "medication_definition_id",
                schema: "inventory",
                table: "packages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "inventory",
                table: "packages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "opened_at",
                schema: "inventory",
                table: "packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ordinal",
                schema: "inventory",
                table: "packages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retired_at",
                schema: "inventory",
                table: "packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "inventory",
                table: "packages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                schema: "inventory",
                table: "packages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "storage_location",
                schema: "inventory",
                table: "packages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unit",
                schema: "inventory",
                table: "packages",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                schema: "inventory",
                table: "ledger_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AddColumn<Guid>(
                name: "actor_account_id",
                schema: "inventory",
                table: "ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                schema: "inventory",
                table: "ledger_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "entry_type",
                schema: "inventory",
                table: "ledger_entries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "medication_definition_id",
                schema: "inventory",
                table: "ledger_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "reverses_entry_id",
                schema: "inventory",
                table: "ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "package_id",
                schema: "inventory",
                table: "count_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "scheduled_for",
                schema: "administrations",
                table: "administration_events",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<long>(
                name: "actual_quantity_denominator",
                schema: "administrations",
                table: "administration_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "actual_quantity_numerator",
                schema: "administrations",
                table: "administration_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "administrations",
                table: "administration_events",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // Supplied by renaming regimen_version_id, so only its nullability
            // changes here: an extra or unplanned dose belongs to no scheduled slot.
            migrationBuilder.AlterColumn<Guid>(
                name: "plan_version_id",
                schema: "administrations",
                table: "administration_events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "actor_account_id",
                schema: "administrations",
                table: "administration_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "planned_quantity_denominator",
                schema: "administrations",
                table: "administration_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "planned_quantity_numerator",
                schema: "administrations",
                table: "administration_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stock_source",
                schema: "administrations",
                table: "administration_events",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "allocations",
                schema: "administrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administration_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_numerator = table.Column<long>(type: "bigint", nullable: false),
                    quantity_denominator = table.Column<long>(type: "bigint", nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    superseded_by_allocation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allocations", x => x.id);
                    table.CheckConstraint("ck_allocations_quantity", "quantity_numerator > 0 AND quantity_denominator > 0");
                    table.ForeignKey(
                        name: "FK_allocations_administration_events_administration_event_id",
                        column: x => x.administration_event_id,
                        principalSchema: "administrations",
                        principalTable: "administration_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allocations_allocations_superseded_by_allocation_id",
                        column: x => x.superseded_by_allocation_id,
                        principalSchema: "administrations",
                        principalTable: "allocations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allocations_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allocations_ledger_entries_ledger_entry_id",
                        column: x => x.ledger_entry_id,
                        principalSchema: "inventory",
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allocations_packages_package_id",
                        column: x => x.package_id,
                        principalSchema: "inventory",
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "allocation_corrections",
                schema: "administrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administration_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    superseded_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replacement_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_numerator = table.Column<long>(type: "bigint", nullable: false),
                    quantity_denominator = table.Column<long>(type: "bigint", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reversal_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consume_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_allocation_corrections", x => x.id);
                    table.CheckConstraint("ck_allocation_corrections_distinct_source", "from_package_id IS DISTINCT FROM to_package_id");
                    table.CheckConstraint("ck_allocation_corrections_quantity", "quantity_numerator > 0 AND quantity_denominator > 0");
                    table.ForeignKey(
                        name: "FK_allocation_corrections_accounts_actor_account_id",
                        column: x => x.actor_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allocation_corrections_administration_events_administration~",
                        column: x => x.administration_event_id,
                        principalSchema: "administrations",
                        principalTable: "administration_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allocation_corrections_allocations_superseded_allocation_id",
                        column: x => x.superseded_allocation_id,
                        principalSchema: "administrations",
                        principalTable: "allocations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allocation_corrections_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allocation_corrections_packages_from_package_id",
                        column: x => x.from_package_id,
                        principalSchema: "inventory",
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_allocation_corrections_packages_to_package_id",
                        column: x => x.to_package_id,
                        principalSchema: "inventory",
                        principalTable: "packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medication_refill_policies",
                schema: "refill",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    low_stock_threshold_numerator = table.Column<long>(type: "bigint", nullable: true),
                    low_stock_threshold_denominator = table.Column<long>(type: "bigint", nullable: true),
                    low_stock_days = table.Column<int>(type: "integer", nullable: true),
                    next_eligible_refill_on = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medication_refill_policies", x => x.id);
                    table.CheckConstraint("ck_refill_policies_days", "low_stock_days IS NULL OR (low_stock_days >= 0 AND low_stock_days <= 365)");
                    table.CheckConstraint("ck_refill_policies_threshold", "(low_stock_threshold_numerator IS NULL AND low_stock_threshold_denominator IS NULL) OR (low_stock_threshold_numerator >= 0 AND low_stock_threshold_denominator > 0)");
                    table.ForeignKey(
                        name: "FK_medication_refill_policies_accounts_updated_by_account_id",
                        column: x => x.updated_by_account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medication_refill_policies_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "households",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medication_refill_policies_medication_definitions_medicatio~",
                        column: x => x.medication_definition_id,
                        principalSchema: "catalog",
                        principalTable: "medication_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Packages addressed stock through the one-per-medication inventory item.
            // They now point at the medication definition directly; the item foreign key is
            // kept so historical ledger and count rows stay valid.
            migrationBuilder.Sql(@"
                UPDATE inventory.packages p
                               SET medication_definition_id = i.medication_id
                              FROM inventory.inventory_items i
                             WHERE i.id = p.inventory_item_id;
            ");

            // Legacy medications were tablets by schema constraint.
            migrationBuilder.Sql(@"
                UPDATE inventory.packages SET unit = 'Tablet' WHERE unit = '';
            ");

            // A package that has never been drawn from is still sealed; one that has is
            // open. The superseded schema stored no opened timestamp, so an opened package
            // is dated from its creation rather than given an invented time.
            migrationBuilder.Sql(@"
                UPDATE inventory.packages p
                               SET state = CASE WHEN drawn.package_id IS NULL THEN 'Sealed' ELSE 'Opened' END,
                                   opened_at = CASE WHEN drawn.package_id IS NULL THEN NULL ELSE p.created_at END
                              FROM (SELECT DISTINCT package_id
                                      FROM inventory.ledger_entries
                                     WHERE package_id IS NOT NULL
                                       AND quantity_numerator < 0) drawn
                             WHERE drawn.package_id = p.id;
                
                            UPDATE inventory.packages SET state = 'Sealed' WHERE state = '';
            ");

            // The friendly "Box N" label is assigned by acquisition order, so the
            // numbering a household already sees in its list stays recognisable.
            migrationBuilder.Sql(@"
                UPDATE inventory.packages p
                               SET ordinal = numbered.position
                              FROM (SELECT id,
                                           ROW_NUMBER() OVER (PARTITION BY medication_definition_id
                                                              ORDER BY created_at, id) AS position
                                      FROM inventory.packages) numbered
                             WHERE numbered.id = p.id;
            ");

            // Legacy packages recorded no creator; attribute them to the household owner.
            migrationBuilder.Sql(@"
                UPDATE inventory.packages p
                               SET created_by_account_id = (SELECT m.account_id
                               FROM households.household_memberships m
                              WHERE m.household_id = p.household_id
                                AND m.role = 'owner'
                              ORDER BY m.valid_from
                              LIMIT 1)
                             WHERE created_by_account_id = '00000000-0000-0000-0000-000000000000'::uuid;
            ");

            migrationBuilder.Sql(@"
                UPDATE inventory.ledger_entries e
                               SET medication_definition_id = i.medication_id
                              FROM inventory.inventory_items i
                             WHERE i.id = e.inventory_item_id;
            ");

            // The free-text reason becomes a typed entry kind. Count reconciliations map
            // to CountAdjustment, which is also the only type permitted to carry a zero
            // delta — the superseded count flow wrote a zero entry whenever a count matched,
            // and those rows must keep passing the non-zero check constraint.
            migrationBuilder.Sql(@"
                UPDATE inventory.ledger_entries
                               SET entry_type = CASE reason
                                       WHEN 'acquisition' THEN 'Acquire'
                                       WHEN 'package_acquisition' THEN 'Acquire'
                                       WHEN 'refill' THEN 'Acquire'
                                       WHEN 'administration' THEN 'Consume'
                                       WHEN 'count' THEN 'CountAdjustment'
                                       WHEN 'count_reconciliation' THEN 'CountAdjustment'
                                       WHEN 'package_allocation_in' THEN 'PackageTransfer'
                                       WHEN 'package_allocation_out' THEN 'PackageTransfer'
                                       ELSE 'ManualAdjustment'
                                   END
                             WHERE entry_type = '';
            ");

            // Legacy entries were not grouped. Each becomes its own correlation rather
            // than being merged into a group it never belonged to.
            migrationBuilder.Sql(@"
                UPDATE inventory.ledger_entries SET correlation_id = id
                             WHERE correlation_id = '00000000-0000-0000-0000-000000000000'::uuid;
            ");

            migrationBuilder.Sql(@"
                UPDATE administrations.administration_events
                               SET outcome = CASE outcome WHEN 'skipped' THEN 'Skipped' ELSE 'Taken' END
                             WHERE outcome IN ('taken', 'skipped');
            ");

            // Every legacy dose that consumed anything drew on tracked inventory; the
            // untracked-source path did not exist, so no historical row can claim it.
            migrationBuilder.Sql(@"
                UPDATE administrations.administration_events
                               SET stock_source = CASE WHEN outcome = 'Skipped'
                                                       THEN 'NotApplicable'
                                                       ELSE 'TrackedInventory' END
                             WHERE stock_source = '';
            ");

            // The superseded model recorded no administered amount: a dose was always
            // exactly the plan's dose, because partial and extra doses could not be
            // expressed. Historical rows therefore take the amount from their plan version,
            // which is what actually happened.
            migrationBuilder.Sql(@"
                UPDATE administrations.administration_events a
                               SET planned_quantity_numerator = v.dose_numerator,
                                   planned_quantity_denominator = v.dose_denominator,
                                   actual_quantity_numerator = CASE WHEN a.outcome = 'Skipped'
                                                                    THEN NULL ELSE v.dose_numerator END,
                                   actual_quantity_denominator = CASE WHEN a.outcome = 'Skipped'
                                                                      THEN NULL ELSE v.dose_denominator END
                              FROM treatments.plan_versions v
                             WHERE v.id = a.plan_version_id;
            ");

            // Legacy doses recorded no actor; attribute them to the household owner.
            migrationBuilder.Sql(@"
                UPDATE administrations.administration_events a
                               SET actor_account_id = (SELECT m.account_id
                               FROM households.household_memberships m
                              WHERE m.household_id = a.household_id
                                AND m.role = 'owner'
                              ORDER BY m.valid_from
                              LIMIT 1)
                             WHERE actor_account_id = '00000000-0000-0000-0000-000000000000'::uuid;
            ");

            // Historical consumption was recorded only as ledger entries that happened to
            // share an administration identifier. Promoting each to an allocation makes the
            // question "which package paid for this dose" answerable for existing history,
            // and makes that history correctable by the same path as new doses. The sign is
            // flipped because a ledger entry is a signed delta while an allocation is the
            // positive amount drawn from one source.
            migrationBuilder.Sql(@"
                INSERT INTO administrations.allocations (
                                id, household_id, administration_event_id, package_id,
                                quantity_numerator, quantity_denominator, ledger_entry_id,
                                correlation_id, is_active, created_at)
                            SELECT gen_random_uuid(), e.household_id, e.administration_event_id, e.package_id,
                                   -e.quantity_numerator, e.quantity_denominator, e.id, e.correlation_id,
                                   true, e.recorded_at
                              FROM inventory.ledger_entries e
                             WHERE e.administration_event_id IS NOT NULL
                               AND e.quantity_numerator < 0;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_packages_created_by_account_id",
                schema: "inventory",
                table: "packages",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_packages_household_id_medication_definition_id",
                schema: "inventory",
                table: "packages",
                columns: new[] { "household_id", "medication_definition_id" });

            migrationBuilder.CreateIndex(
                name: "IX_packages_medication_definition_id_ordinal",
                schema: "inventory",
                table: "packages",
                columns: new[] { "medication_definition_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_packages_single_pinned_per_medication",
                schema: "inventory",
                table: "packages",
                column: "medication_definition_id",
                unique: true,
                filter: "is_pinned");

            migrationBuilder.AddCheckConstraint(
                name: "ck_packages_capacity",
                schema: "inventory",
                table: "packages",
                sql: "nominal_capacity_numerator > 0 AND nominal_capacity_denominator > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_packages_opened_at",
                schema: "inventory",
                table: "packages",
                sql: "(state = 'Sealed' AND opened_at IS NULL) OR (state <> 'Sealed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_packages_ordinal",
                schema: "inventory",
                table: "packages",
                sql: "ordinal > 0");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_actor_account_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_correlation_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_household_id_medication_definition_id",
                schema: "inventory",
                table: "ledger_entries",
                columns: new[] { "household_id", "medication_definition_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_medication_definition_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "medication_definition_id");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_reverses_entry_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "reverses_entry_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_non_zero",
                schema: "inventory",
                table: "ledger_entries",
                sql: "quantity_numerator <> 0 OR entry_type = 'CountAdjustment'");

            migrationBuilder.CreateIndex(
                name: "IX_count_sessions_package_id",
                schema: "inventory",
                table: "count_sessions",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "IX_administration_events_actor_account_id",
                schema: "administrations",
                table: "administration_events",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_administration_events_household_id_occurred_at",
                schema: "administrations",
                table: "administration_events",
                columns: new[] { "household_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_administration_events_household_id_plan_version_id_schedule~",
                schema: "administrations",
                table: "administration_events",
                columns: new[] { "household_id", "plan_version_id", "scheduled_for" },
                unique: true,
                filter: "plan_version_id IS NOT NULL AND scheduled_for IS NOT NULL AND outcome <> 'ExtraDose'");

            migrationBuilder.CreateIndex(
                name: "IX_administration_events_person_id",
                schema: "administrations",
                table: "administration_events",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "IX_administration_events_plan_version_id",
                schema: "administrations",
                table: "administration_events",
                column: "plan_version_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_administration_events_outcome",
                schema: "administrations",
                table: "administration_events",
                sql: "outcome IN ('Taken', 'Skipped', 'PartialDose', 'ExtraDose')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_administration_events_quantity",
                schema: "administrations",
                table: "administration_events",
                sql: "(outcome = 'Skipped' AND actual_quantity_numerator IS NULL AND stock_source = 'NotApplicable') OR (outcome <> 'Skipped' AND actual_quantity_numerator > 0 AND actual_quantity_denominator > 0 AND stock_source <> 'NotApplicable')");

            migrationBuilder.CreateIndex(
                name: "IX_allocation_corrections_actor_account_id",
                schema: "administrations",
                table: "allocation_corrections",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_allocation_corrections_administration_event_id",
                schema: "administrations",
                table: "allocation_corrections",
                column: "administration_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_allocation_corrections_from_package_id",
                schema: "administrations",
                table: "allocation_corrections",
                column: "from_package_id");

            migrationBuilder.CreateIndex(
                name: "IX_allocation_corrections_household_id_administration_event_id~",
                schema: "administrations",
                table: "allocation_corrections",
                columns: new[] { "household_id", "administration_event_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_allocation_corrections_superseded_allocation_id",
                schema: "administrations",
                table: "allocation_corrections",
                column: "superseded_allocation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_allocation_corrections_to_package_id",
                schema: "administrations",
                table: "allocation_corrections",
                column: "to_package_id");

            migrationBuilder.CreateIndex(
                name: "IX_allocations_administration_event_id_is_active",
                schema: "administrations",
                table: "allocations",
                columns: new[] { "administration_event_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_allocations_household_id",
                schema: "administrations",
                table: "allocations",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "IX_allocations_ledger_entry_id",
                schema: "administrations",
                table: "allocations",
                column: "ledger_entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_allocations_package_id",
                schema: "administrations",
                table: "allocations",
                column: "package_id",
                filter: "package_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_allocations_superseded_by_allocation_id",
                schema: "administrations",
                table: "allocations",
                column: "superseded_by_allocation_id");

            migrationBuilder.CreateIndex(
                name: "IX_medication_definition_change_events_account_id",
                schema: "catalog",
                table: "medication_definition_change_events",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_medication_definition_change_events_household_id_medication~",
                schema: "catalog",
                table: "medication_definition_change_events",
                columns: new[] { "household_id", "medication_definition_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_medication_definition_change_events_medication_definition_id",
                schema: "catalog",
                table: "medication_definition_change_events",
                column: "medication_definition_id");

            migrationBuilder.CreateIndex(
                name: "IX_medication_definitions_household_id_name",
                schema: "catalog",
                table: "medication_definitions",
                columns: new[] { "household_id", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_medication_definitions_legacy_person_id",
                schema: "catalog",
                table: "medication_definitions",
                column: "legacy_person_id");

            migrationBuilder.CreateIndex(
                name: "IX_medication_refill_policies_household_id",
                schema: "refill",
                table: "medication_refill_policies",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "IX_medication_refill_policies_medication_definition_id",
                schema: "refill",
                table: "medication_refill_policies",
                column: "medication_definition_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medication_refill_policies_updated_by_account_id",
                schema: "refill",
                table: "medication_refill_policies",
                column: "updated_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_change_events_account_id",
                schema: "treatments",
                table: "plan_change_events",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_change_events_household_id_plan_id_recorded_at",
                schema: "treatments",
                table: "plan_change_events",
                columns: new[] { "household_id", "plan_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_plan_change_events_plan_id",
                schema: "treatments",
                table: "plan_change_events",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_versions_created_by_account_id",
                schema: "treatments",
                table: "plan_versions",
                column: "created_by_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_versions_plan_id_created_at",
                schema: "treatments",
                table: "plan_versions",
                columns: new[] { "plan_id", "created_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plan_versions_plan_id_version_number",
                schema: "treatments",
                table: "plan_versions",
                columns: new[] { "plan_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plans_household_id_person_id_medication_definition_id",
                schema: "treatments",
                table: "plans",
                columns: new[] { "household_id", "person_id", "medication_definition_id" });

            migrationBuilder.CreateIndex(
                name: "IX_plans_medication_definition_id",
                schema: "treatments",
                table: "plans",
                column: "medication_definition_id");

            migrationBuilder.CreateIndex(
                name: "IX_plans_person_id",
                schema: "treatments",
                table: "plans",
                column: "person_id");

            migrationBuilder.AddForeignKey(
                name: "FK_administration_events_accounts_actor_account_id",
                schema: "administrations",
                table: "administration_events",
                column: "actor_account_id",
                principalSchema: "identity",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_administration_events_households_household_id",
                schema: "administrations",
                table: "administration_events",
                column: "household_id",
                principalSchema: "households",
                principalTable: "households",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_administration_events_medication_definitions_medication_def~",
                schema: "administrations",
                table: "administration_events",
                column: "medication_definition_id",
                principalSchema: "catalog",
                principalTable: "medication_definitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_administration_events_people_person_id",
                schema: "administrations",
                table: "administration_events",
                column: "person_id",
                principalSchema: "care",
                principalTable: "people",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_administration_events_plan_versions_plan_version_id",
                schema: "administrations",
                table: "administration_events",
                column: "plan_version_id",
                principalSchema: "treatments",
                principalTable: "plan_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_count_sessions_accounts_account_id",
                schema: "inventory",
                table: "count_sessions",
                column: "account_id",
                principalSchema: "identity",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_count_sessions_count_batches_batch_id",
                schema: "inventory",
                table: "count_sessions",
                column: "batch_id",
                principalSchema: "inventory",
                principalTable: "count_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_count_sessions_households_household_id",
                schema: "inventory",
                table: "count_sessions",
                column: "household_id",
                principalSchema: "households",
                principalTable: "households",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_count_sessions_inventory_items_inventory_item_id",
                schema: "inventory",
                table: "count_sessions",
                column: "inventory_item_id",
                principalSchema: "inventory",
                principalTable: "inventory_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_count_sessions_ledger_entries_ledger_entry_id",
                schema: "inventory",
                table: "count_sessions",
                column: "ledger_entry_id",
                principalSchema: "inventory",
                principalTable: "ledger_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_count_sessions_packages_package_id",
                schema: "inventory",
                table: "count_sessions",
                column: "package_id",
                principalSchema: "inventory",
                principalTable: "packages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_items_medication_definitions_medication_id",
                schema: "inventory",
                table: "inventory_items",
                column: "medication_id",
                principalSchema: "catalog",
                principalTable: "medication_definitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entries_accounts_actor_account_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "actor_account_id",
                principalSchema: "identity",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entries_ledger_entries_reverses_entry_id",
                schema: "inventory",
                table: "ledger_entries",
                column: "reverses_entry_id",
                principalSchema: "inventory",
                principalTable: "ledger_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ledger_entries_medication_definitions_medication_definition~",
                schema: "inventory",
                table: "ledger_entries",
                column: "medication_definition_id",
                principalSchema: "catalog",
                principalTable: "medication_definitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_packages_accounts_created_by_account_id",
                schema: "inventory",
                table: "packages",
                column: "created_by_account_id",
                principalSchema: "identity",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_packages_medication_definitions_medication_definition_id",
                schema: "inventory",
                table: "packages",
                column: "medication_definition_id",
                principalSchema: "catalog",
                principalTable: "medication_definitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_packages_people_holder_person_id",
                schema: "inventory",
                table: "packages",
                column: "holder_person_id",
                principalSchema: "care",
                principalTable: "people",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // EF expected this index to arrive by renaming the old regimen_version_id
            // index; that index now covers plan_version_id, so it is created here.
            migrationBuilder.CreateIndex(
                name: "IX_administration_events_medication_definition_id",
                schema: "administrations",
                table: "administration_events",
                column: "medication_definition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration renames tables, reshapes columns and transforms values in
            // place. A generated reverse would silently discard the amounts, allocations
            // and package identities it cannot reconstruct, so it is refused: the rollback
            // path is the verified backup taken before deployment, which deploy-production.sh
            // writes and checks.
            throw new NotSupportedException(
                "The package-first rebuild is not reversible. Restore the pre-deployment "
                + "PostgreSQL backup instead.");
        }
    }
}
