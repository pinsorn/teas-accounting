using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "gl");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "sys");

            migrationBuilder.EnsureSchema(
                name: "master");

            migrationBuilder.EnsureSchema(
                name: "purchase");

            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.EnsureSchema(
                name: "tax");

            migrationBuilder.CreateTable(
                name: "accounting_periods",
                schema: "gl",
                columns: table => new
                {
                    period_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    closed_by = table.Column<long>(type: "bigint", nullable: true),
                    close_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounting_periods", x => x.period_id);
                    table.CheckConstraint("ck_period_month", "month BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "activity_log",
                schema: "audit",
                columns: table => new
                {
                    activity_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    activity_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    activity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    entity_id = table.Column<long>(type: "bigint", nullable: true),
                    entity_doc_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    before_value = table.Column<string>(type: "jsonb", nullable: true),
                    after_value = table.Column<string>(type: "jsonb", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_log", x => x.activity_id);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "sys",
                columns: table => new
                {
                    api_key_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scopes = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    revoked_by = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.api_key_id);
                });

            migrationBuilder.CreateTable(
                name: "chart_of_accounts",
                schema: "master",
                columns: table => new
                {
                    account_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    account_name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    account_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    is_header = table.Column<bool>(type: "boolean", nullable: false),
                    normal_balance = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chart_of_accounts", x => x.account_id);
                    table.CheckConstraint("ck_coa_account_type", "account_type IN ('ASSET','LIABILITY','EQUITY','REVENUE','EXPENSE')");
                    table.CheckConstraint("ck_coa_normal_balance", "normal_balance IN ('DR','CR')");
                    table.ForeignKey(
                        name: "fk_chart_of_accounts_chart_of_accounts_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "master",
                        principalTable: "chart_of_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "master",
                columns: table => new
                {
                    company_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    legal_entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    registration_date = table.Column<DateOnly>(type: "date", nullable: true),
                    vat_registered = table.Column<bool>(type: "boolean", nullable: false),
                    vat_register_date = table.Column<DateOnly>(type: "date", nullable: true),
                    fiscal_year_start_month = table.Column<short>(type: "smallint", nullable: false),
                    base_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    reporting_standard = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "TFRS_NPAE"),
                    address_th = table.Column<string>(type: "text", nullable: true),
                    sub_district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.company_id);
                    table.CheckConstraint("ck_companies_fiscal_month", "fiscal_year_start_month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_companies_tax_id", "tax_id ~ '^[0-9]{13}$'");
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "master",
                columns: table => new
                {
                    customer_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    customer_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    branch_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: true),
                    branch_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    vat_registered = table.Column<bool>(type: "boolean", nullable: false),
                    billing_address = table.Column<string>(type: "text", nullable: true),
                    contact_person = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    credit_limit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    payment_term_days = table.Column<int>(type: "integer", nullable: false),
                    default_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.customer_id);
                    table.CheckConstraint("ck_customers_type", "customer_type IN ('INDIVIDUAL','CORPORATE')");
                });

            migrationBuilder.CreateTable(
                name: "document_prefixes",
                schema: "sys",
                columns: table => new
                {
                    prefix_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    prefix_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    requires_etax = table.Column<bool>(type: "boolean", nullable: false),
                    is_fiscal_doc = table.Column<bool>(type: "boolean", nullable: false),
                    is_expense = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_prefixes", x => x.prefix_id);
                });

            migrationBuilder.CreateTable(
                name: "expense_categories",
                schema: "sys",
                columns: table => new
                {
                    category_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    category_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    default_expense_account_id = table.Column<long>(type: "bigint", nullable: true),
                    default_tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    default_is_recoverable_vat = table.Column<bool>(type: "boolean", nullable: false),
                    default_wht_type_id = table.Column<int>(type: "integer", nullable: true),
                    is_capex = table.Column<bool>(type: "boolean", nullable: false),
                    is_cogs = table.Column<bool>(type: "boolean", nullable: false),
                    parent_category_id = table.Column<int>(type: "integer", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.category_id);
                    table.ForeignKey(
                        name: "fk_expense_categories_expense_categories_parent_category_id",
                        column: x => x.parent_category_id,
                        principalSchema: "sys",
                        principalTable: "expense_categories",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                schema: "gl",
                columns: table => new
                {
                    journal_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    prefix_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false, defaultValue: 1m),
                    total_debit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_credit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    reversal_of_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.journal_id);
                    table.ForeignKey(
                        name: "fk_journal_entries_journal_entries_reversal_of_id",
                        column: x => x.reversal_of_id,
                        principalSchema: "gl",
                        principalTable: "journal_entries",
                        principalColumn: "journal_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "sys",
                columns: table => new
                {
                    sequence_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    prefix_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sub_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    period_year = table.Column<int>(type: "integer", nullable: false),
                    period_month = table.Column<short>(type: "smallint", nullable: false),
                    current_value = table.Column<int>(type: "integer", nullable: false),
                    last_issued_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => x.sequence_id);
                    table.CheckConstraint("ck_number_sequences_month", "period_month BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "payment_vouchers",
                schema: "purchase",
                columns: table => new
                {
                    payment_voucher_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    prefix_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "PV"),
                    sub_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    vendor_id = table.Column<long>(type: "bigint", nullable: false),
                    expense_category_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    vendor_branch_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: true),
                    vendor_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    vendor_address = table.Column<string>(type: "text", nullable: true),
                    vendor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cheque_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cheque_date = table.Column<DateOnly>(type: "date", nullable: true),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false, defaultValue: 1m),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    wht_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_paid = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount_thb = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_vouchers", x => x.payment_voucher_id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "sys",
                columns: table => new
                {
                    permission_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    permission_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.permission_id);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                schema: "sales",
                columns: table => new
                {
                    receipt_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    customer_address = table.Column<string>(type: "text", nullable: false),
                    customer_tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cheque_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cheque_date = table.Column<DateOnly>(type: "date", nullable: true),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: true),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false, defaultValue: 1m),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount_thb = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipts", x => x.receipt_id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "sys",
                columns: table => new
                {
                    role_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    role_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.role_id);
                });

            migrationBuilder.CreateTable(
                name: "tax_codes",
                schema: "tax",
                columns: table => new
                {
                    tax_code_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    tax_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_recoverable = table.Column<bool>(type: "boolean", nullable: false),
                    is_exempt = table.Column<bool>(type: "boolean", nullable: false),
                    is_zero_rated = table.Column<bool>(type: "boolean", nullable: false),
                    is_reverse_charge = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_codes", x => x.tax_code_id);
                });

            migrationBuilder.CreateTable(
                name: "tax_invoices",
                schema: "sales",
                columns: table => new
                {
                    tax_invoice_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    book_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    tax_point_date = table.Column<DateOnly>(type: "date", nullable: false),
                    tax_point_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    invoice_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "FULL"),
                    is_substitute = table.Column<bool>(type: "boolean", nullable: false),
                    original_invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    supplier_tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    supplier_branch_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: false),
                    supplier_branch_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    supplier_address = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    customer_branch_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: true),
                    customer_branch_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    customer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    customer_address = table.Column<string>(type: "text", nullable: false),
                    customer_vat_registered = table.Column<bool>(type: "boolean", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false, defaultValue: 1m),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    non_taxable_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount_thb = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount_in_words_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_tax_inclusive = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    payment_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "UNPAID"),
                    amount_paid = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_e_tax = table.Column<bool>(type: "boolean", nullable: false),
                    e_tax_xml_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    e_tax_pdf_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    e_tax_signed_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    e_tax_submitted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    e_tax_ack_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    e_tax_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    delivered_to_customer = table.Column<bool>(type: "boolean", nullable: false),
                    delivered_to_customer_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    delivery_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    payment_terms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_invoices", x => x.tax_invoice_id);
                    table.CheckConstraint("ck_ti_invoice_type", "invoice_type = 'FULL'");
                    table.CheckConstraint("ck_ti_tax_point", "doc_date = tax_point_date");
                    table.ForeignKey(
                        name: "fk_tax_invoices_tax_invoices_original_invoice_id",
                        column: x => x.original_invoice_id,
                        principalSchema: "sales",
                        principalTable: "tax_invoices",
                        principalColumn: "tax_invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "sys",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    mfa_secret_enc = table.Column<byte[]>(type: "bytea", nullable: true),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cpd_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_super_admin = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "vendors",
                schema: "master",
                columns: table => new
                {
                    vendor_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    vendor_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    vendor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    branch_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: true),
                    branch_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    vat_registered = table.Column<bool>(type: "boolean", nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    contact_person = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    payment_term_days = table.Column<int>(type: "integer", nullable: false),
                    default_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    default_wht_type_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendors", x => x.vendor_id);
                    table.CheckConstraint("ck_vendors_type", "vendor_type IN ('INDIVIDUAL','CORPORATE')");
                });

            migrationBuilder.CreateTable(
                name: "wht_certificates",
                schema: "tax",
                columns: table => new
                {
                    wht_certificate_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cert_date = table.Column<DateOnly>(type: "date", nullable: false),
                    payment_voucher_id = table.Column<long>(type: "bigint", nullable: false),
                    payer_tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    payer_branch_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: false),
                    payer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    payer_address = table.Column<string>(type: "text", nullable: false),
                    payee_tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    payee_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    payee_address = table.Column<string>(type: "text", nullable: false),
                    payee_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    form_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    income_type_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    income_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    income_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    wht_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    wht_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    issued_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wht_certificates", x => x.wht_certificate_id);
                });

            migrationBuilder.CreateTable(
                name: "wht_types",
                schema: "tax",
                columns: table => new
                {
                    wht_type_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    income_type_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    form_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    default_payable_account_id = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wht_types", x => x.wht_type_id);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "master",
                columns: table => new
                {
                    branch_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: false),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_head_office = table.Column<bool>(type: "boolean", nullable: false),
                    address_th = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.branch_id);
                    table.CheckConstraint("ck_branches_code", "branch_code ~ '^[0-9]{5}$'");
                    table.ForeignKey(
                        name: "fk_branches_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "master",
                        principalTable: "companies",
                        principalColumn: "company_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                schema: "gl",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    journal_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    debit_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    credit_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    dimensions = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_lines", x => x.line_id);
                    table.CheckConstraint("ck_journal_lines_amount_sign", "(debit_amount > 0 AND credit_amount = 0) OR (credit_amount > 0 AND debit_amount = 0)");
                    table.ForeignKey(
                        name: "fk_journal_lines_journal_entries_journal_id",
                        column: x => x.journal_id,
                        principalSchema: "gl",
                        principalTable: "journal_entries",
                        principalColumn: "journal_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_voucher_lines",
                schema: "purchase",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    payment_voucher_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    expense_account_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_recoverable_vat = table.Column<bool>(type: "boolean", nullable: false),
                    wht_type_id = table.Column<int>(type: "integer", nullable: true),
                    wht_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    wht_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_voucher_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_payment_voucher_lines_payment_vouchers_payment_voucher_id",
                        column: x => x.payment_voucher_id,
                        principalSchema: "purchase",
                        principalTable: "payment_vouchers",
                        principalColumn: "payment_voucher_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_applications",
                schema: "sales",
                columns: table => new
                {
                    application_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    receipt_id = table.Column<long>(type: "bigint", nullable: false),
                    tax_invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    applied_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_applications", x => x.application_id);
                    table.ForeignKey(
                        name: "fk_receipt_applications_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "sales",
                        principalTable: "receipts",
                        principalColumn: "receipt_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "sys",
                columns: table => new
                {
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    permission_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "sys",
                        principalTable: "permissions",
                        principalColumn: "permission_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "sys",
                        principalTable: "roles",
                        principalColumn: "role_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_rates",
                schema: "tax",
                columns: table => new
                {
                    tax_rate_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tax_code_id = table.Column<int>(type: "integer", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_rates", x => x.tax_rate_id);
                    table.ForeignKey(
                        name: "fk_tax_rates_tax_codes_tax_code_id",
                        column: x => x.tax_code_id,
                        principalSchema: "tax",
                        principalTable: "tax_codes",
                        principalColumn: "tax_code_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_adjustment_notes",
                schema: "sales",
                columns: table => new
                {
                    note_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    prefix_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    tax_point_date = table.Column<DateOnly>(type: "date", nullable: false),
                    original_tax_invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    customer_branch_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: true),
                    customer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    customer_address = table.Column<string>(type: "text", nullable: false),
                    customer_vat_registered = table.Column<bool>(type: "boolean", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,8)", precision: 19, scale: 8, nullable: false, defaultValue: 1m),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount_thb = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_adjustment_notes", x => x.note_id);
                    table.CheckConstraint("ck_note_tax_point", "doc_date = tax_point_date");
                    table.CheckConstraint("ck_note_type", "note_type IN ('CREDIT','DEBIT')");
                    table.ForeignKey(
                        name: "fk_tax_adjustment_notes_tax_invoices_original_tax_invoice_id",
                        column: x => x.original_tax_invoice_id,
                        principalSchema: "sales",
                        principalTable: "tax_invoices",
                        principalColumn: "tax_invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_invoice_lines",
                schema: "sales",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tax_invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uom_id = table.Column<int>(type: "integer", nullable: false),
                    uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    line_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_code_id = table.Column<int>(type: "integer", nullable: false),
                    tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_invoice_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_tax_invoice_lines_tax_invoices_tax_invoice_id",
                        column: x => x.tax_invoice_id,
                        principalSchema: "sales",
                        principalTable: "tax_invoices",
                        principalColumn: "tax_invoice_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "sys",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "CURRENT_DATE"),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id, x.company_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "sys",
                        principalTable: "roles",
                        principalColumn: "role_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "sys",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounting_periods_company_id_year_month",
                schema: "gl",
                table: "accounting_periods",
                columns: new[] { "company_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_entity",
                schema: "audit",
                table: "activity_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_user_time",
                schema: "audit",
                table: "activity_log",
                columns: new[] { "user_id", "activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_company_id",
                schema: "sys",
                table: "api_keys",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_key_hash",
                schema: "sys",
                table: "api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branches_company_id_branch_code",
                schema: "master",
                table: "branches",
                columns: new[] { "company_id", "branch_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chart_of_accounts_company_id_account_code",
                schema: "master",
                table: "chart_of_accounts",
                columns: new[] { "company_id", "account_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chart_of_accounts_parent_id",
                schema: "master",
                table: "chart_of_accounts",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_tax_id",
                schema: "master",
                table: "companies",
                column: "tax_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_company_id_customer_code",
                schema: "master",
                table: "customers",
                columns: new[] { "company_id", "customer_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_prefixes_prefix_code",
                schema: "sys",
                table: "document_prefixes",
                column: "prefix_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_company_id_category_code",
                schema: "sys",
                table: "expense_categories",
                columns: new[] { "company_id", "category_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_parent_category_id",
                schema: "sys",
                table: "expense_categories",
                column: "parent_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_company_id_doc_date",
                schema: "gl",
                table: "journal_entries",
                columns: new[] { "company_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_company_id_doc_no",
                schema: "gl",
                table: "journal_entries",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_company_id_status_doc_date",
                schema: "gl",
                table: "journal_entries",
                columns: new[] { "company_id", "status", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_reversal_of_id",
                schema: "gl",
                table: "journal_entries",
                column: "reversal_of_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_account_id",
                schema: "gl",
                table: "journal_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_journal_id_line_no",
                schema: "gl",
                table: "journal_lines",
                columns: new[] { "journal_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_number_sequences_period",
                schema: "sys",
                table: "number_sequences",
                columns: new[] { "company_id", "branch_id", "prefix_code", "sub_prefix", "period_year", "period_month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_voucher_lines_payment_voucher_id_line_no",
                schema: "purchase",
                table: "payment_voucher_lines",
                columns: new[] { "payment_voucher_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_vouchers_company_id_branch_id_doc_no",
                schema: "purchase",
                table: "payment_vouchers",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_vouchers_company_id_doc_date",
                schema: "purchase",
                table: "payment_vouchers",
                columns: new[] { "company_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_vouchers_vendor_id_doc_date",
                schema: "purchase",
                table: "payment_vouchers",
                columns: new[] { "vendor_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_permissions_permission_code",
                schema: "sys",
                table: "permissions",
                column: "permission_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_receipt_id_tax_invoice_id",
                schema: "sales",
                table: "receipt_applications",
                columns: new[] { "receipt_id", "tax_invoice_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_tax_invoice_id",
                schema: "sales",
                table: "receipt_applications",
                column: "tax_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_company_id_branch_id_doc_no",
                schema: "sales",
                table: "receipts",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_customer_id_doc_date",
                schema: "sales",
                table: "receipts",
                columns: new[] { "customer_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                schema: "sys",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_role_code",
                schema: "sys",
                table: "roles",
                column: "role_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_adjustment_notes_company_id_branch_id_doc_no",
                schema: "sales",
                table: "tax_adjustment_notes",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_adjustment_notes_original_tax_invoice_id",
                schema: "sales",
                table: "tax_adjustment_notes",
                column: "original_tax_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_codes_company_id_code",
                schema: "tax",
                table: "tax_codes",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoice_lines_tax_invoice_id_line_no",
                schema: "sales",
                table: "tax_invoice_lines",
                columns: new[] { "tax_invoice_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_company_id_branch_id_doc_no",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_company_id_doc_date",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "company_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_customer_id_doc_date",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "customer_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_original_invoice_id",
                schema: "sales",
                table: "tax_invoices",
                column: "original_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_status_doc_date",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "status", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_rates_tax_code_id_effective_from",
                schema: "tax",
                table: "tax_rates",
                columns: new[] { "tax_code_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "sys",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "sys",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                schema: "sys",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendors_company_id_vendor_code",
                schema: "master",
                table: "vendors",
                columns: new[] { "company_id", "vendor_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wht_certificates_company_id_doc_no",
                schema: "tax",
                table: "wht_certificates",
                columns: new[] { "company_id", "doc_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wht_certificates_payment_voucher_id",
                schema: "tax",
                table: "wht_certificates",
                column: "payment_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_wht_types_company_id_code",
                schema: "tax",
                table: "wht_types",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_periods",
                schema: "gl");

            migrationBuilder.DropTable(
                name: "activity_log",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "master");

            migrationBuilder.DropTable(
                name: "chart_of_accounts",
                schema: "master");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "master");

            migrationBuilder.DropTable(
                name: "document_prefixes",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "expense_categories",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "journal_lines",
                schema: "gl");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "payment_voucher_lines",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "receipt_applications",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "tax_adjustment_notes",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "tax_invoice_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "tax_rates",
                schema: "tax");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "vendors",
                schema: "master");

            migrationBuilder.DropTable(
                name: "wht_certificates",
                schema: "tax");

            migrationBuilder.DropTable(
                name: "wht_types",
                schema: "tax");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "master");

            migrationBuilder.DropTable(
                name: "journal_entries",
                schema: "gl");

            migrationBuilder.DropTable(
                name: "payment_vouchers",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "receipts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "tax_invoices",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "tax_codes",
                schema: "tax");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "users",
                schema: "sys");
        }
    }
}
