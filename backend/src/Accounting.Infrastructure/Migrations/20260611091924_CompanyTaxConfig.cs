using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompanyTaxConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pnd30_submission_mode",
                schema: "master",
                table: "companies",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<decimal>(
                name: "vat_rate",
                schema: "master",
                table: "companies",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.07m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_companies_pnd30_submission_mode",
                schema: "master",
                table: "companies",
                sql: "pnd30_submission_mode IN ('manual','auto')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_companies_vat_rate",
                schema: "master",
                table: "companies",
                sql: "vat_rate >= 0 AND vat_rate <= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_companies_pnd30_submission_mode",
                schema: "master",
                table: "companies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_companies_vat_rate",
                schema: "master",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "pnd30_submission_mode",
                schema: "master",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "vat_rate",
                schema: "master",
                table: "companies");
        }
    }
}
