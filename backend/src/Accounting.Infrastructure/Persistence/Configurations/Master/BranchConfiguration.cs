using Accounting.Domain.Entities.Master;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Master;

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("branches", "master");
        b.HasKey(x => x.BranchId);

        b.Property(x => x.BranchCode).IsFixedLength().HasMaxLength(5).IsRequired();
        b.Property(x => x.NameTh).HasMaxLength(255).IsRequired();
        b.Property(x => x.NameEn).HasMaxLength(255);
        b.Property(x => x.AddressTh).HasColumnType("text");

        b.HasOne(x => x.Company)
            .WithMany(c => c.Branches)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t => t.HasCheckConstraint("ck_branches_code", "branch_code ~ '^[0-9]{5}$'"));
        b.HasIndex(x => new { x.CompanyId, x.BranchCode }).IsUnique();
    }
}
