using MedicationTracker.Api.Modules.Administrations;
using MedicationTracker.Api.Modules.Audit;
using MedicationTracker.Api.Modules.Catalog;
using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.People;
using MedicationTracker.Api.Modules.Refill;
using MedicationTracker.Api.Modules.Sync;
using MedicationTracker.Api.Modules.Treatments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicationTracker.Api.Persistence.Configurations;

public sealed class TreatmentPlanConfiguration : IEntityTypeConfiguration<TreatmentPlan>
{
    public void Configure(EntityTypeBuilder<TreatmentPlan> b)
    {
        b.ToTable("plans", "treatments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.PersonId).HasColumnName("person_id");
        b.Property(x => x.MedicationDefinitionId).HasColumnName("medication_definition_id");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        b.Ignore(x => x.IsActive);

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationDefinition>().WithMany().HasForeignKey(x => x.MedicationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.PersonId, x.MedicationDefinitionId });
    }
}

public sealed class TreatmentPlanVersionConfiguration : IEntityTypeConfiguration<TreatmentPlanVersion>
{
    public void Configure(EntityTypeBuilder<TreatmentPlanVersion> b)
    {
        b.ToTable("plan_versions", "treatments", table =>
        {
            table.HasCheckConstraint(
                "ck_plan_versions_period",
                "effective_from IS NULL OR effective_to IS NULL OR effective_to >= effective_from");
            table.HasCheckConstraint("ck_plan_versions_dose", "dose_numerator > 0 AND dose_denominator > 0");
            table.HasCheckConstraint("ck_plan_versions_version_number", "version_number > 0");
            table.HasCheckConstraint("ck_plan_versions_kind", "kind IN ('Scheduled', 'AsNeeded')");

            // A scheduled version must say when: an exact time or a named period.
            table.HasCheckConstraint(
                "ck_plan_versions_schedule",
                "kind = 'AsNeeded' OR local_time IS NOT NULL OR day_period IS NOT NULL");

            // Each recurrence pattern owns exactly the fields it needs and no others.
            table.HasCheckConstraint(
                "ck_plan_versions_recurrence",
                "(pattern = 'Daily' AND weekday_mask IS NULL AND interval_days IS NULL) "
                + "OR (kind = 'Scheduled' AND pattern = 'SelectedWeekdays' AND weekday_mask BETWEEN 1 AND 127 "
                + "AND interval_days IS NULL) "
                + "OR (kind = 'Scheduled' AND pattern = 'EveryNDays' AND effective_from IS NOT NULL "
                + "AND interval_days BETWEEN 1 AND 3650 AND weekday_mask IS NULL)");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TreatmentPlanId).HasColumnName("plan_id");
        b.Property(x => x.VersionNumber).HasColumnName("version_number").HasDefaultValue(1);
        b.Property(x => x.DoseNumerator).HasColumnName("dose_numerator");
        b.Property(x => x.DoseDenominator).HasColumnName("dose_denominator");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Pattern).HasColumnName("pattern").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.WeekdayMask).HasColumnName("weekday_mask");
        b.Property(x => x.IntervalDays).HasColumnName("interval_days");
        b.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        b.Property(x => x.EffectiveTo).HasColumnName("effective_to");
        b.Property(x => x.LocalTime).HasColumnName("local_time");
        b.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100).IsRequired();
        b.Property(x => x.DayPeriod).HasColumnName("day_period").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.MealRelation).HasColumnName("meal_relation").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.MinimumIntervalMinutes).HasColumnName("minimum_interval_minutes");
        b.Property(x => x.Instructions).HasColumnName("instructions").HasMaxLength(1000);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id");
        b.Ignore(x => x.Dose);
        b.Ignore(x => x.Recurrence);

        b.HasOne<TreatmentPlan>().WithMany().HasForeignKey(x => x.TreatmentPlanId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TreatmentPlanId, x.CreatedAt }).IsUnique();
        b.HasIndex(x => new { x.TreatmentPlanId, x.VersionNumber }).IsUnique();
    }
}

