using MedicationTracker.Api.Modules.Administrations;
using MedicationTracker.Api.Modules.Catalog;
using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.Inventory;
using MedicationTracker.Api.Modules.People;
using MedicationTracker.Api.Modules.Refill;
using MedicationTracker.Api.Modules.Subscriptions;
using MedicationTracker.Api.Modules.Sync;
using MedicationTracker.Api.Modules.Treatments;
using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MedicationTracker.Api.Tests;

/// <summary>
/// Asserts the shape of the mapped model: the module separations the product depends on,
/// and the database-level guarantees that protect the inventory invariants. Needs no
/// server, because it inspects the model rather than a schema.
/// </summary>
public sealed class PersistenceModelTests
{
    private readonly MedicationTrackerDbContext _dbContext = new(
        new DbContextOptionsBuilder<MedicationTrackerDbContext>()
            .UseNpgsql("Host=localhost;Database=model_validation")
            .Options);

    [Fact]
    public void Identity_household_membership_and_subscription_remain_separate_concepts()
    {
        var model = _dbContext.Model;

        Assert.NotNull(model.FindEntityType(typeof(Account)));
        Assert.NotNull(model.FindEntityType(typeof(Household)));
        Assert.NotNull(model.FindEntityType(typeof(HouseholdMembership)));
        Assert.NotNull(model.FindEntityType(typeof(Subscription)));
        Assert.NotNull(model.FindEntityType(typeof(Entitlement)));
        Assert.NotNull(model.FindEntityType(typeof(Person)));
    }

    [Fact]
    public void Membership_history_key_includes_effective_start()
    {
        var membership = _dbContext.Model.FindEntityType(typeof(HouseholdMembership));
        var uniqueIndex = Assert.Single(
            membership!.GetIndexes(),
            index => index.IsUnique && index.Properties.Count == 3);

        Assert.Equal(
            [
                nameof(HouseholdMembership.HouseholdId),
                nameof(HouseholdMembership.AccountId),
                nameof(HouseholdMembership.ValidFrom),
            ],
            uniqueIndex.Properties.Select(property => property.Name));
    }

    [Fact]
    public void A_medication_definition_is_catalog_data_and_holds_no_stock()
    {
        var definition = _dbContext.Model.FindEntityType(typeof(MedicationDefinition));

        Assert.NotNull(definition);
        Assert.Equal("medication_definitions", definition.GetTableName());
        Assert.Equal("catalog", definition.GetSchema());

        // No balance, capacity or person-ownership column: those belong to packages and
        // plans. The superseded model's person link survives only as legacy data.
        var columns = definition.GetProperties().Select(property => property.Name).ToList();
        Assert.DoesNotContain("PersonId", columns);
        Assert.Contains(nameof(MedicationDefinition.LegacyPersonId), columns);
    }

    [Fact]
    public void A_package_is_a_physical_container_with_a_snapshotted_capacity_and_no_balance()
    {
        var package = _dbContext.Model.FindEntityType(typeof(MedicationPackage));
        Assert.NotNull(package);

        var columns = package.GetProperties().Select(property => property.Name).ToList();

        // Capacity and unit are snapshots, so a catalog edit cannot reinterpret the box.
        Assert.Contains(nameof(MedicationPackage.NominalCapacityNumerator), columns);
        Assert.Contains(nameof(MedicationPackage.Unit), columns);

        // No remaining-amount column: the balance is derived from the ledger, so the two
        // cannot disagree, and emptiness has one source of truth.
        Assert.DoesNotContain("Balance", columns);
        Assert.DoesNotContain("RemainingNumerator", columns);
        Assert.DoesNotContain("IsFull", columns);
        Assert.DoesNotContain("IsEmpty", columns);

        // Ownership and custody are separate, which is what makes lending auditable.
        Assert.Contains(nameof(MedicationPackage.OwnerPersonId), columns);
        Assert.Contains(nameof(MedicationPackage.HolderPersonId), columns);
    }

