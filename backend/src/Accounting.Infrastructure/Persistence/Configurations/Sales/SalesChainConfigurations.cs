using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Sales;

// Sprint 10 Part B — Q → SO → DO chain. Status enums stored as UPPER strings
// (same convention as TaxInvoice). Lines mirror the TI line shape; nullable
// ProductId FK → master.products (Restrict), like tax_invoice_lines.

internal sealed class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> b)
    {
        b.ToTable("quotations", "sales");
        b.HasKey(x => x.QuotationId);
        b.Property(x => x.DocNo).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<QuotationStatus>(v, true)).HasMaxLength(20);
        b.Property(x => x.CustomerName).HasMaxLength(255).IsRequired();
        b.Property(x => x.CustomerAddress).HasColumnType("text");
        b.Property(x => x.CustomerTaxId).HasMaxLength(13);
        b.Property(x => x.CustomerType).HasConversion(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<CustomerType>(v, true)).HasMaxLength(20);
        b.Property(x => x.CurrencyCode).HasMaxLength(3);
        b.Property(x => x.ExchangeRate).HasPrecision(19, 6);
        b.Property(x => x.SubtotalAmount).HasPrecision(19, 4);
        b.Property(x => x.VatAmount).HasPrecision(19, 4);
        b.Property(x => x.TotalAmount).HasPrecision(19, 4);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.InternalNotes).HasMaxLength(2000);
        b.Property(x => x.CreatedViaApiKeyName).HasMaxLength(120);
        b.Property(x => x.RejectedReason).HasMaxLength(500);
        b.Property(x => x.CancelledReason).HasMaxLength(500);
        b.Property(x => x.SentAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.AcceptedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.ExpiredAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.DocNo }).IsUnique()
            .HasFilter("doc_no IS NOT NULL");
    }
}

internal sealed class QuotationLineConfiguration : IEntityTypeConfiguration<QuotationLine>
{
    public void Configure(EntityTypeBuilder<QuotationLine> b)
    {
        b.ToTable("quotation_lines", "sales");
        b.HasKey(l => l.LineId);
        b.Property(l => l.ProductCode).HasMaxLength(50);
        b.Property(l => l.ProductType).HasMaxLength(20).IsRequired();  // Sprint 13h P7 + 13i C5 NOT NULL
        b.Property(l => l.DescriptionTh).HasMaxLength(500).IsRequired();
        b.Property(l => l.UomText).HasMaxLength(50).IsRequired();
        b.Property(l => l.TaxCode).HasMaxLength(20).IsRequired();
        foreach (var p in new[] { nameof(QuotationLine.Quantity), nameof(QuotationLine.UnitPrice),
            nameof(QuotationLine.DiscountPercent), nameof(QuotationLine.DiscountAmount),
            nameof(QuotationLine.LineAmount), nameof(QuotationLine.TaxAmount), nameof(QuotationLine.TotalAmount) })
            b.Property(p).HasPrecision(19, 4);
        b.Property(l => l.TaxRate).HasPrecision(9, 6);
        b.HasOne<Quotation>().WithMany(q => q.Lines)
            .HasForeignKey(l => l.QuotationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(l => new { l.QuotationId, l.LineNo }).IsUnique();
    }
}

internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> b)
    {
        b.ToTable("sales_orders", "sales");
        b.HasKey(x => x.SalesOrderId);
        b.Property(x => x.DocNo).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<SalesOrderStatus>(v, true)).HasMaxLength(20);
        b.Property(x => x.CustomerName).HasMaxLength(255).IsRequired();
        b.Property(x => x.CustomerAddress).HasColumnType("text");
        b.Property(x => x.CustomerTaxId).HasMaxLength(13);
        b.Property(x => x.CustomerType).HasConversion(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<CustomerType>(v, true)).HasMaxLength(20);
        b.Property(x => x.CurrencyCode).HasMaxLength(3);
        b.Property(x => x.ExchangeRate).HasPrecision(19, 6);
        b.Property(x => x.SubtotalAmount).HasPrecision(19, 4);
        b.Property(x => x.VatAmount).HasPrecision(19, 4);
        b.Property(x => x.TotalAmount).HasPrecision(19, 4);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.CancelledReason).HasMaxLength(500);
        b.Property(x => x.PostedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.ClosedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.DocNo }).IsUnique()
            .HasFilter("doc_no IS NOT NULL");
    }
}

