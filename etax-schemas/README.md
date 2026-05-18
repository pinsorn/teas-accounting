# etax-schemas/ — ETDA มกค.14-2563 XSD set

**Purpose:** local XSD validation of signed e-Tax XML before send
(`IETaxXmlValidator` → `LocalXsdValidator`).

## Status: schemas NOT committed (intentional)

The official ETDA standard **มกค.14-2563** XSD files
(`TaxInvoice.xsd`, `Receipt.xsd`, `Common.xsd`) are **not** committed to this
repo. Reasons (Sprint 13c — flagged, not improvised):

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

## Tier 2 / Tier 3 prerequisite (ops task)

Before UAT/production cutover:

1. Download the current ETDA มกค.14-2563 XSD set from
   `https://www.etda.or.th` (Standard e-Tax Invoice & e-Receipt).
2. Drop `TaxInvoice.xsd`, `Receipt.xsd`, `Common.xsd` into this directory
   (and ensure they are on the `ETax:Validation:XsdSchemaDir` path resolved
   from the API ContentRoot).
3. Set `ETax:Validation:RequireSchemaPass = true`.
4. Record the source URL + version + download date in this file.

| File | Source | Version | Last checked |
|---|---|---|---|
| TaxInvoice.xsd | _pending ops_ | มกค.14-2563 | _—_ |
| Receipt.xsd (Phase 2 e-Receipt) | _pending ops_ | มกค.14-2563 | _—_ |
| Common.xsd | _pending ops_ | มกค.14-2563 | _—_ |
