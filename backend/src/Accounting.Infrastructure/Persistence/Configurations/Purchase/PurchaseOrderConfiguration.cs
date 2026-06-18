using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Purchase;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("purchase_orders", "purchase");
        // cont.77 — the ck_po_sod CHECK (approved_by <> created_by) is dropped: approval is
        // now permission-based only and the creator may approve their own PO (Ham 2026-05-30).
        b.HasKey(x => x.PurchaseOrderId);

        b.Property(x => x.DocNo).HasMaxLength(40);
        b.Property(x => x.CreatedViaApiKeyName).HasMaxLength(120);   // M4 (MCP) — agent draft attribution
        b.Property(x => x.Status).HasConversion(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<PurchaseOrderStatus>(v, true)).HasMaxLength(20);
        b.Property(x => x.VendorName).HasMaxLength(255).IsRequired();
        b.Property(x => x.VendorAddress).HasColumnType("text");
        b.Property(x => x.VendorTaxId).HasMaxLength(13);
        b.Property(x => x.VendorType).HasConversion(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<CustomerType>(v, true)).HasMaxLength(20);
        b.Property(x => x.CurrencyCode).HasMaxLength(3);
        b.Property(x => x.ExchangeRate).HasPrecision(19, 6);
        b.Property(x => x.SubtotalAmount).HasPrecision(19, 4);
        b.Property(x => x.VatAmount).HasPrecision(19, 4);
        b.Property(x => x.TotalAmount).HasPrecision(19, 4);
        b.Property(x => x.TotalAmountThb).HasPrecision(19, 4);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.InternalNotes).HasMaxLength(2000);
        b.Property(x => x.CancellationReason).HasMaxLength(500);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.ApprovedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.SentToVendorAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.ClosedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.CancelledAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasIndex(x => new { x.CompanyId, x.DocNo }).IsUnique()
            .HasFilter("doc_no IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.ToTable("purchase_order_lines", "purchase");
        b.HasKey(l => l.LineId);
        b.Property(l => l.ProductCode).HasMaxLength(50);
        b.Property(l => l.DescriptionTh).HasMaxLength(500).IsRequired();
        b.Property(l => l.UomText).HasMaxLength(50);
        b.Property(l => l.TaxCode).HasMaxLength(20);
        b.Property(l => l.Notes).HasMaxLength(500);
        foreach (var p in new[] { nameof(PurchaseOrderLine.Quantity), nameof(PurchaseOrderLine.UnitPrice),
            nameof(PurchaseOrderLine.LineAmount), nameof(PurchaseOrderLine.TaxAmount),
            nameof(PurchaseOrderLine.TotalAmount) })
            b.Property(p).HasPrecision(19, 4);
        b.Property(l => l.TaxRate).HasPrecision(9, 6);
        b.HasOne<PurchaseOrder>().WithMany(p => p.Lines)
            .HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(l => new { l.PurchaseOrderId, l.LineNo }).IsUnique();
    }
}
