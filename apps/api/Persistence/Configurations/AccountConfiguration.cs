using MedicationTracker.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicationTracker.Api.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", "identity");
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).HasColumnName("id");
        builder.Property(account => account.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(320)
            .IsRequired();
        builder.HasIndex(account => account.NormalizedEmail).IsUnique();
        builder.Property(account => account.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
