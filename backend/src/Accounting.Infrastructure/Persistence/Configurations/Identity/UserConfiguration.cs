using Accounting.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", "sys");
        b.HasKey(u => u.UserId);

        b.Property(u => u.Username).HasMaxLength(100).IsRequired();
        b.Property(u => u.Email).HasMaxLength(255).IsRequired();
        b.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
        b.Property(u => u.FullName).HasMaxLength(255).IsRequired();
        b.Property(u => u.EmployeeCode).HasMaxLength(50);
        b.Property(u => u.CpdNumber).HasMaxLength(50);

        b.Property(u => u.MfaSecretEnc).HasColumnType("bytea");

        b.Property(u => u.LastLoginAt).HasColumnType("timestamptz(3)");
        b.Property(u => u.LockedUntil).HasColumnType("timestamptz(3)");
        b.Property(u => u.PasswordChangedAt).HasColumnType("timestamptz(3)");
        b.Property(u => u.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(u => u.UpdatedAt).HasColumnType("timestamptz(3)");

        b.Property(u => u.Version).IsConcurrencyToken();

        b.HasIndex(u => u.Username).IsUnique();
        b.HasIndex(u => u.Email).IsUnique();
    }
}
