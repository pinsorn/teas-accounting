# Backend Review — TEAS (2026-06-19)

Reviewer: Claude (adversarial backend). Scope: `backend/src/**`. Review-only; no edits/build/commit.
Crown-jewel focus: D1 compliance (SQL triggers/RLS + app layer) and D3 security on the just-shipped
MCP agentic surface (commits 06fc16f / dfe0636 / 03d54d6).

---

## D1 Compliance

- **[CRITICAL]** `Accounting.Infrastructure/Sales/TaxInvoiceService.cs:296,303` — `PostAsync` allocates the
  document number and runs the period gate against the **stale draft `ti.DocDate`** (the create-date pinned
  at draft time, `:137/:241`), NOT the post date. The agent flow is *draft-now / human-posts-later*: a draft
  created in month M and posted in M+1 is assigned an **M-period sequence number** (`NextAsync(..., ti.DocDate)`)
  and keeps `TaxPointDate = create-date`. This violates the tax-point = issue date rule and the monthly-reset /
  sequential-no-gap numbering rule, and `EnsureOpenAsync(ti.DocDate)` will throw if M is already closed (false
  failure). The MCP surface makes the cross-month gap reachable in normal use. Fix: in `PostAsync`, re-pin
  `ti.DocDate = ti.TaxPointDate = _clock.TodayInBangkok()` (re-run `EnsureOpenAsync` on the new value) **before**
  `_numbers.NextAsync`, so the number, period and tax-point all reflect the issue date. (ม.86/4(7) / ม.78; §4.3)
- **[CRITICAL]** `Accounting.Infrastructure/Purchase/PaymentVoucherService.cs:298,308` — same stale-date defect in
  PV `PostAsync` (`EnsureOpenAsync(pv.DocDate)` + `NextAsync(..., pv.DocDate)` use the draft date). A PV post issues
  the WHT 50ทวิ certificate, so a cross-month draft→post mis-dates the certificate period. Fix: re-pin to
  `TodayInBangkok()` at post, mirroring the create path. (§4.3; ภ.ง.ด. period correctness)
