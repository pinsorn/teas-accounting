using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Entities.Master;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Identity;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> b)
    {
        b.ToTable("api_keys", "sys");
        b.HasKey(k => k.ApiKeyId);

        b.Property(k => k.Name).HasMaxLength(255).IsRequired();
        b.Property(k => k.KeyHash).HasMaxLength(255).IsRequired();
        b.Property(k => k.KeyPrefix).HasMaxLength(20).IsRequired();
        b.Property(k => k.ScopesJson).HasColumnType("jsonb").HasColumnName("scopes").IsRequired();

        // M1 (MCP) — key profile (integration|mcp). Text column with a non-null
        // SQL default so existing rows backfill to 'integration' in the migration.
        b.Property(k => k.Kind).HasColumnName("kind").HasMaxLength(20)
            .HasDefaultValue(Accounting.Domain.Entities.Identity.ApiKeyKinds.Integration)
            .IsRequired();

        b.Property(k => k.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(k => k.ExpiresAt).HasColumnType("timestamptz(3)");
        b.Property(k => k.LastUsedAt).HasColumnType("timestamptz(3)");
        b.Property(k => k.RevokedAt).HasColumnType("timestamptz(3)");

        b.HasIndex(k => k.KeyHash).IsUnique();
        b.HasIndex(k => k.KeyPrefix).IsUnique();   // Sprint 14 — resolver lookup
        b.HasIndex(k => k.CompanyId);

        // Sprint 14 — per-key BU binding (nullable; same-tenant active BU
        // enforced at the service layer + FK). Restrict: a BU in use by a key
        // can't be hard-deleted.
        b.HasOne<BusinessUnit>()
            .WithMany()
            .HasForeignKey(k => k.DefaultBusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