internal sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> b)
    {
        b.ToTable("sales_order_lines", "sales");
        b.HasKey(l => l.LineId);
        b.Property(l => l.ProductCode).HasMaxLength(50);
        b.Property(l => l.ProductType).HasMaxLength(20).IsRequired();  // Sprint 13h P7 + 13i C5 NOT NULL
        b.Property(l => l.DescriptionTh).HasMaxLength(500).IsRequired();
        b.Property(l => l.UomText).HasMaxLength(50).IsRequired();
        b.Property(l => l.TaxCode).HasMaxLength(20).IsRequired();
        foreach (var p in new[] { nameof(SalesOrderLine.Quantity), nameof(SalesOrderLine.DeliveredQuantity),
            nameof(SalesOrderLine.UnitPrice), nameof(SalesOrderLine.DiscountPercent),
            nameof(SalesOrderLine.DiscountAmount), nameof(SalesOrderLine.LineAmount),
            nameof(SalesOrderLine.TaxAmount), nameof(SalesOrderLine.TotalAmount) })
            b.Property(p).HasPrecision(19, 4);
        b.Property(l => l.TaxRate).HasPrecision(9, 6);
        b.HasOne<SalesOrder>().WithMany(s => s.Lines)
            .HasForeignKey(l => l.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(l => new { l.SalesOrderId, l.LineNo }).IsUnique();
    }
}

internal sealed class DeliveryOrderConfiguration : IEntityTypeConfiguration<DeliveryOrder>
{
    public void Configure(EntityTypeBuilder<DeliveryOrder> b)
    {
        b.ToTable("delivery_orders", "sales");
        b.HasKey(x => x.DeliveryOrderId);
        b.Property(x => x.DocNo).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<DeliveryOrderStatus>(v, true)).HasMaxLength(20);
        b.Property(x => x.CustomerName).HasMaxLength(255).IsRequired();
        b.Property(x => x.CustomerAddress).HasColumnType("text");
        b.Property(x => x.CustomerTaxId).HasMaxLength(13);
        b.Property(x => x.CustomerType).HasConversion(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<CustomerType>(v, true)).HasMaxLength(20);
        b.Property(x => x.CurrencyCode).HasMaxLength(3);
        b.Property(x => x.ExchangeRate).HasPrecision(19, 6);
        b.Property(x => x.SubtotalAmount).HasPrecision(19, 4);
        b.Property(x => x.VatAmount).HasPrecision(19, 4);
        b.Property(x => x.TotalAmount).HasPrecision(19, 4);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.CancelledReason).HasMaxLength(500);
        b.Property(x => x.DeliveredAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.PostedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.DocNo }).IsUnique()
            .HasFilter("doc_no IS NOT NULL");
    }
}

// Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้). Header + lines. Optional grouping of
// TaxInvoices via long[] (PG array) on the header. Numbering on Issue.
internal sealed class BillingNoteConfiguration : IEntityTypeConfiguration<BillingNote>
{
    public void Configure(EntityTypeBuilder<BillingNote> b)
    {
        b.ToTable("billing_notes", "sales");
        b.HasKey(x => x.BillingNoteId);
        b.Property(x => x.DocNo).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<BillingNoteStatus>(v, true)).HasMaxLength(20);
        b.Property(x => x.CustomerName).HasMaxLength(255).IsRequired();
        b.Property(x => x.CustomerAddress).HasColumnType("text");
        b.Property(x => x.CustomerTaxId).HasMaxLength(13);
        b.Property(x => x.CustomerType).HasConversion(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<CustomerType>(v, true)).HasMaxLength(20);
        b.Property(x => x.CurrencyCode).HasMaxLength(3);
        b.Property(x => x.ExchangeRate).HasPrecision(19, 6);
        b.Property(x => x.SubtotalAmount).HasPrecision(19, 4);
        b.Property(x => x.VatAmount).HasPrecision(19, 4);
        b.Property(x => x.TotalAmount).HasPrecision(19, 4);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.InternalNotes).HasMaxLength(2000);
        b.Property(x => x.CancelledReason).HasMaxLength(500);
        b.Property(x => x.IssuedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.SettledAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.DocNo }).IsUnique()
            .HasFilter("doc_no IS NOT NULL");
        b.HasOne<Quotation>().WithMany().HasForeignKey(x => x.QuotationId)
            .OnDelete(DeleteBehavior.Restrict);
        // cont.69 Phase 1 — source DO link (DO → Invoice).
        b.HasOne<DeliveryOrder>().WithMany().HasForeignKey(x => x.DeliveryOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.DeliveryOrderId).HasFilter("delivery_order_id IS NOT NULL");
    }
}

