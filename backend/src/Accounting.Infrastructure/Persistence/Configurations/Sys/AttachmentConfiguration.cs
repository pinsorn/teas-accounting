using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Sys;

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> b)
    {
        b.ToTable("attachments", "sys");
        b.HasKey(a => a.AttachmentId);

        b.Property(a => a.ParentType)
            .HasConversion(v => AttachmentCodes.ToDb(v), v => AttachmentCodes.ParentFrom(v))
            .HasMaxLength(30);
        b.Property(a => a.Category)
            .HasConversion(v => AttachmentCodes.ToDb(v), v => AttachmentCodes.CategoryFrom(v))
            .HasMaxLength(30);

        b.Property(a => a.FileName).HasMaxLength(255).IsRequired();
        b.Property(a => a.MimeType).HasMaxLength(100).IsRequired();
        b.Property(a => a.StoragePath).HasMaxLength(500).IsRequired();
        b.Property(a => a.Description).HasMaxLength(500);
        b.Property(a => a.UploadedAt).HasColumnType("timestamptz(3)");
        b.Property(a => a.DeletedAt).HasColumnType("timestamptz(3)");

        b.HasIndex(a => new { a.CompanyId, a.ParentType, a.ParentId })
            .HasDatabaseName("ix_attachments_parent")
            .HasFilter("deleted_at IS NULL");
        b.HasIndex(a => new { a.CompanyId, a.Category, a.UploadedAt })
            .HasDatabaseName("ix_attachments_category")
            .HasFilter("deleted_at IS NULL");
    }
}
