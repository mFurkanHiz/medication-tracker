using MedicationTracker.Api.Modules.Care;
using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicationTracker.Api.Persistence.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> b)
    {
        b.ToTable("people", "care"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired(); b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.Id }).IsUnique();
    }
}

public sealed class MedicationConfiguration : IEntityTypeConfiguration<Medication>
{
    public void Configure(EntityTypeBuilder<Medication> b)
    {
        b.ToTable("medications", "care", t => t.HasCheckConstraint("ck_medications_tablet_form", "form = 'tablet'")); b.HasKey(x => x.Id);
        b.Property(x => x.Strength).HasColumnName("strength").HasMaxLength(100);
        b.Property(x => x.ActiveIngredient).HasColumnName("active_ingredient").HasMaxLength(200);
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(100);
        b.Property(x => x.Tags).HasColumnName("tags").HasColumnType("text[]");
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.PersonId).HasColumnName("person_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired(); b.Property(x => x.Form).HasColumnName("form").HasMaxLength(40).IsRequired(); b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> b)
    {
        b.ToTable("inventory_items", "inventory"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.MedicationId).HasColumnName("medication_id"); b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Medication>().WithMany().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.MedicationId).IsUnique();
    }
}

public sealed class InventoryLedgerEntryConfiguration : IEntityTypeConfiguration<InventoryLedgerEntry>
{
    public void Configure(EntityTypeBuilder<InventoryLedgerEntry> b)
    {
        b.ToTable("ledger_entries", "inventory", t => t.HasCheckConstraint("ck_ledger_entries_denominator", "quantity_denominator > 0")); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.InventoryItemId).HasColumnName("inventory_item_id"); b.Property(x => x.AdministrationEventId).HasColumnName("administration_event_id"); b.Property(x => x.PackageId).HasColumnName("package_id");
        b.Property(x => x.QuantityNumerator).HasColumnName("quantity_numerator"); b.Property(x => x.QuantityDenominator).HasColumnName("quantity_denominator"); b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(40).IsRequired(); b.Property(x => x.OccurredAt).HasColumnName("occurred_at"); b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
        b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdministrationEvent>().WithMany().HasForeignKey(x => x.AdministrationEventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryPackage>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.AdministrationEventId).HasFilter("administration_event_id IS NOT NULL");
        b.HasIndex(x => x.PackageId).HasFilter("package_id IS NOT NULL");
    }
}

public sealed class InventoryPackageConfiguration : IEntityTypeConfiguration<InventoryPackage>
{
    public void Configure(EntityTypeBuilder<InventoryPackage> b)
    {
        b.ToTable("packages", "inventory", t => t.HasCheckConstraint("ck_packages_capacity", "capacity_numerator > 0 AND capacity_denominator > 0")); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.InventoryItemId).HasColumnName("inventory_item_id"); b.Property(x => x.PersonId).HasColumnName("person_id");
        b.Property(x => x.CapacityNumerator).HasColumnName("capacity_numerator"); b.Property(x => x.CapacityDenominator).HasColumnName("capacity_denominator"); b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict); b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.InventoryItemId });
    }
}

public sealed class InventoryPackageAssignmentEventConfiguration : IEntityTypeConfiguration<InventoryPackageAssignmentEvent>
{
    public void Configure(EntityTypeBuilder<InventoryPackageAssignmentEvent> b)
    {
        b.ToTable("package_assignment_events", "inventory"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.PackageId).HasColumnName("package_id"); b.Property(x => x.AccountId).HasColumnName("account_id"); b.Property(x => x.FromPersonId).HasColumnName("from_person_id"); b.Property(x => x.ToPersonId).HasColumnName("to_person_id"); b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict); b.HasOne<InventoryPackage>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.FromPersonId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Person>().WithMany().HasForeignKey(x => x.ToPersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.PackageId, x.RecordedAt });
    }
}

public sealed class MedicationChangeEventConfiguration : IEntityTypeConfiguration<MedicationChangeEvent>
{
    public void Configure(EntityTypeBuilder<MedicationChangeEvent> b)
    {
        b.ToTable("medication_change_events", "care"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.MedicationId).HasColumnName("medication_id"); b.Property(x => x.AccountId).HasColumnName("account_id"); b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40).IsRequired(); b.Property(x => x.PreviousValue).HasColumnName("previous_value").HasMaxLength(4000); b.Property(x => x.NewValue).HasColumnName("new_value").HasMaxLength(4000); b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Medication>().WithMany().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.MedicationId, x.RecordedAt });
    }
}

