using Accounting.Domain.Entities.Master;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Master;

internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> b)
    {
        b.ToTable("company_profile", "master");

        // 1:1 with Company — PK is the company_id (also the FK). No identity.
        b.HasKey(x => x.CompanyId);
        b.Property(x => x.CompanyId).ValueGeneratedNever();
        b.HasOne<Company>()
            .WithOne()
            .HasForeignKey<CompanyProfile>(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // ---- HARD ----
        b.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
        b.Property(x => x.TaxId).IsFixedLength().HasMaxLength(13).IsRequired();
        b.Property(x => x.RegistrationNumber).IsFixedLength().HasMaxLength(13);
        b.Property(x => x.RegisteredAddressLine1).HasMaxLength(200).IsRequired();
        b.Property(x => x.RegisteredAddressLine2).HasMaxLength(200);
        b.Property(x => x.RegisteredSubdistrict).HasMaxLength(100);
        b.Property(x => x.RegisteredDistrict).HasMaxLength(100);
        b.Property(x => x.RegisteredProvince).HasMaxLength(100).IsRequired();
        b.Property(x => x.RegisteredPostalCode).IsFixedLength().HasMaxLength(5).IsRequired();
        b.Property(x => x.BranchCode).HasMaxLength(10).HasDefaultValue("00000");

        // ---- SOFT ----
        b.Property(x => x.TradeName).HasMaxLength(200);
        b.Property(x => x.LogoUrl).HasMaxLength(500);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Website).HasMaxLength(200);
        b.Property(x => x.ContactName).HasMaxLength(200);
        b.Property(x => x.BankName).HasMaxLength(100);
        b.Property(x => x.BankAccountNo).HasMaxLength(50);
        b.Property(x => x.BankAccountName).HasMaxLength(200);

        // ---- Audit ----
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
    }
}
