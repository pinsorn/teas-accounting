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
        // ภ.ง.ด.2 filing (specs/pnd2-filing.md A2) — RD INC_TYPE_PND code, meaningful only
        // when FormType == Pnd2. Additive + nullable, no backfill. Explicit HasColumnName:
        // EFCore.NamingConventions' default for this property is "pnd2income_code" (no
        // underscore after the digit — same behaviour as Pnd51EstimatedProfit/RequiresPnd36...),
        // not "pnd2_income_code"; override it explicitly, mirroring Pnd30SubmissionMode
        // (CompanyConfiguration.cs) which does the same for the same reason.
        b.Property(w => w.Pnd2IncomeCode).HasColumnName("pnd2_income_code").HasMaxLength(1);
        b.Property(w => w.Rate).HasPrecision(9, 6);
        b.Property(w => w.EffectiveFrom).HasDefaultValue(new DateOnly(2020, 1, 1));

        // Sprint 8.6 — effective-date pattern: code is unique per (company, from).
        b.HasIndex(w => new { w.CompanyId, w.Code, w.EffectiveFrom }).IsUnique();
    }
}
