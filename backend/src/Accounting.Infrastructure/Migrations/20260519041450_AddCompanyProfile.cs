using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_profile",
                schema: "master",
                columns: table => new
                {
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    registration_number = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    registered_address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    registered_address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    registered_subdistrict = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    registered_district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    registered_province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    registered_postal_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: false),
                    vat_registration_date = table.Column<DateOnly>(type: "date", nullable: true),
                    branch_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "00000"),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    website = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bank_account_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_profile", x => x.company_id);
                    table.ForeignKey(
                        name: "fk_company_profile_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "master",
                        principalTable: "companies",
                        principalColumn: "company_id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_profile",
                schema: "master");
        }
    }
}
