using Accounting.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Identity;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles", "sys");
        b.HasKey(r => r.RoleId);
        b.Property(r => r.RoleCode).HasMaxLength(50).IsRequired();
        b.Property(r => r.RoleName).HasMaxLength(100).IsRequired();
        b.Property(r => r.Description).HasMaxLength(500);
        b.HasIndex(r => r.RoleCode).IsUnique();
    }
}
