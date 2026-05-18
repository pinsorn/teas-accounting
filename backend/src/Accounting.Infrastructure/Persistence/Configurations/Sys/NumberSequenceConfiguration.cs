using Accounting.Domain.Entities.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Sys;

internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.ToTable("number_sequences", "sys");
        b.HasKey(s => s.SequenceId);

        b.Property(s => s.PrefixCode).HasMaxLength(20).IsRequired();
        b.Property(s => s.SubPrefix).HasMaxLength(20).HasDefaultValue("").IsRequired();
        b.Property(s => s.BranchId).HasDefaultValue(0);
        b.Property(s => s.LastIssuedAt).HasColumnType("timestamptz(3)");

        b.ToTable(t => t.HasCheckConstraint(
            "ck_number_sequences_month",
            "period_month BETWEEN 1 AND 12"));

        b.HasIndex(s => new { s.CompanyId, s.BranchId, s.PrefixCode, s.SubPrefix, s.PeriodYear, s.PeriodMonth })
            .IsUnique()
            .HasDatabaseName("ux_number_sequences_period");
    }
}
