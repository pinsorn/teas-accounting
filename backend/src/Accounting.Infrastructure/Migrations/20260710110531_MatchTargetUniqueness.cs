using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MatchTargetUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_statement_lines_matched_payment_voucher_id",
                schema: "bank",
                table: "statement_lines",
                column: "matched_payment_voucher_id",
                unique: true,
                filter: "matched_payment_voucher_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_statement_lines_matched_receipt_id",
                schema: "bank",
                table: "statement_lines",
                column: "matched_receipt_id",
                unique: true,
                filter: "matched_receipt_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_statement_lines_matched_payment_voucher_id",
                schema: "bank",
                table: "statement_lines");

            migrationBuilder.DropIndex(
                name: "ix_statement_lines_matched_receipt_id",
                schema: "bank",
                table: "statement_lines");
        }
    }
}
