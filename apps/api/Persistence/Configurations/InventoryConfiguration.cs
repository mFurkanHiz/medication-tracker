using MedicationTracker.Api.Modules.Administrations;
using MedicationTracker.Api.Modules.Catalog;
using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicationTracker.Api.Persistence.Configurations;

public sealed class LegacyInventoryItemConfiguration : IEntityTypeConfiguration<LegacyInventoryItem>
{
    public void Configure(EntityTypeBuilder<LegacyInventoryItem> b)
    {
        b.ToTable("inventory_items", "inventory");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.MedicationDefinitionId).HasColumnName("medication_id");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationDefinition>().WithMany().HasForeignKey(x => x.MedicationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.MedicationDefinitionId).IsUnique();
    }
}

public sealed class MedicationPackageConfiguration : IEntityTypeConfiguration<MedicationPackage>
{
    public void Configure(EntityTypeBuilder<MedicationPackage> b)
    {
        b.ToTable("packages", "inventory", table =>
        {
            table.HasCheckConstraint(
                "ck_packages_capacity",
                "nominal_capacity_numerator > 0 AND nominal_capacity_denominator > 0");
            table.HasCheckConstraint("ck_packages_ordinal", "ordinal > 0");

            // A sealed package has never been opened; an opened one has a timestamp.
            table.HasCheckConstraint(
                "ck_packages_opened_at",
                "(state = 'Sealed' AND opened_at IS NULL) OR (state <> 'Sealed')");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.MedicationDefinitionId).HasColumnName("medication_definition_id");
        b.Property(x => x.LegacyInventoryItemId).HasColumnName("inventory_item_id");
        b.Property(x => x.Ordinal).HasColumnName("ordinal");
        b.Property(x => x.NominalCapacityNumerator).HasColumnName("nominal_capacity_numerator");
        b.Property(x => x.NominalCapacityDenominator).HasColumnName("nominal_capacity_denominator");
        b.Property(x => x.Unit).HasColumnName("unit").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.OpenedAt).HasColumnName("opened_at");
        b.Property(x => x.ExpiresOn).HasColumnName("expires_on");
        b.Property(x => x.AcquiredOn).HasColumnName("acquired_on");
        b.Property(x => x.LotNumber).HasColumnName("lot_number").HasMaxLength(100);
        b.Property(x => x.Barcode).HasColumnName("barcode").HasMaxLength(100);
        b.Property(x => x.Source).HasColumnName("source").HasMaxLength(200);
        b.Property(x => x.StorageLocation).HasColumnName("storage_location").HasMaxLength(200);
        b.Property(x => x.Note).HasColumnName("note").HasMaxLength(2000);
        b.Property(x => x.Label).HasColumnName("label").HasMaxLength(MedicationPackage.MaximumLabelLength);
        b.Property(x => x.Coverage).HasColumnName("coverage").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.OwnerPersonId).HasColumnName("owner_person_id");
        b.Property(x => x.HolderPersonId).HasColumnName("holder_person_id");
        b.Property(x => x.IsPinned).HasColumnName("is_pinned").HasDefaultValue(false);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id");
        b.Property(x => x.RetiredAt).HasColumnName("retired_at");
        b.Ignore(x => x.NominalCapacity);
        b.Ignore(x => x.IsAvailable);

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationDefinition>().WithMany().HasForeignKey(x => x.MedicationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LegacyInventoryItem>().WithMany().HasForeignKey(x => x.LegacyInventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.OwnerPersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.HolderPersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.HouseholdId, x.MedicationDefinitionId });

        // The friendly "Box N" label must be stable and unique per medication.
        b.HasIndex(x => new { x.MedicationDefinitionId, x.Ordinal }).IsUnique();

        // At most one pinned package per medication, enforced by the database rather
        // than by application convention.
        b.HasIndex(x => x.MedicationDefinitionId)
            .HasFilter("is_pinned")
            .IsUnique()
            .HasDatabaseName("ix_packages_single_pinned_per_medication");
    }
}