public sealed class RegimenConfiguration : IEntityTypeConfiguration<Regimen>
{
    public void Configure(EntityTypeBuilder<Regimen> b)
    {
        b.ToTable("regimens", "treatments"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.PersonId).HasColumnName("person_id"); b.Property(x => x.MedicationId).HasColumnName("medication_id"); b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Medication>().WithMany().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RegimenChangeEventConfiguration : IEntityTypeConfiguration<RegimenChangeEvent>
{
    public void Configure(EntityTypeBuilder<RegimenChangeEvent> b)
    {
        b.ToTable("regimen_change_events", "treatments"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.RegimenId).HasColumnName("regimen_id"); b.Property(x => x.AccountId).HasColumnName("account_id"); b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40).IsRequired(); b.Property(x => x.PreviousValue).HasColumnName("previous_value").HasMaxLength(4000); b.Property(x => x.NewValue).HasColumnName("new_value").HasMaxLength(4000); b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Regimen>().WithMany().HasForeignKey(x => x.RegimenId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.RegimenId, x.RecordedAt });
    }
}

public sealed class RegimenVersionConfiguration : IEntityTypeConfiguration<RegimenVersion>
{
    public void Configure(EntityTypeBuilder<RegimenVersion> b)
    {
        b.ToTable("regimen_versions", "treatments", t => { t.HasCheckConstraint("ck_regimen_versions_period", "valid_from IS NULL OR valid_to IS NULL OR valid_to >= valid_from"); t.HasCheckConstraint("ck_regimen_versions_denominator", "dose_denominator > 0"); t.HasCheckConstraint("ck_regimen_versions_schedule_type", "schedule_type IN ('scheduled', 'as_needed')"); t.HasCheckConstraint("ck_regimen_versions_schedule", "schedule_type = 'as_needed' OR local_time IS NOT NULL OR day_period IS NOT NULL"); }); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.RegimenId).HasColumnName("regimen_id"); b.Property(x => x.ValidFrom).HasColumnName("valid_from"); b.Property(x => x.ValidTo).HasColumnName("valid_to"); b.Property(x => x.DoseNumerator).HasColumnName("dose_numerator"); b.Property(x => x.DoseDenominator).HasColumnName("dose_denominator"); b.Property(x => x.LocalTime).HasColumnName("local_time"); b.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100).IsRequired(); b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.ScheduleType).HasColumnName("schedule_type").HasMaxLength(20).HasDefaultValue("scheduled").IsRequired(); b.Property(x => x.DayPeriod).HasColumnName("day_period").HasMaxLength(20); b.Property(x => x.MealRelation).HasColumnName("meal_relation").HasMaxLength(20); b.Property(x => x.MinimumIntervalMinutes).HasColumnName("minimum_interval_minutes");
        b.HasOne<Regimen>().WithMany().HasForeignKey(x => x.RegimenId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.RegimenId, x.CreatedAt }).IsUnique();
    }
}

public sealed class AdministrationEventConfiguration : IEntityTypeConfiguration<AdministrationEvent>
{
    public void Configure(EntityTypeBuilder<AdministrationEvent> b)
    {
        b.ToTable("administration_events", "administrations", t => t.HasCheckConstraint("ck_administration_events_outcome", "outcome IN ('taken', 'skipped')")); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.PersonId).HasColumnName("person_id"); b.Property(x => x.MedicationId).HasColumnName("medication_id"); b.Property(x => x.RegimenVersionId).HasColumnName("regimen_version_id"); b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(20).IsRequired(); b.Property(x => x.ScheduledFor).HasColumnName("scheduled_for"); b.Property(x => x.OccurredAt).HasColumnName("occurred_at"); b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
        b.HasOne<RegimenVersion>().WithMany().HasForeignKey(x => x.RegimenVersionId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.HouseholdId, x.RegimenVersionId, x.ScheduledFor }).IsUnique();
    }
}

public sealed class SyncCommandReceiptConfiguration : IEntityTypeConfiguration<SyncCommandReceipt>
{
    public void Configure(EntityTypeBuilder<SyncCommandReceipt> b)
    {
        b.ToTable("command_receipts", "sync"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.AccountId).HasColumnName("account_id"); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired(); b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(60).IsRequired(); b.Property(x => x.ResultEntityId).HasColumnName("result_entity_id"); b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.IdempotencyKey }).IsUnique();
    }
}

public sealed class ProcessedAdministrationCommandConfiguration : IEntityTypeConfiguration<ProcessedAdministrationCommand>
{
    public void Configure(EntityTypeBuilder<ProcessedAdministrationCommand> b)
    {
        b.ToTable("processed_administration_commands", "sync"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.HouseholdId).HasColumnName("household_id"); b.Property(x => x.AccountId).HasColumnName("account_id"); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired(); b.Property(x => x.AdministrationEventId).HasColumnName("administration_event_id"); b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict); b.HasOne<AdministrationEvent>().WithMany().HasForeignKey(x => x.AdministrationEventId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => x.AdministrationEventId);
    }
}
