using Accounting.Domain.Entities.ETax;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.ETax;

internal sealed class ETaxSubmissionConfiguration : IEntityTypeConfiguration<ETaxSubmission>
{
    public void Configure(EntityTypeBuilder<ETaxSubmission> b)
    {
        b.ToTable("submissions", "etax");
        b.HasKey(s => s.SubmissionId);

        // VARCHAR(20) — EF stores the enum member name (SignedOk, SendOk, ...).
        b.Property(s => s.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();

        b.Property(s => s.XmlSha256).HasMaxLength(64);
        b.Property(s => s.SignedXmlPath).HasMaxLength(500);
        b.Property(s => s.PdfPath).HasMaxLength(500);
        b.Property(s => s.EmailMessageId).HasMaxLength(255);
        b.Property(s => s.ToEmailSnapshot).HasMaxLength(255).IsRequired();
        b.Property(s => s.CcEmailSnapshot).HasMaxLength(255);
        b.Property(s => s.IntendedToEmail).HasMaxLength(255);
        b.Property(s => s.SmtpResponse).HasMaxLength(2000);
        b.Property(s => s.RdAckRef).HasMaxLength(100);
        b.Property(s => s.RdRejectionCode).HasMaxLength(50);
        b.Property(s => s.Notes).HasMaxLength(1000);

        b.Property(s => s.AttemptedAt).HasColumnType("timestamptz(3)");
        b.Property(s => s.RetryAfter).HasColumnType("timestamptz(3)");
        b.Property(s => s.CreatedAt).HasColumnType("timestamptz(3)");

        b.HasIndex(s => new { s.CompanyId, s.TaxInvoiceId, s.AttemptedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_etax_sub_invoice");
        b.HasIndex(s => new { s.CompanyId, s.Outcome, s.AttemptedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_etax_sub_outcome");
        b.HasIndex(s => new { s.CompanyId, s.DeadLetter, s.AttemptedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_etax_sub_dead")
            .HasFilter("dead_letter = true");
    }
}
