using Accounting.Domain.Entities.FixedAsset;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.FixedAsset;

// Cycle D (specs/fixed-assets.md §1.3) — all three FA configs in one file, dedicated
// `fixedasset` schema (EnsureSchema below). Mirrors ExpenseClaimConfiguration's header+child
// grouping convention.

internal sealed class FixedAssetConfiguration : IEntityTypeConfiguration<Accounting.Domain.Entities.FixedAsset.FixedAsset>
{
    public void Configure(EntityTypeBuilder<Accounting.Domain.Entities.FixedAsset.FixedAsset> b)
    {
        // dotnet ef migrations add auto-detects the new schema from ToTable(..., "fixedasset")
        // and emits migrationBuilder.EnsureSchema("fixedasset") in the generated migration.
        b.ToTable("fixed_assets", "fixedasset");
        b.HasKey(x => x.FixedAssetId);

        b.Property(x => x.DocNo).HasMaxLength(40);
        b.Property(x => x.PrefixCode).HasMaxLength(20).HasDefaultValue("FA");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Category).HasMaxLength(60);

        b.Property(x => x.Cost).HasPrecision(19, 4);
        b.Property(x => x.SalvageValue).HasPrecision(19, 4).HasDefaultValue(0m);
        b.Property(x => x.DepreciableBase).HasPrecision(19, 4).HasDefaultValue(0m);
        b.Property(x => x.MonthlyAmount).HasPrecision(19, 4).HasDefaultValue(0m);
        b.Property(x => x.AccumulatedDepreciation).HasPrecision(19, 4).HasDefaultValue(0m);
        // NULL is meaningful (legacy row, units derived from posted lines at run time) — no
        // HasDefaultValue here; a DB default would destroy that discriminator (§3.4).
        b.Property(x => x.MonthsDepreciated).HasPrecision(9, 4);
        b.Property(x => x.DisposalProceeds).HasPrecision(19, 4);
        b.Property(x => x.DisposalVatAmount).HasPrecision(19, 4);
        b.Property(x => x.DisposalGainLoss).HasPrecision(19, 4);

        b.Property(x => x.Status)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<FixedAssetStatus>(v, ignoreCase: true))
            .HasMaxLength(20)
            .HasDefaultValue(FixedAssetStatus.Draft);

        b.Property(x => x.DisposalBuyerName).HasMaxLength(200);
        b.Property(x => x.WriteoffReason).HasMaxLength(500);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.ActivatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.DisposedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasOne<VendorInvoice>().WithMany()
            .HasForeignKey(x => x.VendorInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<ChartOfAccount>().WithMany()
            .HasForeignKey(x => x.AssetCostAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ChartOfAccount>().WithMany()
            .HasForeignKey(x => x.AccumDepAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ChartOfAccount>().WithMany()
            .HasForeignKey(x => x.DepExpenseAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<JournalEntry>().WithMany()
            .HasForeignKey(x => x.DisposalJournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<BusinessUnit>().WithMany()
            .HasForeignKey(x => x.BusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.BusinessUnitId).HasFilter("business_unit_id IS NOT NULL");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_fixed_assets_useful_life", "useful_life_months > 0");
            t.HasCheckConstraint("ck_fixed_assets_salvage",
                "salvage_value >= 0 AND salvage_value <= cost");
        });

        // H1 (specs/fix-duplicate-tax-doc-numbers.md) WP-4 — company-wide, matching the WP-1 sequence
        // scope. Keep `doc_no` in the generated name; NumberedDocumentWriter's retry matches on it (F6).
        b.HasIndex(x => new { x.CompanyId, x.DocNo })
            .IsUnique().HasFilter("doc_no IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.Status });
        b.HasIndex(x => new { x.CompanyId, x.AcquireDate });
    }
}

internal sealed class DepreciationRunConfiguration : IEntityTypeConfiguration<DepreciationRun>
{
    public void Configure(EntityTypeBuilder<DepreciationRun> b)
    {
        b.ToTable("depreciation_runs", "fixedasset");
        b.HasKey(x => x.DepreciationRunId);

        b.Property(x => x.TotalAmount).HasPrecision(19, 4);

        b.Property(x => x.Status)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<DepreciationRunStatus>(v, ignoreCase: true))
            .HasMaxLength(20)
            .HasDefaultValue(DepreciationRunStatus.Posted);

        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasOne<JournalEntry>().WithMany()
            .HasForeignKey(x => x.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t => t.HasCheckConstraint("ck_depreciation_runs_month", "period_month BETWEEN 1 AND 12"));

        // FA-E — one run per company-month; the idempotency early-return AND the concurrent-race
        // backstop both key off this constraint.
        b.HasIndex(x => new { x.CompanyId, x.PeriodYear, x.PeriodMonth }).IsUnique();
    }
}

internal sealed class DepreciationRunLineConfiguration : IEntityTypeConfiguration<DepreciationRunLine>
{
    public void Configure(EntityTypeBuilder<DepreciationRunLine> b)
    {
        b.ToTable("depreciation_run_lines", "fixedasset");
        b.HasKey(x => x.DepreciationRunLineId);

        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.AccumulatedAfter).HasPrecision(19, 4);

        b.HasOne<DepreciationRun>().WithMany(x => x.Lines)
            .HasForeignKey(x => x.DepreciationRunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<Accounting.Domain.Entities.FixedAsset.FixedAsset>().WithMany()
            .HasForeignKey(x => x.FixedAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.FixedAssetId, x.DepreciationRunId }).IsUnique();
    }
}
