# Spec — Per-Company VAT Mode (config → companies table)

**Date:** 2026-06-11 · **Approved by:** Ham (chat, 2026-06-11) — explicit §4.6 rule change.
**Goal:** one multi-tenant instance hosts companies with different VAT registration status.
`Tax:VatMode` / `Tax:VatRate` / `Tax:Pnd30SubmissionMode` move from per-instance env config
to per-company columns on `companies`. Non-VAT doc labels stay in config (cosmetic, instance-wide).

## 1. Compliance decision (§4.6 amendment)

Old rule: VAT mode/rate are env-only, never UI settings (audit via git+deploy).
New rule: VAT mode/rate/ภ.พ.30 mode are **company master data**:
- Settable at company creation (`POST /companies`, permission `Master.CompanyManage` = super-admin).
- Changeable only via `PUT /companies/{id}` (same permission) → every change of a tax field is
  recorded in `audit.activity_log` (old → new values).
- **Never** exposed as a regular user-facing settings UI. Intent of §4.6 (no casual flips,
  full audit trail) preserved — trail moves from git to activity_log.

## 2. Schema (EF migration `CompanyTaxConfig`)

`companies` (entity `Company`):
- `vat_registered` — already exists. Becomes the live VAT-mode switch.
- ADD `vat_rate NUMERIC(5,4) NOT NULL DEFAULT 0.07`
- ADD `pnd30_submission_mode TEXT NOT NULL DEFAULT 'manual'`
  + `ck_companies_pnd30_submission_mode CHECK (pnd30_submission_mode IN ('manual','auto'))`

Backfill: defaults are correct for existing rows (dev company 1 is VAT-registered).

## 3. Application layer

New `Accounting.Application/Abstractions/ICompanyTaxConfigService.cs`:

```csharp
public sealed record CompanyTaxConfig(
    bool VatMode, decimal VatRate, string Pnd30SubmissionMode,
    string NonVatDocLabelTh, string NonVatDocLabelEn);

public interface ICompanyTaxConfigService
{
    /// <summary>Tax config of the current tenant's company (per-request cached).</summary>
    Task<CompanyTaxConfig> GetAsync(CancellationToken ct);
}
```

DTO changes (`CompanyDtos.cs`): `CreateCompanyRequest` + `UpdateCompanyRequest` + `CompanyDto`
gain `decimal VatRate = 0.07m` and `string Pnd30SubmissionMode = "manual"` (optional w/ defaults —
additive, existing callers unaffected). Validators: `VatRate` 0–1; mode `manual|auto`.

## 4. Infrastructure layer

- `CompanyTaxConfigService` (scoped): reads the `ITenantContext.CompanyId` row from
  `AccountingDbContext.Companies` (AsNoTracking), caches the result in a field for the
  request lifetime. Labels come from `VatModeOptions` (unchanged config).
- `VatModeOptions`: keep class + registration, but **only labels remain meaningful**;
  `VatMode` + `Pnd30SubmissionMode` properties deleted after consumer swap.
- `CompanyService.CreateAsync/UpdateAsync`: map new fields; on update, when any of
  (`VatRegistered`, `VatRate`, `Pnd30SubmissionMode`) changes → `IActivityRecorder` entry
  with old/new values (§4.6 audit).

### Consumer swap (mechanical — 12 files)

Replace `IOptions<VatModeOptions> vat` injection with `ICompanyTaxConfigService taxCfg`;
at each use site `vat.Value.VatMode` → `(await taxCfg.GetAsync(ct)).VatMode` (fetch once
per method into a local). Files:
AttachmentService, VatThresholdService, BillingNoteService, QuotationChainServices,
ReceiptService, SalesChainPdfService, SalesOrderDeliveryServices, TaxAdjustmentNoteService,
TaxInvoiceService, TaxFilingService, WhtFilingService (+ PaperDocModel mappers already take
`ShowVat` bool — only the mapper call sites change source).

## 5. API layer

- `/system/info` (Program.cs): becomes **authenticated**; `vat_mode`/`vat_rate`/
  `pnd30_submission_mode` come from `ICompanyTaxConfigService` (current company).
  FE `useSystemInfo()` already calls with JWT — no FE change.
- `/api/v1` system info (ApiV1Endpoints.cs:139): same swap (API-key carries company).
- `TaxConfig` (Program.cs): remove now-dead `VatMode`/`VatRate`/`Pnd30SubmissionMode`
  reads if nothing else consumes them (verify with grep before deleting).
- OpenAPI: companies create/update/list gain `vat_rate`, `pnd30_submission_mode`; note delta for Sana.

## 6. Tests

Existing 5 test files inject `["Tax:VatMode"]` via in-memory config — that path dies.
Replacement pattern (**no mutation of shared company 1** — parallel suites share `teas_test`):
- TestKit helper: create a fresh company (TestIds.TaxId()) with desired `VatRegistered`/`VatRate`
  + an admin user in that company (direct DbContext seed + password hasher), return JWT.
- Non-VAT scenarios run against that company. VAT-mode=true scenarios keep company 1.
- New tests: CompanyTaxConfigService reads correct row; PUT /companies tax-field change writes
  activity_log; ck constraint rejects bad pnd30 mode.
- Gate: build 0/0 · all suites ≥ baseline · new/changed tests pass 2× consecutive on `teas_test`.

## 7. Docs / bookkeeping (main agent)

- CLAUDE.md §4.6 rewritten (per-company, audit via activity_log, still never a user-facing UI toggle).
- CLAUDE.md §6 dev note: non-VAT testing = flip `companies.vat_registered` (or seed helper), not appsettings.
- progress.md entry + plan.md tick.

## 8. Execution order

1. **Main:** migration + entity + DTOs + ICompanyTaxConfigService + impl + DI + CompanyService
   audit + endpoints + build green. (schema/compliance core — not delegated)
2. **Subagent A (sequential):** consumer swap (12 files) until solution builds 0/0 + existing
   Domain tests ≥ baseline.
3. **Subagent B (sequential):** test migration per §6 above, 2× green.
4. **Main:** consolidated gate, docs, progress/plan. No commit unless Ham asks.
