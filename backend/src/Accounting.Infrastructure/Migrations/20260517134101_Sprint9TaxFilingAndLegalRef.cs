using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint9TaxFilingAndLegalRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "legal_ref",
                schema: "tax",
                table: "tax_codes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tax_filings",
                schema: "tax",
                columns: table => new
                {
                    filing_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    form_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    finalized_by = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    submission_mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    rd_ack_ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    pdf_storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_filings", x => x.filing_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_filings_company_id_form_type_period",
                schema: "tax",
                table: "tax_filings",
                columns: new[] { "company_id", "form_type", "period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_filings",
                schema: "tax");

            migrationBuilder.DropColumn(
                name: "legal_ref",
                schema: "tax",
                table: "tax_codes");
        }
    }
}
