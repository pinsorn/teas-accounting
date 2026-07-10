using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "expense");

            migrationBuilder.CreateTable(
                name: "expense_claims",
                schema: "expense",
                columns: table => new
                {
                    expense_claim_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    prefix_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "EX"),
                    sub_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    claim_date = table.Column<DateOnly>(type: "date", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bank_account_id = table.Column<int>(type: "integer", nullable: true),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    paid_by = table.Column<long>(type: "bigint", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_claims", x => x.expense_claim_id);
                    table.ForeignKey(
                        name: "fk_expense_claims_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "bank",
                        principalTable: "bank_accounts",
                        principalColumn: "bank_account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_claims_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalSchema: "master",
                        principalTable: "business_units",
                        principalColumn: "business_unit_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_claims_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "master",
                        principalTable: "employees",
                        principalColumn: "employee_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_claims_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalSchema: "gl",
                        principalTable: "journal_entries",
                        principalColumn: "journal_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_claim_lines",
                schema: "expense",
                columns: table => new
                {
                    expense_claim_line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    expense_claim_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    expense_category_id = table.Column<int>(type: "integer", nullable: false),
                    expense_account_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_recoverable_vat = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    line_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_claim_lines", x => x.expense_claim_line_id);
                    table.ForeignKey(
                        name: "fk_expense_claim_lines_expense_categories_expense_category_id",
                        column: x => x.expense_category_id,
                        principalSchema: "sys",
                        principalTable: "expense_categories",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_claim_lines_expense_claims_expense_claim_id",
                        column: x => x.expense_claim_id,
                        principalSchema: "expense",
                        principalTable: "expense_claims",
                        principalColumn: "expense_claim_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expense_claim_lines_expense_category_id",
                schema: "expense",
                table: "expense_claim_lines",
                column: "expense_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_claim_lines_expense_claim_id_line_no",
                schema: "expense",
                table: "expense_claim_lines",
                columns: new[] { "expense_claim_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_bank_account_id",
                schema: "expense",
                table: "expense_claims",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_business_unit_id",
                schema: "expense",
                table: "expense_claims",
                column: "business_unit_id",
                filter: "business_unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_company_id_branch_id_doc_no",
                schema: "expense",
                table: "expense_claims",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_company_id_claim_date",
                schema: "expense",
                table: "expense_claims",
                columns: new[] { "company_id", "claim_date" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_employee_id_claim_date",
                schema: "expense",
                table: "expense_claims",
                columns: new[] { "employee_id", "claim_date" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_claims_journal_entry_id",
                schema: "expense",
                table: "expense_claims",
                column: "journal_entry_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_claim_lines",
                schema: "expense");

            migrationBuilder.DropTable(
                name: "expense_claims",
                schema: "expense");
        }
    }
}
