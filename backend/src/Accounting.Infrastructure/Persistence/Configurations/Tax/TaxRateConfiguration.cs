using Accounting.Domain.Entities.Tax;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Tax;

internal sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> b)
    {
        b.ToTable("tax_rates", "tax");
        b.HasKey(r => r.TaxRateId);

        b.Property(r => r.Rate).HasPrecision(9, 6).IsRequired();

        b.HasOne(r => r.TaxCode)
            .WithMany(c => c.Rates)
            .HasForeignKey(r => r.TaxCodeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(r => new { r.TaxCodeId, r.EffectiveFrom }).IsUnique();
    }
}
