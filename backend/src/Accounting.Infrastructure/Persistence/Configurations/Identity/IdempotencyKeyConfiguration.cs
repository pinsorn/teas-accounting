using Accounting.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Identity;

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> b)
    {
        b.ToTable("idempotency_keys", "sys");
        b.HasKey(k => k.IdempotencyKeyId);

        b.Property(k => k.Key).HasMaxLength(255).IsRequired();
        b.Property(k => k.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(k => k.ResponseBody).HasColumnType("jsonb").IsRequired();
        b.Property(k => k.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(k => k.ExpiresAt).HasColumnType("timestamptz(3)");

        b.HasIndex(k => new { k.CompanyId, k.ApiKeyId, k.Key })
            .IsUnique()
            .HasDatabaseName("ux_idemp_company_apikey_key");

        // Cleanup scan support. NOTE: the spec's partial predicate
        // "WHERE expires_at > NOW()" is INVALID in PostgreSQL (index predicates
        // must be IMMUTABLE; NOW() is not). A plain btree on expires_at fully
        // serves the bounded "DELETE WHERE expires_at < NOW()" cleanup.
        // (Mechanism note → Report-Backend19.)
        b.HasIndex(k => k.ExpiresAt).HasDatabaseName("ix_idemp_expiry");
    }
}
