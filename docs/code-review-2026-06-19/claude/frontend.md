# TEAS Frontend Code Review — 2026-06-19 (Claude, adversarial)

Scope: `frontend/{app,components,lib,hooks,messages,middleware.ts}`. Review only — no edits/build/commit.
Focus per task: D3 Security (MCP agentic surface, token handling, VAT-mode exposure), D2 Correctness, D4 i18n parity.

---

## D1 Compliance (FE-visible)

- [MINOR] `app/(dashboard)/settings/company/page.tsx:343-382` — The "paid-up capital" sub-section calls `apiGet('companies')` and, on save, PUTs `vatRate` + `pnd30SubmissionMode` back to `/companies/{id}` (echoed read-only, never an editable VAT input — good). The section is hidden only by a silently-swallowed `.catch()` when the GET 403s for non-`master.company.manage` users (line 350). This is hide-on-server-403, not a client gate — correct as defense-in-depth ONLY because the BE enforces `master.company.manage` on both GET-list and PUT. Fix: gate this sub-component with `useMePermissions().isSuperAdmin` (same hidden-not-disabled pattern as `settings/companies/page.tsx:44`) so a regular user never even fires the master-data GET. Not a §4.6 violation (no VAT-rate input is rendered to regular users), but the reliance on a swallowed catch is fragile. (§4.6 / §10 "no VAT rate/mode to regular users")

## D2 Correctness

