using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employees",
                schema: "master",
                columns: table => new
                {
                    employee_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title_th = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    first_name_th = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    last_name_th = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    title_en = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    first_name_en = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    last_name_en = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    national_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    address_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    moo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    soi = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    sub_district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: true),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    termination_date = table.Column<DateOnly>(type: "date", nullable: true),
                    base_salary = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    bank_account_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sso_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    sso_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    marital_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    spouse_has_income = table.Column<bool>(type: "boolean", nullable: false),
                    children_count = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.employee_id);
                    table.CheckConstraint("ck_employees_children_nonneg", "children_count >= 0");
                    table.CheckConstraint("ck_employees_marital", "marital_status IN ('SINGLE','MARRIED')");
                    table.CheckConstraint("ck_employees_salary_nonneg", "base_salary >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_employees_company_id_employee_code",
                schema: "master",
                table: "employees",
                columns: new[] { "company_id", "employee_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employees",
                schema: "master");
        }
    }
}
