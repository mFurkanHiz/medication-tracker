using MedicationTracker.Api.Modules.Audit;
using MedicationTracker.Api.Modules.Catalog;
using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicationTracker.Api.Persistence.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> b)
    {
        b.ToTable("people", "care");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.ArchivedAt).HasColumnName("archived_at");
        b.Ignore(x => x.IsArchived);
        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.Id }).IsUnique();
    }
}

public sealed class MedicationDefinitionConfiguration : IEntityTypeConfiguration<MedicationDefinition>
{
    public void Configure(EntityTypeBuilder<MedicationDefinition> b)
    {
        // Renamed from care.medications: the table no longer doubles as a person's
        // stock, so its name should stop implying that it might. See ADR 0013.
        b.ToTable("medication_definitions", "catalog", table => table.HasCheckConstraint(
            "ck_medication_definitions_default_capacity",
            "(default_package_capacity_numerator IS NULL AND default_package_capacity_denominator IS NULL) "
            + "OR (default_package_capacity_numerator > 0 AND default_package_capacity_denominator > 0)"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Strength).HasColumnName("strength").HasMaxLength(100);
        b.Property(x => x.Brand).HasColumnName("brand").HasMaxLength(200);
        b.Property(x => x.Manufacturer).HasColumnName("manufacturer").HasMaxLength(200);
        b.Property(x => x.Form).HasColumnName("form").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.Unit).HasColumnName("unit").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.ActiveIngredients).HasColumnName("active_ingredients").HasColumnType("text[]");
        b.Property(x => x.DefaultPackageCapacityNumerator).HasColumnName("default_package_capacity_numerator");
        b.Property(x => x.DefaultPackageCapacityDenominator).HasColumnName("default_package_capacity_denominator");
        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(100);
        b.Property(x => x.Tags).HasColumnName("tags").HasColumnType("text[]");
        b.Property(x => x.Coverage).HasColumnName("coverage").HasConversion<string>().HasMaxLength(20).IsRequired().HasDefaultValueSql("'Unspecified'");
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        b.Property(x => x.CautionDoNotTakeWith).HasColumnName("caution_do_not_take_with").HasColumnType("text");
        b.Property(x => x.CautionFoodsToAvoid).HasColumnName("caution_foods_to_avoid").HasColumnType("text");
        b.Property(x => x.CautionThingsToDo).HasColumnName("caution_things_to_do").HasColumnType("text");
        b.Property(x => x.CautionThingsToAvoid).HasColumnName("caution_things_to_avoid").HasColumnType("text");
        b.Property(x => x.CautionWarning).HasColumnName("caution_warning").HasColumnType("text");
        b.Property(x => x.DoNotTakeWithTags).HasColumnName("do_not_take_with_tags").HasColumnType("text[]").IsRequired().HasDefaultValueSql("'{}'");
        b.Property(x => x.ExternalCodes).HasColumnName("external_codes").HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.ArchivedAt).HasColumnName("archived_at");
        b.Property(x => x.LegacyPersonId).HasColumnName("legacy_person_id");
        b.Ignore(x => x.DefaultPackageCapacity);
        b.Ignore(x => x.Cautions);
        b.Ignore(x => x.IsArchived);

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.LegacyPersonId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.Name });
    }
}

public sealed class MedicationDefinitionChangeEventConfiguration
    : IEntityTypeConfiguration<MedicationDefinitionChangeEvent>
{
    public void Configure(EntityTypeBuilder<MedicationDefinitionChangeEvent> b)
    {
        b.ToTable("medication_definition_change_events", "catalog");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.HouseholdId).HasColumnName("household_id");
        b.Property(x => x.MedicationDefinitionId).HasColumnName("medication_definition_id");
        b.Property(x => x.AccountId).HasColumnName("account_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.PreviousValue).HasColumnName("previous_value").HasColumnType("text");
        b.Property(x => x.NewValue).HasColumnName("new_value").HasColumnType("text");
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at");

        b.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MedicationDefinition>().WithMany().HasForeignKey(x => x.MedicationDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HouseholdId, x.MedicationDefinitionId, x.RecordedAt });
    }
}