- **[MAJOR]** `Accounting.Infrastructure/Migrations/SqlScripts/040,060,570,571` — posted-doc immutability triggers
  exist on the **headers** (`tax_invoices`, `vendor_invoices`, `receipts`, `tax_adjustment_notes`,
  `journal_entries`) but there is **no DB-level trigger on any `*_lines` table** (`tax_invoice_lines`,
  `receipt_lines`, `vendor_invoice_lines`, etc.). The header trigger freezes header totals, so a raw
  `UPDATE sales.tax_invoice_lines SET unit_price=… WHERE …` on a POSTED parent is **not blocked at the DB layer**
  — only by app code. The rubric requires immutability to live in triggers, not app code. (Mitigant: the posted GL
  `journal_entries` rows are header-frozen, so the *ledger* total is protected; the printed Tax Invoice line detail
  is not.) Fix: add a `BEFORE UPDATE/DELETE` trigger on each line table that raises when the parent header
  `status = 'POSTED'` (join parent in the trigger fn). (ม.86/4 #2 / §4.2)
- **[MINOR]** `Accounting.Infrastructure/Purchase/PurchaseOrderService.cs:51,100` — PO create/edit store
  `DocDate = req.DocDate` (trusts caller input) instead of `_clock.TodayInBangkok()` as every tax document does.
  A PO is an internal, non-tax document so this is not a ม.86/4 breach, but it is inconsistent with the §10
  "never trust user `doc_date`" convention and lets an agent backdate a PO. Fix: pin to Bangkok-today (or
  document the intentional exception). (§10)

Verified correct (no finding): header immutability triggers (040/020) freeze doc_no/dates/amounts/company/branch;
`audit.activity_log` is append-only via UPDATE+DELETE triggers (030) and DB-role revoke; e-Tax submissions
append-only (300); RLS `ENABLE + FORCE + company_isolation` present on all MCP-reachable business tables
(010, 040, 060, 200, 322/323, 500, 570/571, 572 sales-chain, 573 purchase-chain); doc number allocated only at
POST via atomic UPSERT on `sys.number_sequences` with a unique period index (no gap, monotonic);
TaxInvoice/VendorInvoice create pin `DocDate=TaxPointDate=TodayInBangkok()` ignoring request input.

---

## D2 Correctness

- **[MINOR]** `Accounting.Infrastructure/Identity/ApiKeyResolver.cs:18,70` — `LastTouchUtcTicks` is a `static`
  process-wide `ConcurrentDictionary<long,long>` that only ever grows (one entry per ApiKeyId, never evicted).
  Unbounded over the lifetime of many keys; also not shared across instances (each replica re-touches every
  5 min). Low impact. Fix: cap size / periodic prune, or accept and document.
- No `.Result` / `.Wait()` / `Task.Run` / `.GetAwaiter().GetResult()` in any request/async path (swept
  `backend/src` — zero matches). Money is `decimal` throughout Domain (no `double`/`float` on monetary fields).
  Dates use `DateTimeOffset` + `_clock.TodayInBangkok()` for doc dates (CE calendar). ProblemDetails / stable
  error-envelope out, domain exceptions in. These are clean.

---

## D3 Security (MCP agentic surface)

- **[MAJOR]** `Accounting.Api/Mcp/TeasMcpTools.cs:833` (`get_document_status`) — gated only on
  `Authorize(Policy = TaxInvoiceRead)` (`apiperm:sales.tax_invoice.read`) yet it returns status + **DocNo** for
  **all six document types** (tax-invoice, quotation, receipt, purchase-order, vendor-invoice, payment-voucher)
  by id, with **no restriction to the key's own drafts**. A key holding only `sales.tax_invoice.read` can
  enumerate ids and harvest the DocNo/status/existence of *purchase orders, vendor invoices and payment vouchers*
  it has no read scope for. The class already defines `PurchaseOrderRead` / `VendorInvoiceRead` /
  `PaymentVoucherRead` etc. Fix: require the per-type read scope inside each `switch` arm (resolve the scope from
  `type`, authorize before querying), instead of a single tax-invoice-read gate.
- **[MAJOR]** `Accounting.Api/Mcp/TeasMcpTools.cs:760` (`list_pending_approvals`) — same single-scope gate
  (`TaxInvoiceRead`) but the result enumerates pending PO / VI / PV / quotation / receipt drafts (type, id, DocNo,
  CreatedAt, approval URL). A key with only `sales.tax_invoice.read` learns of pending purchase documents across
  types. (Slightly softer than the above because it is filtered to `CreatedViaApiKeyName == callingKey`, so it is
  the key's *own* drafts — but cross-type scope is still not enforced.) Fix: filter the returned item types to the
  scopes the key actually holds (check `ctx.User` scopes per type), or split into per-type tools each carrying its
  own `*Read` policy.
- **[MINOR]** `Accounting.Infrastructure/Identity/ApiKeyService.cs:148` (`EnforceMcpNoPostGuard`) — the mcp-kind
  no-post guard only rejects scopes ending in **`.post`**. It does NOT reject `.approve`, `.issue`, `.send`,
  `.void`, `.cancel` or `.manage` finalising scopes. Today this is latent / defense-in-depth (no MCP tool exposes
  any non-draft transition — the live gate is the tool surface, which offers only `.read` + `.create` + master
  `.manage`), so it is not currently exploitable. But the guard's intent ("an mcp key can only draft; a human
  posts") is under-enforced: a future `.approve`/`.issue` scope could be granted to an mcp key and the guard would
  pass it. Fix: whitelist the allowed mcp suffixes (`.read`, `.create`, master `.manage`) and reject everything
  else, rather than blacklisting only `.post`.
- **[MINOR]** `docs/api/openapi.yaml:2521` vs `Accounting.Api/Program.cs:278-280` — the `/mcp` endpoint is
  documented "Intended for **mcp-kind keys only**", but the runtime policy (`ApiKeyOnlyPolicy`) accepts **any**
  valid API key, including an `integration`-kind key that legitimately holds `.post` scopes. Not a hole (the MCP
  tools themselves only expose read/draft, so a post-capable key gains nothing extra at `/mcp`), but it is a
  doc-vs-impl drift and a least-privilege gap. Fix: enforce `kind == mcp` at `/mcp` (claim check), or soften the
  doc wording.

Verified correct (no finding): API keys are stored as bcrypt hashes + a 16-char deterministic lookup prefix only
(plaintext shown once, Stripe pattern); resolver looks up by prefix then bcrypt-verifies the full secret
(`ApiKeyResolver.cs:38-41`). `PermissionHandler` authorizes api-key principals against the key's CSV scopes with
**ordinal exact match** and **never** grants the super-admin bypass (`PermissionRequirement.cs:20-29`).
API-key principals carry **no** `IsSuperAdmin` claim, so they cannot trip the RLS `app.is_super_admin` escape
hatch. `TenantMiddleware` sets `app.company_id` / `app.is_super_admin` via **parameterised** `set_config(...)`
(no string interpolation → no SQL injection) and resets on a `finally` to avoid poisoning the pooled connection.
Every MCP write tool calls `CreateDraftAsync` only (no post/issue/send/approve tool exists); the approval deep-link
is a plain URL gated by the human's session + `.post` permission, not a one-click-post token. Audit writes on key
create/revoke/rotate are secret-free (never hash/plaintext). The PDF-url guards correctly reject `"Draft"` (the
enum `.ToString()` is PascalCase `"Draft"`, matching the literal — the earlier case-mismatch concern is resolved).
`create_*` master-data tools (customer/product/vendor) write immediately, which is acceptable: master data is
mutable and carries no tax-point.

---

## D4 Spec drift

- **[MINOR]** Individual MCP tools (24 of them in `TeasMcpTools.cs`) are not enumerated anywhere in
  `docs/api/openapi.yaml` or `docs/accounting-system-plan.md` beyond the single `/mcp` transport entry. They are
  not REST endpoints so strict OpenAPI coverage is not expected, but there is no machine- or human-readable
  contract listing the tool names, scopes and draft-only semantics outside source. Fix: add a short MCP tool
  inventory to `accounting-system-plan.md` (or a `docs/mcp-tools.md`) so the agentic surface is documented as-built.
- i18n (`messages/th.json` ↔ `en.json`) is a frontend concern — out of backend scope, deferred to the FE reviewer.

Verified correct (no finding): `/api-keys`, `/api-keys/{id}`, `/rotate` and the `kind` enum
(`integration | mcp`) ARE documented in `openapi.yaml:2326-2398`; the `/mcp` HTTP transport is documented at
`:2518-2534`; `ApiKeyAuth` security scheme is declared.

---

## Summary table

| Dimension | Critical | Major | Minor |
|---|---|---|---|
| D1 Compliance | 2 | 1 | 1 |
| D2 Correctness | 0 | 0 | 1 |
| D3 Security (MCP) | 0 | 2 | 2 |
| D4 Spec drift | 0 | 0 | 1 |
| **Total** | **2** | **3** | **5** |
