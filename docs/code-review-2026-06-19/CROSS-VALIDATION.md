# TEAS Codebase Review — Cross-Validation (2026-06-19)

Two independent reviews against one shared rubric (`RUBRIC.md`), then adjudicated by the overseer
**against the actual source** (not collated).

- **Claude** (3 subagents): `claude/spec.md`, `claude/backend.md`, `claude/frontend.md`
- **AGY / Gemini** (different model, throwaway copy): `agy/spec-backend.md`, `agy/frontend.md`
- **Adjudication** = overseer opened the cited files and broke each tie with evidence (file:line below).

Ruling legend: ✅ confirmed · ⬇️ severity downgraded · ⬆️ upgraded · ⚖️ reconciled · ❌ rejected (false positive).

---

## A. Both reviewers agree → highest confidence

| # | Finding | Sev (adjudicated) | Evidence |
|---|---|---|---|
| A1 | **openapi.yaml drift**: `/api/v1/*` (list/detail/PDF) + all MCP tools undocumented; lifecycle path names diverge (quotations `send` vs spec `submit/revise`; SO `post` vs `confirm`; DO `issue/mark-delivered` vs `post`) | **Major** | Claude spec #1 + agy D4; `ApiV1Endpoints.cs`, `SalesChainEndpoints.cs` vs `openapi.yaml` |
| A2 | **i18n parity is clean** — th.json = en.json = 1453 keys, zero key drift (both reviewers independently counted) | n/a (PASS) | both FE reviews |
| A3 | **Hardcoded Thai strings** bypass the message bundles (receipts/[id], receipts/new, tax-invoices/new, layout meta, api-keys) | Minor | agy FE D4 + Claude FE notes |
| A4 | **Login route leaks `e.stack` to the client** on 500 | **Major** (both found; agy=crit, Claude=minor → split to Major) | ✅ verified `app/api/auth/login/route.ts:67` — `detail = ...${e.stack}` is **unguarded** (no `NODE_ENV` gate; the token-cookie `secure` flag *is* env-gated, the stack is not) |

## B. Conflicts adjudicated against source (the real value-add)

### B1 — agy CRITICAL "DocumentNumber regex crashes on hyphenated sub-prefix" → ⬇️ **Minor (latent)**
agy: `(?<sub>[A-Z]{2,10})` (`DocumentNumber.cs:12`) rejects the hyphenated sub-prefix that
`PaymentVoucherService.cs:306` builds (`$"{buCode}-{pv.SubPrefix}"`, e.g. `MKT-RENT`) → `ArgumentException`.
**Ruling:** the hyphenated sub-prefix is real (PV is the only doc with both BU **and** category), **but**
`grep DocumentNumber.(Parse|TryParse)` = **zero callers in `backend/src`** — numbers are built via
`NextAsync`/`Build` (no validation) and stored as plain strings; nothing round-trips them through the
regex. So **no live crash today** — a latent landmine if anyone ever calls `Parse` on a BU-scoped PV/VI
number. Fix: widen the `sub` group to `(?<sub>[A-Z]{2,10}(?:-[A-Z]{2,10})*)` so the VO matches what the
system emits.

### B2 — agy CRITICAL "WHT bypass via client-supplied ProductType" → ⚖️ **Major (mechanism corrected)**
agy claimed `SalesLineBackstop.Resolve` gets `EmptyProductTypes`, blocking the master lookup.
**Ruling:** that reasoning is wrong — `TaxInvoiceService.cs:178-195` **does** fetch ProductType from the
Product master and apply it; `EmptyProductTypes` at :214 is a spent fallback. The VAT *rate* is also
correctly derived from tax-code classification, not trusted (:198-217) — obs 8084 hole is closed.
**Real kernel that survives:** the master snapshot only fires when the caller leaves `ProductType` **empty**
(:174). A caller (MCP agent) sending a *non-empty wrong* type (Service→`GOOD`) is trusted → corrupts the
goods/service split / downstream WHT base. **Major**, clean fix: for any product-linked line, always
resolve `ProductType` from the master and ignore caller input (mirror the VAT-rate treatment).
*(Framing precision: this is the **sales** TI path — WHT is withheld purchase-side, so the real impact is
goods/service-**split integrity**, not WHT evasion per se. Fix is unchanged.)*

