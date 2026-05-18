using Accounting.Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Audit;

internal sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> b)
    {
        b.ToTable("activity_log", "audit");
        b.HasKey(a => a.ActivityId);

        b.Property(a => a.Username).HasMaxLength(100);
        b.Property(a => a.SessionId).HasMaxLength(100);
        b.Property(a => a.UserAgent).HasMaxLength(500);

        b.Property(a => a.ActivityAt).HasColumnType("timestamptz(3)");
        b.Property(a => a.ActivityType).HasMaxLength(50).IsRequired();
        b.Property(a => a.Module).HasMaxLength(50);
        b.Property(a => a.EntityType).HasMaxLength(50);
        b.Property(a => a.EntityDocNo).HasMaxLength(50);

        b.Property(a => a.IpAddress).HasColumnType("inet");

        b.Property(a => a.BeforeValueJson).HasColumnType("jsonb").HasColumnName("before_value");
        b.Property(a => a.AfterValueJson).HasColumnType("jsonb").HasColumnName("after_value");
        b.Property(a => a.MetadataJson).HasColumnType("jsonb").HasColumnName("metadata");

        b.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("ix_audit_entity");
        b.HasIndex(a => new { a.UserId, a.ActivityAt }).HasDatabaseName("ix_audit_user_time");
    }
}
