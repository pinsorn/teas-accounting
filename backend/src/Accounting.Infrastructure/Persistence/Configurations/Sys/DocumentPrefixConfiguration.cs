using Accounting.Domain.Entities.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Sys;

internal sealed class DocumentPrefixConfiguration : IEntityTypeConfiguration<DocumentPrefix>
{
    public void Configure(EntityTypeBuilder<DocumentPrefix> b)
    {
        b.ToTable("document_prefixes", "sys");
        b.HasKey(p => p.PrefixId);
        b.Property(p => p.PrefixCode).HasMaxLength(20).IsRequired();
        b.Property(p => p.DocumentType).HasMaxLength(50).IsRequired();
        b.Property(p => p.DescriptionTh).HasMaxLength(255).IsRequired();
        b.Property(p => p.DescriptionEn).HasMaxLength(255);
        b.Property(p => p.CreatedAt).HasColumnType("timestamptz(3)");
        b.HasIndex(p => p.PrefixCode).IsUnique();
    }
}