// Sprint 13i C7 — BN ↔ TI join table. Composite PK, persisted applied_amount.
internal sealed class BillingNoteTaxInvoiceConfiguration : IEntityTypeConfiguration<BillingNoteTaxInvoice>
{
    public void Configure(EntityTypeBuilder<BillingNoteTaxInvoice> b)
    {
        b.ToTable("billing_note_tax_invoices", "sales");
        b.HasKey(x => new { x.BillingNoteId, x.TaxInvoiceId });
        b.Property(x => x.AppliedAmount).HasPrecision(19, 4);
        b.HasOne<BillingNote>().WithMany(bn => bn.TaxInvoiceLinks)
            .HasForeignKey(x => x.BillingNoteId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<TaxInvoice>().WithMany().HasForeignKey(x => x.TaxInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.TaxInvoiceId);
    }
}

internal sealed class BillingNoteLineConfiguration : IEntityTypeConfiguration<BillingNoteLine>
{
    public void Configure(EntityTypeBuilder<BillingNoteLine> b)
    {
        b.ToTable("billing_note_lines", "sales");
        b.HasKey(l => l.LineId);
        b.Property(l => l.ProductCode).HasMaxLength(50);
        b.Property(l => l.ProductType).HasMaxLength(20).IsRequired();  // Sprint 13i C5 NOT NULL
        b.Property(l => l.DescriptionTh).HasMaxLength(500).IsRequired();
        b.Property(l => l.UomText).HasMaxLength(50).IsRequired();
        b.Property(l => l.TaxCode).HasMaxLength(20).IsRequired();
        foreach (var p in new[] { nameof(BillingNoteLine.Quantity), nameof(BillingNoteLine.UnitPrice),
            nameof(BillingNoteLine.DiscountPercent), nameof(BillingNoteLine.DiscountAmount),
            nameof(BillingNoteLine.LineAmount), nameof(BillingNoteLine.TaxAmount), nameof(BillingNoteLine.TotalAmount) })
            b.Property(p).HasPrecision(19, 4);
        b.Property(l => l.TaxRate).HasPrecision(9, 6);
        b.HasOne<BillingNote>().WithMany(bn => bn.Lines)
            .HasForeignKey(l => l.BillingNoteId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TaxInvoice>().WithMany().HasForeignKey(l => l.TaxInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(l => new { l.BillingNoteId, l.LineNo }).IsUnique();
    }
}

internal sealed class DeliveryOrderLineConfiguration : IEntityTypeConfiguration<DeliveryOrderLine>
{
    public void Configure(EntityTypeBuilder<DeliveryOrderLine> b)
    {
        b.ToTable("delivery_order_lines", "sales");
        b.HasKey(l => l.LineId);
        b.Property(l => l.ProductCode).HasMaxLength(50);
        b.Property(l => l.ProductType).HasMaxLength(20).IsRequired();  // Sprint 13h P7 + 13i C5 NOT NULL
        b.Property(l => l.DescriptionTh).HasMaxLength(500).IsRequired();
        b.Property(l => l.UomText).HasMaxLength(50).IsRequired();
        b.Property(l => l.TaxCode).HasMaxLength(20).IsRequired();
        foreach (var p in new[] { nameof(DeliveryOrderLine.Quantity), nameof(DeliveryOrderLine.UnitPrice),
            nameof(DeliveryOrderLine.DiscountPercent), nameof(DeliveryOrderLine.DiscountAmount),
            nameof(DeliveryOrderLine.LineAmount), nameof(DeliveryOrderLine.TaxAmount), nameof(DeliveryOrderLine.TotalAmount) })
            b.Property(p).HasPrecision(19, 4);
        b.Property(l => l.TaxRate).HasPrecision(9, 6);
        b.HasOne<DeliveryOrder>().WithMany(d => d.Lines)
            .HasForeignKey(l => l.DeliveryOrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(l => new { l.DeliveryOrderId, l.LineNo }).IsUnique();
    }
}
