using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Master;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers", "master");
        b.HasKey(c => c.CustomerId);

        b.Property(c => c.CustomerCode).HasMaxLength(50).IsRequired();
        b.Property(c => c.CustomerType)
            .HasConversion(
                v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<CustomerType>(v, ignoreCase: true))
            .HasMaxLength(20);

        b.Property(c => c.TaxId).IsFixedLength().HasMaxLength(13);
        b.Property(c => c.BranchCode).IsFixedLength().HasMaxLength(5);
        b.Property(c => c.BranchName).HasMaxLength(255);
        b.Property(c => c.NameTh).HasMaxLength(255).IsRequired();
        b.Property(c => c.NameEn).HasMaxLength(255);
        b.Property(c => c.BillingAddress).HasColumnType("text");
        b.Property(c => c.ContactPerson).HasMaxLength(255);
        b.Property(c => c.Phone).HasMaxLength(50);
        b.Property(c => c.Email).HasMaxLength(255);

        b.Property(c => c.CreditLimit).HasPrecision(19, 4);
        b.Property(c => c.DefaultCurrency).IsFixedLength().HasMaxLength(3).HasDefaultValue("THB");
        b.Property(c => c.CreatedAt).HasColumnType("timestamptz(3)");

        b.ToTable(t => t.HasCheckConstraint(
            "ck_customers_type", "customer_type IN ('INDIVIDUAL','CORPORATE')"));

        // Sprint 8.6 — optional default WHT type for B2B pre-fill.
        b.HasOne<Accounting.Domain.Entities.Tax.WhtType>().WithMany()
            .HasForeignKey(c => c.DefaultWhtTypeId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(c => new { c.CompanyId, c.CustomerCode }).IsUnique();
    }
}
