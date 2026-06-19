# Thailand Enterprise Accounting System (TEAS) — As-Built Specification

> **What this document is.** A description of the system **as it actually exists in the
> codebase** as of 2026-06-17. It supersedes the original pre-build blueprint (archived at
> `docs/_archive/2026-06-17-pre-rewrite/accounting-system-plan.md`). The archive remains the
> reference for full legal-citation tables and the original aspirational schema; this document
> describes only what is implemented. Section numbers (§4.x etc.) referenced elsewhere in the
> repo (CLAUDE.md, progress.md) map to §3 here.
>
> **Authority.** The code is the source of truth. EF Core migrations are authoritative for the
> schema; `docs/api/openapi.yaml` and `docs/manual/api/` are authoritative for the REST surface.
> Where a claim could not be confirmed in code, a `<!-- VERIFY -->` note is left inline.

---

## Table of Contents

1. [Overview & Purpose](#1-overview--purpose)
2. [Architecture](#2-architecture)
3. [Compliance Rules — As Implemented](#3-compliance-rules--as-implemented)
4. [Functional Modules](#4-functional-modules-as-built)
5. [Data Model Overview](#5-data-model-overview)
6. [Tax-Form / PDF Coverage](#6-tax-form--pdf-coverage)
7. [Versioning, Build & Release](#7-versioning-build--release)
8. [Out of Scope / Not Built](#8-out-of-scope--not-built)
9. [Tech Stack](#9-tech-stack)

---

## 1. Overview & Purpose

**TEAS** is a multi-tenant B2B+B2C accounting platform for Thai companies, VAT-compliant by
design. It implements the document chains and statutory outputs a Thai SME/enterprise needs to
operate and to withstand a Thai Revenue Department (กรมสรรพากร) audit at any time.

- **Who it is for.** Thai juristic persons (and B2C sellers) that issue full tax invoices, run
  payroll, withhold tax, and file VAT/WHT/CIT returns. Each tenant is a company; one deployment
  serves many companies with strict data isolation.
- **Compliance bar.** Every fiscal document and number sequence must satisfy the Revenue Code
  (ประมวลรัษฎากร) and the Accounting Act (พ.ร.บ. การบัญชี พ.ศ. 2543): full tax invoices with all
  eight ม.86/4 fields, gap-free sequential numbering, immutability after posting, a five-year
  audit trail, and per-tenant isolation.
- **Current stage.** Substantial business logic is shipped and tested: Identity/RBAC, master
  data, the full sales chain (Quotation → Sales Order → Delivery Order → Tax Invoice → Receipt)
  with Credit/Debit Notes, purchases (Vendor Invoice → Payment Voucher) with WHT 50ทวิ, payroll
  with ภ.ง.ด.1/1ก and SSO, the general ledger and financial reports, and a suite of RD tax-form
  PDF fillers. e-Tax (RD e-filing) is present only as inert Phase-1 scaffolding (see §8).

---

## 2. Architecture

### 2.1 Clean Architecture layers (backend)

The backend (`backend/src`) is a .NET 10 solution in four layers with strict inward dependencies
(`Domain ← Application ← Infrastructure ← Api`), plus a worker host:

| Project | Responsibility |
|---|---|
| `Accounting.Domain` | Entities, enums, value objects, pure domain calculators (PIT, CIT, payroll math). No framework dependencies. |
| `Accounting.Application` | Use-case abstractions, DTOs, service interfaces, FluentValidation validators. |
| `Accounting.Infrastructure` | EF Core (`AccountingDbContext`), service implementations, RD PDF fillers, e-Tax pipeline, numbering, storage, SQL bootstrap scripts. |
| `Accounting.Api` | ASP.NET Core Minimal-API host; endpoint groups under `Endpoints/*.cs`; JWT auth; RFC 7807 ProblemDetails. |
| `Accounting.Workers` | Background host. Two jobs: `Pnd30DeadlineAlertJob` (ภ.พ.30 deadline reminder — logs only) and `VatRegisterSnapshotJob` (periodic VAT-register snapshot). |

Conventions (enforced repo-wide): money is `decimal` (4 dp); IDs are `long` (BIGINT), `int` for
lookups; time is `DateTimeOffset` internally, converted to `Asia/Bangkok` only at display;
CE/Gregorian calendar internally (never Buddhist); async everywhere with `CancellationToken`.

### 2.2 Multi-tenant model

Every business table carries `company_id INT NOT NULL`. Isolation is defense-in-depth:

1. **PostgreSQL Row-Level Security (RLS).** Business tables `ENABLE`/`FORCE ROW LEVEL SECURITY`
   with a `company_isolation` policy:
   `company_id = NULLIF(current_setting('app.company_id', true), '')::INT
   OR COALESCE(NULLIF(current_setting('app.is_super_admin', true),'')::BOOLEAN, FALSE)`.
2. **`SET LOCAL app.company_id` per request.** Tenant middleware pins the current company (and a
   super-admin flag) on the connection for the lifetime of the request.
3. **EF Core global query filter** as a backstop on every tenant-owned entity.

Super-admins (company switcher) set `app.is_super_admin` to operate across companies; ordinary
users see only their own company's rows.

### 2.3 Auth & RBAC

- **OAuth2 + JWT** (`Microsoft.AspNetCore.Authentication.JwtBearer`). Login returns an
  `access_token`; passwords are BCrypt-hashed.
- **Per-company RBAC.** `User`, `Role`, `Permission`, `RolePermission`, `UserRole` (in the `sys`
  schema). Permissions are fine-grained, dotted codes (e.g. `sales.tax_invoice.post`,
  `tax.filing.finalize`, `master.company.manage`). A user holds roles **within a company**.
- **Super-admin** bypasses tenant scoping and finalize-gates, and can switch the active company.
- **Onboarding wizard** runs on first use to create the founding company/profile/user.
- **External API keys** (`ApiKey`, `IdempotencyKey`) back an `/api/v1/...` surface with
  idempotency support and optional business-unit binding.

### 2.4 Frontend

Next.js 15 (App Router) under `frontend/app/(dashboard)/*`, React + TypeScript, Tailwind +
shadcn/Radix (DaisyUI components in places), React Query (TanStack) v5, React Hook Form + Zod,
`next-intl` (TH primary, EN secondary). The Next.js server acts as a **BFF proxy** to the .NET
API. Screens exist for every shipped module (sales chain, purchases, payroll, tax filings,
reports, settings/RBAC) — see §4.

---

## 3. Compliance Rules — As Implemented

These are the hard legal rules the system enforces. Thai citations are from the Revenue Code
(ประมวลรัษฎากร) and the Accounting Act unless noted.

### 3.1 Full Tax Invoice — ม.86/4 (§4.1)

Every Tax Invoice carries all **eight** mandatory ม.86/4 fields:

1. prominent **"ใบกำกับภาษี"** label;
2. seller name + address + 13-digit Tax ID + 5-digit branch (`00000` = สำนักงานใหญ่/HQ);
3. buyer name + address + Tax ID + branch **when the buyer is VAT-registered**;
4. a **sequential document number, no gaps** (assigned on post — see §3.3);
5. item name/type/quantity/value **per line**;
6. **VAT shown SEPARATELY** from the goods/service value (never merged);
7. issue date = tax-point date (ม.78/78/1);
8. other text the Director-General requires (e.g. combined "ใบกำกับภาษี/ใบเสร็จรับเงิน").

Only the **full** tax invoice (ม.86/4) is issued — the simplified form (ม.86/6) is intentionally
not supported (§8). Tax-inclusive lines compute VAT = total × 7/107 and still show VAT
separately.

### 3.2 Posted-document immutability (§4.2)

A posted tax invoice (and other posted fiscal documents) is **immutable**. There is no
edit/delete path for a posted document — corrections are made by **Credit Note** (ม.86/10) and
reissue, never by editing the original. Immutability is enforced at **both** layers: a database
trigger on critical post-fields **and** the application services (no service exposes a mutate
path once a document is posted).

### 3.3 Document numbering — `MM-YYYY-PREFIX-NNNN` (§4.3)

Numbers follow `MM-YYYY-PREFIX-NNNN` (Payment Vouchers insert a category segment,
`MM-YYYY-PREFIX-CATEGORY-NNNN`). Numbering is **sequential, gap-free, monthly-reset**, served by
`INumberSequenceService` over `sys.number_sequences` and per-document-type prefixes
(`sys.document_prefixes`). A number is assigned **only on POST/Issue**, never on Draft. A voided
document keeps its number with status `VOIDED`; numbers are never reused. The **number-gap
report** (`/reports/number-gaps`, FE `/number-gaps`) audits sequences for compliance.

### 3.4 Per-company VAT mode / rate / ภ.พ.30 mode (§4.6)

VAT behaviour is **company master data**, not instance configuration. `master.companies` carries
`vat_registered`, `vat_rate`, and `pnd30_submission_mode`; `ICompanyTaxConfigService` serves them
per request. These fields are settable **only** via `POST/PUT /companies` under the super-admin
permission `master.company.manage`; every tax-field change writes an `audit.activity_log`
`tax_config_change` event. There is **no user-facing VAT settings UI** and the VAT rate/mode is
never exposed in the UI. Non-VAT companies run in non-VAT mode (no output VAT, non-VAT document
labels from instance-wide cosmetic config).

**Server-side VAT-rate derivation (sales).** The Tax Invoice path derives the VAT rate
server-side rather than trusting the caller. `SalesLineBackstop` resolves the line rate against
the company's configured `vat_rate`, so a request that omits/under-specifies a rate cannot post a
0% invoice for a VAT company. (Added 2026-06-17; lives in `SalesLineBackstop` +
`TaxInvoiceService`.)

**Per-line PV VAT guards (purchase).** `PaymentVoucherService` derives input VAT per line (never
typed by the user) and enforces three legal guards in order of specificity:
- **ม.82/5** — a non-VAT-registered vendor issues no tax invoice → input VAT must be 0%;
- **ม.81** — a VAT-exempt product carries no input VAT even from a VAT vendor;
- otherwise — a VATable line may carry only 0% or the company's standard rate (else
  `pv.vat_rate_invalid`).

### 3.5 ภ.พ.30 (VAT return) mode (§4.5)

ภ.พ.30 is monthly, due the 15th of the following month. Each company's
`pnd30_submission_mode` is `auto` or `manual`. **As implemented, no automatic submission to the
RD occurs**: `auto` mode routes through an inert `MockRdEfilingClient` (synthetic ACK), there is
**no auto-submit cron**, and `Pnd30DeadlineAlertJob` only **logs** a reminder. The onboarding
wizard force-disables `auto`. ภ.พ.30 is produced as a **filled RD PDF** for print-and-file (§6).

### 3.6 Tax point (ม.78/78/1)

The tax point is enforced as the document/issue date control: goods on delivery, services on the
earlier of payment / invoice / use. `doc_date` is always **today in `Asia/Bangkok`**, never taken
from user input.

### 3.7 Withholding tax (ภ.ง.ด.)

WHT is captured on Payment Vouchers and certified by the 50ทวิ certificate. Filings cover
ภ.ง.ด.3 (individuals), ภ.ง.ด.53 (juristic persons), ภ.ง.ด.54 (payments abroad, incl. ม.70), and
ภ.พ.36 reverse charge (§6). WHT types/rates are master data (`tax.wht_types`).

### 3.8 RLS isolation, audit trail & retention (§4.7, §4.8)

Per-tenant RLS (§2.2). Every state change writes `audit.activity_log` (append-only; deletion
forbidden). Critical post-fields are made immutable by DB trigger. Retention target is **5 years**
(พ.ร.บ. การบัญชี ม.14); audit and e-Tax XML stores are append-only.

---

## 4. Functional Modules (as built)

Endpoint references below correspond to `docs/manual/api/` categories; every documented route
exists in `Accounting.Api/Endpoints/*.cs`. Money is camelCase decimal (4 dp); `period` params are
`YYYYMM`; `year` params are CE.

### 4.1 Master data — `docs/manual/api/master-data.md`

Companies, company profile, branches, customers, vendors, products, business units, chart of
accounts, document prefixes, expense categories, WHT types. FE: `/customers`, `/vendors`,
`/settings/{companies,company,products,employees,business-units,expense-categories,wht-types}`.
Company tax fields are super-admin-only (§3.4). Customers/vendors carry VAT-registration and
branch data feeding ม.86/4 field 3 and the PV VAT guards.

### 4.2 Sales chain & AR — `docs/manual/api/sales.md`

Document flow **Quotation → Sales Order → Delivery Order → Tax Invoice → Receipt**, plus Credit /
Debit Notes and Billing Notes. Cross-references chain documents; activity log and print-tracking
per document.

- **Quotations / Sales Orders / Delivery Orders** — draft→confirm lifecycle, convert-forward.
- **Tax Invoices** (`/tax-invoices`) — compliance core (§3.1–§3.4). `POST /tax-invoices` creates
  a draft; `POST /tax-invoices/{id}/post` assigns the sequential number and (if configured) fires
  the e-Tax pipeline; `GET /tax-invoices/{id}/pdf` renders the ม.86/4 PDF; `GET .../xml` returns
  the e-Tax XML; posted = immutable. Auth: `sales.tax_invoice.{create,post,read}`.
- **Receipts** (`/receipts`) — ใบเสร็จรับเงิน, applied against invoices.
- **Credit / Debit Notes** (FE `/credit-notes`, `/debit-notes`) — both back onto the single
  `TaxAdjustmentNote` domain entity, discriminated by `TaxAdjustmentNoteType`
  (`CreditNote` ม.86/10 / `DebitNote` ม.86/9), with structured `AdjustmentReasonCode`s.
- **Billing Notes** (`/invoices`, ใบแจ้งหนี้/ใบวางบิล) — non-tax billing documents.
- Print tracking and a unified **Documents** view (FE `/documents`) span the chain.

### 4.3 Purchases & AP + WHT — `docs/manual/api/purchases.md`

Document flow **(Purchase Order) → Vendor Invoice → Payment Voucher → WHT certificate (50ทวิ)**.
FE: `/purchase-orders`, `/vendor-invoices`, `/payment-vouchers`, `/wht-certificates`.

- **Vendor Invoices** record purchases and input VAT (recoverable/non-recoverable split, ม.82/4
  VAT-claim period).
- **Payment Vouchers** settle vendor invoices, derive input VAT per line under the §3.4 guards,
  and compute withholding. PV numbering carries the category segment (§3.3).
- **WHT certificates (50ทวิ)** are generated as filled RD PDFs (§6).

### 4.4 Payroll — `docs/manual/api/payroll.md`

Employees, **payroll runs**, **payslips**, Thai PIT (progressive `ThaiPitCalculator` /
`PitSchedule`), social-security (ปกส./SSO with the statutory contribution cap), and statutory
outputs **ภ.ง.ด.1** (monthly) and **ภ.ง.ด.1ก** (annual; requires a posted run) plus **SSO
สปส.1-10**. FE: `/payroll`. ภ.ง.ด.1 PDF route: `GET /payroll/runs/{id}/pnd1/pdf` (works on a
draft run). 50ทวิ certificates are also produced here.

### 4.5 Tax filings — `docs/manual/api/tax-filings.md`

Computes statutory returns and renders **print-and-file RD PDFs** (no automatic RD submission).
POST compute routes take `?mode=preview|finalize`; `finalize` additionally requires
`tax.filing.finalize` (super-admin bypasses). FE: `/tax-filings` and sub-pages
`{pnd3,pnd51,pnd53,pnd54,pnd36,cit,missing-wht-cert}`.

- **VAT** — `POST /tax-filings/pnd30` (+ `GET /tax-filings/pnd30/pdf`) → ภ.พ.30.
- **WHT** — `POST /tax-filings/{pnd3,pnd53,pnd54}` with `GET .../pdf`; `POST /tax-filings/pnd36`
  (ภ.พ.36 reverse charge, auto-JV on finalize — computed/filed, **no PDF**). RD batch-upload
  `.txt` exports for ภ.ง.ด.3/53.
- **CIT** — `GET /tax-filings/pnd50/pdf` (annual, ม.65/66 base), `GET /tax-filings/pnd50/preview`
  (dry-run); `GET /tax-filings/pnd51/pdf` (mid-year prepayment, ม.67ทวิ method A) +
  `POST /tax-filings/pnd51/estimate` (ม.67ตรี check). CIT figures via `CitCalculator`,
  `CitYearDataService`, `CitYearSummary`/`CitAdjustment`.
- **VAT registration** — ภ.พ.01 / ภ.พ.09 filled PDFs (`VatRegFormService`).
- **Filing history** — `GET /tax-filings` immutable index. **VAT registers** (input/output, ม.87).
- **e-Tax** routes exist but the pipeline is inert (§8).

### 4.6 General ledger & reports — `docs/manual/api/reports.md`, `system.md`

- **GL** — `JournalEntry`/`JournalLine` (schema `gl`), double-entry posting via
  `IGlPostingService`, accounting periods with period close/lock (`AccountingPeriod`,
  `IPeriodCloseService`). Posting refuses into a closed period.
- **Financial reports** — Trial Balance, Profit & Loss, Balance Sheet (`FinancialReportService`,
  `GlReportService`).
- **Tax/sales reports** — monthly Tax Summary (`/reports/tax-summary`, `TaxSummaryService`),
  Sales Summary, VAT report (`VatReportService`), VAT threshold watch (ม.85, 1.8 MB/yr,
  `VatThresholdService`).
- **AP & WHT reports** — AP Aging (`/reports/ap-aging`, `ApAgingService`), WHT Receivable
  (`/reports/wht-receivable`, `WhtReceivableReportService`).
- **Compliance reports** — Number-gap report (`NumberGapReportService`).

---

## 5. Data Model Overview

**EF Core migrations are the source of truth.** The history is a single squashed baseline,
`20260616130322_InitialCreate`, plus raw-SQL bootstrap in `Migrations/SqlScripts/` for compliance
DDL the EF model does not express: **RLS policies**, **immutability triggers**, **views**, and
**reference/demo seed data** (numbered `*.sql` scripts, each applied once and tracked).
`infra/db/schema.sql` is reference-only.

The database uses **nine schemas** (created by `EnsureSchema` in `InitialCreate`). Folder names in
`Accounting.Domain/Entities/*` do not all match DB schema names — the mapping below is the
authoritative DB layout:

| DB schema | Key tables (entities) | Notes |
|---|---|---|
| `master` | companies, company_profiles, branches, business_units, chart_of_accounts, customers, vendors, products, employees | Company tax config (§3.4) lives on `companies`. |
| `sys` | users, roles, permissions, role_permissions, user_roles, api_keys, idempotency_keys, number_sequences, document_prefixes, expense_categories, attachments | Identity/RBAC lives here (no separate `identity` DB schema). |
| `sales` | quotations, sales_orders, delivery_orders, tax_invoices, tax_invoice_lines, receipts, tax_adjustment_notes, billing_notes | CN/DN unified in `tax_adjustment_notes`. |
| `purchase` | purchase_orders, vendor_invoices, vendor_invoice_lines, payment_vouchers, payment_voucher_lines, payment_voucher_applications, po_settlements | |
| `gl` | journal_entries, journal_lines, accounting_periods | Domain folder is `Ledger`. |
| `tax` | tax_codes, tax_rates, wht_types, wht_certificates, tax_filings, cit_year_summaries, cit_adjustments | |
| `payroll` | payroll_runs, payslips | |
| `audit` | activity_log | Append-only; never deleted. |
| `etax` | etax_submissions | Phase-1 scaffolding only (§8). |

All business tables carry `company_id` and an RLS `company_isolation` policy (§2.2). Naming
conventions: `ix_<table>_<col>`, `fk_<table>_<ref>`, `ck_<table>_<rule>`.

> The per-schema table lists above are representative, derived from the entity model and the RLS
> scripts. EF Core migrations (`AccountingDbContextModelSnapshot.cs`) are authoritative for the
> exact, current table set.

---

## 6. Tax-Form / PDF Coverage

The system renders **filled RD AcroForm PDFs** via `RdAcroFormFiller` + per-form fillers, with RD
templates embedded under `Infrastructure/Pdf/Templates/`. These are **print-and-file** outputs —
there is no automatic RD submission (§8).

| RD form | Purpose | Status |
|---|---|---|
| ใบกำกับภาษี (ม.86/4) | Full tax invoice | ✅ Generated (PDF) |
| 50ทวิ | WHT certificate | ✅ Generated (`Wht50TawiFormFiller`) |
| ภ.พ.30 | Monthly VAT return | ✅ Filled PDF (`Pnd30FormFiller`) |
| ภ.ง.ด.1 | Monthly PIT withholding | ✅ Filled PDF (`Pnd1FormFiller`) |
| ภ.ง.ด.1ก | Annual PIT summary | ✅ Filled PDF (`Pnd1aFormFiller`) — requires a posted run |
| ภ.ง.ด.3 | WHT on individuals (+ ใบแนบ) | ✅ Filled PDF (`WhtFormFiller`) + batch `.txt` |
| ภ.ง.ด.53 | WHT on juristic persons (+ ใบแนบ) | ✅ Filled PDF (`WhtFormFiller`) + batch `.txt` |
| ภ.ง.ด.54 | WHT/VAT on payments abroad (ม.70) | ✅ Filled PDF (`Pnd54FormFiller`) |
| ภ.พ.36 | VAT reverse charge | ✅ Computed + auto-JV on finalize — **no PDF filler** |
| ภ.ง.ด.50 | Annual CIT return | ✅ Filled PDF (`Pnd50FormFiller`) |
| ภ.ง.ด.51 | Mid-year CIT prepayment (ม.67ทวิ) | ✅ Filled PDF (`Pnd51FormFiller`) |
| ภ.พ.01 | VAT registration | ✅ Filled PDF (`VatRegFormFillers`) |
| ภ.พ.09 | VAT registration change | ✅ Filled PDF (`VatRegFormFillers`) |
| สปส.1-10 (SSO) | Social-security contribution | ✅ Filled PDF (`Sps110FormFiller`) + batch export |

Fillers are AcroForm-coordinate-mapped and render-verified; numeric figures bind to the same
computation services that drive the dashboards (e.g. ภ.พ.30 PDF, the ภ.พ.30 preview, and the Tax
Summary all read one `Pnd30Filing`).

---

## 7. Versioning, Build & Release

- **Versioning** — [MinVer](https://github.com/adamralph/minver) derives the assembly version
  from git tags (`vX.Y.Z`), surfaced on `GET /system/info` and in the dashboard footer. (Building
  from a `subst` drive that cannot reach `.git` stamps `0.0.0` — build from the real path.)
- **Release automation** — [release-please](https://github.com/googleapis/release-please) turns
  conventional commits on `main` into release PRs (version bump + changelog + tag).
- **CI** — `.github/workflows/ci.yml` builds and tests the backend and type-checks the frontend.
- **Tests** — xUnit + FluentAssertions; integration tests run against a real PostgreSQL
  (`TEAS_TEST_PG`, falling back to Testcontainers). Current verified baseline: Domain 146 tests,
  Api 385 pass / 0 fail / 7 skip (per `progress.md` 2026-06-17). FE gate: `tsc --noEmit` 0.
- **Backend publish** — self-contained `win-x64` / `linux-x64` builds.

---

## 8. Out of Scope / Not Built

Stated plainly so the as-built claim holds. The following are **not** implemented:

- **Live e-Tax / RD e-filing (default-inert; wireable by config).** Phase-1 scaffolding exists: an
  `ETaxSubmissionPipeline`, `ETaxXmlBuilder`, `ETaxSigner` (XAdES helpers), `ETaxEmailSender`, retry
  worker, and a `MockRdEfilingClient`. By **default** there is no live submission to the RD and no
  auto-submit cron: the signer is inert (`ETaxBehaviorOptions.Enabled = false` — never signs/sends at
  runtime) with **no real signing certificate wired**, and the RD client is the `MockRdEfilingClient`
  (synthetic ACK). However, this is a **config toggle, not a hard wall**: `AddInfrastructure`
  (`DependencyInjection.cs:131-134`) registers `RdHttpEfilingClient` as the active `IRdEfilingClient`
  whenever `RdApi:Provider != "Mock"` — at which point the client makes **real outbound HTTP calls** to
  the configured `RdApi:BaseUrl`. The Phase-1 default keeps `Provider = "Mock"` (inert); flipping it to
  a real provider + wiring a cert (`ETax:Signing:PfxPath/PfxPassword`) + `ETax:Enabled = true` enables a
  live path. See the tier matrix in `docs/etax-environment-tiers.md` (Tier 1 mock → Tier 2 UAT → Tier 3
  prod, config-only). Reference: `docs/etax-xades-spec.md`, `docs/etax-environment-tiers.md`.
- **Automatic ภ.พ.30 submission.** `auto` mode is inert (§3.5) — print-and-file only.
- **Simplified Tax Invoice (ม.86/6).** Intentionally not supported — full ม.86/4 only.
- **Inventory / stock management.** No FIFO/perpetual stock, warehouses, or stock balances. (LIFO
  is illegal under TAS 2 and is never an option.)
- **Same-day void & reissue.** Corrections are Credit Notes, not voids.
- **PR → PO → GR three-way match.** No purchase requisitions or goods-receipt matching.
- **Cash Flow statement; Budget vs Actual.** Not implemented.
- **Cash & Bank reconciliation module.** Not implemented as a standalone module.
- **Formal performance/load testing; disaster-recovery / HA.** Not in scope.

**Forbidden by policy (never propose):** MS SQL Server (the DB is PostgreSQL 16+), LIFO costing,
plaintext PII, or skipping the `company_id`/RLS tenant filter.

---

## 9. Tech Stack

| Layer | Choice |
|---|---|
| Backend | C# / .NET 10 LTS, ASP.NET Core Minimal APIs, EF Core 10 |
| Database | **PostgreSQL 16+** via Npgsql; EF migrations authoritative; RLS + triggers in SqlScripts |
| Frontend | Next.js 15 (App Router) + React, TypeScript 5, Tailwind 3, shadcn/Radix (+ DaisyUI) |
| State / forms | React Query (TanStack) v5 · React Hook Form + Zod |
| Auth | OAuth2 + JWT (`JwtBearer`); BCrypt password hashing; per-company RBAC |
| i18n | `next-intl` — TH primary, EN secondary |
| PDF | PdfSharp + AcroForm fillers; Sarabun font |
| Test | xUnit + FluentAssertions + Testcontainers (BE) · Playwright (FE e2e) |
| Build / release | MinVer (git-tag versioning) · release-please · GitHub Actions CI |
