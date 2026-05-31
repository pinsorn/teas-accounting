using Accounting.Domain.Entities.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Payroll;

internal sealed class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> b)
    {
        b.ToTable("payslips", "payroll");
        b.HasKey(p => p.PayslipId);

        b.Property(p => p.EmployeeCode).HasMaxLength(50).IsRequired();
        b.Property(p => p.EmployeeName).HasMaxLength(320).IsRequired();
        b.Property(p => p.NationalId).IsFixedLength().HasMaxLength(13).IsRequired();
        b.Property(p => p.AddressText).HasMaxLength(600);
        b.Property(p => p.BankName).HasMaxLength(120);
        b.Property(p => p.BankAccountNo).HasMaxLength(50);
        b.Property(p => p.BankAccountName).HasMaxLength(255);

        foreach (var p in new[]
                 {
                     nameof(Payslip.GrossTaxable), nameof(Payslip.GrossNonTaxable),
                     nameof(Payslip.PitWithheld), nameof(Payslip.SsoEmployee),
                     nameof(Payslip.SsoEmployer), nameof(Payslip.OtherDeductions),
                     nameof(Payslip.NetPay), nameof(Payslip.YtdIncome), nameof(Payslip.YtdPit),
                 })
            b.Property(p).HasColumnType("numeric(18,4)");

        // One payslip per employee per run.
        b.HasIndex(p => new { p.PayrollRunId, p.EmployeeId }).IsUnique();
        b.HasIndex(p => p.EmployeeId);
    }
}
