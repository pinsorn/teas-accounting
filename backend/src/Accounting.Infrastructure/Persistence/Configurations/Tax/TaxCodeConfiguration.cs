using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Tax;

internal sealed class TaxCodeConfiguration : IEntityTypeConfiguration<TaxCode>
{
    public void Configure(EntityTypeBuilder<TaxCode> b)
    {
        b.ToTable("tax_codes", "tax");
        b.HasKey(t => t.TaxCodeId);

        b.Property(t => t.Code).HasMaxLength(20).IsRequired();
        b.Property(t => t.NameTh).HasMaxLength(255).IsRequired();
        b.Property(t => t.LegalRef).HasMaxLength(100);   // Sprint 9 B1 (R-Q3)
        b.Property(t => t.TaxType)
            .HasConversion(
                v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<TaxType>(v, ignoreCase: true))
            .HasMaxLength(20);
        b.Property(t => t.Direction)
            .HasConversion(
                v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<TaxDirection>(v, ignoreCase: true))
            .HasMaxLength(10);

        b.HasIndex(t => new { t.CompanyId, t.Code }).IsUnique();
    }
}
