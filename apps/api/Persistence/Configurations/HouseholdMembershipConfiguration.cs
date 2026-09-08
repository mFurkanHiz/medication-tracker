using MedicationTracker.Api.Modules.Households;
using MedicationTracker.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicationTracker.Api.Persistence.Configurations;

public sealed class HouseholdMembershipConfiguration : IEntityTypeConfiguration<HouseholdMembership>
{
    public void Configure(EntityTypeBuilder<HouseholdMembership> builder)
    {
        builder.ToTable(
            "household_memberships",
            "households",
            table => table.HasCheckConstraint(
                "ck_household_memberships_valid_period",
                "valid_to IS NULL OR valid_to > valid_from"));
        builder.HasKey(membership => membership.Id);
        builder.Property(membership => membership.Id).HasColumnName("id");
        builder.Property(membership => membership.HouseholdId).HasColumnName("household_id");
        builder.Property(membership => membership.AccountId).HasColumnName("account_id");
        builder.Property(membership => membership.Role)
            .HasColumnName("role")
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(membership => membership.ValidFrom).HasColumnName("valid_from").IsRequired();
        builder.Property(membership => membership.ValidTo).HasColumnName("valid_to");
        builder.HasIndex(membership => new
        {
            membership.HouseholdId,
            membership.AccountId,
            membership.ValidFrom
        }).IsUnique();
        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(membership => membership.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(membership => membership.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
