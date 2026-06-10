using Accounting.Domain.Entities.Tax;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Tax;

internal sealed class CitYearSummaryConfiguration : IEntityTypeConfiguration<CitYearSummary>
{
    public void Configure(EntityTypeBuilder<CitYearSummary> b)
    {
        b.ToTable("cit_year_summaries", "tax");
        b.HasKey(x => x.CitYearSummaryId);

        b.Property(x => x.ComputedNetProfit).HasPrecision(19, 4);
        b.Property(x => x.OverrideNetProfit).HasPrecision(19, 4);
        b.Property(x => x.Pnd51EstimatedProfit).HasPrecision(19, 4);
        b.Property(x => x.Pnd51Prepaid).HasPrecision(19, 4);
        b.Property(x => x.Note).HasMaxLength(500);

        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");

        b.Ignore(x => x.EffectiveNetProfit);

        // One summary per (company, fiscal year) — the loss-c/f ledger key.
        b.HasIndex(x => new { x.CompanyId, x.FiscalYear }).IsUnique();
    }
}
