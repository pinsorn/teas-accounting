using Accounting.Domain.Entities.Master;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Master;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable("companies", "master");
        b.HasKey(c => c.CompanyId);

        b.Property(c => c.TaxId).IsFixedLength().HasMaxLength(13).IsRequired();
        b.Property(c => c.NameTh).HasMaxLength(255).IsRequired();
        b.Property(c => c.NameEn).HasMaxLength(255);
        b.Property(c => c.LegalEntityType).HasConversion<string>().HasMaxLength(50);

        b.Property(c => c.BaseCurrency).IsFixedLength().HasMaxLength(3).HasDefaultValue("THB");
        b.Property(c => c.ReportingStandard).HasMaxLength(20).HasDefaultValue("TFRS_NPAE");

        b.Property(c => c.AddressTh).HasColumnType("text");
        b.Property(c => c.SubDistrict).HasMaxLength(100);
        b.Property(c => c.District).HasMaxLength(100);
        b.Property(c => c.Province).HasMaxLength(100);
        b.Property(c => c.PostalCode).HasMaxLength(10);
        b.Property(c => c.Phone).HasMaxLength(50);
        b.Property(c => c.Email).HasMaxLength(255);

        b.Property(c => c.RequiresBusinessUnit).HasDefaultValue(false);
        b.Property(c => c.PaidUpCapital).HasPrecision(19, 4);

        // fix-codex-ui-review-2026-08-20 UI-2 — HasDefaultValue(0.07m) alone means EF's
        // insert convention treats a tracked value == CLR default(decimal) (0m) as "never
        // set" and omits the column, letting the DB default win — so an explicit 0 (a
        // deliberately non-VAT-rate company) silently became 7%. HasSentinel(-1m) moves
        // that "was this ever set" check off 0 (a real, valid rate per ck_companies_vat_rate
        // below) onto an impossible value, so 0 now round-trips like any other rate. The DB
        // default stays: raw-SQL company seeds (120/400/440_seed_*.sql) INSERT without a
        // vat_rate column and rely on it.
        b.Property(c => c.VatRate).HasPrecision(5, 4).HasDefaultValue(0.07m).HasSentinel(-1m);
        // Explicit name: the snake_case convention would emit "pnd30submission_mode"
        // (digit boundary), which the ck constraint below doesn't match.
        b.Property(c => c.Pnd30SubmissionMode).HasColumnName("pnd30_submission_mode")
            .HasMaxLength(10).HasDefaultValue("manual");

        b.Property(c => c.CreatedAt).HasColumnType("timestamptz(3)");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_companies_tax_id", "tax_id ~ '^[0-9]{13}$'");
            t.HasCheckConstraint("ck_companies_fiscal_month", "fiscal_year_start_month BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_companies_pnd30_submission_mode", "pnd30_submission_mode IN ('manual','auto')");
            t.HasCheckConstraint("ck_companies_vat_rate", "vat_rate >= 0 AND vat_rate <= 1");
        });

        b.HasIndex(c => c.TaxId).IsUnique();
    }
}