public sealed class TreatmentPlanChangeEventConfiguration : IEntityTypeConfiguration<TreatmentPlanChangeEvent>
{
    public void Configure(EntityTypeBuilder<TreatmentPlanChangeEvent> b)
    {
        b.ToTable("plan_change_events", "treatments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.TreatmentPlanId).HasColumnName("plan_id");
        b.Property(x => x.AccountId).HasColumnName("account_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.PreviousValue).HasColumnName("previous_value").HasMaxLength(4000);
        b.Property(x => x.NewValue).HasColumnName("new_value").HasMaxLength(4000);
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at");

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TreatmentPlan>().WithMany().HasForeignKey(x => x.TreatmentPlanId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.TreatmentPlanId, x.RecordedAt });
    }
}

public sealed class AdministrationEventConfiguration : IEntityTypeConfiguration<AdministrationEvent>
{
    public void Configure(EntityTypeBuilder<AdministrationEvent> b)
    {
        b.ToTable("administration_events", "administrations", table =>
        {
            table.HasCheckConstraint(
                "ck_administration_events_outcome",
                "outcome IN ('Taken', 'Skipped', 'PartialDose', 'ExtraDose')");

            // A skipped dose consumes nothing and records no amount; every other
            // outcome must say how much was taken and where it came from.
            table.HasCheckConstraint(
                "ck_administration_events_quantity",
                "(outcome = 'Skipped' AND actual_quantity_numerator IS NULL AND stock_source = 'NotApplicable') "
                + "OR (outcome <> 'Skipped' AND actual_quantity_numerator > 0 "
                + "AND actual_quantity_denominator > 0 AND stock_source <> 'NotApplicable')");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.PersonId).HasColumnName("person_id");
        b.Property(x => x.MedicationDefinitionId).HasColumnName("medication_definition_id");
        b.Property(x => x.TreatmentPlanVersionId).HasColumnName("plan_version_id");
        b.Property(x => x.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.StockSource).HasColumnName("stock_source").HasConversion<string>().HasMaxLength(30)
            .IsRequired();
        b.Property(x => x.PlannedQuantityNumerator).HasColumnName("planned_quantity_numerator");
        b.Property(x => x.PlannedQuantityDenominator).HasColumnName("planned_quantity_denominator");
        b.Property(x => x.ActualQuantityNumerator).HasColumnName("actual_quantity_numerator");
        b.Property(x => x.ActualQuantityDenominator).HasColumnName("actual_quantity_denominator");
        b.Property(x => x.ScheduledFor).HasColumnName("scheduled_for");
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
        b.Property(x => x.ActorAccountId).HasColumnName("actor_account_id");
        b.Property(x => x.Note).HasColumnName("note").HasMaxLength(2000);
        b.Ignore(x => x.PlannedQuantity);
        b.Ignore(x => x.ActualQuantity);
        b.Ignore(x => x.ConsumesStock);
        b.Ignore(x => x.DrawsFromTrackedInventory);
        b.Ignore(x => x.LatenessMinutes);

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationDefinition>().WithMany().HasForeignKey(x => x.MedicationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TreatmentPlanVersion>().WithMany().HasForeignKey(x => x.TreatmentPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);

        // One record per scheduled slot, so a replayed "taken" cannot duplicate it.
        // Extra and unplanned doses carry no plan version and are therefore exempt,
        // which is what makes recording a second dose in the same slot possible.
        b.HasIndex(x => new { x.HouseholdId, x.TreatmentPlanVersionId, x.ScheduledFor })
            .IsUnique()
            .HasFilter("plan_version_id IS NOT NULL AND scheduled_for IS NOT NULL AND outcome <> 'ExtraDose'");

        b.HasIndex(x => new { x.HouseholdId, x.OccurredAt });
    }
}

public sealed class AdministrationAllocationConfiguration : IEntityTypeConfiguration<AdministrationAllocation>
{
    public void Configure(EntityTypeBuilder<AdministrationAllocation> b)
    {
        b.ToTable("allocations", "administrations", table =>
            table.HasCheckConstraint(
                "ck_allocations_quantity",
                "quantity_numerator > 0 AND quantity_denominator > 0"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.AdministrationEventId).HasColumnName("administration_event_id");
        b.Property(x => x.PackageId).HasColumnName("package_id");
        b.Property(x => x.QuantityNumerator).HasColumnName("quantity_numerator");
        b.Property(x => x.QuantityDenominator).HasColumnName("quantity_denominator");
        b.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id");
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.SupersededByAllocationId).HasColumnName("superseded_by_allocation_id");
        b.Property(x => x.SupersededAt).HasColumnName("superseded_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdministrationEvent>().WithMany().HasForeignKey(x => x.AdministrationEventId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationPackage>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryLedgerEntry>().WithMany().HasForeignKey(x => x.LedgerEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdministrationAllocation>().WithMany().HasForeignKey(x => x.SupersededByAllocationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.AdministrationEventId, x.IsActive });
        b.HasIndex(x => x.PackageId).HasFilter("package_id IS NOT NULL");

        // Each consuming ledger entry is accounted for by exactly one allocation.
        b.HasIndex(x => x.LedgerEntryId).IsUnique();
    }
}

public sealed class AdministrationAllocationCorrectionConfiguration
    : IEntityTypeConfiguration<AdministrationAllocationCorrection>
{
    public void Configure(EntityTypeBuilder<AdministrationAllocationCorrection> b)
    {
        b.ToTable("allocation_corrections", "administrations", table =>
        {
            table.HasCheckConstraint(
                "ck_allocation_corrections_quantity",
                "quantity_numerator > 0 AND quantity_denominator > 0");

            // Moving stock to where it already was is not a correction. The null-safe
            // comparison also rejects a loose-to-loose "move".
            table.HasCheckConstraint(
                "ck_allocation_corrections_distinct_source",
                "from_package_id IS DISTINCT FROM to_package_id");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.AdministrationEventId).HasColumnName("administration_event_id");
        b.Property(x => x.SupersededAllocationId).HasColumnName("superseded_allocation_id");
        b.Property(x => x.ReplacementAllocationId).HasColumnName("replacement_allocation_id");
        b.Property(x => x.FromPackageId).HasColumnName("from_package_id");
        b.Property(x => x.ToPackageId).HasColumnName("to_package_id");
        b.Property(x => x.QuantityNumerator).HasColumnName("quantity_numerator");
        b.Property(x => x.QuantityDenominator).HasColumnName("quantity_denominator");
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        b.Property(x => x.ReversalLedgerEntryId).HasColumnName("reversal_ledger_entry_id");
        b.Property(x => x.ConsumeLedgerEntryId).HasColumnName("consume_ledger_entry_id");
        b.Property(x => x.ActorAccountId).HasColumnName("actor_account_id");
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
        b.Ignore(x => x.Quantity);

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdministrationEvent>().WithMany().HasForeignKey(x => x.AdministrationEventId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdministrationAllocation>().WithMany().HasForeignKey(x => x.SupersededAllocationId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationPackage>().WithMany().HasForeignKey(x => x.FromPackageId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationPackage>().WithMany().HasForeignKey(x => x.ToPackageId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.HouseholdId, x.AdministrationEventId, x.RecordedAt });

        // An allocation can only be superseded once, matching the domain rule.
        b.HasIndex(x => x.SupersededAllocationId).IsUnique();
    }
}

public sealed class MedicationRefillPolicyConfiguration : IEntityTypeConfiguration<MedicationRefillPolicy>
{
    public void Configure(EntityTypeBuilder<MedicationRefillPolicy> b)
    {
        b.ToTable("medication_refill_policies", "refill", table =>
        {
            table.HasCheckConstraint(
                "ck_refill_policies_threshold",
                "(low_stock_threshold_numerator IS NULL AND low_stock_threshold_denominator IS NULL) "
                + "OR (low_stock_threshold_numerator >= 0 AND low_stock_threshold_denominator > 0)");
            table.HasCheckConstraint(
                "ck_refill_policies_days",
                "low_stock_days IS NULL OR (low_stock_days >= 0 AND low_stock_days <= 365)");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.MedicationDefinitionId).HasColumnName("medication_definition_id");
        b.Property(x => x.LowStockThresholdNumerator).HasColumnName("low_stock_threshold_numerator");
        b.Property(x => x.LowStockThresholdDenominator).HasColumnName("low_stock_threshold_denominator");
        b.Property(x => x.LowStockDays).HasColumnName("low_stock_days");
        b.Property(x => x.NextEligibleRefillOn).HasColumnName("next_eligible_refill_on");
        b.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.UpdatedByAccountId).HasColumnName("updated_by_account_id");
        b.Ignore(x => x.LowStockThreshold);

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationDefinition>().WithMany().HasForeignKey(x => x.MedicationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.UpdatedByAccountId).OnDelete(DeleteBehavior.Restrict);

        // One policy per medication.
        b.HasIndex(x => x.MedicationDefinitionId).IsUnique();
    }
}

public sealed class SyncCommandReceiptConfiguration : IEntityTypeConfiguration<SyncCommandReceipt>
{
    public void Configure(EntityTypeBuilder<SyncCommandReceipt> b)
    {
        b.ToTable("command_receipts", "sync");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.AccountId).HasColumnName("account_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(60).IsRequired();
        b.Property(x => x.ResultEntityId).HasColumnName("result_entity_id");
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at");

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.IdempotencyKey }).IsUnique();
    }
}

public sealed class ProcessedAdministrationCommandConfiguration
    : IEntityTypeConfiguration<ProcessedAdministrationCommand>
{
    public void Configure(EntityTypeBuilder<ProcessedAdministrationCommand> b)
    {
        b.ToTable("processed_administration_commands", "sync");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.AccountId).HasColumnName("account_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        b.Property(x => x.AdministrationEventId).HasColumnName("administration_event_id");
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at");

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdministrationEvent>().WithMany().HasForeignKey(x => x.AdministrationEventId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => x.AdministrationEventId);
    }
}
