using Accounting.Domain.Entities.Payroll;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Payroll;

internal sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> b)
    {
        b.ToTable("payroll_runs", "payroll");
        b.HasKey(r => r.PayrollRunId);

        b.Property(r => r.PeriodYearMonth).IsFixedLength().HasMaxLength(6).IsRequired();
        b.Property(r => r.PrefixCode).HasMaxLength(10).HasDefaultValue("PR");
        b.Property(r => r.DocNo).HasMaxLength(40);
        b.Property(r => r.Notes).HasMaxLength(1000);

        b.Property(r => r.Status)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<DocumentStatus>(v, ignoreCase: true))
            .HasMaxLength(20)
            .HasDefaultValue(DocumentStatus.Draft);

        foreach (var p in new[]
                 {
                     nameof(PayrollRun.TotalGrossTaxable), nameof(PayrollRun.TotalGrossNonTaxable),
                     nameof(PayrollRun.TotalPit), nameof(PayrollRun.TotalSsoEmployee),
                     nameof(PayrollRun.TotalSsoEmployer), nameof(PayrollRun.TotalOtherDeductions),
                     nameof(PayrollRun.TotalNet),
                 })
            b.Property(p).HasColumnType("numeric(18,4)");

        b.Property(r => r.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(r => r.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(r => r.ApprovedAt).HasColumnType("timestamptz(3)");
        b.Property(r => r.PostedAt).HasColumnType("timestamptz(3)");
        b.Property(r => r.PaidAt).HasColumnType("timestamptz(3)");

        b.Property(r => r.Version).IsConcurrencyToken();

        // One run per company per period — the app guards too, this is the hard backstop.
        b.HasIndex(r => new { r.CompanyId, r.PeriodYearMonth }).IsUnique();
        // Posted doc numbers are unique per company (NULL drafts excluded by the filter).
        b.HasIndex(r => new { r.CompanyId, r.DocNo }).IsUnique().HasFilter("doc_no IS NOT NULL");

        b.HasMany(r => r.Payslips)
            .WithOne(p => p.Run!)
            .HasForeignKey(p => p.PayrollRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
