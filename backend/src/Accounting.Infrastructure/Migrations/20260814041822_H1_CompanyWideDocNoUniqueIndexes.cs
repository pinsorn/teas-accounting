using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class H1_CompanyWideDocNoUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vendor_invoices_company_id_branch_id_doc_no",
                schema: "purchase",
                table: "vendor_invoices");

            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_company_id_branch_id_doc_no",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ix_tax_adjustment_notes_company_id_branch_id_doc_no",
                schema: "sales",
                table: "tax_adjustment_notes");

            migrationBuilder.DropIndex(
                name: "ix_receipts_company_id_branch_id_doc_no",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ix_payment_vouchers_company_id_branch_id_doc_no",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropIndex(
                name: "ix_fixed_assets_company_id_branch_id_doc_no",
                schema: "fixedasset",
                table: "fixed_assets");

            migrationBuilder.DropIndex(
                name: "ix_expense_claims_company_id_branch_id_doc_no",
                schema: "expense",
                table: "expense_claims");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_invoices_company_id_doc_no",
                schema: "purchase",
                table: "vendor_invoices",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_company_id_doc_no",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_adjustment_notes_company_id_doc_no",
                schema: "sales",
                table: "tax_adjustment_notes",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_company_id_doc_no",
                schema: "sales",
                table: "receipts",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_vouchers_company_id_doc_no",
                schema: "purchase",
                table: "payment_vouchers",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_company_id_doc_no",
                schema: "fixedasset",
                table: "fixed_assets",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_company_id_doc_no",
                schema: "expense",
                table: "expense_claims",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vendor_invoices_company_id_doc_no",
                schema: "purchase",
                table: "vendor_invoices");

            migrationBuilder.DropIndex(
                name: "ix_tax_invoices_company_id_doc_no",
                schema: "sales",
                table: "tax_invoices");

            migrationBuilder.DropIndex(
                name: "ix_tax_adjustment_notes_company_id_doc_no",
                schema: "sales",
                table: "tax_adjustment_notes");

            migrationBuilder.DropIndex(
                name: "ix_receipts_company_id_doc_no",
                schema: "sales",
                table: "receipts");

            migrationBuilder.DropIndex(
                name: "ix_payment_vouchers_company_id_doc_no",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropIndex(
                name: "ix_fixed_assets_company_id_doc_no",
                schema: "fixedasset",
                table: "fixed_assets");

            migrationBuilder.DropIndex(
                name: "ix_expense_claims_company_id_doc_no",
                schema: "expense",
                table: "expense_claims");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_invoices_company_id_branch_id_doc_no",
                schema: "purchase",
                table: "vendor_invoices",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_company_id_branch_id_doc_no",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_adjustment_notes_company_id_branch_id_doc_no",
                schema: "sales",
                table: "tax_adjustment_notes",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_company_id_branch_id_doc_no",
                schema: "sales",
                table: "receipts",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_vouchers_company_id_branch_id_doc_no",
                schema: "purchase",
                table: "payment_vouchers",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_company_id_branch_id_doc_no",
                schema: "fixedasset",
                table: "fixed_assets",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_company_id_branch_id_doc_no",
                schema: "expense",
                table: "expense_claims",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");
        }
    }
}
