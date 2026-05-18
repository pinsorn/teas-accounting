using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddETaxSubmissionsAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "etax");

            migrationBuilder.CreateTable(
                name: "submissions",
                schema: "etax",
                columns: table => new
                {
                    submission_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    tax_invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    attempt_no = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xml_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    signed_xml_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    pdf_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    email_message_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    to_email_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    cc_email_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    redirect_applied = table.Column<bool>(type: "boolean", nullable: false),
                    intended_to_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    smtp_response = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    rd_ack_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rd_rejection_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    retry_after = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    dead_letter = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submissions", x => x.submission_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_etax_sub_dead",
                schema: "etax",
                table: "submissions",
                columns: new[] { "company_id", "dead_letter", "attempted_at" },
                descending: new[] { false, false, true },
                filter: "dead_letter = true");

            migrationBuilder.CreateIndex(
                name: "ix_etax_sub_invoice",
                schema: "etax",
                table: "submissions",
                columns: new[] { "company_id", "tax_invoice_id", "attempted_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_etax_sub_outcome",
                schema: "etax",
                table: "submissions",
                columns: new[] { "company_id", "outcome", "attempted_at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "submissions",
                schema: "etax");
        }
    }
}
