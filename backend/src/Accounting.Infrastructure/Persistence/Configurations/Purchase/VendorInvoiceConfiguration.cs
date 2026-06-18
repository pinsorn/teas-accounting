using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Purchase;

internal sealed class VendorInvoiceConfiguration : IEntityTypeConfiguration<VendorInvoice>
{
    public void Configure(EntityTypeBuilder<VendorInvoice> b)
    {
        b.ToTable("vendor_invoices", "purchase");
        b.HasKey(v => v.VendorInvoiceId);

        b.Property(v => v.DocNo).HasMaxLength(50);
        b.Property(v => v.CreatedViaApiKeyName).HasMaxLength(120);   // M4 (MCP) — agent draft attribution
        b.Property(v => v.VendorTaxInvoiceNo).HasMaxLength(50).IsRequired();

        b.Property(v => v.VendorTaxId).IsFixedLength().HasMaxLength(13);
        b.Property(v => v.VendorBranchCode).IsFixedLength().HasMaxLength(5);
        b.Property(v => v.VendorName).HasMaxLength(255).IsRequired();
        b.Property(v => v.VendorAddress).HasColumnType("text");

        b.Property(v => v.VendorType)
            .HasConversion(x => x.ToString().ToUpperInvariant(),
                           x => Enum.Parse<CustomerType>(x, ignoreCase: true))
            .HasMaxLength(20);

        b.Property(v => v.CurrencyCode).IsFixedLength().HasMaxLength(3).HasDefaultValue("THB");
        b.Property(v => v.ExchangeRate).HasPrecision(19, 8).HasDefaultValue(1m);
        b.Property(v => v.SubtotalAmount).HasPrecision(19, 4);
        b.Property(v => v.VatAmount).HasPrecision(19, 4);
        b.Property(v => v.NonRecoverableVatAmount).HasPrecision(19, 4);
        b.Property(v => v.TotalAmount).HasPrecision(19, 4);
        b.Property(v => v.TotalAmountThb).HasPrecision(19, 4);
        b.Property(v => v.SettledAmount).HasPrecision(19, 4).HasDefaultValue(0m);
        b.Property(v => v.SettlementStatus).HasMaxLength(20).HasDefaultValue("UNPAID");

        // Sprint 8.7 — receipt-only + ภ.พ.36 reverse-charge flags.
        b.Property(v => v.HasInputVat).HasDefaultValue(true);
        b.Property(v => v.RequiresPnd36ReverseCharge).HasDefaultValue(false);

        b.Property(v => v.Status)
            .HasConversion(x => x.ToString().ToUpperInvariant(),
                           x => Enum.Parse<DocumentStatus>(x, ignoreCase: true))
            .HasMaxLength(20)
            .HasDefaultValue(DocumentStatus.Draft);

        b.Property(v => v.Notes).HasColumnType("text");
        b.Property(v => v.PostedAt).HasColumnType("timestamptz(3)");
        b.Property(v => v.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(v => v.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(v => v.Version).IsConcurrencyToken();

        b.ToTable(t =>
        {
            // §1B — settled within [0, total + 0.01 baht rounding tolerance].
            t.HasCheckConstraint("ck_vi_settled",
                "settled_amount >= 0 AND settled_amount <= total_amount + 0.01");
        });

        b.HasIndex(v => new { v.CompanyId, v.BranchId, v.DocNo })
            .IsUnique().HasFilter("doc_no IS NOT NULL");
        b.HasIndex(v => new { v.CompanyId, v.DocDate });
        b.HasIndex(v => new { v.VendorId, v.DocDate });
        // §1A — ภ.พ.30 input-VAT register lookup by claim period.
        b.HasIndex(v => new { v.CompanyId, v.VatClaimPeriod })
            .HasDatabaseName("ix_vendor_invoices_vat_claim_period");

        // Sprint 12 — optional retroactive PO link (nullable; Restrict so a PO
        // can't be hard-deleted while VIs reference it).
        b.HasOne<Accounting.Domain.Entities.Purchase.PurchaseOrder>()
            .WithMany().HasForeignKey(v => v.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => v.PurchaseOrderId);

        // cont.79 — Business Unit GL dimension.
        b.HasOne<Accounting.Domain.Entities.Master.BusinessUnit>().WithMany()
            .HasForeignKey(v => v.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => v.BusinessUnitId).HasFilter("business_unit_id IS NOT NULL");
    }
}

internal sealed class VendorInvoiceLineConfiguration : IEntityTypeConfiguration<VendorInvoiceLine>
{
    public void Configure(EntityTypeBuilder<VendorInvoiceLine> b)
    {
        b.ToTable("vendor_invoice_lines", "purchase");
        b.HasKey(l => l.LineId);

        b.Property(l => l.Description).HasMaxLength(500).IsRequired();
        // cont.76 — สินค้า/บริการ snapshot (ProductType enum codes, nullable for legacy rows).
        b.Property(l => l.ProductType).HasMaxLength(20);
        b.Property(l => l.Amount).HasPrecision(19, 4);
        b.Property(l => l.VatRate).HasPrecision(9, 6);
        b.Property(l => l.VatAmount).HasPrecision(19, 4);

        b.HasOne<VendorInvoice>().WithMany(v => v.Lines)
            .HasForeignKey(l => l.VendorInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(l => new { l.VendorInvoiceId, l.LineNo }).IsUnique();
    }
}

internal sealed class PaymentVoucherApplicationConfiguration
    : IEntityTypeConfiguration<PaymentVoucherApplication>
{
    public void Configure(EntityTypeBuilder<PaymentVoucherApplication> b)
    {
        b.ToTable("payment_voucher_applications", "purchase");
        b.HasKey(a => a.ApplicationId);

        b.Property(a => a.AppliedAmount).HasPrecision(19, 4);

        b.HasOne<PaymentVoucher>().WithMany()
            .HasForeignKey(a => a.PaymentVoucherId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<VendorInvoice>().WithMany()
            .HasForeignKey(a => a.VendorInvoiceId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(a => new { a.PaymentVoucherId, a.VendorInvoiceId }).IsUnique();
    }
}
