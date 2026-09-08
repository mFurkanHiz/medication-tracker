using MedicationTracker.Api.Modules.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicationTracker.Api.Persistence.Configurations;

public sealed class EntitlementConfiguration : IEntityTypeConfiguration<Entitlement>
{
    public void Configure(EntityTypeBuilder<Entitlement> builder)
    {
        builder.ToTable(
            "entitlements",
            "subscriptions",
            table => table.HasCheckConstraint(
                "ck_entitlements_valid_period",
                "valid_to IS NULL OR valid_to > valid_from"));
        builder.HasKey(entitlement => entitlement.Id);
        builder.Property(entitlement => entitlement.Id).HasColumnName("id");
        builder.Property(entitlement => entitlement.SubscriptionId).HasColumnName("subscription_id");
        builder.Property(entitlement => entitlement.Code)
            .HasColumnName("code")
            .HasMaxLength(120)
            .IsRequired();
        builder.Property(entitlement => entitlement.ValidFrom).HasColumnName("valid_from").IsRequired();
        builder.Property(entitlement => entitlement.ValidTo).HasColumnName("valid_to");
        builder.HasIndex(entitlement => new
        {
            entitlement.SubscriptionId,
            entitlement.Code,
            entitlement.ValidFrom
        }).IsUnique();
        builder.HasOne<Subscription>()
            .WithMany()
            .HasForeignKey(entitlement => entitlement.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
