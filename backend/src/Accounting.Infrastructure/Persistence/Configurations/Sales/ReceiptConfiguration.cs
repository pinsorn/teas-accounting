using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Sales;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> b)
    {
        b.ToTable("receipts", "sales");
        b.HasKey(r => r.ReceiptId);

        b.Property(r => r.DocNo).HasMaxLength(50);
        b.Property(r => r.CustomerName).HasMaxLength(255).IsRequired();
        b.Property(r => r.CustomerAddress).HasColumnType("text").IsRequired();
        b.Property(r => r.CustomerTaxId).IsFixedLength().HasMaxLength(13);

        b.Property(r => r.PaymentMethod)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<PaymentMethod>(v, ignoreCase: true))
            .HasMaxLength(20);
        b.Property(r => r.ChequeNo).HasMaxLength(50);

        b.Property(r => r.CurrencyCode).IsFixedLength().HasMaxLength(3).HasDefaultValue("THB");
        b.Property(r => r.ExchangeRate).HasPrecision(19, 8).HasDefaultValue(1m);
        b.Property(r => r.Amount).HasPrecision(19, 4);
        b.Property(r => r.TotalAmount).HasPrecision(19, 4);
        b.Property(r => r.TotalAmountThb).HasPrecision(19, 4);

        // Sprint 8.6 — AR-side WHT.
        b.Property(r => r.WhtAmount).HasPrecision(19, 4).HasDefaultValue(0m);
        b.Property(r => r.CashReceived).HasPrecision(19, 4).HasDefaultValue(0m);
        b.Property(r => r.CustomerWhtCertNo).HasMaxLength(50);

        b.Property(r => r.Status)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<DocumentStatus>(v, ignoreCase: true))
            .HasMaxLength(20)
            .HasDefaultValue(DocumentStatus.Draft);

        b.Property(r => r.Notes).HasColumnType("text");

        b.Property(r => r.PostedAt).HasColumnType("timestamptz(3)");
        b.Property(r => r.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(r => r.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(r => r.Version).IsConcurrencyToken();

        b.HasOne<BusinessUnit>().WithMany().HasForeignKey(r => r.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Accounting.Domain.Entities.Tax.WhtType>().WithMany()
            .HasForeignKey(r => r.WhtTypeId).OnDelete(DeleteBehavior.Restrict);

        // Sprint 8.6 — WHT integrity (cross-row cash_received balance = service layer).
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_receipts_wht_nonneg", "wht_amount >= 0");
            t.HasCheckConstraint("ck_receipts_wht_type",
                "(wht_amount = 0 AND wht_type_id IS NULL) OR (wht_amount > 0 AND wht_type_id IS NOT NULL)");
        });

        b.HasIndex(r => new { r.CompanyId, r.BranchId, r.DocNo }).IsUnique().HasFilter("doc_no IS NOT NULL");
        b.HasIndex(r => new { r.CustomerId, r.DocDate });
        b.HasIndex(r => new { r.CompanyId, r.BusinessUnitId }).HasFilter("business_unit_id IS NOT NULL");
    }
}

internal sealed class ReceiptApplicationConfiguration : IEntityTypeConfiguration<ReceiptApplication>
{
    public void Configure(EntityTypeBuilder<ReceiptApplication> b)
    {
        b.ToTable("receipt_applications", "sales");
        b.HasKey(a => a.ApplicationId);

        b.Property(a => a.AppliedAmount).HasPrecision(19, 4);

        b.HasOne<Receipt>().WithMany(r => r.Applications)
            .HasForeignKey(a => a.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(a => new { a.ReceiptId, a.TaxInvoiceId }).IsUnique();
        b.HasIndex(a => a.TaxInvoiceId);
    }
}
