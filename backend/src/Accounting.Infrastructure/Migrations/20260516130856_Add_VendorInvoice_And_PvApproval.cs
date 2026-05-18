using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_VendorInvoice_And_PvApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "purchase",
                table: "payment_vouchers",
                type: "timestamp(3) with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "approved_by",
                schema: "purchase",
                table: "payment_vouchers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "vendor_invoice_id",
                schema: "purchase",
                table: "payment_vouchers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "vendor_invoices",
                schema: "purchase",
                columns: table => new
                {
                    vendor_invoice_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    vendor_tax_invoice_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    vendor_tax_invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    vat_claim_period = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<long>(type: "bigint", nullable: false),
                    vendor_tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    vendor_branch_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: true),
                    vendor_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    vendor_address = table.Column<string>(type: "text", nullable: true),
                    vendor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false, defaultValue: 1m),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    non_recoverable_vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount_thb = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    settled_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    settlement_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "UNPAID"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_invoices", x => x.vendor_invoice_id);
                    table.CheckConstraint("ck_vi_settled", "settled_amount >= 0 AND settled_amount <= total_amount + 0.01");
                });

            migrationBuilder.CreateTable(
                name: "payment_voucher_applications",
                schema: "purchase",
                columns: table => new
                {
                    application_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    payment_voucher_id = table.Column<long>(type: "bigint", nullable: false),
                    vendor_invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    applied_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_voucher_applications", x => x.application_id);
                    table.ForeignKey(
                        name: "fk_payment_voucher_applications_payment_vouchers_payment_vouch",
                        column: x => x.payment_voucher_id,
                        principalSchema: "purchase",
                        principalTable: "payment_vouchers",
                        principalColumn: "payment_voucher_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_payment_voucher_applications_vendor_invoices_vendor_invoice",
                        column: x => x.vendor_invoice_id,
                        principalSchema: "purchase",
                        principalTable: "vendor_invoices",
                        principalColumn: "vendor_invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vendor_invoice_lines",
                schema: "purchase",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    expense_category_id = table.Column<int>(type: "integer", nullable: false),
                    expense_account_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_recoverable_vat = table.Column<bool>(type: "boolean", nullable: false),
                    is_capex = table.Column<bool>(type: "boolean", nullable: false),
                    is_cogs = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_invoice_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_vendor_invoice_lines_vendor_invoices_vendor_invoice_id",
                        column: x => x.vendor_invoice_id,
                        principalSchema: "purchase",
                        principalTable: "vendor_invoices",
                        principalColumn: "vendor_invoice_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_vouchers_vendor_invoice_id",
                schema: "purchase",
                table: "payment_vouchers",
                column: "vendor_invoice_id",
                filter: "vendor_invoice_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pv_sod",
                schema: "purchase",
                table: "payment_vouchers",
                sql: "approved_by IS NULL OR approved_by <> created_by");

            migrationBuilder.CreateIndex(
                name: "ix_payment_voucher_applications_payment_voucher_id_vendor_invo",
                schema: "purchase",
                table: "payment_voucher_applications",
                columns: new[] { "payment_voucher_id", "vendor_invoice_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_voucher_applications_vendor_invoice_id",
                schema: "purchase",
                table: "payment_voucher_applications",
                column: "vendor_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_invoice_lines_vendor_invoice_id_line_no",
                schema: "purchase",
                table: "vendor_invoice_lines",
                columns: new[] { "vendor_invoice_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_invoices_company_id_branch_id_doc_no",
                schema: "purchase",
                table: "vendor_invoices",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_invoices_company_id_doc_date",
                schema: "purchase",
                table: "vendor_invoices",
                columns: new[] { "company_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_invoices_vat_claim_period",
                schema: "purchase",
                table: "vendor_invoices",
                columns: new[] { "company_id", "vat_claim_period" });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_invoices_vendor_id_doc_date",
                schema: "purchase",
                table: "vendor_invoices",
                columns: new[] { "vendor_id", "doc_date" });

            migrationBuilder.AddForeignKey(
                name: "fk_payment_vouchers_vendor_invoices_vendor_invoice_id",
                schema: "purchase",
                table: "payment_vouchers",
                column: "vendor_invoice_id",
                principalSchema: "purchase",
                principalTable: "vendor_invoices",
                principalColumn: "vendor_invoice_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_vouchers_vendor_invoices_vendor_invoice_id",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropTable(
                name: "payment_voucher_applications",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "vendor_invoice_lines",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "vendor_invoices",
                schema: "purchase");

            migrationBuilder.DropIndex(
                name: "ix_payment_vouchers_vendor_invoice_id",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pv_sod",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropColumn(
                name: "approved_by",
                schema: "purchase",
                table: "payment_vouchers");

            migrationBuilder.DropColumn(
                name: "vendor_invoice_id",
                schema: "purchase",
                table: "payment_vouchers");
        }
    }
}
