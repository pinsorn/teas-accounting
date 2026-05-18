using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ArWhtCertReceivableDocNoFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wht_certificates_company_id_doc_no",
                schema: "tax",
                table: "wht_certificates");

            migrationBuilder.CreateIndex(
                name: "ix_wht_certificates_company_id_doc_no",
                schema: "tax",
                table: "wht_certificates",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "direction = 'P'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wht_certificates_company_id_doc_no",
                schema: "tax",
                table: "wht_certificates");

            migrationBuilder.CreateIndex(
                name: "ix_wht_certificates_company_id_doc_no",
                schema: "tax",
                table: "wht_certificates",
                columns: new[] { "company_id", "doc_no" },
                unique: true);
        }
    }
}
