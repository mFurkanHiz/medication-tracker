using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using MedicationTracker.Api.Modules.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicationTracker.Api.Persistence.Configurations;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable(
            "subscriptions",
            "subscriptions",
            table => table.HasCheckConstraint(
                "ck_subscriptions_exactly_one_owner",
                "(account_id IS NOT NULL AND household_id IS NULL) OR " +
                "(account_id IS NULL AND household_id IS NOT NULL)"));
        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Id).HasColumnName("id");
        builder.Property(subscription => subscription.AccountId).HasColumnName("account_id");
        builder.Property(subscription => subscription.HouseholdId).HasColumnName("household_id");
        builder.Property(subscription => subscription.Provider)
            .HasColumnName("provider")
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(subscription => subscription.ExternalReference)
            .HasColumnName("external_reference")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(subscription => subscription.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(subscription => new
        {
            subscription.Provider,
            subscription.ExternalReference
        }).IsUnique();
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(subscription => subscription.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(subscription => subscription.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
