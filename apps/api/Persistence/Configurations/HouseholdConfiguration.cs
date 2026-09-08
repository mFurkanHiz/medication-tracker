using MedicationTracker.Api.Modules.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicationTracker.Api.Persistence.Configurations;

public sealed class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> builder)
    {
        builder.ToTable("households", "households");
        builder.HasKey(household => household.Id);
        builder.Property(household => household.Id).HasColumnName("id");
        builder.Property(household => household.Name)
            .HasColumnName("name")
            .HasMaxLength(160)
            .IsRequired();
        builder.Property(household => household.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