### B3 — agy CRITICAL "FE approve gate bypassed (relies on `?action=approve`)" vs Claude "gating complete (PASS)" → ⚖️ **Major (UX / defense-in-depth, not a security hole)**
**Ruling:** the **hard** gate is server-side and intact — MCP keys are barred from `.post` scopes
(`ApiKeyService.EnforceMcpNoPostGuard`; Claude BE verified), so an agent can only DRAFT; a human posting
**is** the approval. agy's "CRITICAL bypass" overstates it: there is no privilege escalation. But agy's
real point stands — on normal navigation the detail page neither surfaces that a draft was agent-created
nor steers the human to the review banner, even though the data exists (`TaxInvoiceService.cs:272`
stamps `CreatedViaApiKeyName`). **Major UX/informed-approval gap.** Fix: drive the banner + a distinct
"approve agent draft" affordance off `createdViaApiKeyName` on the detail response, not off a URL param.
**Load-bearing fact verified:** `ApiKeyService.EnforceMcpNoPostGuard` (`:148-152`) runs at the grant site
(`CreateAsync:71`) and rejects any `.post` scope for `kind=mcp` keys → an MCP key *structurally* cannot
post. The downgrade from CRITICAL is therefore safe.

### B4 — agy MAJOR "formatDate uses Buddhist calendar = non-compliant" → ❌ **Rejected (by design)**
CLAUDE.md §5 bans Buddhist era **internally**; **display** in Buddhist era is the Thai convention (RD tax
forms themselves use พ.ศ.). **Verified `lib/utils.ts:24-44`:** `formatDate` is display-only (Buddhist string,
`timeZone: 'Asia/Bangkok'`); the *value* helper `bangkokToday()` uses `en-CA` **Gregorian** `yyyy-MM-dd`, and
the comment confirms `doc_date` is server-authoritative. The Buddhist output never feeds a backend value /
filename / key. **Rejection stands.** *(Flag for Ham only if a specific report must print ค.ศ.)*

### B5 — Claude BE MAJOR "get_document_status cross-type enumeration" vs "list_pending_approvals harvester" → ✅ #4 **confirmed Major** / ⬇️ #5 **Minor**
**Ruling (read `TeasMcpTools.cs:760-857`):**
- `get_document_status` (:833) is gated only on `TaxInvoiceRead` and queries `Where(id == id)` with **no
  `CreatedViaApiKeyName` filter** → a key with one read scope can poll status + **DocNo** of *any* doc of
  *any* of 6 types by id, tenant-wide. **Confirmed Major** (within-tenant cross-type info disclosure /
  DocNo enumeration). Fix: require the per-type read scope for `type`, or restrict to the key's own drafts.
- `list_pending_approvals` (:760) **is** filtered by `CreatedViaApiKeyName == keyName` → own-key drafts
  only. Claude's "harvester" label is overstated → **Minor** (scope-granularity polish only).

