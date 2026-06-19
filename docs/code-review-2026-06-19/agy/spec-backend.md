# AGY (Gemini) review — Spec + Backend (2026-06-19)

> Independent second-model review. agy explored doc-numbering, TaxInvoiceService.PostAsync/CreateDraftCoreAsync,
> PaymentVoucherService, TeasMcpTools, ApiV1Endpoints, SalesChainEndpoints, openapi.yaml, accounting-system-plan,
> etax specs, ETaxSigner, DependencyInjection. Verbatim findings below (file refs point at the throwaway copy
> `C:\agy-review\teas`; map to repo root).

### D1 Compliance (Thai Law)

#### Critical — TaxInvoiceService.cs:173-195 — WHT bypass via client-supplied ProductType
In `CreateDraftCoreAsync`, `needType` is evaluated only for lines where `string.IsNullOrEmpty(l.ProductType)`. A caller
submitting a draft with `l.ProductType = "GOOD"` for a ProductId registered as a `Service` bypasses the DB lookup;
`SalesLineBackstop.Resolve` is invoked with `EmptyProductTypes`, so it can't fetch the master product type. External
clients / AI agents can override services as goods, bypassing 3%/5% WHT (Section 50 bis) when PVs settle those lines.
**Fix:** resolve ProductType of all referenced ProductIds directly from the Products DB set, ignoring client-supplied
`ProductType`; pass the resolved map to `SalesLineBackstop.Resolve` instead of `EmptyProductTypes`.

#### Major — ETaxSigner.cs:38-62 — single signature vs two-signature requirement
`etax-xades-spec.md` §4 + ETDA recommendation มคก.14-2563 mandate a two-signature pattern (software cert + organization CA
cert). `ETaxSigner.SignAsync` invokes `signer.Sign` once (leaf cert only) → single enveloped signature; RD's strict
validator would reject. **Fix:** sign twice (software then organization cert), appending both `<ds:Signature>` siblings.
*(NB: e-Tax pipeline is Phase-1 inert per §8 — severity to be weighed against that.)*

### D2 Correctness

#### Critical — DocumentNumber.cs:11-13 — regex crash on hyphenated sub-prefix
`TryParse` sub-prefix group is `(?<sub>[A-Z]{2,10})` (letters only). `PaymentVoucherService.PostAsync` builds
`subPrefix = $"{buCode}-{pv.SubPrefix}"` when a BU is present → a hyphen in the sub-prefix (e.g. `MKT-RENT`). The number
`05-2026-PV-MKT-RENT-0001` fails `TryParse` → `ArgumentException` when the number is parsed/loaded from DB.
**Fix:** allow hyphen-joined groups: `(?<sub>[A-Z]{2,10}(?:-[A-Z]{2,10})*)`.

#### Major — BillingNoteEndpoints.cs:44 — cancel body mismatch vs openapi
Endpoint expects `[FromBody] ReasonBody`; `openapi.yaml` defines `/billing-notes/{id}/cancel` with no requestBody.
Spec-following clients send no body → HTTP 400. **Fix:** add the `ReasonBody` requestBody to openapi.

### D3 Security

#### Minor — SalesChainEndpoints.cs:41-46 — quotation reject/cancel authz granularity
`/quotations/{id}/reject` and `/quotations/{id}/cancel` accept a `ReasonBody` but lack granular permission checks for who
may cancel/override the workflow. **Fix:** apply explicit policy authorization tags.

### D4 Spec Drift

#### Major — openapi.yaml — api/v1 + MCP routes omitted
`/api/v1/{tax-invoices,receipts,quotations,customers,products}` list/detail and all `/api/v1/*/pdf` routes (from
`ApiV1Endpoints.cs`), plus all MCP tools (`TeasMcpTools.cs`), absent from openapi. **Fix:** document under External API
v1 + MCP tags.

#### Major — openapi.yaml:16-18 — base-URL path conflict
Servers base path is `/v1`; C# maps `/api/v1`. Spec base + spec paths → `/v1/tax-invoices` → 404. **Fix:** align server
URL to `/api/v1` (or change route mapping).

#### Major — openapi.yaml — ghost / mismatched lifecycle routes
- Quotations: spec `submit` + `revise` (ghost); backend implements `send`, no `revise`.
- Sales Orders: spec `confirm`; backend `post`.
- Delivery Orders: spec `post`; backend `issue` + `mark-delivered`.
**Fix:** sync openapi path strings with `SalesChainEndpoints.cs`.

#### Minor — accounting-system-plan.md §8 — e-Tax "inert" contradicts DI wiring
§8 says e-Tax/RD module is inert Phase-1 scaffolding, but `DependencyInjection.cs` registers `RdHttpEfilingClient` as the
active `IRdEfilingClient` whenever `RdApi:Provider` != `"Mock"` → real outbound RD network calls possible in UAT/prod.
**Fix:** document that `RdHttpEfilingClient` can be configured active.

### Summary
| Dimension | Critical | Major | Minor |
|---|---|---|---|
| D1 Compliance | 1 | 1 | 0 |
| D2 Correctness | 1 | 1 | 0 |
| D3 Security | 0 | 0 | 1 |
| D4 Spec Drift | 0 | 3 | 1 |
