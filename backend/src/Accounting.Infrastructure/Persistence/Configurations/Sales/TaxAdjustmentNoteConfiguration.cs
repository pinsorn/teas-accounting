using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Sales;

internal sealed class TaxAdjustmentNoteConfiguration : IEntityTypeConfiguration<TaxAdjustmentNote>
{
    public void Configure(EntityTypeBuilder<TaxAdjustmentNote> b)
    {
        b.ToTable("tax_adjustment_notes", "sales");
        b.HasKey(n => n.NoteId);

        b.Property(n => n.DocNo).HasMaxLength(50);
        b.Property(n => n.PrefixCode).HasMaxLength(20).IsRequired();
        b.Property(n => n.NoteType)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<TaxAdjustmentNoteType>(v, ignoreCase: true))
            .HasMaxLength(10);

        b.Property(n => n.ReasonCode).HasMaxLength(40);
        b.Property(n => n.Reason).HasMaxLength(500).IsRequired();
        b.Property(n => n.CustomerTaxId).IsFixedLength().HasMaxLength(13);
        b.Property(n => n.CustomerBranchCode).IsFixedLength().HasMaxLength(5);
        b.Property(n => n.CustomerName).HasMaxLength(255).IsRequired();
        b.Property(n => n.CustomerAddress).HasColumnType("text").IsRequired();

        b.Property(n => n.CurrencyCode).IsFixedLength().HasMaxLength(3).HasDefaultValue("THB");
        b.Property(n => n.ExchangeRate).HasPrecision(19, 8).HasDefaultValue(1m);
        b.Property(n => n.SubtotalAmount).HasPrecision(19, 4);
        b.Property(n => n.TaxAmount).HasPrecision(19, 4);
        b.Property(n => n.TotalAmount).HasPrecision(19, 4);
        b.Property(n => n.TotalAmountThb).HasPrecision(19, 4);
        b.Property(n => n.TaxRate).HasPrecision(9, 6);

        b.Property(n => n.Status)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<DocumentStatus>(v, ignoreCase: true))
            .HasMaxLength(20)
            .HasDefaultValue(DocumentStatus.Draft);

        b.Property(n => n.Notes).HasColumnType("text");

        b.Property(n => n.PostedAt).HasColumnType("timestamptz(3)");
        b.Property(n => n.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(n => n.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(n => n.Version).IsConcurrencyToken();

        b.HasOne<TaxInvoice>().WithMany().HasForeignKey(n => n.OriginalTaxInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BusinessUnit>().WithMany().HasForeignKey(n => n.BusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_note_type", "note_type IN ('CREDIT','DEBIT')");
            t.HasCheckConstraint("ck_note_tax_point", "doc_date = tax_point_date");
        });

        b.HasIndex(n => new { n.CompanyId, n.BranchId, n.DocNo }).IsUnique().HasFilter("doc_no IS NOT NULL");
        b.HasIndex(n => n.OriginalTaxInvoiceId);
    }
}
