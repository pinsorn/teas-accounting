using Accounting.Domain.Entities.Bank;
using Accounting.Domain.Entities.Master;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Bank;

// Bank reconciliation (specs/bank-reconciliation.md B1.2) — the three bank.* tables' EF
// configs, combined in one file (mirrors PaymentVoucherConfiguration.cs's parent+line
// grouping convention).

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> b)
    {
        b.ToTable("bank_accounts", "bank");
        b.HasKey(x => x.BankAccountId);

        b.Property(x => x.BankCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.BankName).HasMaxLength(255).IsRequired();
        b.Property(x => x.AccountNo).HasMaxLength(50).IsRequired();
        b.Property(x => x.AccountName).HasMaxLength(255);
        b.Property(x => x.AccountType).HasMaxLength(50);
        b.Property(x => x.Currency).IsFixedLength().HasMaxLength(3).HasDefaultValue("THB");

        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");

        // Restrict — a referenced CoA row can't vanish while a bank account maps to it
        // (mirrors BusinessUnitConfiguration's DefaultRevenueAccountId FK).
        b.HasOne<ChartOfAccount>()
            .WithMany()
            .HasForeignKey(x => x.GlCashAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.CompanyId, x.AccountNo }).IsUnique();
    }
}

internal sealed class StatementImportConfiguration : IEntityTypeConfiguration<StatementImport>
{
    public void Configure(EntityTypeBuilder<StatementImport> b)
    {
        b.ToTable("statement_imports", "bank");
        b.HasKey(x => x.StatementImportId);

        b.Property(x => x.AdapterCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.SourceFileName).HasMaxLength(255).IsRequired();

        b.Property(x => x.OpeningBalance).HasPrecision(19, 4);
        b.Property(x => x.ClosingBalance).HasPrecision(19, 4);
        b.Property(x => x.WithdrawalTotal).HasPrecision(19, 4);
        b.Property(x => x.DepositTotal).HasPrecision(19, 4);

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ImportedAt).HasColumnType("timestamptz(3)");

        b.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.PeriodStart });
    }
}

internal sealed class StatementLineConfiguration : IEntityTypeConfiguration<StatementLine>
{
    public void Configure(EntityTypeBuilder<StatementLine> b)
    {
        b.ToTable("statement_lines", "bank");
        b.HasKey(x => x.StatementLineId);

        b.Property(x => x.Channel).HasMaxLength(100).IsRequired();
        b.Property(x => x.TxnType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.RawRef).HasMaxLength(100);

        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.RunningBalance).HasPrecision(19, 4);

        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.MatchStatus).HasConversion<string>().HasMaxLength(20);

        b.Property(x => x.MatchedAt).HasColumnType("timestamptz(3)");

        // NO FK nav to StatementImport/Receipt/PaymentVoucher/JournalEntry — id only (D1/B1.2).
        b.HasIndex(x => new { x.StatementImportId, x.LineNo });
        b.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.TxnDate });
    }
}