public sealed class InventoryLedgerEntryConfiguration : IEntityTypeConfiguration<InventoryLedgerEntry>
{
    public void Configure(EntityTypeBuilder<InventoryLedgerEntry> b)
    {
        b.ToTable("ledger_entries", "inventory", table =>
        {
            table.HasCheckConstraint("ck_ledger_entries_denominator", "quantity_denominator > 0");

            // A zero delta is only meaningful for a count that confirmed the balance, or
            // for a medication added with no stock yet — production holds six of the
            // latter. For a consumption, loss or disposal it would be a stock change that
            // changed nothing, which is a bug worth refusing at the database.
            table.HasCheckConstraint(
                "ck_ledger_entries_non_zero",
                "quantity_numerator <> 0 OR entry_type IN ('Acquire', 'CountAdjustment')");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.MedicationDefinitionId).HasColumnName("medication_definition_id");
        b.Property(x => x.LegacyInventoryItemId).HasColumnName("inventory_item_id");
        b.Property(x => x.PackageId).HasColumnName("package_id");
        b.Property(x => x.QuantityNumerator).HasColumnName("quantity_numerator");
        b.Property(x => x.QuantityDenominator).HasColumnName("quantity_denominator");
        b.Property(x => x.EntryType).HasColumnName("entry_type").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        b.Property(x => x.AdministrationEventId).HasColumnName("administration_event_id");
        b.Property(x => x.ReversesEntryId).HasColumnName("reverses_entry_id");
        b.Property(x => x.ActorAccountId).HasColumnName("actor_account_id");
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(200);
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
        b.Ignore(x => x.Quantity);

        b.HasOne<MedicationDefinition>().WithMany().HasForeignKey(x => x.MedicationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LegacyInventoryItem>().WithMany().HasForeignKey(x => x.LegacyInventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdministrationEvent>().WithMany().HasForeignKey(x => x.AdministrationEventId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationPackage>().WithMany().HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryLedgerEntry>().WithMany().HasForeignKey(x => x.ReversesEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.AdministrationEventId).HasFilter("administration_event_id IS NOT NULL");
        b.HasIndex(x => x.PackageId).HasFilter("package_id IS NOT NULL");
        b.HasIndex(x => x.CorrelationId);

        // Balance projection reads every entry for one medication in a household.
        b.HasIndex(x => new { x.HouseholdId, x.MedicationDefinitionId });
    }
}

public sealed class PackageAssignmentEventConfiguration : IEntityTypeConfiguration<PackageAssignmentEvent>
{
    public void Configure(EntityTypeBuilder<PackageAssignmentEvent> b)
    {
        b.ToTable("package_assignment_events", "inventory");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.PackageId).HasColumnName("package_id");
        b.Property(x => x.AccountId).HasColumnName("account_id");
        b.Property(x => x.FromPersonId).HasColumnName("from_person_id");
        b.Property(x => x.ToPersonId).HasColumnName("to_person_id");
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at");

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationPackage>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.FromPersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ToPersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.PackageId, x.RecordedAt });
    }
}

public sealed class PackageLoanConfiguration : IEntityTypeConfiguration<PackageLoan>
{
    public void Configure(EntityTypeBuilder<PackageLoan> b)
    {
        b.ToTable("package_loans", "inventory", table =>
            table.HasCheckConstraint("ck_package_loans_people", "owner_person_id <> borrower_person_id"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.PackageId).HasColumnName("package_id");
        b.Property(x => x.OwnerPersonId).HasColumnName("owner_person_id");
        b.Property(x => x.BorrowerPersonId).HasColumnName("borrower_person_id");
        b.Property(x => x.LentByAccountId).HasColumnName("lent_by_account_id");
        b.Property(x => x.LentAt).HasColumnName("lent_at");
        b.Property(x => x.ReturnedByAccountId).HasColumnName("returned_by_account_id");
        b.Property(x => x.ReturnedAt).HasColumnName("returned_at");
        b.Ignore(x => x.IsOutstanding);

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationPackage>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.OwnerPersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.BorrowerPersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.LentByAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ReturnedByAccountId).OnDelete(DeleteBehavior.Restrict);

        // One outstanding loan per package at a time.
        b.HasIndex(x => new { x.HouseholdId, x.PackageId }).HasFilter("returned_at IS NULL").IsUnique();
    }
}

public sealed class InventoryCountBatchConfiguration : IEntityTypeConfiguration<InventoryCountBatch>
{
    public void Configure(EntityTypeBuilder<InventoryCountBatch> b)
    {
        b.ToTable("count_batches", "inventory", table =>
            table.HasCheckConstraint("ck_count_batches_revision", "revision_number > 0"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.AccountId).HasColumnName("account_id");
        b.Property(x => x.PreviousBatchId).HasColumnName("previous_batch_id");
        b.Property(x => x.RevisionNumber).HasColumnName("revision_number");
        b.Property(x => x.AcceptedAt).HasColumnName("accepted_at");

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryCountBatch>().WithMany().HasForeignKey(x => x.PreviousBatchId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.AcceptedAt });

        // A batch may be revised at most once, which is what makes the chain linear.
        b.HasIndex(x => x.PreviousBatchId).IsUnique();
    }
}

public sealed class InventoryCountConfiguration : IEntityTypeConfiguration<InventoryCount>
{
    public void Configure(EntityTypeBuilder<InventoryCount> b)
    {
        b.ToTable("count_sessions", "inventory");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.BatchId).HasColumnName("batch_id");
        b.Property(x => x.LegacyInventoryItemId).HasColumnName("inventory_item_id");
        b.Property(x => x.PackageId).HasColumnName("package_id");
        b.Property(x => x.AccountId).HasColumnName("account_id");
        b.Property(x => x.BeforeNumerator).HasColumnName("before_numerator");
        b.Property(x => x.BeforeDenominator).HasColumnName("before_denominator");
        b.Property(x => x.ObservedNumerator).HasColumnName("observed_numerator");
        b.Property(x => x.ObservedDenominator).HasColumnName("observed_denominator");
        b.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id");
        b.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
        b.Ignore(x => x.Before);
        b.Ignore(x => x.Observed);
        b.Ignore(x => x.Adjustment);

        b.HasOne<InventoryCountBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LegacyInventoryItem>().WithMany().HasForeignKey(x => x.LegacyInventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationPackage>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryLedgerEntry>().WithMany().HasForeignKey(x => x.LedgerEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.HouseholdId, x.BatchId });
        b.HasIndex(x => x.LedgerEntryId).IsUnique();
    }
}