- [MAJOR] `app/(dashboard)/page.tsx:68-69` — The pending-agent-approvals dashboard alert ALWAYS links to `/tax-invoices` (`href: '/tax-invoices'`) regardless of which doc type has pending agent drafts. `PendingAgentApprovals` carries per-type counts (`taxInvoices`, plus receipts/quotations/PO/VI/PV via the aggregate `count`), but a user with only a pending *purchase order* draft is sent to the empty tax-invoice list and cannot find the doc to approve. Fix: either route to a combined "pending agent approvals" view, or pick the href from whichever per-type count is non-zero; at minimum surface the breakdown so the user knows where to go.
- [MINOR] `lib/queries.ts:691-696` — `usePendingAgentApprovals` has `staleTime: 60_000`, and NO mutation (`useApprovePaymentVoucher`, `usePostVendorInvoice`, `usePostTaxInvoice`, `usePostReceipt`, the PO `approve`/`mark-sent` action, etc.) invalidates `['pending-agent-approvals']`. After a human approves/posts an agent-drafted doc, the dashboard count stays stale for up to 60 s, so the alert lingers pointing at a doc already actioned. Fix: add `qc.invalidateQueries({ queryKey: ['pending-agent-approvals'] })` to the `onSuccess` of every post/approve mutation that can clear an agent draft.
- [MINOR] `app/(dashboard)/purchase-orders/[id]/page.tsx:175-176` — PO summary reconstructs the pre-discount gross with floating-point money math: `d.lines.reduce((s,l) => s + l.unitPrice * (l.quantity ?? 0), 0)` then `Math.round((gross - subtotal)*100)/100`. CLAUDE.md §5 forbids `double`/`float` for money. JS `number` is IEEE-754 double; `unitPrice * quantity` summed across lines can drift, and the rounding to 2dp (not the system's 4dp) can produce a phantom 0.01 discount row or hide one. This is display-only (BE numbers are authoritative), so not a posting bug, but the displayed discount can disagree with the backend. Fix: have the PO read DTO expose the per-line/document discount directly (mirrors Sales) instead of reconstructing it client-side, or round at 4dp consistently.
- [MINOR] `lib/queries.ts:549` — `useMePermissions` has `staleTime: 5*60_000` and nothing invalidates `['me-permissions']` on company switch. After `/api/auth/switch-company`, a stale permission set can drive `PermissionGate`/`useHasScope` for up to 5 min (wrong buttons shown/hidden for the new company). The full-page reload on switch likely masks this, but if switch is ever made SPA-soft it breaks. Fix: invalidate `['me-permissions']` (and `['company-profile']`, `['system-info']`) in the switch-company flow.
- [MINOR] `lib/utils.ts:24-28` — `formatDate` uses `calendar: 'buddhist'`. This is DISPLAY only and Thai-correct (§5 allows Buddhist at display; "CE internally" governs storage/logic, which uses `DateTimeOffset`/`bangkokToday()` en-CA). NOT a violation — flagged to pre-empt a false positive from the other reviewer; the explicit comment at line 21-23 documents the intent.

## D3 Security (MCP agentic surface)

- [MINOR] `app/api/auth/login/route.ts:66-71` — On an unexpected handler exception the route returns `detail: \`${e.name}: ${e.message}\n${e.stack ?? ''}\`` to the client with status 500. Server stack-trace disclosure to the browser. The comment says this was intentional for dev debugging, but it ships in prod builds too. Fix: log the stack server-side (already done via `console.error`) but return a generic `detail` (or gate the verbose body behind `process.env.NODE_ENV !== 'production'`).
- [INFO/PASS] Token handling is correct: JWT `access_token` is stored ONLY in an httpOnly, `secure`-in-prod, `sameSite=lax` cookie on the Next origin (`app/api/auth/login/route.ts:55-61`); the BFF proxy (`app/api/proxy/[...path]/route.ts`) reads it server-side and forwards `Authorization: Bearer`. No JWT/PII in `localStorage`/`sessionStorage` — the only `localStorage` use is the cosmetic sidebar-collapse key (`components/app-shell/SidebarNav.tsx:147,153`). Satisfies §10.
- [INFO/PASS] Agent draft→approve gating is correct and symmetric. All six doc types — tax-invoices, receipts, quotations, purchase-orders, vendor-invoices, payment-vouchers — show `AgentPendingBadge` on `status==='Draft' && createdViaApiKey` list rows, and a `?action=approve` warning banner on the detail page whose CTA is wrapped in `useHasScope(...)` / `PermissionGate` and posts under the human's own session+permission (`run('approve')` / `setConfirm(true)`). MCP keys cannot hold `.post` scopes (`settings/api-keys/page.tsx:35-37,77` mirrors the M1 backend guard), so an agent can only DRAFT. No client-side authz trust beyond hide-not-disable (the real gate is the BE). This matches the rubric's required agent-draft + human-approval model.
- [MINOR] `app/(dashboard)/settings/api-keys/page.tsx:20-52` — `ALL_SCOPES`/`MCP_DEFAULT_SCOPES` are hard-coded client-side allow-lists that MUST stay in sync with the backend `MCP_DEFAULT_SCOPES`/grantable-scope set. If the BE adds/removes a grantable scope, this UI silently drifts (offers a scope the BE rejects → confusing 400, or hides a newly-valid one). This is a maintainability/sync risk, not an auth bypass (BE validates scopes on create). Fix: serve the grantable-scope catalogue from an endpoint (e.g. `GET /api-keys/scopes`) and render from that, rather than duplicating the list in TS.

## D4 i18n parity & spec drift

- [PASS] `messages/th.json` ↔ `messages/en.json` key parity is **exact: 1453 keys each, 0 missing in either direction** (deep dotted-path diff). All MCP keys present in both: `approve.{bannerTitle,bannerDesc,cta,ctaApprove,ctaSend,alreadyPosted,noPermission}`, `common.agentPending`, `apiKey.{mcpSetupTitle,mcpEndpointLabel,mcpHeaderLabel,mcpConfigLabel,mcpScopeNote,kindMcp}`.
- [MINOR] Hardcoded (non-i18n) strings in JSX:
  - `app/(dashboard)/settings/api-keys/page.tsx:170` — `integration` literal in the kind badge (the MCP badge `MCP` at :169 and `X-Api-Key` at :328/331 are protocol/brand tokens, acceptably literal; `integration` should use `t('kindIntegration')` for consistency).
  - `app/(dashboard)/settings/companies/page.tsx:75` — `<th>ID</th>` column header is a hardcoded literal (every other header uses `t(...)`). Add an i18n key.
- [MINOR] 9 keys have byte-identical TH==EN values; most are legitimately untranslatable brand/protocol terms (`apiKey.title`="API Keys", `apiKey.kindMcp`="MCP (AI Agent)", `apiKey.mcpEndpointLabel`, `ven.payment.swiftCode`="SWIFT / BIC", `report.balanced`="Dr = Cr ✓", `nav.apiKeys`="API Keys", `users.superAdmin`="Super Admin", `attachment.allowedTypes`). Verify `apiKey.prefix`="Key prefix" should have a Thai gloss; the rest are fine.

---

## Summary table

| Dimension | Critical | Major | Minor | Pass/Info |
|---|---|---|---|---|
| D1 Compliance (FE) | 0 | 0 | 1 | — |
| D2 Correctness | 0 | 1 | 4 | — |
| D3 Security (MCP/token) | 0 | 0 | 3 | 2 PASS |
| D4 i18n / spec drift | 0 | 0 | 3 | 1 PASS |
| **Total** | **0** | **1** | **11** | **3** |

**Headline:** No critical FE flaws. The just-shipped MCP agentic surface is sound — token handling (httpOnly cookie + BFF), agent-DRAFT-only (no `.post` scope) → human-approve-under-own-permission gating, and i18n key parity (1453/1453, 0 mismatch) all pass. The one MAJOR is a UX correctness bug: the dashboard pending-agent-approvals alert hard-links every doc type to `/tax-invoices`, stranding users with pending purchase drafts. Secondary: that count is never invalidated on approve/post (≤60 s stale), a prod stack-trace leak on the login route, float money math in the PO summary, and two hardcoded JSX strings.
