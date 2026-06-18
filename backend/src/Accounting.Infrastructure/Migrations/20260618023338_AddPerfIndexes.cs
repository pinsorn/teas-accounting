using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerfIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_status_doc_date",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id_company_id",
                schema: "sys",
                table: "user_roles",
                columns: new[] { "user_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_company_id_status_doc_date",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "company_id", "status", "doc_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_roles_user_id_company_id",
                schema: "sys",
                table: "user_roles");

            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_company_id_status_doc_date",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_status_doc_date",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "status", "doc_date" });
        }
    }
}
