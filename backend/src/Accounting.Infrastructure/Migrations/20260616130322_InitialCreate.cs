using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounting.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
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
                name: "sales");

            migrationBuilder.EnsureSchema(
                name: "master");

            migrationBuilder.EnsureSchema(
                name: "tax");

            migrationBuilder.EnsureSchema(
                name: "purchase");

            migrationBuilder.EnsureSchema(
                name: "payroll");

            migrationBuilder.EnsureSchema(
                name: "etax");

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
                name: "attachments",
                schema: "sys",
                columns: table => new
                {
                    attachment_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    parent_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    uploaded_by = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.attachment_id);
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
                name: "cit_adjustments",
                schema: "tax",
                columns: table => new
                {
                    cit_adjustment_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    legal_ref_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cit_adjustments", x => x.cit_adjustment_id);
                });

            migrationBuilder.CreateTable(
                name: "cit_year_summaries",
                schema: "tax",
                columns: table => new
                {
                    cit_year_summary_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    computed_net_profit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    override_net_profit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    pnd51estimated_profit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    pnd51prepaid = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cit_year_summaries", x => x.cit_year_summary_id);
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
                    vat_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false, defaultValue: 0.07m),
                    pnd30_submission_mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "manual"),
                    fiscal_year_start_month = table.Column<short>(type: "smallint", nullable: false),
                    base_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "THB"),
                    reporting_standard = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "TFRS_NPAE"),
                    paid_up_capital = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    address_th = table.Column<string>(type: "text", nullable: true),
                    sub_district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    requires_business_unit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.company_id);
                    table.CheckConstraint("ck_companies_fiscal_month", "fiscal_year_start_month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_companies_pnd30_submission_mode", "pnd30_submission_mode IN ('manual','auto')");
                    table.CheckConstraint("ck_companies_tax_id", "tax_id ~ '^[0-9]{13}$'");
                    table.CheckConstraint("ck_companies_vat_rate", "vat_rate >= 0 AND vat_rate <= 1");
                });

            migrationBuilder.CreateTable(
                name: "delivery_orders",
                schema: "sales",
                columns: table => new
                {
                    delivery_order_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    customer_address = table.Column<string>(type: "text", nullable: true),
                    customer_tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    customer_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    sales_order_id = table.Column<long>(type: "bigint", nullable: true),
                    is_combined_with_ti = table.Column<bool>(type: "boolean", nullable: false),
                    tax_invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    cancelled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    original_printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_orders", x => x.delivery_order_id);
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
                name: "employees",
                schema: "master",
                columns: table => new
                {
                    employee_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title_th = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    first_name_th = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    last_name_th = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    title_en = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    first_name_en = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    last_name_en = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    national_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    tax_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    address_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    moo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    soi = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    sub_district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character(5)", fixedLength: true, maxLength: 5, nullable: true),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    termination_date = table.Column<DateOnly>(type: "date", nullable: true),
                    base_salary = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    bank_account_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sso_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    sso_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    marital_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    spouse_has_income = table.Column<bool>(type: "boolean", nullable: false),
                    children_count = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.employee_id);
                    table.CheckConstraint("ck_employees_children_nonneg", "children_count >= 0");
                    table.CheckConstraint("ck_employees_marital", "marital_status IN ('SINGLE','MARRIED')");
                    table.CheckConstraint("ck_employees_salary_nonneg", "base_salary >= 0");
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
                name: "idempotency_keys",
                schema: "sys",
                columns: table => new
                {
                    idempotency_key_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    api_key_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => x.idempotency_key_id);
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
                    sub_prefix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: ""),
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
                name: "payroll_runs",
                schema: "payroll",
                columns: table => new
                {
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    period_year_month = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    prefix_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "PR"),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    total_gross_taxable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_gross_non_taxable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_pit = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_sso_employee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_sso_employer = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_other_deductions = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_net = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    journal_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    paid_by = table.Column<long>(type: "bigint", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_runs", x => x.payroll_run_id);
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
                name: "purchase_orders",
                schema: "purchase",
                columns: table => new
                {
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_delivery_date = table.Column<DateOnly>(type: "date", nullable: true),
                    vendor_id = table.Column<long>(type: "bigint", nullable: false),
                    vendor_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    vendor_address = table.Column<string>(type: "text", nullable: true),
                    vendor_tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    vendor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount_thb = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    internal_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    sent_to_vendor_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    original_printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.purchase_order_id);
                });

            migrationBuilder.CreateTable(
                name: "quotations",
                schema: "sales",
                columns: table => new
                {
                    quotation_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_until_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    customer_address = table.Column<string>(type: "text", nullable: true),
                    customer_tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    customer_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    internal_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    show_wht_note = table.Column<bool>(type: "boolean", nullable: false),
                    converted_to_so_id = table.Column<long>(type: "bigint", nullable: true),
                    rejected_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    original_printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotations", x => x.quotation_id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "sys",
                columns: table => new
                {
                    role_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: true),
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
                name: "sales_orders",
                schema: "sales",
                columns: table => new
                {
                    sales_order_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_delivery_date = table.Column<DateOnly>(type: "date", nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    customer_address = table.Column<string>(type: "text", nullable: true),
                    customer_tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    customer_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    quotation_id = table.Column<long>(type: "bigint", nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    cancelled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    original_printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_orders", x => x.sales_order_id);
                });

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
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    legal_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_codes", x => x.tax_code_id);
                });

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
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    bank_account_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    swift_code = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    default_wht_type_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_foreign = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    has_thai_vat_d_reg = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendors", x => x.vendor_id);
                    table.CheckConstraint("ck_vendors_foreign_vatreg", "is_foreign IS NOT TRUE OR vat_registered IS TRUE");
                    table.CheckConstraint("ck_vendors_type", "vendor_type IN ('INDIVIDUAL','CORPORATE')");
                    table.CheckConstraint("ck_vendors_vatd_foreign", "has_thai_vat_d_reg IS NOT TRUE OR is_foreign IS TRUE");
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
                    direction = table.Column<string>(type: "character(1)", fixedLength: true, maxLength: 1, nullable: false, defaultValue: "P"),
                    payment_voucher_id = table.Column<long>(type: "bigint", nullable: true),
                    receipt_id = table.Column<long>(type: "bigint", nullable: true),
                    cert_received_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    reconciled_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
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
                    wht_condition = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    issued_by = table.Column<long>(type: "bigint", nullable: true),
                    pdf_storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wht_certificates", x => x.wht_certificate_id);
                    table.CheckConstraint("ck_wht_certificates_condition", "wht_condition IN (1, 2, 3)");
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
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false, defaultValue: new DateOnly(2020, 1, 1)),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    default_payable_account_id = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wht_types", x => x.wht_type_id);
                });

            migrationBuilder.CreateTable(
                name: "business_units",
                schema: "master",
                columns: table => new
                {
                    business_unit_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    default_revenue_account_id = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_units", x => x.business_unit_id);
                    table.ForeignKey(
                        name: "fk_business_units_chart_of_accounts_default_revenue_account_id",
                        column: x => x.default_revenue_account_id,
                        principalSchema: "master",
                        principalTable: "chart_of_accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
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
                    reg_building = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    reg_room_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reg_floor = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reg_village = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    reg_house_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reg_moo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reg_soi = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    reg_street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
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
                    sso_employer_account_no = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
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

            migrationBuilder.CreateTable(
                name: "payslips",
                schema: "payroll",
                columns: table => new
                {
                    payslip_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    employee_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    national_id = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    address_text = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    bank_account_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    gross_taxable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gross_non_taxable = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    pit_withheld = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    sso_employee = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    sso_employer = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    other_deductions = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_pay = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ytd_income = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ytd_pit = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payslips", x => x.payslip_id);
                    table.ForeignKey(
                        name: "fk_payslips_payroll_runs_payroll_run_id",
                        column: x => x.payroll_run_id,
                        principalSchema: "payroll",
                        principalTable: "payroll_runs",
                        principalColumn: "payroll_run_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billing_notes",
                schema: "sales",
                columns: table => new
                {
                    billing_note_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    customer_address = table.Column<string>(type: "text", nullable: true),
                    customer_tax_id = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    customer_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    quotation_id = table.Column<long>(type: "bigint", nullable: true),
                    delivery_order_id = table.Column<long>(type: "bigint", nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    internal_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancelled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    original_printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_notes", x => x.billing_note_id);
                    table.ForeignKey(
                        name: "fk_billing_notes_delivery_orders_delivery_order_id",
                        column: x => x.delivery_order_id,
                        principalSchema: "sales",
                        principalTable: "delivery_orders",
                        principalColumn: "delivery_order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_billing_notes_quotations_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "sales",
                        principalTable: "quotations",
                        principalColumn: "quotation_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "sys",
                columns: table => new
                {
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    permission_id = table.Column<int>(type: "integer", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: true)
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
                    default_wht_type_id = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.customer_id);
                    table.CheckConstraint("ck_customers_type", "customer_type IN ('INDIVIDUAL','CORPORATE')");
                    table.ForeignKey(
                        name: "fk_customers_wht_types_default_wht_type_id",
                        column: x => x.default_wht_type_id,
                        principalSchema: "tax",
                        principalTable: "wht_types",
                        principalColumn: "wht_type_id",
                        onDelete: ReferentialAction.Restrict);
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
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    default_business_unit_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.api_key_id);
                    table.ForeignKey(
                        name: "fk_api_keys_business_units_default_business_unit_id",
                        column: x => x.default_business_unit_id,
                        principalSchema: "master",
                        principalTable: "business_units",
                        principalColumn: "business_unit_id",
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
                    dimensions = table.Column<string>(type: "jsonb", nullable: true),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_lines", x => x.line_id);
                    table.CheckConstraint("ck_journal_lines_amount_sign", "(debit_amount > 0 AND credit_amount = 0) OR (credit_amount > 0 AND debit_amount = 0)");
                    table.ForeignKey(
                        name: "fk_journal_lines_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalSchema: "master",
                        principalTable: "business_units",
                        principalColumn: "business_unit_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_lines_journal_entries_journal_id",
                        column: x => x.journal_id,
                        principalSchema: "gl",
                        principalTable: "journal_entries",
                        principalColumn: "journal_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "master",
                columns: table => new
                {
                    product_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_th = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name_en = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_saleable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_purchasable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    default_uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    default_unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    default_output_tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    default_input_tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    default_wht_type_id = table.Column<int>(type: "integer", nullable: true),
                    description_th = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.product_id);
                    table.CheckConstraint("ck_products_purpose", "is_saleable OR is_purchasable");
                    table.CheckConstraint("ck_products_type", "product_type IN ('GOOD','SERVICE','EXEMPT_GOOD','EXEMPT_SERVICE')");
                    table.ForeignKey(
                        name: "fk_products_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalSchema: "master",
                        principalTable: "business_units",
                        principalColumn: "business_unit_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_tax_codes_default_input_tax_code_id",
                        column: x => x.default_input_tax_code_id,
                        principalSchema: "tax",
                        principalTable: "tax_codes",
                        principalColumn: "tax_code_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_tax_codes_default_output_tax_code_id",
                        column: x => x.default_output_tax_code_id,
                        principalSchema: "tax",
                        principalTable: "tax_codes",
                        principalColumn: "tax_code_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_wht_types_default_wht_type_id",
                        column: x => x.default_wht_type_id,
                        principalSchema: "tax",
                        principalTable: "wht_types",
                        principalColumn: "wht_type_id",
                        onDelete: ReferentialAction.Restrict);
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
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
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
                    wht_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    wht_type_id = table.Column<int>(type: "integer", nullable: true),
                    customer_wht_cert_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    customer_wht_cert_date = table.Column<DateOnly>(type: "date", nullable: true),
                    cash_received = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    original_printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipts", x => x.receipt_id);
                    table.CheckConstraint("ck_receipts_wht_nonneg", "wht_amount >= 0");
                    table.ForeignKey(
                        name: "fk_receipts_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalSchema: "master",
                        principalTable: "business_units",
                        principalColumn: "business_unit_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receipts_wht_types_wht_type_id",
                        column: x => x.wht_type_id,
                        principalSchema: "tax",
                        principalTable: "wht_types",
                        principalColumn: "wht_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vendor_invoices",
                schema: "purchase",
                columns: table => new
                {
                    vendor_invoice_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
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
                    has_input_vat = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    requires_pnd36reverse_charge = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: true),
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
                    table.ForeignKey(
                        name: "fk_vendor_invoices_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalSchema: "master",
                        principalTable: "business_units",
                        principalColumn: "business_unit_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vendor_invoices_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "purchase",
                        principalTable: "purchase_orders",
                        principalColumn: "purchase_order_id",
                        onDelete: ReferentialAction.Restrict);
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
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    invoice_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "FULL"),
                    is_substitute = table.Column<bool>(type: "boolean", nullable: false),
                    original_invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    quotation_id = table.Column<long>(type: "bigint", nullable: true),
                    billing_note_id = table.Column<long>(type: "bigint", nullable: true),
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
                    version = table.Column<long>(type: "bigint", nullable: false),
                    original_printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_invoices", x => x.tax_invoice_id);
                    table.CheckConstraint("ck_ti_invoice_type", "invoice_type = 'FULL'");
                    table.CheckConstraint("ck_ti_tax_point", "doc_date = tax_point_date");
                    table.ForeignKey(
                        name: "fk_tax_invoices_billing_notes_billing_note_id",
                        column: x => x.billing_note_id,
                        principalSchema: "sales",
                        principalTable: "billing_notes",
                        principalColumn: "billing_note_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_invoices_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalSchema: "master",
                        principalTable: "business_units",
                        principalColumn: "business_unit_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_invoices_quotations_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "sales",
                        principalTable: "quotations",
                        principalColumn: "quotation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_invoices_tax_invoices_original_invoice_id",
                        column: x => x.original_invoice_id,
                        principalSchema: "sales",
                        principalTable: "tax_invoices",
                        principalColumn: "tax_invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_order_lines",
                schema: "sales",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    delivery_order_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    sales_order_line_id = table.Column<long>(type: "bigint", nullable: true),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
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
                    table.PrimaryKey("pk_delivery_order_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_delivery_order_lines_delivery_orders_delivery_order_id",
                        column: x => x.delivery_order_id,
                        principalSchema: "sales",
                        principalTable: "delivery_orders",
                        principalColumn: "delivery_order_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_delivery_order_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "master",
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "purchase",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    line_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_code_id = table.Column<int>(type: "integer", nullable: true),
                    tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tax_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "master",
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "purchase",
                        principalTable: "purchase_orders",
                        principalColumn: "purchase_order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quotation_lines",
                schema: "sales",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    quotation_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
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
                    table.PrimaryKey("pk_quotation_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_quotation_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "master",
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_quotation_lines_quotations_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "sales",
                        principalTable: "quotations",
                        principalColumn: "quotation_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_lines",
                schema: "sales",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sales_order_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    delivered_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
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
                    table.PrimaryKey("pk_sales_order_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_sales_order_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "master",
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_order_lines_sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalSchema: "sales",
                        principalTable: "sales_orders",
                        principalColumn: "sales_order_id",
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
                    tax_invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    delivery_order_id = table.Column<long>(type: "bigint", nullable: true),
                    billing_note_id = table.Column<long>(type: "bigint", nullable: true),
                    applied_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_applications", x => x.application_id);
                    table.CheckConstraint("ck_receipt_applications_one_doc", "(CASE WHEN tax_invoice_id IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN delivery_order_id IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN billing_note_id IS NOT NULL THEN 1 ELSE 0 END) = 1");
                    table.ForeignKey(
                        name: "fk_receipt_applications_billing_notes_billing_note_id",
                        column: x => x.billing_note_id,
                        principalSchema: "sales",
                        principalTable: "billing_notes",
                        principalColumn: "billing_note_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receipt_applications_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "sales",
                        principalTable: "receipts",
                        principalColumn: "receipt_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_lines",
                schema: "sales",
                columns: table => new
                {
                    receipt_line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    receipt_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "GOOD"),
                    description_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_lines", x => x.receipt_line_id);
                    table.CheckConstraint("ck_receipt_lines_nonneg", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_receipt_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "master",
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receipt_lines_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "sales",
                        principalTable: "receipts",
                        principalColumn: "receipt_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_wht_lines",
                schema: "sales",
                columns: table => new
                {
                    receipt_wht_line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    receipt_id = table.Column<long>(type: "bigint", nullable: false),
                    wht_type_id = table.Column<int>(type: "integer", nullable: false),
                    income_type_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    wht_type_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    wht_rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    wht_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_wht_lines", x => x.receipt_wht_line_id);
                    table.CheckConstraint("ck_receipt_wht_lines_nonneg", "base_amount >= 0 AND wht_amount >= 0");
                    table.ForeignKey(
                        name: "fk_receipt_wht_lines_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "sales",
                        principalTable: "receipts",
                        principalColumn: "receipt_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_receipt_wht_lines_wht_types_wht_type_id",
                        column: x => x.wht_type_id,
                        principalSchema: "tax",
                        principalTable: "wht_types",
                        principalColumn: "wht_type_id",
                        onDelete: ReferentialAction.Restrict);
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
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
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
                    vendor_invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    self_withhold_mode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    wht_payer_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DEDUCT"),
                    requires_pnd36reverse_charge = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    original_printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_vouchers", x => x.payment_voucher_id);
                    table.CheckConstraint("ck_payment_vouchers_wht_payer_mode", "wht_payer_mode IN ('DEDUCT','GROSS_UP_FOREVER','GROSS_UP_ONCE')");
                    table.ForeignKey(
                        name: "fk_payment_vouchers_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalSchema: "master",
                        principalTable: "business_units",
                        principalColumn: "business_unit_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_vouchers_vendor_invoices_vendor_invoice_id",
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
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
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

            migrationBuilder.CreateTable(
                name: "billing_note_lines",
                schema: "sales",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    billing_note_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    description_th = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uom_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
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
                    table.PrimaryKey("pk_billing_note_lines", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_billing_note_lines_billing_notes_billing_note_id",
                        column: x => x.billing_note_id,
                        principalSchema: "sales",
                        principalTable: "billing_notes",
                        principalColumn: "billing_note_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_billing_note_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "master",
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_billing_note_lines_tax_invoices_tax_invoice_id",
                        column: x => x.tax_invoice_id,
                        principalSchema: "sales",
                        principalTable: "tax_invoices",
                        principalColumn: "tax_invoice_id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    business_unit_id = table.Column<int>(type: "integer", nullable: true),
                    original_tax_invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
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
                    version = table.Column<long>(type: "bigint", nullable: false),
                    original_printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_adjustment_notes", x => x.note_id);
                    table.CheckConstraint("ck_note_tax_point", "doc_date = tax_point_date");
                    table.CheckConstraint("ck_note_type", "note_type IN ('CREDIT','DEBIT')");
                    table.ForeignKey(
                        name: "fk_tax_adjustment_notes_business_units_business_unit_id",
                        column: x => x.business_unit_id,
                        principalSchema: "master",
                        principalTable: "business_units",
                        principalColumn: "business_unit_id",
                        onDelete: ReferentialAction.Restrict);
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
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                        name: "fk_tax_invoice_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "master",
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_invoice_lines_tax_invoices_tax_invoice_id",
                        column: x => x.tax_invoice_id,
                        principalSchema: "sales",
                        principalTable: "tax_invoices",
                        principalColumn: "tax_invoice_id",
                        onDelete: ReferentialAction.Cascade);
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
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
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
                name: "ix_api_keys_default_business_unit_id",
                schema: "sys",
                table: "api_keys",
                column: "default_business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_key_hash",
                schema: "sys",
                table: "api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_key_prefix",
                schema: "sys",
                table: "api_keys",
                column: "key_prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attachments_category",
                schema: "sys",
                table: "attachments",
                columns: new[] { "company_id", "category", "uploaded_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_parent",
                schema: "sys",
                table: "attachments",
                columns: new[] { "company_id", "parent_type", "parent_id" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_billing_note_lines_billing_note_id_line_no",
                schema: "sales",
                table: "billing_note_lines",
                columns: new[] { "billing_note_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_billing_note_lines_product_id",
                schema: "sales",
                table: "billing_note_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_billing_note_lines_tax_invoice_id",
                schema: "sales",
                table: "billing_note_lines",
                column: "tax_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_billing_note_tax_invoices_tax_invoice_id",
                schema: "sales",
                table: "billing_note_tax_invoices",
                column: "tax_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_billing_notes_company_id_doc_no",
                schema: "sales",
                table: "billing_notes",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_billing_notes_delivery_order_id",
                schema: "sales",
                table: "billing_notes",
                column: "delivery_order_id",
                filter: "delivery_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_billing_notes_quotation_id",
                schema: "sales",
                table: "billing_notes",
                column: "quotation_id");

            migrationBuilder.CreateIndex(
                name: "ix_branches_company_id_branch_code",
                schema: "master",
                table: "branches",
                columns: new[] { "company_id", "branch_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_units_company_id_code",
                schema: "master",
                table: "business_units",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_units_default_revenue_account_id",
                schema: "master",
                table: "business_units",
                column: "default_revenue_account_id");

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
                name: "ix_cit_adjustments_company_id_fiscal_year",
                schema: "tax",
                table: "cit_adjustments",
                columns: new[] { "company_id", "fiscal_year" });

            migrationBuilder.CreateIndex(
                name: "ix_cit_year_summaries_company_id_fiscal_year",
                schema: "tax",
                table: "cit_year_summaries",
                columns: new[] { "company_id", "fiscal_year" },
                unique: true);

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
                name: "ix_customers_default_wht_type_id",
                schema: "master",
                table: "customers",
                column: "default_wht_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_order_lines_delivery_order_id_line_no",
                schema: "sales",
                table: "delivery_order_lines",
                columns: new[] { "delivery_order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_order_lines_product_id",
                schema: "sales",
                table: "delivery_order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_orders_company_id_doc_no",
                schema: "sales",
                table: "delivery_orders",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_document_prefixes_prefix_code",
                schema: "sys",
                table: "document_prefixes",
                column: "prefix_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_company_id_employee_code",
                schema: "master",
                table: "employees",
                columns: new[] { "company_id", "employee_code" },
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
                name: "ix_idemp_expiry",
                schema: "sys",
                table: "idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_idemp_company_apikey_key",
                schema: "sys",
                table: "idempotency_keys",
                columns: new[] { "company_id", "api_key_id", "key" },
                unique: true);

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
                name: "ix_journal_lines_business_unit_id",
                schema: "gl",
                table: "journal_lines",
                column: "business_unit_id",
                filter: "business_unit_id IS NOT NULL");

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
                name: "ix_payment_voucher_lines_payment_voucher_id_line_no",
                schema: "purchase",
                table: "payment_voucher_lines",
                columns: new[] { "payment_voucher_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_vouchers_business_unit_id",
                schema: "purchase",
                table: "payment_vouchers",
                column: "business_unit_id",
                filter: "business_unit_id IS NOT NULL");

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
                name: "ix_payment_vouchers_vendor_invoice_id",
                schema: "purchase",
                table: "payment_vouchers",
                column: "vendor_invoice_id",
                filter: "vendor_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_company_id_doc_no",
                schema: "payroll",
                table: "payroll_runs",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_runs_company_id_period_year_month",
                schema: "payroll",
                table: "payroll_runs",
                columns: new[] { "company_id", "period_year_month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payslips_employee_id",
                schema: "payroll",
                table: "payslips",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payslips_payroll_run_id_employee_id",
                schema: "payroll",
                table: "payslips",
                columns: new[] { "payroll_run_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permissions_permission_code",
                schema: "sys",
                table: "permissions",
                column: "permission_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_business_unit_id",
                schema: "master",
                table: "products",
                column: "business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_company_id_product_code",
                schema: "master",
                table: "products",
                columns: new[] { "company_id", "product_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_default_input_tax_code_id",
                schema: "master",
                table: "products",
                column: "default_input_tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_default_output_tax_code_id",
                schema: "master",
                table: "products",
                column: "default_output_tax_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_default_wht_type_id",
                schema: "master",
                table: "products",
                column: "default_wht_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_product_id",
                schema: "purchase",
                table: "purchase_order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id_line_no",
                schema: "purchase",
                table: "purchase_order_lines",
                columns: new[] { "purchase_order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_company_id_doc_no",
                schema: "purchase",
                table: "purchase_orders",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_company_id_status",
                schema: "purchase",
                table: "purchase_orders",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_quotation_lines_product_id",
                schema: "sales",
                table: "quotation_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotation_lines_quotation_id_line_no",
                schema: "sales",
                table: "quotation_lines",
                columns: new[] { "quotation_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quotations_company_id_doc_no",
                schema: "sales",
                table: "quotations",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_billing_note_id",
                schema: "sales",
                table: "receipt_applications",
                column: "billing_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_delivery_order_id",
                schema: "sales",
                table: "receipt_applications",
                column: "delivery_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_receipt_id_billing_note_id",
                schema: "sales",
                table: "receipt_applications",
                columns: new[] { "receipt_id", "billing_note_id" },
                unique: true,
                filter: "billing_note_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_receipt_id_delivery_order_id",
                schema: "sales",
                table: "receipt_applications",
                columns: new[] { "receipt_id", "delivery_order_id" },
                unique: true,
                filter: "delivery_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_receipt_id_tax_invoice_id",
                schema: "sales",
                table: "receipt_applications",
                columns: new[] { "receipt_id", "tax_invoice_id" },
                unique: true,
                filter: "tax_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_applications_tax_invoice_id",
                schema: "sales",
                table: "receipt_applications",
                column: "tax_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_lines_product_id",
                schema: "sales",
                table: "receipt_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_lines_receipt_id",
                schema: "sales",
                table: "receipt_lines",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_wht_lines_receipt_id",
                schema: "sales",
                table: "receipt_wht_lines",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_wht_lines_wht_type_id",
                schema: "sales",
                table: "receipt_wht_lines",
                column: "wht_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_business_unit_id",
                schema: "sales",
                table: "receipts",
                column: "business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_company_id_branch_id_doc_no",
                schema: "sales",
                table: "receipts",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_company_id_business_unit_id",
                schema: "sales",
                table: "receipts",
                columns: new[] { "company_id", "business_unit_id" },
                filter: "business_unit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_customer_id_doc_date",
                schema: "sales",
                table: "receipts",
                columns: new[] { "customer_id", "doc_date" });

            migrationBuilder.CreateIndex(
                name: "ix_receipts_wht_type_id",
                schema: "sales",
                table: "receipts",
                column: "wht_type_id");

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
                name: "ix_sales_order_lines_product_id",
                schema: "sales",
                table: "sales_order_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_sales_order_id_line_no",
                schema: "sales",
                table: "sales_order_lines",
                columns: new[] { "sales_order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_company_id_doc_no",
                schema: "sales",
                table: "sales_orders",
                columns: new[] { "company_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

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

            migrationBuilder.CreateIndex(
                name: "ix_tax_adjustment_notes_business_unit_id",
                schema: "sales",
                table: "tax_adjustment_notes",
                column: "business_unit_id");

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
                name: "ix_tax_filings_company_id_form_type_period",
                schema: "tax",
                table: "tax_filings",
                columns: new[] { "company_id", "form_type", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoice_lines_product_id",
                schema: "sales",
                table: "tax_invoice_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoice_lines_tax_invoice_id_line_no",
                schema: "sales",
                table: "tax_invoice_lines",
                columns: new[] { "tax_invoice_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_billing_note_id",
                schema: "sales",
                table: "tax_invoices",
                column: "billing_note_id",
                filter: "billing_note_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_business_unit_id",
                schema: "sales",
                table: "tax_invoices",
                column: "business_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_company_id_branch_id_doc_no",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "company_id", "branch_id", "doc_no" },
                unique: true,
                filter: "doc_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tax_invoices_company_id_business_unit_id",
                schema: "sales",
                table: "tax_invoices",
                columns: new[] { "company_id", "business_unit_id" },
                filter: "business_unit_id IS NOT NULL");

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
                name: "ix_tax_invoices_quotation_id",
                schema: "sales",
                table: "tax_invoices",
                column: "quotation_id",
                filter: "quotation_id IS NOT NULL");

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
                name: "ix_vendor_invoice_lines_vendor_invoice_id_line_no",
                schema: "purchase",
                table: "vendor_invoice_lines",
                columns: new[] { "vendor_invoice_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_invoices_business_unit_id",
                schema: "purchase",
                table: "vendor_invoices",
                column: "business_unit_id",
                filter: "business_unit_id IS NOT NULL");

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
                name: "ix_vendor_invoices_purchase_order_id",
                schema: "purchase",
                table: "vendor_invoices",
                column: "purchase_order_id");

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
                unique: true,
                filter: "direction = 'P'");

            migrationBuilder.CreateIndex(
                name: "ix_wht_certificates_payment_voucher_id",
                schema: "tax",
                table: "wht_certificates",
                column: "payment_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_wht_certificates_receipt_id",
                schema: "tax",
                table: "wht_certificates",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_wht_types_company_id_code_effective_from",
                schema: "tax",
                table: "wht_types",
                columns: new[] { "company_id", "code", "effective_from" },
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
                name: "attachments",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "billing_note_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "billing_note_tax_invoices",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "master");

            migrationBuilder.DropTable(
                name: "cit_adjustments",
                schema: "tax");

            migrationBuilder.DropTable(
                name: "cit_year_summaries",
                schema: "tax");

            migrationBuilder.DropTable(
                name: "company_profile",
                schema: "master");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "master");

            migrationBuilder.DropTable(
                name: "delivery_order_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "document_prefixes",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "master");

            migrationBuilder.DropTable(
                name: "expense_categories",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "journal_lines",
                schema: "gl");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "payment_voucher_applications",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "payment_voucher_lines",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "payslips",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "quotation_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "receipt_applications",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "receipt_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "receipt_wht_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "sales_order_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "submissions",
                schema: "etax");

            migrationBuilder.DropTable(
                name: "tax_adjustment_notes",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "tax_filings",
                schema: "tax");

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
                name: "vendor_invoice_lines",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "vendors",
                schema: "master");

            migrationBuilder.DropTable(
                name: "wht_certificates",
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
                name: "payroll_runs",
                schema: "payroll");

            migrationBuilder.DropTable(
                name: "receipts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "sales_orders",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "products",
                schema: "master");

            migrationBuilder.DropTable(
                name: "tax_invoices",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "users",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "vendor_invoices",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "tax_codes",
                schema: "tax");

            migrationBuilder.DropTable(
                name: "wht_types",
                schema: "tax");

            migrationBuilder.DropTable(
                name: "billing_notes",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "business_units",
                schema: "master");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "purchase");

            migrationBuilder.DropTable(
                name: "delivery_orders",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "quotations",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "chart_of_accounts",
                schema: "master");
        }
    }
}
