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

        b.Property(c => c.CreatedAt).HasColumnType("timestamptz(3)");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_companies_tax_id", "tax_id ~ '^[0-9]{13}$'");
            t.HasCheckConstraint("ck_companies_fiscal_month", "fiscal_year_start_month BETWEEN 1 AND 12");
        });

        b.HasIndex(c => c.TaxId).IsUnique();
    }
}