### B6 — Claude BE CRITICAL "stale doc-date at Post" → ✅ **Confirmed (headline)**
`TaxInvoiceService.PostAsync` gates the period (`:296 EnsureOpenAsync(ti.DocDate)`) and allocates the
sequential number (`:302 NextAsync(..., ti.DocDate)`) against **`ti.DocDate` fixed at draft creation**
(`:241-242`, pinned to creation-day Bangkok). PV mirrors this. So an agent draft created May 31 and
human-posted Jun 1 gets a **May** sequence number and a **May** tax-point. Pre-existing (obs 8329 "Tax-Point
Date Control"; 8353 pins at create), **newly reachable** via the MCP draft-now/post-later flow.
Compliance exposure under ม.78/ม.86/4(7) + §4.3. Fix: at POST, re-pin `DocDate`/`TaxPointDate` to
Asia/Bangkok *today* and bucket the number on the post-date period — or refuse to post a draft whose
`DocDate` ≠ today.

### B7 — Claude BE MAJOR "no DB immutability trigger on `*_lines`" → ✅ **Confirmed Major**
Header triggers exist (`040` TI, `060` VI, `570` Receipt, `020` JE, `030` audit, `300` etax). **No trigger
on any `*_lines` table** — `060:53` / `322:3` confirm lines rely on FK + EF filter only. A raw
`UPDATE sales.tax_invoice_lines SET line_amount=…` on a posted invoice trips **no** DB trigger (header row
untouched; FK doesn't guard value edits). §4.2 requires enforcement at **DB and** app. Fix: add
`BEFORE UPDATE/DELETE` triggers on `*_lines` that block when the parent doc is posted. *(Also: CN/DN header
triggers — `200:25` hints they're app-only — worth a follow-up check.)* *(Severity nuance: the posted
**header total** IS trigger-protected, so raw line tampering desyncs header-vs-lines and is **detectable** by
a totals-reconcile — it can't silently alter the authoritative posted total. Real, but Minor–Major, not a
silent-corruption Critical.)*

### B8 — agy MAJOR "receipt detail has no Post button in Draft" (Claude FE: "all sales pages carry post+approve") → ✅ **Confirmed Major (restored — was dropped in the first pass)**
**Ruling (read `receipts/[id]/page.tsx` + `receipts/new/page.tsx` + list):** `receipts/new` offers a real
**saveDraft** path (`:332/:374` → redirect to `/receipts`), so a human can create a *draft* receipt. But on
the detail page the Post CTA (`rc-approve-cta`, `:70-88`, scope `sales.receipt.post`) is rendered **only**
inside `isApproveAction && status==='Draft'` (`:65`) — i.e. it requires `?action=approve` in the URL. The
always-rendered `DocActionBar` (`:98`) receives **no** post callback, and the list row (`page.tsx:70`) has no
post action. So a normally-saved draft receipt is **un-postable via normal navigation** — only the agent
approval deep-link (which carries `?action=approve`) can post it. agy was right; Claude-FE's blanket "all
sales pages carry post" was imprecise for receipts. Fix: render the Post CTA in `DocActionBar` for any
`Draft` (gated on `sales.receipt.post`), independent of the URL param.

## C. Unique findings worth carrying (single-source, plausible, not yet contradicted)

- **agy** — `etax-environment-tiers.md` / plan.md drift on the `RdCcAddress` dedup TODO + `ETaxSigner`
  "✅ production safe" vs §8 "inert" (D4 Minor); `RdHttpEfilingClient` is wired active when
  `RdApi:Provider != "Mock"` while §8 calls e-Tax inert — **document the real toggle** (D4 Minor).
- **agy** — `ETaxSigner` single-signature vs the two-signature ETDA pattern in the spec (D1 Major **on the
  spec**, but the pipeline is Phase-1 inert → real priority Low until e-Tax goes live).
- **agy** — `BillingNoteEndpoints.cs:44` cancel expects a body; openapi defines none → 400 for spec clients (Major).
- **agy** — `app/api/proxy/[...path]/route.ts` `forward` has no try/catch → unhandled throw on upstream down (Major).
- **agy** — React Query: `usePostReceipt`/`usePostAdjustmentNote` don't invalidate the `[doc,id]` detail key;
  post mutations don't invalidate report keys (`tax-summary`/`profit-loss`/VAT registers) → stale UI (Major).
- **agy** — `settings/companies/page.tsx:44` renders super-admin UI during the `useMePermissions` loading
  window (client gate only; backend still enforces `Master.CompanyManage`) → **Minor** cosmetic exposure.
- **Claude** — `ApiKeyService.EnforceMcpNoPostGuard` blacklists only `.post`; `.approve/.issue/.send/.void`
  would slip through (latent — no such tool today). Whitelist `.read/.create/.manage` instead (Minor).
- **Claude** — `PurchaseOrderService.cs:51,100` trusts `req.DocDate` (agent can backdate a PO) — §10
  pin-to-today convention (Minor; PO is not a tax document).
- **Claude FE** — dashboard pending-approvals alert hard-links **all** doc types to `/tax-invoices`
  (`app/(dashboard)/page.tsx:68`) → wrong list for PO/VI/PV drafts (Major UX).

---

## D. Consolidated fix list — priority order

1. **[Compliance · Major]** B6 — re-pin `DocDate`/`TaxPointDate` + number-period to post-date at POST (TI + PV); or block posting a stale-dated draft. *Newly reachable via MCP.*
2. **[Security · Major]** B5 — scope `get_document_status` per doc-type (or own-key only); stop one read scope exposing all 6 types tenant-wide.
3. **[Compliance · Major]** B7 — add DB immutability triggers on `*_lines` (and check CN/DN headers).
4. **[Compliance · Major]** B2 — always resolve line `ProductType` from the Product master; never trust caller input.
5. **[Security · Major]** A4 — login route: log stack server-side, return message/code only. + agy proxy try/catch → 502.
6. **[UX · Major]** B3 + B8 + Claude-FE dashboard link — drive the agent-draft banner/approve CTA off `createdViaApiKeyName`; **render the receipt Post CTA in `DocActionBar` for any Draft (not only `?action=approve`)** so human-saved draft receipts are postable; fix the hard-coded `/tax-invoices` deep-link.
7. **[Correctness · Major]** agy React-Query — invalidate `[doc,id]` + report keys on post mutations.
8. **[Spec · Major]** A1 — sync `openapi.yaml` to as-built (api/v1 + MCP + lifecycle path names + base path `/v1` vs `/api/v1`).
9. **[Minor batch]** B1 regex widen · list_pending_approvals scope polish · `EnforceMcpNoPostGuard` whitelist · PO DocDate · companies loading gate · hardcoded Thai strings · e-Tax doc drift.
10. **Rejected:** B4 Buddhist-era display (by design).

> Methodology note: AGY's wandering crawl twice exhausted its budget on the spec/BE pass; the findings were
> harvested by forcing an emit (`-Continue`) after it had read the hotspots. The bridge needed a `-NoProfile`
> child process to dodge a profile-loaded `Remove-Item` guard.