    [Fact]
    public void The_friendly_package_label_is_unique_per_medication()
    {
        var package = _dbContext.Model.FindEntityType(typeof(MedicationPackage));

        Assert.Contains(package!.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(MedicationPackage.MedicationDefinitionId),
                nameof(MedicationPackage.Ordinal)]));
    }

    [Fact]
    public void At_most_one_package_per_medication_can_be_pinned()
    {
        var package = _dbContext.Model.FindEntityType(typeof(MedicationPackage));

        var pinned = Assert.Single(
            package!.GetIndexes(),
            index => index.GetDatabaseName() == "ix_packages_single_pinned_per_medication");

        Assert.True(pinned.IsUnique);
        Assert.Equal("is_pinned", pinned.GetFilter());
    }

    [Fact]
    public void Each_consuming_ledger_entry_is_accounted_for_by_exactly_one_allocation()
    {
        var allocation = _dbContext.Model.FindEntityType(typeof(AdministrationAllocation));

        Assert.Contains(allocation!.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(AdministrationAllocation.LedgerEntryId)]));
    }

    [Fact]
    public void An_allocation_can_only_be_superseded_once()
    {
        var correction = _dbContext.Model.FindEntityType(typeof(AdministrationAllocationCorrection));

        Assert.Contains(correction!.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(AdministrationAllocationCorrection.SupersededAllocationId)]));
    }

    [Fact]
    public void A_scheduled_slot_holds_one_record_while_extra_doses_stay_possible()
    {
        var administration = _dbContext.Model.FindEntityType(typeof(AdministrationEvent));

        var slot = Assert.Single(
            administration!.GetIndexes(),
            index => index.IsUnique && index.Properties.Count == 3);

        Assert.Equal(
            [
                nameof(AdministrationEvent.HouseholdId),
                nameof(AdministrationEvent.TreatmentPlanVersionId),
                nameof(AdministrationEvent.ScheduledFor),
            ],
            slot.Properties.Select(property => property.Name));

        // The filter is what lets a second, extra dose exist in the same slot while
        // still preventing a replayed scheduled dose from duplicating.
        var filter = slot.GetFilter();
        Assert.Contains("plan_version_id IS NOT NULL", filter);
        Assert.Contains("outcome <> 'ExtraDose'", filter);
    }

    [Fact]
    public void An_unplanned_dose_needs_no_plan_version()
    {
        var administration = _dbContext.Model.FindEntityType(typeof(AdministrationEvent));

        var planVersion = administration!.FindProperty(nameof(AdministrationEvent.TreatmentPlanVersionId));

        Assert.True(planVersion!.IsNullable);
    }

    [Fact]
    public void A_count_may_be_revised_only_once_so_the_chain_stays_linear()
    {
        var batch = _dbContext.Model.FindEntityType(typeof(InventoryCountBatch));

        Assert.Contains(batch!.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(InventoryCountBatch.PreviousBatchId)]));
    }

    [Fact]
    public void One_refill_policy_exists_per_medication()
    {
        var policy = _dbContext.Model.FindEntityType(typeof(MedicationRefillPolicy));

        Assert.Contains(policy!.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(MedicationRefillPolicy.MedicationDefinitionId)]));
    }

    [Fact]
    public void A_replayed_offline_command_is_rejected_by_the_database_not_only_by_code()
    {
        var command = _dbContext.Model.FindEntityType(typeof(ProcessedAdministrationCommand));

        Assert.Contains(command!.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(ProcessedAdministrationCommand.HouseholdId),
                nameof(ProcessedAdministrationCommand.IdempotencyKey)]));

        var receipt = _dbContext.Model.FindEntityType(typeof(SyncCommandReceipt));

        Assert.Contains(receipt!.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(SyncCommandReceipt.HouseholdId),
                nameof(SyncCommandReceipt.IdempotencyKey)]));
    }

    [Fact]
    public void Treatment_plan_versions_are_numbered_uniquely_within_their_plan()
    {
        var version = _dbContext.Model.FindEntityType(typeof(TreatmentPlanVersion));

        Assert.Contains(version!.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(TreatmentPlanVersion.TreatmentPlanId),
                nameof(TreatmentPlanVersion.VersionNumber)]));
    }

    [Fact]
    public void Every_medication_amount_is_stored_as_an_exact_integer_pair()
    {
        // A binary floating-point column anywhere in a quantity would reintroduce the
        // drift the rational quantity type exists to prevent.
        var quantityColumns = _dbContext.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Select(property => (Entity: entity.ShortName(), Property: property)))
            .Where(pair => pair.Property.Name.Contains("Numerator", StringComparison.Ordinal)
                           || pair.Property.Name.Contains("Denominator", StringComparison.Ordinal)
                           || pair.Property.Name.Contains("Quantity", StringComparison.Ordinal)
                           || pair.Property.Name.Contains("Capacity", StringComparison.Ordinal)
                           || pair.Property.Name.Contains("Dose", StringComparison.Ordinal)
                           || pair.Property.Name.Contains("Threshold", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(quantityColumns);

        var floating = quantityColumns
            .Where(pair => pair.Property.ClrType == typeof(double)
                           || pair.Property.ClrType == typeof(float)
                           || pair.Property.ClrType == typeof(double?)
                           || pair.Property.ClrType == typeof(float?))
            .Select(pair => $"{pair.Entity}.{pair.Property.Name}")
            .ToList();

        Assert.Empty(floating);
    }

    [Fact]
    public void Care_tables_are_grouped_by_module_rather_than_by_sprint()
    {
        var schemas = new Dictionary<Type, string>
        {
            [typeof(MedicationDefinition)] = "catalog",
            [typeof(MedicationPackage)] = "inventory",
            [typeof(InventoryLedgerEntry)] = "inventory",
            [typeof(TreatmentPlan)] = "treatments",
            [typeof(TreatmentPlanVersion)] = "treatments",
            [typeof(AdministrationEvent)] = "administrations",
            [typeof(AdministrationAllocation)] = "administrations",
            [typeof(MedicationRefillPolicy)] = "refill",
            [typeof(ProcessedAdministrationCommand)] = "sync",
        };

        foreach (var (type, schema) in schemas)
        {
            var entity = _dbContext.Model.FindEntityType(type);
            Assert.NotNull(entity);
            Assert.Equal(schema, entity.GetSchema());
        }
    }
}
