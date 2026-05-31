using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Accounting.Infrastructure.Persistence.Configurations.Master;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("employees", "master");
        b.HasKey(e => e.EmployeeId);

        b.Property(e => e.EmployeeCode).HasMaxLength(50).IsRequired();

        b.Property(e => e.TitleTh).HasMaxLength(50);
        b.Property(e => e.FirstNameTh).HasMaxLength(150).IsRequired();
        b.Property(e => e.LastNameTh).HasMaxLength(150).IsRequired();
        b.Property(e => e.TitleEn).HasMaxLength(50);
        b.Property(e => e.FirstNameEn).HasMaxLength(150);
        b.Property(e => e.LastNameEn).HasMaxLength(150);

        b.Property(e => e.NationalId).IsFixedLength().HasMaxLength(13).IsRequired();
        b.Property(e => e.TaxId).IsFixedLength().HasMaxLength(13);

        b.Property(e => e.AddressNo).HasMaxLength(50);
        b.Property(e => e.Moo).HasMaxLength(50);
        b.Property(e => e.Soi).HasMaxLength(150);
        b.Property(e => e.Street).HasMaxLength(150);
        b.Property(e => e.SubDistrict).HasMaxLength(100);
        b.Property(e => e.District).HasMaxLength(100);
        b.Property(e => e.Province).HasMaxLength(100);
        b.Property(e => e.PostalCode).IsFixedLength().HasMaxLength(5);

        b.Property(e => e.BaseSalary).HasColumnType("numeric(18,4)");

        b.Property(e => e.BankName).HasMaxLength(120);
        b.Property(e => e.BankAccountNo).HasMaxLength(50);
        b.Property(e => e.BankAccountName).HasMaxLength(255);

        b.Property(e => e.SsoNumber).HasMaxLength(20);

        b.Property(e => e.MaritalStatus)
            .HasConversion(
                x => x.ToString().ToUpperInvariant(),
                x => Enum.Parse<MaritalStatus>(x, ignoreCase: true))
            .HasMaxLength(10);

        b.Property(e => e.CreatedAt).HasColumnType("timestamptz(3)");

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_employees_marital", "marital_status IN ('SINGLE','MARRIED')");
            t.HasCheckConstraint("ck_employees_salary_nonneg", "base_salary >= 0");
            t.HasCheckConstraint("ck_employees_children_nonneg", "children_count >= 0");
        });

        b.HasIndex(e => new { e.CompanyId, e.EmployeeCode }).IsUnique();
    }
}
