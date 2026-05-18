using Accounting.Domain.Entities.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Ledger;

internal sealed class AccountingPeriodConfiguration : IEntityTypeConfiguration<AccountingPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingPeriod> b)
    {
        b.ToTable("accounting_periods", "gl");
        b.HasKey(p => p.PeriodId);

        b.Property(p => p.Status)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<PeriodStatus>(v, ignoreCase: true))
            .HasMaxLength(10);

        b.Property(p => p.ClosedAt).HasColumnType("timestamptz(3)");
        b.Property(p => p.CloseNotes).HasMaxLength(500);

        b.ToTable(t => t.HasCheckConstraint("ck_period_month", "month BETWEEN 1 AND 12"));
        b.HasIndex(p => new { p.CompanyId, p.Year, p.Month }).IsUnique();
    }
}
