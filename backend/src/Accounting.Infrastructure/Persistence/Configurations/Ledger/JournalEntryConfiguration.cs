using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Ledger;

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> b)
    {
        b.ToTable("journal_entries", "gl");
        b.HasKey(j => j.JournalId);

        b.Property(j => j.DocNo).HasMaxLength(50);
        b.Property(j => j.PrefixCode).HasMaxLength(20).IsRequired();
        b.Property(j => j.Description).HasMaxLength(500).IsRequired();
        b.Property(j => j.Reference).HasMaxLength(255);
        b.Property(j => j.CurrencyCode).IsFixedLength().HasMaxLength(3).HasDefaultValue("THB");
        b.Property(j => j.ExchangeRate).HasPrecision(19, 8).HasDefaultValue(1m);
        b.Property(j => j.TotalDebit).HasPrecision(19, 4);
        b.Property(j => j.TotalCredit).HasPrecision(19, 4);

        b.Property(j => j.Status)
            .HasConversion(
                v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<DocumentStatus>(v, ignoreCase: true))
            .HasMaxLength(20);

        b.Property(j => j.PostedAt).HasColumnType("timestamptz(3)");
        b.Property(j => j.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(j => j.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(j => j.Version).IsConcurrencyToken();

        b.HasOne(j => j.ReversalOf)
            .WithMany()
            .HasForeignKey(j => j.ReversalOfId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(j => new { j.CompanyId, j.DocNo }).IsUnique()
            .HasFilter("doc_no IS NOT NULL");
        b.HasIndex(j => new { j.CompanyId, j.DocDate });
        b.HasIndex(j => new { j.CompanyId, j.Status, j.DocDate });
    }
}
