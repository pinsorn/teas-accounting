using Accounting.Domain.Entities.Identity;
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

        b.Property(k => k.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(k => k.ExpiresAt).HasColumnType("timestamptz(3)");
        b.Property(k => k.LastUsedAt).HasColumnType("timestamptz(3)");
        b.Property(k => k.RevokedAt).HasColumnType("timestamptz(3)");

        b.HasIndex(k => k.KeyHash).IsUnique();
        b.HasIndex(k => k.CompanyId);
    }
}
