using Accounting.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Identity;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("permissions", "sys");
        b.HasKey(p => p.PermissionId);
        b.Property(p => p.PermissionCode).HasMaxLength(100).IsRequired();
        b.Property(p => p.Module).HasMaxLength(50).IsRequired();
        b.Property(p => p.Resource).HasMaxLength(50).IsRequired();
        b.Property(p => p.Action).HasMaxLength(50).IsRequired();
        b.Property(p => p.Description).HasMaxLength(500);
        b.HasIndex(p => p.PermissionCode).IsUnique();
    }
}
