using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Master;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    // Enum ⇄ RD-style screaming-snake string ('EXEMPT_GOOD' etc.) so the DB
    // CHECK + seeds read naturally and match the spec literals.
    private static string ToDb(ProductType t) => t switch
    {
        ProductType.Good => "GOOD",
        ProductType.Service => "SERVICE",
        ProductType.ExemptGood => "EXEMPT_GOOD",
        ProductType.ExemptService => "EXEMPT_SERVICE",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };
    private static ProductType FromDb(string v) => v switch
    {
        "GOOD" => ProductType.Good,
        "SERVICE" => ProductType.Service,
        "EXEMPT_GOOD" => ProductType.ExemptGood,
        "EXEMPT_SERVICE" => ProductType.ExemptService,
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, null),
    };

    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products", "master", t =>
            t.HasCheckConstraint("ck_products_type",
                "product_type IN ('GOOD','SERVICE','EXEMPT_GOOD','EXEMPT_SERVICE')"));
        b.HasKey(p => p.ProductId);

        b.Property(p => p.ProductCode).HasMaxLength(50).IsRequired();
        b.Property(p => p.NameTh).HasMaxLength(255).IsRequired();
        b.Property(p => p.NameEn).HasMaxLength(255);
        b.Property(p => p.ProductType)
            .HasConversion(v => ToDb(v), v => FromDb(v))
            .HasMaxLength(20);
        b.Property(p => p.DefaultUomText).HasMaxLength(50);
        b.Property(p => p.DefaultUnitPrice).HasPrecision(19, 4);
        b.Property(p => p.DescriptionTh).HasMaxLength(1000);
        b.Property(p => p.Notes).HasMaxLength(500);

        b.Property(p => p.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(p => p.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Property(p => p.Version).IsConcurrencyToken();

        // Case-insensitive uniqueness is enforced at the service validator
        // (DB index is plain — functional index = raw SQL, avoided to keep the
        // EF migration clean; mechanism note → Report-Backend15).
        b.HasIndex(p => new { p.CompanyId, p.ProductCode }).IsUnique();

        b.HasOne<Accounting.Domain.Entities.Tax.TaxCode>()
            .WithMany().HasForeignKey(p => p.DefaultOutputTaxCodeId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Accounting.Domain.Entities.Tax.TaxCode>()
            .WithMany().HasForeignKey(p => p.DefaultInputTaxCodeId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Accounting.Domain.Entities.Tax.WhtType>()
            .WithMany().HasForeignKey(p => p.DefaultWhtTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
