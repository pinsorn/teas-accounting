using Accounting.Domain.Entities.Master;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Master;

internal sealed class BusinessUnitConfiguration : IEntityTypeConfiguration<BusinessUnit>
{
    public void Configure(EntityTypeBuilder<BusinessUnit> b)
    {
        b.ToTable("business_units", "master");
        b.HasKey(x => x.BusinessUnitId);

        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.NameTh).HasMaxLength(255).IsRequired();
        b.Property(x => x.NameEn).HasMaxLength(255);

        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.Version).IsConcurrencyToken();

        // Optional pre-fill account — restrict delete so a referenced CoA row can't vanish.
        b.HasOne<ChartOfAccount>()
            .WithMany()
            .HasForeignKey(x => x.DefaultRevenueAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}
