# etax-schemas/ — ETDA มกค.14-2563 XSD set

**Purpose:** local XSD validation of signed e-Tax XML before send
(`IETaxXmlValidator` → `LocalXsdValidator`).

## Status: schemas NOT committed (intentional)

The official ETDA standard **มกค.14-2563** XSD files are **not** committed to
this repo. Reasons (Sprint 13c — flagged, not improvised):

1. They are an external, controlled ETDA artifact — fabricating placeholder
   XSDs would produce *false* validation results (worse than none).
2. The build/test environment cannot fetch + verify the authoritative file
   (no guessed URLs per CLAUDE.md). Real-schema refresh is an explicit
   out-of-scope item (Answer-Sana-Backend18 §10: "XML schema auto-update from
   ETDA — manual schema refresh").

## Behaviour without schemas (Tier 1 — current)

`LocalXsdValidator` finds no `*.xsd` here → `ValidateAsync` returns
`IsValid=true` (graceful skip). The dev pipeline is **not** blocked.
`ETax:Validation:RequireSchemaPass` is `false` in Tier 1.

---

## Authoritative source (verified 2026-05-22)

The XSDs are maintained by ETDA at the **TEDA GitLab mirror**:

- **Master = มกค.14-2563 V2.0** (use this — RD/TEDA Validation Portal currently
  accepts this profile):
  `https://gitlab.com/etdath-teda-schema/teda-objects/common/e-tax-invoice-receipt`
- **V2.1** (added 2025-10):
  `https://gitlab.com/etdath-teda-schema/teda-objects/common/e-tax-invoice-receipt-v2.1`
  Do **not** swap to V2.1 until ETDA confirms Tier-2 acceptance — V2.0 is what
  the current `TaxInvoice_CrossIndustryInvoice_2p0.xsd` references.
- GitLab UI mirror at: `https://schemas.teda.th/teda/...`
- TEDA Web Validation Portal (third-party reference validator that consumes
  these schemas + cert trust roots): `https://validation.teda.th/th/validate`

A historical GitHub mirror exists at `https://github.com/ETDA/XMLValidation`
(v0.2.3, 2018) — older snapshot; **prefer the GitLab source** unless GitLab is
unreachable.

## Land the schemas (one-time, ~30 seconds)

Run from `U:\backend\` (or whichever prefix points at the repo root):

```powershell
cd U:\backend

# 1. clone the full TEDA repo to a staging dir
git clone https://gitlab.com/etdath-teda-schema/teda-objects/common/e-tax-invoice-receipt.git etax-schemas-staging

# 2. copy XSDs + Schematron + dependent code-lists into etax-schemas/
robocopy etax-schemas-staging\ETDA\data\standard      etax-schemas\ETDA\data\standard      *.xsd *.sch /E
robocopy etax-schemas-staging\ETDA\codelist\standard  etax-schemas\ETDA\codelist\standard  *.xsd /E
robocopy etax-schemas-staging\uncefact                etax-schemas\uncefact                *.xsd /E

# 3. drop the staging clone
Remove-Item etax-schemas-staging -Recurse -Force

# 4. flip RequireSchemaPass on (dev only — Tier 1 still skips if loader can't
#    find a root; the real switch happens here):
#    appsettings.Development.json → "ETax:Validation:RequireSchemaPass": true

# 5. smoke-test the loader: build + run any TI-post test that triggers the
#    pipeline (Sprint13cEtaxPipelineTests) — should now exercise schema check
dotnet test backend/tests/Accounting.Api.Tests/Hardening/Sprint13cEtaxPipelineTests.cs

# 6. commit
git add etax-schemas/
git commit -m "Land ETDA มกค.14-2563 V2.0 XSD set (from schemas.teda.th)"
```

## File checklist (V2.0 master — refresh after clone)

After step 2 above, the layout under `etax-schemas/` should look like:

```
etax-schemas/
├── ETDA/
│   ├── data/standard/
│   │   ├── TaxInvoice_CrossIndustryInvoice_2p0.xsd        ← Tax Invoice root
│   │   ├── Invoice_CrossIndustryInvoice_2p0.xsd
│   │   ├── CancellationNote_CrossIndustryInvoice_2p0.xsd  ← void/credit note
│   │   ├── *_ReusableAggregateBusinessInformationEntity_*.xsd
│   │   ├── QualifiedDataType_1p0.xsd
│   │   ├── TaxInvoice_Schematron_2p0.sch                  ← business rules
│   │   └── AbbreviatedTaxInvoice_Schematron_2p0.sch
│   └── codelist/standard/
│       ├── ThaiDocumentNameCode_Invoice_1p0.xsd
│       ├── ThaiISOCountrySubdivisionCode_1p0.xsd
│       └── ...
└── uncefact/
    ├── data/standard/UnqualifiedDataType_16p0.xsd
    └── codelist/standard/*.xsd
```

`LocalXsdValidator` resolves XSDs via the `ETax:Validation:XsdSchemaDir` config
key (default `etax-schemas/`). XSDs reference each other via relative
`schemaLocation` — copy preserves the directory layout, do **not** flatten.

## Tier checklist (binding)

| Step | Tier 1 (local dev) | Tier 2 (UAT) | Tier 3 (prod) |
|---|---|---|---|
| XSDs present | optional (graceful skip) | **required** | **required** |
| `RequireSchemaPass` | `false` | `true` | `true` |
| Test against `validation.teda.th` | recommended | required | smoke before cutover |

## Refresh policy

Re-run the clone every **6 months**, on every **ETDA schema bump** announced via
`ratchakitcha.soc.go.th` ETDA notice, and immediately when TEDA Validation
Portal starts rejecting valid TEAS XML with structural errors. Record the
download date + commit SHA in the table below.

| File set | Source commit | Version | Last refreshed |
|---|---|---|---|
| V2.0 master | _set on next clone_ | มกค.14-2563 | _—_ |
