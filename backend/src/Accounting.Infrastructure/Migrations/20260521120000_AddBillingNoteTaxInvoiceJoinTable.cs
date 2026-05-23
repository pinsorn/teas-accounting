using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Accounting.Infrastructure.Persistence;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <summary>
    /// Sprint 13i C7 — replace BillingNote.tax_invoice_ids (bigint[]) with a dedicated
    /// sales.billing_note_tax_invoices join table carrying applied_amount. RLS for the
    /// new table is applied via SqlScript 323_billing_note_tax_invoices_rls.sql.
    /// </summary>
    [DbContext(typeof(AccountingDbContext))]
    [Migration("20260521120000_AddBillingNoteTaxInvoiceJoinTable")]
    public partial class AddBillingNoteTaxInvoiceJoinTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tax_invoice_ids",
                schema: "sales",
                table: "billing_notes");

            migrationBuilder.CreateTable(
                name: "billing_note_tax_invoices",
                schema: "sales",
                columns: table => new
                {
                    billing_note_id = table.Column<long>(type: "bigint", nullable: false),
                    tax_invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    applied_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_note_tax_invoices", x => new { x.billing_note_id, x.tax_invoice_id });
                    table.ForeignKey(
                        name: "fk_billing_note_tax_invoices_billing_notes_billing_note_id",
                        column: x => x.billing_note_id,
                        principalSchema: "sales",
                        principalTable: "billing_notes",
                        principalColumn: "billing_note_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_billing_note_tax_invoices_tax_invoices_tax_invoice_id",
                        column: x => x.tax_invoice_id,
                        principalSchema: "sales",
                        principalTable: "tax_invoices",
                        principalColumn: "tax_invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_billing_note_tax_invoices_tax_invoice_id",
                schema: "sales",
                table: "billing_note_tax_invoices",
                column: "tax_invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_note_tax_invoices",
                schema: "sales");

            migrationBuilder.AddColumn<long[]>(
                name: "tax_invoice_ids",
                schema: "sales",
                table: "billing_notes",
                type: "bigint[]",
                nullable: true);
        }
    }
}
