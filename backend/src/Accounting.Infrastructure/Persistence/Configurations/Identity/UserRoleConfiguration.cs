using Accounting.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("user_roles", "sys");
        b.HasKey(ur => new { ur.UserId, ur.RoleId, ur.CompanyId, ur.BranchId });

        b.HasOne(ur => ur.User)
            .WithMany(u => u!.Roles)
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(ur => ur.Role)
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(ur => ur.BranchId).HasDefaultValue(0);
        b.Property(ur => ur.ValidFrom).HasDefaultValueSql("CURRENT_DATE");
    }
}
