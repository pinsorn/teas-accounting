using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Master;

internal sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> b)
    {
        b.ToTable("vendors", "master");
        b.HasKey(v => v.VendorId);

        b.Property(v => v.VendorCode).HasMaxLength(50).IsRequired();
        b.Property(v => v.VendorType)
            .HasConversion(
                x => x.ToString().ToUpperInvariant(),
                x => Enum.Parse<CustomerType>(x, ignoreCase: true))
            .HasMaxLength(20);

        b.Property(v => v.TaxId).IsFixedLength().HasMaxLength(13);
        b.Property(v => v.BranchCode).IsFixedLength().HasMaxLength(5);
        b.Property(v => v.BranchName).HasMaxLength(255);
        b.Property(v => v.NameTh).HasMaxLength(255).IsRequired();
        b.Property(v => v.NameEn).HasMaxLength(255);
        b.Property(v => v.Address).HasColumnType("text");
        b.Property(v => v.ContactPerson).HasMaxLength(255);
        b.Property(v => v.Phone).HasMaxLength(50);
        b.Property(v => v.Email).HasMaxLength(255);
        b.Property(v => v.DefaultWhtTypeCode).HasMaxLength(20);
        b.Property(v => v.DefaultCurrency).IsFixedLength().HasMaxLength(3).HasDefaultValue("THB");
        // cont.77 — vendor payment/remittance details.
        b.Property(v => v.BankName).HasMaxLength(120);
        b.Property(v => v.BankAccountNo).HasMaxLength(50);
        b.Property(v => v.BankAccountName).HasMaxLength(255);
        b.Property(v => v.SwiftCode).HasMaxLength(11);   // ISO 9362: 8 or 11 chars
        b.Property(v => v.CreatedAt).HasColumnType("timestamptz(3)");

        // Sprint 8.7 — foreign / VAT-D flags. is_vat_registered = the existing
        // VatRegistered column (reused; no duplicate — Report-Backend13 note).
        b.Property(v => v.IsForeign).HasDefaultValue(false);
        b.Property(v => v.HasThaiVatDReg).HasDefaultValue(false);
        b.Property(v => v.CountryCode).IsFixedLength().HasMaxLength(2);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_vendors_type", "vendor_type IN ('INDIVIDUAL','CORPORATE')");
            t.HasCheckConstraint("ck_vendors_vatd_foreign",
                "has_thai_vat_d_reg IS NOT TRUE OR is_foreign IS TRUE");
            t.HasCheckConstraint("ck_vendors_foreign_vatreg",
                "is_foreign IS NOT TRUE OR vat_registered IS TRUE");
        });

        b.HasIndex(v => new { v.CompanyId, v.VendorCode }).IsUnique();
    }
}
