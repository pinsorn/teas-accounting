-- =================================================================
-- Thailand Enterprise Accounting System — PostgreSQL Reference Schema
-- Target: PostgreSQL 16+
-- =================================================================
-- NOTE: EF Core Migrations is the SOURCE OF TRUTH for schema.
-- This file is a reference / sanity check for the data model.
-- Generate with: dotnet ef migrations script
-- =================================================================

SET timezone = 'Asia/Bangkok';

-- ============================================================
-- SCHEMAS
-- ============================================================
CREATE SCHEMA IF NOT EXISTS sys;
CREATE SCHEMA IF NOT EXISTS master;
CREATE SCHEMA IF NOT EXISTS tax;
CREATE SCHEMA IF NOT EXISTS sales;
CREATE SCHEMA IF NOT EXISTS purchase;
CREATE SCHEMA IF NOT EXISTS gl;
CREATE SCHEMA IF NOT EXISTS cash;
CREATE SCHEMA IF NOT EXISTS etax;
CREATE SCHEMA IF NOT EXISTS notif;
CREATE SCHEMA IF NOT EXISTS audit;
CREATE SCHEMA IF NOT EXISTS jobs;

-- ============================================================
-- EXTENSIONS
-- ============================================================
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ============================================================
-- SYSTEM
-- ============================================================
CREATE TABLE sys.users (
    user_id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username             VARCHAR(100) NOT NULL UNIQUE,
    email                VARCHAR(255) NOT NULL UNIQUE,
    password_hash        VARCHAR(255) NOT NULL,
    mfa_secret_enc       BYTEA,
    full_name            VARCHAR(255) NOT NULL,
    employee_code        VARCHAR(50),
    cpd_number           VARCHAR(50),
    is_super_admin       BOOLEAN NOT NULL DEFAULT FALSE,
    is_active            BOOLEAN NOT NULL DEFAULT TRUE,
    last_login_at        TIMESTAMPTZ(3),
    failed_login_count   INT NOT NULL DEFAULT 0,
    locked_until         TIMESTAMPTZ(3),
    password_changed_at  TIMESTAMPTZ(3),
    must_change_password BOOLEAN NOT NULL DEFAULT FALSE,
    created_at           TIMESTAMPTZ(3) NOT NULL DEFAULT NOW(),
    created_by           BIGINT,
    updated_at           TIMESTAMPTZ(3) NOT NULL DEFAULT NOW(),
    updated_by           BIGINT,
    version              BIGINT NOT NULL DEFAULT 0
);

