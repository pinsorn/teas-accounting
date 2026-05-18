using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Tax;

internal sealed class WhtTypeConfiguration : IEntityTypeConfiguration<WhtType>
{
    public void Configure(EntityTypeBuilder<WhtType> b)
    {
        b.ToTable("wht_types", "tax");
        b.HasKey(w => w.WhtTypeId);

        b.Property(w => w.Code).HasMaxLength(20).IsRequired();
        b.Property(w => w.NameTh).HasMaxLength(255).IsRequired();
        b.Property(w => w.NameEn).HasMaxLength(255);
        b.Property(w => w.IncomeTypeCode).HasMaxLength(20).IsRequired();
        b.Property(w => w.FormType)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<WhtFormType>(v, ignoreCase: true))
            .HasMaxLength(10);
        b.Property(w => w.Rate).HasPrecision(9, 6);
        b.Property(w => w.EffectiveFrom).HasDefaultValue(new DateOnly(2020, 1, 1));

        // Sprint 8.6 — effective-date pattern: code is unique per (company, from).
        b.HasIndex(w => new { w.CompanyId, w.Code, w.EffectiveFrom }).IsUnique();
    }
}
