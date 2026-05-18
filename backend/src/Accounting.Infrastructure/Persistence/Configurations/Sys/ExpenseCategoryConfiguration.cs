using Accounting.Domain.Entities.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Sys;

internal sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> b)
    {
        b.ToTable("expense_categories", "sys");
        b.HasKey(c => c.CategoryId);
        b.Property(c => c.CategoryCode).HasMaxLength(20).IsRequired();
        b.Property(c => c.NameTh).HasMaxLength(255).IsRequired();
        b.Property(c => c.NameEn).HasMaxLength(255);
        b.Property(c => c.Description).HasColumnType("text");
        b.Property(c => c.CreatedAt).HasColumnType("timestamptz(3)");

        b.HasOne(c => c.ParentCategory)
            .WithMany()
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(c => new { c.CompanyId, c.CategoryCode }).IsUnique();
    }
}
