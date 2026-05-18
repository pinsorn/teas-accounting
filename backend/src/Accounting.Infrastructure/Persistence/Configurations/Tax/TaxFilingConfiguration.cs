using Accounting.Domain.Entities.Tax;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Tax;

internal sealed class TaxFilingConfiguration : IEntityTypeConfiguration<TaxFiling>
{
    public void Configure(EntityTypeBuilder<TaxFiling> b)
    {
        b.ToTable("tax_filings", "tax");
        b.HasKey(f => f.FilingId);

        b.Property(f => f.FormType).HasMaxLength(10).IsRequired();
        b.Property(f => f.Status).HasMaxLength(20).IsRequired();
        b.Property(f => f.SubmissionMode).HasMaxLength(10);
        b.Property(f => f.RdAckRef).HasMaxLength(50);
        b.Property(f => f.PayloadJson).HasColumnType("jsonb").IsRequired();
        b.Property(f => f.PdfStoragePath).HasMaxLength(500);

        b.Property(f => f.FinalizedAt).HasColumnType("timestamptz(3)");
        b.Property(f => f.SubmittedAt).HasColumnType("timestamptz(3)");
        b.Property(f => f.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(f => f.UpdatedAt).HasColumnType("timestamptz(3)");

        // One filing per (company, form, period) — amendment = Phase 2 (new record).
        b.HasIndex(f => new { f.CompanyId, f.FormType, f.Period }).IsUnique();
    }
}
