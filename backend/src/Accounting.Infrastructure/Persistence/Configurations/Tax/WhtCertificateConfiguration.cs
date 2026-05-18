using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Tax;

internal sealed class WhtCertificateConfiguration : IEntityTypeConfiguration<WhtCertificate>
{
    public void Configure(EntityTypeBuilder<WhtCertificate> b)
    {
        b.ToTable("wht_certificates", "tax");
        b.HasKey(w => w.WhtCertificateId);

        b.Property(w => w.DocNo).HasMaxLength(50).IsRequired();
        b.Property(w => w.PayerTaxId).IsFixedLength().HasMaxLength(13).IsRequired();
        b.Property(w => w.PayerBranchCode).IsFixedLength().HasMaxLength(5).IsRequired();
        b.Property(w => w.PayerName).HasMaxLength(255).IsRequired();
        b.Property(w => w.PayerAddress).HasColumnType("text").IsRequired();
        b.Property(w => w.PayeeTaxId).IsFixedLength().HasMaxLength(13);
        b.Property(w => w.PayeeName).HasMaxLength(255).IsRequired();
        b.Property(w => w.PayeeAddress).HasColumnType("text").IsRequired();
        b.Property(w => w.PayeeType)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<CustomerType>(v, ignoreCase: true))
            .HasMaxLength(20);
        b.Property(w => w.FormType)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<WhtFormType>(v, ignoreCase: true))
            .HasMaxLength(10);
        b.Property(w => w.IncomeTypeCode).HasMaxLength(20).IsRequired();
        b.Property(w => w.IncomeDescription).HasMaxLength(500);
        b.Property(w => w.IncomeAmount).HasPrecision(19, 4);
        b.Property(w => w.WhtRate).HasPrecision(9, 6);
        b.Property(w => w.WhtAmount).HasPrecision(19, 4);

        b.Property(w => w.Status)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<DocumentStatus>(v, ignoreCase: true))
            .HasMaxLength(20);

        b.Property(w => w.IssuedAt).HasColumnType("timestamptz(3)");
        b.Property(w => w.CertReceivedAt).HasColumnType("timestamptz(3)");
        b.Property(w => w.ReconciledAt).HasColumnType("timestamptz(3)");

        // Sprint 8.6 — direction ('P' Payable / 'R' Receivable).
        b.Property(w => w.Direction).IsFixedLength().HasMaxLength(1).HasDefaultValue("P");

        // Sprint 8.6 — DocNo is unique only for Payable certs (our WT sequence).
        // Receivable (Direction='R') DocNo = the customer's own cert no, which is
        // outside our control and can legitimately repeat across customers.
        b.HasIndex(w => new { w.CompanyId, w.DocNo }).IsUnique()
            .HasFilter("direction = 'P'");
        b.HasIndex(w => w.PaymentVoucherId);
        b.HasIndex(w => w.ReceiptId);
    }
}
