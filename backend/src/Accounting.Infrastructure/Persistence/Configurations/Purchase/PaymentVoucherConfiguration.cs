using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Purchase;

internal sealed class PaymentVoucherConfiguration : IEntityTypeConfiguration<PaymentVoucher>
{
    public void Configure(EntityTypeBuilder<PaymentVoucher> b)
    {
        b.ToTable("payment_vouchers", "purchase");
        b.HasKey(p => p.PaymentVoucherId);

        b.Property(p => p.DocNo).HasMaxLength(50);
        b.Property(p => p.PrefixCode).HasMaxLength(20).HasDefaultValue("PV");
        b.Property(p => p.SubPrefix).HasMaxLength(20).IsRequired();

        b.Property(p => p.VendorTaxId).IsFixedLength().HasMaxLength(13);
        b.Property(p => p.VendorBranchCode).IsFixedLength().HasMaxLength(5);
        b.Property(p => p.VendorName).HasMaxLength(255).IsRequired();
        b.Property(p => p.VendorAddress).HasColumnType("text");

        b.Property(p => p.VendorType)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<CustomerType>(v, ignoreCase: true))
            .HasMaxLength(20);

        b.Property(p => p.PaymentMethod)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<PaymentMethod>(v, ignoreCase: true))
            .HasMaxLength(20);

        b.Property(p => p.ChequeNo).HasMaxLength(50);

        b.Property(p => p.CurrencyCode).IsFixedLength().HasMaxLength(3).HasDefaultValue("THB");
        b.Property(p => p.ExchangeRate).HasPrecision(19, 8).HasDefaultValue(1m);
        b.Property(p => p.SubtotalAmount).HasPrecision(19, 4);
        b.Property(p => p.VatAmount).HasPrecision(19, 4);
        b.Property(p => p.WhtAmount).HasPrecision(19, 4);
        b.Property(p => p.TotalPaid).HasPrecision(19, 4);
        b.Property(p => p.TotalAmountThb).HasPrecision(19, 4);

        // Sprint 8.7 — self-withhold + ภ.พ.36 reverse-charge flags.
        b.Property(p => p.SelfWithholdMode).HasDefaultValue(false);
        b.Property(p => p.RequiresPnd36ReverseCharge).HasDefaultValue(false);

        b.Property(p => p.Status)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<DocumentStatus>(v, ignoreCase: true))
            .HasMaxLength(20)
            .HasDefaultValue(DocumentStatus.Draft);

        b.Property(p => p.Description).HasMaxLength(500);
        b.Property(p => p.Notes).HasColumnType("text");

        b.Property(p => p.PostedAt).HasColumnType("timestamptz(3)");
        b.Property(p => p.ApprovedAt).HasColumnType("timestamptz(3)");
        b.Property(p => p.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(p => p.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(p => p.Version).IsConcurrencyToken();

        b.HasOne<VendorInvoice>().WithMany()
            .HasForeignKey(p => p.VendorInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // cont.79 — Business Unit GL dimension (Restrict so a BU can't be hard-deleted
        // while PVs reference it).
        b.HasOne<Accounting.Domain.Entities.Master.BusinessUnit>().WithMany()
            .HasForeignKey(p => p.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => p.BusinessUnitId).HasFilter("business_unit_id IS NOT NULL");

        // cont.77 — the ck_pv_sod CHECK (approved_by <> created_by) is dropped: approval is
        // now permission-based only and the creator may approve their own PV (Ham 2026-05-30).

        b.HasIndex(p => new { p.CompanyId, p.BranchId, p.DocNo })
            .IsUnique().HasFilter("doc_no IS NOT NULL");
        b.HasIndex(p => new { p.CompanyId, p.DocDate });
        b.HasIndex(p => new { p.VendorId, p.DocDate });
        b.HasIndex(p => p.VendorInvoiceId).HasFilter("vendor_invoice_id IS NOT NULL");
    }
}

internal sealed class PaymentVoucherLineConfiguration : IEntityTypeConfiguration<PaymentVoucherLine>
{
    public void Configure(EntityTypeBuilder<PaymentVoucherLine> b)
    {
        b.ToTable("payment_voucher_lines", "purchase");
        b.HasKey(l => l.LineId);

        b.Property(l => l.Description).HasMaxLength(500).IsRequired();
        // cont.76 — สินค้า/บริการ snapshot (ProductType enum codes, nullable for legacy rows).
        b.Property(l => l.ProductType).HasMaxLength(20);
        b.Property(l => l.Amount).HasPrecision(19, 4);
        b.Property(l => l.VatRate).HasPrecision(9, 6);
        b.Property(l => l.VatAmount).HasPrecision(19, 4);
        b.Property(l => l.WhtRate).HasPrecision(9, 6);
        b.Property(l => l.WhtAmount).HasPrecision(19, 4);

        b.HasOne<PaymentVoucher>().WithMany(p => p.Lines)
            .HasForeignKey(l => l.PaymentVoucherId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(l => new { l.PaymentVoucherId, l.LineNo }).IsUnique();
    }
}
