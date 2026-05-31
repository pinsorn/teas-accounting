using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payroll");

            migrationBuilder.CreateTable(
                name: "payroll_runs",
                schema: "payroll",
                columns: table => new
                {
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    period_year_month = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    prefix_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "PR"),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    total_gross_taxable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_gross_non_taxable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_pit = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_sso_employee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_sso_employer = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_other_deductions = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_net = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    journal_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    paid_by = table.Column<long>(type: "bigint", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_runs", x => x.payroll_run_id);
                });

            migrationBuilder.CreateTable(
                name: "payslips",
                schema: "payroll",
                columns: table => new
                {
                    payslip_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    employee_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    national_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    address_text = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    bank_account_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    gross_taxable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gross_non_taxable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    pit_withheld = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    sso_employee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    sso_employer = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    other_deductions = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_pay = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ytd_income = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ytd_pit = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payslips", x => x.payslip_id);
                    table.ForeignKey(
                        name: "fk_payslips_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalSchema: "payroll",
                        principalTable: "payroll_runs",
                        principalColumn: "payroll_run_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_company_id_doc_no",
                schema: "payroll",
                table: "payroll_runs",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_company_id_period_year_month",
                schema: "payroll",
                table: "payroll_runs",
                columns: new[] { "company_id", "period_year_month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payslips_employee_id",
                schema: "payroll",
                table: "payslips",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payslips_payroll_run_id_employee_id",
                schema: "payroll",
                table: "payslips",
                columns: new[] { "payroll_run_id", "employee_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payslips",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "payroll_runs",
                schema: "payroll");
        }
    }
}
