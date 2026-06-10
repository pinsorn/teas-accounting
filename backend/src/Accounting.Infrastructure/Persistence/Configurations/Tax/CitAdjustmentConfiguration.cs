using Accounting.Domain.Entities.Tax;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Tax;

internal sealed class CitAdjustmentConfiguration : IEntityTypeConfiguration<CitAdjustment>
{
    public void Configure(EntityTypeBuilder<CitAdjustment> b)
    {
        b.ToTable("cit_adjustments", "tax");
        b.HasKey(x => x.CitAdjustmentId);

        b.Property(x => x.LegalRefCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Label).HasMaxLength(255).IsRequired();
        b.Property(x => x.Amount).HasPrecision(19, 4);

        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");

        b.HasIndex(x => new { x.CompanyId, x.FiscalYear });
    }
}
