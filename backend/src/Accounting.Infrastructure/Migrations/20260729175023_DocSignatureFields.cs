using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocSignatureFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "position",
                schema: "sys",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sent_by",
                schema: "sales",
                table: "quotations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_doc_notes",
                schema: "master",
                table: "company_profile",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "issued_by",
                schema: "sales",
                table: "billing_notes",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "position",
                schema: "sys",
                table: "users");

            migrationBuilder.DropColumn(
                name: "sent_by",
                schema: "sales",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "default_doc_notes",
                schema: "master",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "issued_by",
                schema: "sales",
                table: "billing_notes");
        }
    }
}
