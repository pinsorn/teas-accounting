using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Master;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Ledger;

internal sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> b)
    {
        b.ToTable("journal_lines", "gl");
        b.HasKey(l => l.LineId);

        b.Property(l => l.DebitAmount).HasPrecision(19, 4);
        b.Property(l => l.CreditAmount).HasPrecision(19, 4);
        b.Property(l => l.Description).HasMaxLength(500);
        b.Property(l => l.Reference).HasMaxLength(255);
        b.Property(l => l.DimensionsJson).HasColumnType("jsonb").HasColumnName("dimensions");

        b.HasOne(l => l.Journal)
            .WithMany(j => j.Lines)
            .HasForeignKey(l => l.JournalId)
            .OnDelete(DeleteBehavior.Cascade);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_journal_lines_amount_sign",
            "(debit_amount > 0 AND credit_amount = 0) OR (credit_amount > 0 AND debit_amount = 0)"));

        b.HasOne<BusinessUnit>().WithMany().HasForeignKey(l => l.BusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(l => new { l.JournalId, l.LineNo }).IsUnique();
        b.HasIndex(l => l.AccountId);
        // Reports filter by BU. journal_lines has no company_id (it's on the
        // parent journal_entry); company scoping comes via the join in reports.
        b.HasIndex(l => l.BusinessUnitId).HasFilter("business_unit_id IS NOT NULL");
    }
}
