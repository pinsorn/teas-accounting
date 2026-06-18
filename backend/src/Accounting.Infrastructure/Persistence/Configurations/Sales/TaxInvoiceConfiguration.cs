using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Sales;

internal sealed class TaxInvoiceConfiguration : IEntityTypeConfiguration<TaxInvoice>
{
    public void Configure(EntityTypeBuilder<TaxInvoice> b)
    {
        b.ToTable("tax_invoices", "sales");
        b.HasKey(t => t.TaxInvoiceId);

        b.Property(t => t.DocNo).HasMaxLength(50);
        b.Property(t => t.BookNo).HasMaxLength(20);
        b.Property(t => t.InvoiceType).HasMaxLength(20).HasDefaultValue("FULL");
        b.Property(t => t.TaxPointReason).HasMaxLength(50);

        b.Property(t => t.SupplierTaxId).IsFixedLength().HasMaxLength(13).IsRequired();
        b.Property(t => t.SupplierBranchCode).IsFixedLength().HasMaxLength(5).IsRequired();
        b.Property(t => t.SupplierBranchName).HasMaxLength(255).IsRequired();
        b.Property(t => t.SupplierName).HasMaxLength(255).IsRequired();
        b.Property(t => t.SupplierAddress).HasColumnType("text").IsRequired();

        b.Property(t => t.CustomerTaxId).IsFixedLength().HasMaxLength(13);
        b.Property(t => t.CustomerBranchCode).IsFixedLength().HasMaxLength(5);
        b.Property(t => t.CustomerBranchName).HasMaxLength(255);
        b.Property(t => t.CustomerName).HasMaxLength(255).IsRequired();
        b.Property(t => t.CustomerAddress).HasColumnType("text").IsRequired();

        b.Property(t => t.CurrencyCode).IsFixedLength().HasMaxLength(3).HasDefaultValue("THB");
        b.Property(t => t.ExchangeRate).HasPrecision(19, 8).HasDefaultValue(1m);
        b.Property(t => t.SubtotalAmount).HasPrecision(19, 4);
        b.Property(t => t.DiscountAmount).HasPrecision(19, 4);
        b.Property(t => t.TaxableAmount).HasPrecision(19, 4);
        b.Property(t => t.NonTaxableAmount).HasPrecision(19, 4);
        b.Property(t => t.TaxAmount).HasPrecision(19, 4);
        b.Property(t => t.TotalAmount).HasPrecision(19, 4);
        b.Property(t => t.TotalAmountThb).HasPrecision(19, 4);
        b.Property(t => t.AmountPaid).HasPrecision(19, 4);
        b.Property(t => t.AmountInWordsTh).HasMaxLength(500);

        b.Property(t => t.Status)
            .HasConversion(
                v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<DocumentStatus>(v, ignoreCase: true))
            .HasMaxLength(20)
            .HasDefaultValue(DocumentStatus.Draft);

        b.Property(t => t.PaymentStatus).HasMaxLength(20).HasDefaultValue("UNPAID");
        b.Property(t => t.ETaxXmlUrl).HasMaxLength(500);
        b.Property(t => t.ETaxPdfUrl).HasMaxLength(500);
        b.Property(t => t.ETaxAckId).HasMaxLength(100);
        b.Property(t => t.ETaxStatus).HasMaxLength(20);
        b.Property(t => t.DeliveryMethod).HasMaxLength(20);
        b.Property(t => t.PaymentTerms).HasMaxLength(500);
        b.Property(t => t.Notes).HasColumnType("text");
        b.Property(t => t.CreatedViaApiKeyName).HasMaxLength(120);

        b.Property(t => t.PostedAt).HasColumnType("timestamptz(3)");
        b.Property(t => t.ETaxSignedAt).HasColumnType("timestamptz(3)");
        b.Property(t => t.ETaxSubmittedAt).HasColumnType("timestamptz(3)");
        b.Property(t => t.DeliveredToCustomerAt).HasColumnType("timestamptz(3)");
        b.Property(t => t.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(t => t.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(t => t.Version).IsConcurrencyToken();

        b.HasOne<TaxInvoice>().WithMany().HasForeignKey(t => t.OriginalInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BusinessUnit>().WithMany().HasForeignKey(t => t.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => new { t.CompanyId, t.BusinessUnitId }).HasFilter("business_unit_id IS NOT NULL");

        // Sprint 13h P6.1 — optional FK to the originating Quotation.
        b.HasOne<Quotation>().WithMany().HasForeignKey(t => t.QuotationId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => t.QuotationId).HasFilter("quotation_id IS NOT NULL");

        // cont.69 Phase 1 — optional FK to the source Invoice (BillingNote) the TI was created from.
        b.HasOne<BillingNote>().WithMany().HasForeignKey(t => t.BillingNoteId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(t => t.BillingNoteId).HasFilter("billing_note_id IS NOT NULL");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_ti_invoice_type", "invoice_type = 'FULL'");
            t.HasCheckConstraint("ck_ti_tax_point", "doc_date = tax_point_date");
        });

        b.HasIndex(t => new { t.CompanyId, t.BranchId, t.DocNo }).IsUnique().HasFilter("doc_no IS NOT NULL");
        b.HasIndex(t => new { t.CompanyId, t.DocDate });
        b.HasIndex(t => new { t.CustomerId, t.DocDate });
        // ponytail: compound (company_id, status, doc_date) replaces the bare (status, doc_date)
        // index — status-filter list queries are always tenant-scoped (03-L2).
        b.HasIndex(t => new { t.CompanyId, t.Status, t.DocDate })
            .HasDatabaseName("ix_tax_invoices_company_id_status_doc_date");
    }
}

internal sealed class TaxInvoiceLineConfiguration : IEntityTypeConfiguration<TaxInvoiceLine>
{
    public void Configure(EntityTypeBuilder<TaxInvoiceLine> b)
    {
        b.ToTable("tax_invoice_lines", "sales");
        b.HasKey(l => l.LineId);

        b.Property(l => l.ProductCode).HasMaxLength(50);
        b.Property(l => l.ProductType).HasMaxLength(20).IsRequired();  // Sprint 13h P7 + 13i C5 NOT NULL
        b.Property(l => l.DescriptionTh).HasMaxLength(500).IsRequired();
        b.Property(l => l.UomText).HasMaxLength(50).IsRequired();
        b.Property(l => l.TaxCode).HasMaxLength(20).IsRequired();

        b.Property(l => l.Quantity).HasPrecision(19, 4);
        b.Property(l => l.UnitPrice).HasPrecision(19, 4);
        b.Property(l => l.DiscountPercent).HasPrecision(9, 4);
        b.Property(l => l.DiscountAmount).HasPrecision(19, 4);
        b.Property(l => l.LineAmount).HasPrecision(19, 4);
        b.Property(l => l.TaxRate).HasPrecision(9, 6);
        b.Property(l => l.TaxAmount).HasPrecision(19, 4);
        b.Property(l => l.TotalAmount).HasPrecision(19, 4);

        b.HasOne<TaxInvoice>().WithMany(t => t.Lines)
            .HasForeignKey(l => l.TaxInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sprint 10 A2 — connect the Sprint-1 nullable ProductId scaffold to the
        // real Product master. Nullable FK; Restrict so a product can't be hard-
        // deleted out from under a line (deactivate instead). No new column.
        b.HasOne<Accounting.Domain.Entities.Master.Product>()
            .WithMany().HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(l => new { l.TaxInvoiceId, l.LineNo }).IsUnique();
    }
}
