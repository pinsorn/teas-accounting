using Accounting.Domain.Entities.Expense;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Expense;

// Cycle C (specs/expense-claims.md §1) — header + line configs in one file, mirroring
// PaymentVoucherConfiguration.cs's parent+line grouping convention. Dedicated `expense` schema.

internal sealed class ExpenseClaimConfiguration : IEntityTypeConfiguration<ExpenseClaim>
{
    public void Configure(EntityTypeBuilder<ExpenseClaim> b)
    {
        b.ToTable("expense_claims", "expense");
        b.HasKey(x => x.ExpenseClaimId);

        b.Property(x => x.DocNo).HasMaxLength(40);
        b.Property(x => x.PrefixCode).HasMaxLength(20).HasDefaultValue("EX");
        b.Property(x => x.SubPrefix).HasMaxLength(20);

        b.Property(x => x.Title).HasMaxLength(200);

        b.Property(x => x.Status)
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                           v => Enum.Parse<ExpenseClaimStatus>(v, ignoreCase: true))
            .HasMaxLength(20)
            .HasDefaultValue(ExpenseClaimStatus.Draft);

        b.Property(x => x.PaymentMethod).HasMaxLength(20);

        b.Property(x => x.SubtotalAmount).HasPrecision(19, 4);
        b.Property(x => x.VatAmount).HasPrecision(19, 4);
        b.Property(x => x.TotalAmount).HasPrecision(19, 4);

        b.Property(x => x.RejectReason).HasMaxLength(500);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.ApprovedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.PaidAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasOne<Employee>().WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Accounting.Domain.Entities.Bank.BankAccount>().WithMany()
            .HasForeignKey(x => x.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Accounting.Domain.Entities.Ledger.JournalEntry>().WithMany()
            .HasForeignKey(x => x.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        // cont.79-style GL dimension (mirrors PaymentVoucherConfiguration's BusinessUnitId FK).
        b.HasOne<BusinessUnit>().WithMany()
            .HasForeignKey(x => x.BusinessUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.BusinessUnitId).HasFilter("business_unit_id IS NOT NULL");

        // H1 (specs/fix-duplicate-tax-doc-numbers.md) WP-4 — company-wide, matching the WP-1 sequence
        // scope. Keep `doc_no` in the generated name; NumberedDocumentWriter's retry matches on it (F6).
        b.HasIndex(x => new { x.CompanyId, x.DocNo })
            .IsUnique().HasFilter("doc_no IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.ClaimDate });
        b.HasIndex(x => new { x.EmployeeId, x.ClaimDate });
    }
}

internal sealed class ExpenseClaimLineConfiguration : IEntityTypeConfiguration<ExpenseClaimLine>
{
    public void Configure(EntityTypeBuilder<ExpenseClaimLine> b)
    {
        b.ToTable("expense_claim_lines", "expense");
        b.HasKey(x => x.ExpenseClaimLineId);

        b.Property(x => x.Description).HasMaxLength(300).IsRequired();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        // Tier-2 review — stored as a fraction (0.07 = 7%); (5,2) can't represent e.g. 0.075.
        b.Property(x => x.VatRate).HasPrecision(5, 4);
        b.Property(x => x.VatAmount).HasPrecision(19, 4);
        b.Property(x => x.LineTotal).HasPrecision(19, 4);
        b.Property(x => x.IsRecoverableVat).HasDefaultValue(false);

        b.HasOne<ExpenseClaim>().WithMany(x => x.Lines)
            .HasForeignKey(x => x.ExpenseClaimId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<ExpenseCategory>().WithMany()
            .HasForeignKey(x => x.ExpenseCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ExpenseClaimId, x.LineNo }).IsUnique();
    }
}