CREATE TABLE sys.roles (
    role_id     INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    role_code   VARCHAR(50) NOT NULL UNIQUE,
    role_name   VARCHAR(100) NOT NULL,
    description VARCHAR(500),
    is_system   BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE sys.permissions (
    permission_id   INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    module          VARCHAR(50) NOT NULL,
    resource        VARCHAR(50) NOT NULL,
    action          VARCHAR(50) NOT NULL,
    description     VARCHAR(500)
);

CREATE TABLE sys.role_permissions (
    role_id       INT NOT NULL REFERENCES sys.roles(role_id) ON DELETE CASCADE,
    permission_id INT NOT NULL REFERENCES sys.permissions(permission_id),
    PRIMARY KEY (role_id, permission_id)
);

CREATE TABLE sys.user_roles (
    user_id    BIGINT NOT NULL REFERENCES sys.users(user_id),
    role_id    INT    NOT NULL REFERENCES sys.roles(role_id),
    company_id INT    NOT NULL,
    branch_id  INT    NOT NULL DEFAULT 0,
    valid_from DATE   NOT NULL DEFAULT CURRENT_DATE,
    valid_to   DATE,
    PRIMARY KEY (user_id, role_id, company_id, branch_id)
);

CREATE TABLE sys.api_keys (
    api_key_id  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id  INT NOT NULL,
    name        VARCHAR(255) NOT NULL,
    key_hash    VARCHAR(255) NOT NULL UNIQUE,
    key_prefix  VARCHAR(20) NOT NULL,
    scopes      JSONB NOT NULL,
    created_by  BIGINT NOT NULL,
    created_at  TIMESTAMPTZ(3) NOT NULL DEFAULT NOW(),
    expires_at  TIMESTAMPTZ(3),
    last_used_at TIMESTAMPTZ(3),
    revoked_at  TIMESTAMPTZ(3),
    revoked_by  BIGINT,
    is_active   BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE sys.document_prefixes (
    prefix_id      INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    prefix_code    VARCHAR(20) NOT NULL UNIQUE,
    document_type  VARCHAR(50) NOT NULL,
    description_th VARCHAR(255) NOT NULL,
    description_en VARCHAR(255),
    requires_etax  BOOLEAN NOT NULL DEFAULT FALSE,
    is_fiscal_doc  BOOLEAN NOT NULL DEFAULT FALSE,
    is_expense     BOOLEAN NOT NULL DEFAULT FALSE,
    is_active      BOOLEAN NOT NULL DEFAULT TRUE,
    created_at     TIMESTAMPTZ(3) NOT NULL DEFAULT NOW()
);

CREATE TABLE sys.expense_categories (
    category_id                INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id                 INT NOT NULL,
    category_code              VARCHAR(20) NOT NULL,
    name_th                    VARCHAR(255) NOT NULL,
    name_en                    VARCHAR(255),
    description                TEXT,
    default_expense_account_id BIGINT,
    default_tax_code_id        INT,
    default_is_recoverable_vat BOOLEAN NOT NULL DEFAULT TRUE,
    default_wht_type_id        INT,
    is_capex                   BOOLEAN NOT NULL DEFAULT FALSE,
    is_cogs                    BOOLEAN NOT NULL DEFAULT FALSE,
    parent_category_id         INT REFERENCES sys.expense_categories(category_id),
    sort_order                 INT,
    is_active                  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at                 TIMESTAMPTZ(3) NOT NULL DEFAULT NOW(),
    UNIQUE (company_id, category_code)
);

CREATE TABLE sys.number_sequences (
    sequence_id    INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id     INT NOT NULL,
    branch_id      INT NOT NULL DEFAULT 0,
    prefix_code    VARCHAR(20) NOT NULL,
    sub_prefix     VARCHAR(20) NOT NULL DEFAULT '',
    period_year    INT NOT NULL,
    period_month   SMALLINT NOT NULL CHECK (period_month BETWEEN 1 AND 12),
    current_value  INT NOT NULL DEFAULT 0,
    last_issued_at TIMESTAMPTZ(3),
    UNIQUE (company_id, branch_id, prefix_code, sub_prefix, period_year, period_month)
);

-- ============================================================
-- MASTER DATA
-- ============================================================
CREATE TABLE master.companies (
    company_id              INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tax_id                  CHAR(13) NOT NULL UNIQUE CHECK (tax_id ~ '^[0-9]{13}$'),
    name_th                 VARCHAR(255) NOT NULL,
    name_en                 VARCHAR(255),
    legal_entity_type       VARCHAR(50) NOT NULL,
    registration_date       DATE,
    vat_registered          BOOLEAN NOT NULL DEFAULT FALSE,
    vat_register_date       DATE,
    fiscal_year_start_month SMALLINT NOT NULL DEFAULT 1 CHECK (fiscal_year_start_month BETWEEN 1 AND 12),
    base_currency           CHAR(3) NOT NULL DEFAULT 'THB',
    reporting_standard      VARCHAR(20) NOT NULL DEFAULT 'TFRS_NPAE',
    address_th              TEXT,
    sub_district            VARCHAR(100),
    district                VARCHAR(100),
    province                VARCHAR(100),
    postal_code             VARCHAR(10),
    phone                   VARCHAR(50),
    email                   VARCHAR(255),
    is_active               BOOLEAN NOT NULL DEFAULT TRUE,
    created_at              TIMESTAMPTZ(3) NOT NULL DEFAULT NOW()
);

CREATE TABLE master.branches (
    branch_id      INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id     INT NOT NULL REFERENCES master.companies(company_id),
    branch_code    CHAR(5) NOT NULL CHECK (branch_code ~ '^[0-9]{5}$'),
    name_th        VARCHAR(255) NOT NULL,
    name_en        VARCHAR(255),
    is_head_office BOOLEAN NOT NULL DEFAULT FALSE,
    address_th     TEXT,
    is_active      BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE (company_id, branch_code)
);

CREATE TABLE master.chart_of_accounts (
    account_id      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      INT NOT NULL REFERENCES master.companies(company_id),
    account_code    VARCHAR(20) NOT NULL,
    account_name_th VARCHAR(255) NOT NULL,
    account_name_en VARCHAR(255),
    account_type    VARCHAR(20) NOT NULL CHECK (account_type IN ('ASSET','LIABILITY','EQUITY','REVENUE','EXPENSE')),
    parent_id       BIGINT REFERENCES master.chart_of_accounts(account_id),
    is_header       BOOLEAN NOT NULL DEFAULT FALSE,
    normal_balance  CHAR(2) NOT NULL CHECK (normal_balance IN ('DR','CR')),
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ(3) NOT NULL DEFAULT NOW(),
    UNIQUE (company_id, account_code)
);

CREATE TABLE master.customers (
    customer_id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id         INT NOT NULL REFERENCES master.companies(company_id),
    customer_code      VARCHAR(50) NOT NULL,
    customer_type      VARCHAR(20) NOT NULL CHECK (customer_type IN ('INDIVIDUAL','CORPORATE')),
    tax_id             CHAR(13),
    branch_code        CHAR(5),
    branch_name        VARCHAR(255),
    name_th            VARCHAR(255) NOT NULL,
    name_en            VARCHAR(255),
    vat_registered     BOOLEAN NOT NULL DEFAULT FALSE,
    billing_address    TEXT,
    contact_person     VARCHAR(255),
    phone              VARCHAR(50),
    email              VARCHAR(255),
    credit_limit       NUMERIC(19,4) NOT NULL DEFAULT 0,
    payment_term_days  INT NOT NULL DEFAULT 0,
    default_currency   CHAR(3) NOT NULL DEFAULT 'THB',
    is_active          BOOLEAN NOT NULL DEFAULT TRUE,
    created_at         TIMESTAMPTZ(3) NOT NULL DEFAULT NOW(),
    UNIQUE (company_id, customer_code)
);

-- ============================================================
-- TAX
-- ============================================================
CREATE TABLE tax.tax_codes (
    tax_code_id        INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id         INT NOT NULL,
    code               VARCHAR(20) NOT NULL,
    name_th            VARCHAR(255) NOT NULL,
    tax_type           VARCHAR(20) NOT NULL,
    direction          VARCHAR(10) NOT NULL,
    is_recoverable     BOOLEAN NOT NULL DEFAULT TRUE,
    is_exempt          BOOLEAN NOT NULL DEFAULT FALSE,
    is_zero_rated      BOOLEAN NOT NULL DEFAULT FALSE,
    is_reverse_charge  BOOLEAN NOT NULL DEFAULT FALSE,
    is_active          BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE (company_id, code)
);

CREATE TABLE tax.tax_rates (
    tax_rate_id    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tax_code_id    INT NOT NULL REFERENCES tax.tax_codes(tax_code_id),
    rate           NUMERIC(9,6) NOT NULL,
    effective_from DATE NOT NULL,
    effective_to   DATE,
    UNIQUE (tax_code_id, effective_from)
);

-- ============================================================
-- SALES — Tax Invoice is the core compliance entity
-- ============================================================
CREATE TABLE sales.tax_invoices (
    tax_invoice_id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id              INT NOT NULL REFERENCES master.companies(company_id),
    branch_id               INT NOT NULL REFERENCES master.branches(branch_id),
    doc_no                  VARCHAR(50) NOT NULL,
    book_no                 VARCHAR(20),
    doc_date                DATE NOT NULL,
    invoice_type            VARCHAR(20) NOT NULL DEFAULT 'FULL' CHECK (invoice_type = 'FULL'),
    is_substitute           BOOLEAN NOT NULL DEFAULT FALSE,
    original_invoice_id     BIGINT REFERENCES sales.tax_invoices(tax_invoice_id),
    tax_point_date          DATE NOT NULL,
    tax_point_reason        VARCHAR(50),
    -- Supplier snapshot
    supplier_tax_id         CHAR(13) NOT NULL,
    supplier_branch_code    CHAR(5)  NOT NULL,
    supplier_branch_name    VARCHAR(255) NOT NULL,
    supplier_name           VARCHAR(255) NOT NULL,
    supplier_address        TEXT NOT NULL,
    -- Customer snapshot
    customer_id             BIGINT NOT NULL REFERENCES master.customers(customer_id),
    customer_tax_id         CHAR(13),
    customer_branch_code    CHAR(5),
    customer_branch_name    VARCHAR(255),
    customer_name           VARCHAR(255) NOT NULL,
    customer_address        TEXT NOT NULL,
    customer_vat_registered BOOLEAN NOT NULL DEFAULT FALSE,
    -- Amounts
    currency_code           CHAR(3) NOT NULL DEFAULT 'THB',
    exchange_rate           NUMERIC(19,8) NOT NULL DEFAULT 1,
    subtotal_amount         NUMERIC(19,4) NOT NULL,
    discount_amount         NUMERIC(19,4) NOT NULL DEFAULT 0,
    taxable_amount          NUMERIC(19,4) NOT NULL,
    nontaxable_amount       NUMERIC(19,4) NOT NULL DEFAULT 0,
    tax_amount              NUMERIC(19,4) NOT NULL,
    total_amount            NUMERIC(19,4) NOT NULL,
    total_amount_thb        NUMERIC(19,4) NOT NULL,
    amount_in_words_th      VARCHAR(500),
    is_tax_inclusive        BOOLEAN NOT NULL DEFAULT FALSE,
    -- Status
    status                  VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    posted_at               TIMESTAMPTZ(3),
    posted_by               BIGINT,
    payment_status          VARCHAR(20) NOT NULL DEFAULT 'UNPAID',
    amount_paid             NUMERIC(19,4) NOT NULL DEFAULT 0,
    due_date                DATE,
    -- e-Tax
    is_e_tax                BOOLEAN NOT NULL DEFAULT FALSE,
    e_tax_xml_url           VARCHAR(500),
    e_tax_pdf_url           VARCHAR(500),
    e_tax_signed_at         TIMESTAMPTZ(3),
    e_tax_submitted_at      TIMESTAMPTZ(3),
    e_tax_ack_id            VARCHAR(100),
    e_tax_status            VARCHAR(20),
    -- Delivery tracking
    delivered_to_customer    BOOLEAN NOT NULL DEFAULT FALSE,
    delivered_to_customer_at TIMESTAMPTZ(3),
    delivery_method          VARCHAR(20),
    -- Misc
    payment_terms           VARCHAR(500),
    notes                   TEXT,
    created_at              TIMESTAMPTZ(3) NOT NULL DEFAULT NOW(),
    created_by              BIGINT NOT NULL,
    version                 BIGINT NOT NULL DEFAULT 0,
    UNIQUE (company_id, branch_id, doc_no),
    CHECK (doc_date = tax_point_date)
);

CREATE INDEX ix_ti_period   ON sales.tax_invoices (company_id, doc_date);
CREATE INDEX ix_ti_customer ON sales.tax_invoices (customer_id, doc_date);
CREATE INDEX ix_ti_status   ON sales.tax_invoices (status, doc_date);

CREATE TABLE sales.tax_invoice_lines (
    line_id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tax_invoice_id      BIGINT NOT NULL REFERENCES sales.tax_invoices(tax_invoice_id) ON DELETE CASCADE,
    line_no             INT NOT NULL,
    product_id          BIGINT,
    product_code        VARCHAR(50),
    description_th      VARCHAR(500) NOT NULL,
    quantity            NUMERIC(19,4) NOT NULL,
    uom_id              INT NOT NULL,
    uom_text            VARCHAR(50) NOT NULL,
    unit_price          NUMERIC(19,4) NOT NULL,
    discount_percent    NUMERIC(9,4) DEFAULT 0,
    discount_amount     NUMERIC(19,4) DEFAULT 0,
    line_amount         NUMERIC(19,4) NOT NULL,
    tax_code_id         INT NOT NULL,
    tax_code            VARCHAR(20) NOT NULL,
    tax_rate            NUMERIC(9,6) NOT NULL,
    tax_amount          NUMERIC(19,4) NOT NULL,
    total_amount        NUMERIC(19,4) NOT NULL,
    UNIQUE (tax_invoice_id, line_no)
);

-- ============================================================
-- IMMUTABILITY TRIGGER for posted Tax Invoices
-- ============================================================
CREATE OR REPLACE FUNCTION sales.fn_enforce_ti_immutability() RETURNS trigger AS $$
BEGIN
    IF OLD.status = 'POSTED' THEN
        IF (OLD.doc_no IS DISTINCT FROM NEW.doc_no
         OR OLD.doc_date IS DISTINCT FROM NEW.doc_date
         OR OLD.tax_point_date IS DISTINCT FROM NEW.tax_point_date
         OR OLD.supplier_tax_id IS DISTINCT FROM NEW.supplier_tax_id
         OR OLD.subtotal_amount IS DISTINCT FROM NEW.subtotal_amount
         OR OLD.tax_amount IS DISTINCT FROM NEW.tax_amount
         OR OLD.total_amount IS DISTINCT FROM NEW.total_amount
        ) THEN
            RAISE EXCEPTION 'Cannot modify critical fields of posted Tax Invoice (doc_no=%)', OLD.doc_no;
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_ti_immutable
    BEFORE UPDATE ON sales.tax_invoices
    FOR EACH ROW EXECUTE FUNCTION sales.fn_enforce_ti_immutability();

CREATE OR REPLACE FUNCTION sales.fn_no_delete_posted_ti() RETURNS trigger AS $$
BEGIN
    IF OLD.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'Cannot delete non-draft Tax Invoice (doc_no=%)', OLD.doc_no;
    END IF;
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_ti_no_delete_posted
    BEFORE DELETE ON sales.tax_invoices
    FOR EACH ROW EXECUTE FUNCTION sales.fn_no_delete_posted_ti();

-- ============================================================
-- ROW-LEVEL SECURITY (multi-tenant)
-- ============================================================
ALTER TABLE sales.tax_invoices ENABLE ROW LEVEL SECURITY;
ALTER TABLE master.customers   ENABLE ROW LEVEL SECURITY;

CREATE POLICY company_isolation_ti ON sales.tax_invoices
    USING (
        company_id = current_setting('app.company_id', true)::INT
        OR current_setting('app.is_super_admin', true)::BOOLEAN IS TRUE
    );

CREATE POLICY company_isolation_cust ON master.customers
    USING (
        company_id = current_setting('app.company_id', true)::INT
        OR current_setting('app.is_super_admin', true)::BOOLEAN IS TRUE
    );

-- (Apply similar policies to ALL business tables — TODO in Phase 1 migration)

-- ============================================================
-- COMPLIANCE VIEWS
-- ============================================================
-- tax.v_number_gaps — compliance §17.6 / §4.3
--
-- Returns any missing sequence number within the issued range for each
-- (company_id, branch_id, prefix, sub_prefix, period_year, period_month).
-- A row here = a numbering defect. In production this view MUST be empty
-- for every period. Tenant-scoped: callers MUST set `app.company_id`
-- session var (RLS applies via the underlying tables).
--
-- Sources: sales.tax_invoices, gl.journal_entries, purchase.payment_vouchers
-- (extend with new doc types as schemas grow).
--
-- Backend service: Reports/NumberGapReportService.cs
-- HTTP surface:    GET /reports/number-gaps?year=&month=&doc_type=
-- Permission:      report.audit.read
-- Sprint added:    Sprint 1 hardening (Report-Backend2.md §4)
-- ============================================================
CREATE OR REPLACE VIEW tax.v_number_gaps AS
WITH numbered AS (
    -- Tax invoices
    SELECT
        ti.company_id,
        ti.branch_id,
        'TI'                                   AS doc_type,
        EXTRACT(YEAR  FROM ti.doc_date)::INT   AS period_year,
        EXTRACT(MONTH FROM ti.doc_date)::INT   AS period_month,
        SPLIT_PART(ti.doc_no, '-', 1) || '-' ||
        SPLIT_PART(ti.doc_no, '-', 2) || '-' ||
        SPLIT_PART(ti.doc_no, '-', 3)          AS series,
        CAST(REGEXP_REPLACE(SPLIT_PART(ti.doc_no, '-', 4), '^0+', '') AS INT) AS seq_no
    FROM sales.tax_invoices ti
    WHERE ti.status <> 'DRAFT'

    UNION ALL

    -- Journal entries (manual JV)
    SELECT
        je.company_id,
        je.branch_id,
        'JV',
        EXTRACT(YEAR  FROM je.je_date)::INT,
        EXTRACT(MONTH FROM je.je_date)::INT,
        SPLIT_PART(je.doc_no, '-', 1) || '-' ||
        SPLIT_PART(je.doc_no, '-', 2) || '-' ||
        SPLIT_PART(je.doc_no, '-', 3),
        CAST(REGEXP_REPLACE(SPLIT_PART(je.doc_no, '-', 4), '^0+', '') AS INT)
    FROM gl.journal_entries je
    WHERE je.status <> 'DRAFT'

    -- Add more UNION ALL blocks here as new doc types ship
    -- (payment_vouchers, credit_notes, debit_notes, receipts, etc.)
),
expected_range AS (
    SELECT
        company_id, branch_id, doc_type, period_year, period_month, series,
        MIN(seq_no) AS min_seq,
        MAX(seq_no) AS max_seq
    FROM numbered
    GROUP BY 1,2,3,4,5,6
),
expected_seq AS (
    SELECT
        er.company_id, er.branch_id, er.doc_type, er.period_year,
        er.period_month, er.series,
        gs.seq AS expected_seq_no
    FROM expected_range er
    CROSS JOIN LATERAL generate_series(er.min_seq, er.max_seq) AS gs(seq)
)
SELECT
    es.company_id,
    es.branch_id,
    es.doc_type,
    es.period_year,
    es.period_month,
    es.series,
    es.expected_seq_no                              AS missing_seq_no,
    es.series || '-' || LPAD(es.expected_seq_no::TEXT, 4, '0') AS missing_doc_no
FROM expected_seq es
LEFT JOIN numbered n
       ON  n.company_id   = es.company_id
       AND n.branch_id    = es.branch_id
       AND n.doc_type     = es.doc_type
       AND n.period_year  = es.period_year
       AND n.period_month = es.period_month
       AND n.series       = es.series
       AND n.seq_no       = es.expected_seq_no
WHERE n.seq_no IS NULL;

COMMENT ON VIEW tax.v_number_gaps IS
  'Compliance audit (§17.6 / §4.3): any row = a missing document number in the
   issued sequence for that period+series. Production: MUST be empty.';

-- ============================================================
-- AUDIT
-- ============================================================
CREATE TABLE audit.activity_log (
    activity_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id      INT,
    user_id         BIGINT,
    username        VARCHAR(100),
    session_id      VARCHAR(100),
    ip_address      INET,
    user_agent      VARCHAR(500),
    activity_at     TIMESTAMPTZ(3) NOT NULL DEFAULT NOW(),
    activity_type   VARCHAR(50) NOT NULL,
    module          VARCHAR(50),
    entity_type     VARCHAR(50),
    entity_id       BIGINT,
    entity_doc_no   VARCHAR(50),
    before_value    JSONB,
    after_value     JSONB,
    metadata        JSONB
);

CREATE INDEX ix_audit_entity ON audit.activity_log (entity_type, entity_id);
CREATE INDEX ix_audit_user_time ON audit.activity_log (user_id, activity_at);

-- Audit log is append-only — revoke UPDATE/DELETE for app role (configure post-deployment)

-- ============================================================
-- SEED DATA
-- ============================================================
INSERT INTO sys.document_prefixes (prefix_code, document_type, description_th, description_en, requires_etax, is_fiscal_doc, is_expense) VALUES
('QT', 'QUOTATION',       'ใบเสนอราคา',                 'Quotation',         FALSE, FALSE, FALSE),
('SO', 'SALES_ORDER',     'ใบสั่งขาย',                  'Sales Order',       FALSE, FALSE, FALSE),
('DO', 'DELIVERY_ORDER',  'ใบส่งของ',                  'Delivery Order',    FALSE, FALSE, FALSE),
('TI', 'TAX_INVOICE',     'ใบกำกับภาษี',                'Tax Invoice',       TRUE,  TRUE,  FALSE),
('RC', 'RECEIPT',         'ใบเสร็จรับเงิน',             'Receipt',           TRUE,  TRUE,  FALSE),
('CN', 'CREDIT_NOTE',     'ใบลดหนี้',                  'Credit Note',       TRUE,  TRUE,  FALSE),
('DN', 'DEBIT_NOTE',      'ใบเพิ่มหนี้',                'Debit Note',        TRUE,  TRUE,  FALSE),
('BN', 'BILLING_NOTE',    'ใบวางบิล',                  'Billing Note',      FALSE, FALSE, FALSE),
('RV', 'RECEIPT_VOUCHER', 'ใบสำคัญรับ',                 'Receipt Voucher',   FALSE, TRUE,  FALSE),
('PV', 'PAYMENT_VOUCHER', 'ใบสำคัญจ่าย',                'Payment Voucher',   FALSE, TRUE,  TRUE),
('WT', 'WHT_CERT',        'หนังสือรับรองหักภาษี ณ ที่จ่าย', '50 Tawi',     FALSE, TRUE,  FALSE),
('JV', 'JOURNAL_VOUCHER', 'ใบสำคัญทั่วไป',              'Journal Voucher',   FALSE, TRUE,  FALSE)
ON CONFLICT (prefix_code) DO NOTHING;

-- ============================================================
-- END
-- ============================================================
-- NOTE: This is a partial schema for reference.
-- See accounting-system-plan.md §19 for complete model.
-- Implementation source of truth: EF Core entity classes + Migrations.
-- ============================================================
