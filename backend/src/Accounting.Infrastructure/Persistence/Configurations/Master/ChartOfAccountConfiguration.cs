using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Master;

internal sealed class ChartOfAccountConfiguration : IEntityTypeConfiguration<ChartOfAccount>
{
    public void Configure(EntityTypeBuilder<ChartOfAccount> b)
    {
        b.ToTable("chart_of_accounts", "master");
        b.HasKey(a => a.AccountId);

        b.Property(a => a.AccountCode).HasMaxLength(20).IsRequired();
        b.Property(a => a.AccountNameTh).HasMaxLength(255).IsRequired();
        b.Property(a => a.AccountNameEn).HasMaxLength(255);

        b.Property(a => a.AccountType)
            .HasConversion(
                v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<AccountType>(v, ignoreCase: true))
            .HasMaxLength(20);

        b.Property(a => a.NormalBalance)
            .HasConversion(
                v => v == NormalBalance.Debit ? "DR" : "CR",
                v => v == "DR" ? NormalBalance.Debit : NormalBalance.Credit)
            .IsFixedLength()
            .HasMaxLength(2);

        b.Property(a => a.CreatedAt).HasColumnType("timestamptz(3)");

        b.HasOne(a => a.Parent)
            .WithMany(a => a.Children)
            .HasForeignKey(a => a.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_coa_account_type",
                "account_type IN ('ASSET','LIABILITY','EQUITY','REVENUE','EXPENSE')");
            t.HasCheckConstraint("ck_coa_normal_balance", "normal_balance IN ('DR','CR')");
        });

        b.HasIndex(a => new { a.CompanyId, a.AccountCode }).IsUnique();
    }
}
