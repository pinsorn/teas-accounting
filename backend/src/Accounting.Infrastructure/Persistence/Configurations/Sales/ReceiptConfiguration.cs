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
        b.Property(r => r.CreatedViaApiKeyName).HasMaxLength(120);

        b.Property(r => r.PostedAt).HasColumnType("timestamptz(3)");
        b.Property(r => r.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(r => r.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(r => r.Version).IsConcurrencyToken();

        b.HasOne<BusinessUnit>().WithMany().HasForeignKey(r => r.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Accounting.Domain.Entities.Tax.WhtType>().WithMany()
            .HasForeignKey(r => r.WhtTypeId).OnDelete(DeleteBehavior.Restrict);

        // Sprint 8.6 — WHT integrity (cross-row cash_received balance = service layer).
        // Sprint (multi-category WHT, 2026-05-22) — DROPPED ck_receipts_wht_type:
        // with a multi-category bill the header wht_type_id is NULL while wht_amount
        // (= Σ WhtLines) is > 0, which the old constraint forbade. The per-line
        // breakdown integrity now lives in receipt_wht_lines. nonneg kept.
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_receipts_wht_nonneg", "wht_amount >= 0");
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

        // An application settles EXACTLY ONE source: a Tax Invoice (VAT, settles AR),
        // a Delivery Order (non-VAT legacy, cont.68), or an Invoice/BillingNote (non-VAT,
        // cont.69). Never more than one. Standalone receipts have no rows here.
        b.ToTable(t => t.HasCheckConstraint("ck_receipt_applications_one_doc",
            "(CASE WHEN tax_invoice_id IS NOT NULL THEN 1 ELSE 0 END) + " +
            "(CASE WHEN delivery_order_id IS NOT NULL THEN 1 ELSE 0 END) + " +
            "(CASE WHEN billing_note_id IS NOT NULL THEN 1 ELSE 0 END) = 1"));

        // cont.69 Phase 1 — Invoice (BillingNote) application FK.
        b.HasOne<BillingNote>().WithMany().HasForeignKey(a => a.BillingNoteId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique only within the populated reference (each nullable).
        b.HasIndex(a => new { a.ReceiptId, a.TaxInvoiceId }).IsUnique()
            .HasFilter("tax_invoice_id IS NOT NULL");
        b.HasIndex(a => new { a.ReceiptId, a.DeliveryOrderId }).IsUnique()
            .HasFilter("delivery_order_id IS NOT NULL");
        b.HasIndex(a => new { a.ReceiptId, a.BillingNoteId }).IsUnique()
            .HasFilter("billing_note_id IS NOT NULL");
        b.HasIndex(a => a.TaxInvoiceId);
        b.HasIndex(a => a.DeliveryOrderId);
        b.HasIndex(a => a.BillingNoteId);
    }
}

internal sealed class ReceiptLineConfiguration : IEntityTypeConfiguration<ReceiptLine>
{
    public void Configure(EntityTypeBuilder<ReceiptLine> b)
    {
        b.ToTable("receipt_lines", "sales");
        b.HasKey(l => l.ReceiptLineId);

        b.Property(l => l.ProductCode).HasMaxLength(50);
        b.Property(l => l.ProductType).HasMaxLength(20).IsRequired().HasDefaultValue("GOOD");
        b.Property(l => l.DescriptionTh).HasMaxLength(500).IsRequired();
        b.Property(l => l.UomText).HasMaxLength(50);
        b.Property(l => l.Quantity).HasPrecision(19, 4);
        b.Property(l => l.UnitPrice).HasPrecision(19, 4);
        b.Property(l => l.Amount).HasPrecision(19, 4);

        b.HasOne<Receipt>().WithMany(r => r.Lines)
            .HasForeignKey(l => l.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t => t.HasCheckConstraint("ck_receipt_lines_nonneg", "amount >= 0"));
        b.HasIndex(l => l.ReceiptId);
    }
}

internal sealed class ReceiptWhtLineConfiguration : IEntityTypeConfiguration<ReceiptWhtLine>
{
    public void Configure(EntityTypeBuilder<ReceiptWhtLine> b)
    {
        b.ToTable("receipt_wht_lines", "sales");
        b.HasKey(l => l.ReceiptWhtLineId);

        b.Property(l => l.IncomeTypeCode).HasMaxLength(20).IsRequired();
        b.Property(l => l.WhtTypeCode).HasMaxLength(50).IsRequired();
        b.Property(l => l.WhtRate).HasPrecision(9, 6);
        b.Property(l => l.BaseAmount).HasPrecision(19, 4);
        b.Property(l => l.WhtAmount).HasPrecision(19, 4);

        b.HasOne<Receipt>().WithMany(r => r.WhtLines)
            .HasForeignKey(l => l.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Accounting.Domain.Entities.Tax.WhtType>().WithMany()
            .HasForeignKey(l => l.WhtTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t => t.HasCheckConstraint("ck_receipt_wht_lines_nonneg",
            "base_amount >= 0 AND wht_amount >= 0"));
        b.HasIndex(l => l.ReceiptId);
    }
}
