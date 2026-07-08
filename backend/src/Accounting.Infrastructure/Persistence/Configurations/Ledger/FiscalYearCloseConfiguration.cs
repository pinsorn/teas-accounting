using Accounting.Domain.Entities.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Ledger;

internal sealed class FiscalYearCloseConfiguration : IEntityTypeConfiguration<FiscalYearClose>
{
    public void Configure(EntityTypeBuilder<FiscalYearClose> b)
    {
        b.ToTable("fiscal_year_closes", "gl");
        b.HasKey(x => x.FiscalYearCloseId);

        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.NetProfit).HasPrecision(19, 4);

        b.Property(x => x.ClosedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.ReversedAt).HasColumnType("timestamptz(3)");

        // D7 — one ACTIVE close per (company, fiscal year); reversed rows stay for audit and
        // free the slot for a clean re-close.
        b.HasIndex(x => new { x.CompanyId, x.FiscalYear })
            .HasFilter("reversed_at IS NULL")
            .IsUnique();
    }
}
