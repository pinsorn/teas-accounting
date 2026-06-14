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
        b.Property(r => r.CompanyId).HasColumnName("company_id");
        b.Property(r => r.RoleCode).HasMaxLength(50).IsRequired();
        b.Property(r => r.RoleName).HasMaxLength(100).IsRequired();
        b.Property(r => r.Description).HasMaxLength(500);
        // NB: the global unique(role_code) is kept here so the historical seed scripts'
        // ON CONFLICT (role_code) still work at bootstrap. The reconcile SQL script
        // (510_per_company_roles_reconcile.sql) replaces it with a per-company unique
        // (company_id, role_code) AFTER seeds run. EF never inspects the live DB, so the
        // raw-SQL swap causes no model drift (same approach as RLS/triggers).
        b.HasIndex(r => r.RoleCode).IsUnique();
    }
}
