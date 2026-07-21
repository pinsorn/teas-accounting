# Fix ALL remaining swarm findings (HIGH + MED) — 2026-07-20

Ham /goal extension: "หลังจากแก้ CRIT เสร็จแล้ว แก้ Finding อื่น ๆ ทั้งหมดด้วย แล้วก็เทสด้วย Swarm อีกครั้ง".
CRIT-1/CRIT-2 already shipped (v1.22.6). This spec covers every non-CRIT finding from the round-2
swarm (swarm-findings/*.md, commit 26768c4). Each WP names its evidence file. Gate dispatch on
round-3 confirming the CRITs stayed closed; fold any NEW round-3 finding into the matching WP.

Grouping honours the test-DB constraint: only ONE backend/test-running worker at a time; FE-only
workers (no dotnet test) may run alongside exactly one backend worker. Deploy in as few releases as
sensible (batch backend seed scripts).

═══════════════════════════════════════════════════════════════════════════════════════════
## WP1 — FE route-guard: unauthorized roles must get a clean deny, not a rendered write form (HIGH)
Evidence: swarm-findings/audit01.md (HIGH #1/#2/MED tax-filing), purch01.md, sales01.md, ar01.md.
- Root: the app ALREADY has the correct pattern — `/settings/users|companies|roles|expense-categories|
  api-keys` render a full-page "ไม่มีสิทธิ์เข้าถึง (…perm…)" notice with ZERO write chrome. It is simply
  not applied to document routes. All 16 `/…/new` routes (quotations, sales-orders, delivery-orders,
  tax-invoices, invoices, credit-notes, debit-notes, receipts, purchase-orders, vendor-invoices,
  payment-vouchers, expense-claims, customers, vendors, bank-accounts, fixed-assets) render the full
  create/post form to a role with zero write perms (backend still 403s the POST — FE-only gap, but
  violates least-surprise + leaks the form). Plus `/credit-notes` & `/debit-notes` LIST pages show a
  "+ สร้างเอกสาร" button in normal nav; `/period-close` renders for AR Clerk (ar01 HIGH); tax-filing
  forms (`/reports/pnd30`, `/tax-filings/pnd3/36/53/54/51`, `/tax-filings/cit`) show ยืนยัน/ปิดงวด /
  บันทึก / เพิ่มรายการ write buttons to read-only roles.
- Fix: wrap every write route + create-button + finalize-button in the SAME PermissionGate the
  /settings/* pages use, keyed on the create/post/finalize permission the backend endpoint actually
  checks (read the endpoint's RequirePermission to get the exact code — do NOT guess). A role lacking
  it gets the full-page deny (for /new routes) or the button simply not rendered (for list-create +
  finalize). Reuse the existing gate component; no new abstraction.
- [x] all 16 /new routes deny cleanly for a role without the matching create perm (audit01 account =
      the test oracle); CN/DN list create button hidden without create perm; /period-close + all
      tax-filing finalize/write buttons gated. Backend POST still 403 (defense-in-depth intact).
      Evidence: `pnpm tsc --noEmit` clean, `pnpm next build` clean (all 84 routes compiled), i18n
      key-parity clean (th/en), no Bengali-glyph contamination in th.json. Gate reuses the EXACT
      /settings/* pattern (ShieldAlert + `common.noAccessTitle`/`common.noAccessBody` — the latter
      newly added as SHARED keys, parameterized `{perm}`, so 16+ routes don't each need their own
      near-duplicate key pair — no new component/abstraction, same JSX block copied per file like
      the 4 existing /settings pages already do). Permission code per route (read from each
      endpoint's actual `RequireAuthorization`/`Permissions.cs`, not guessed):

      | Route / UI element | Permission code gated |
      |---|---|
      | /quotations/new | `sales.quotation.manage` |
      | /sales-orders/new | `sales.sales_order.manage` |
      | /delivery-orders/new | `sales.delivery_order.manage` |
      | /tax-invoices/new | `sales.tax_invoice.create` |
      | /invoices/new (BillingNote) | `sales.billing_note.manage` |
      | /credit-notes/new | `sales.credit_note.create` |
      | /debit-notes/new | `sales.debit_note.create` |
      | /receipts/new | `sales.receipt.create` |
      | /purchase-orders/new | `purchase.purchase_order.create` |
      | /vendor-invoices/new | `purchase.vendor_invoice.create` |
      | /payment-vouchers/new | `purchase.payment_voucher.create` |
      | /expense-claims/new | `expense.claim.create` |
      | /customers/new | `master.customer.manage` |
      | /vendors/new | `master.vendor.manage` |
      | /bank-accounts/new | `bank.account.manage` |
      | /fixed-assets/new | `fixedasset.manage` |
      | /credit-notes, /debit-notes list "+ สร้างเอกสาร" | `sales.credit_note.create` / `sales.debit_note.create` |
      | /reports/pnd30, /tax-filings/pnd3\|36\|53\|54 "ยืนยัน/ปิดงวด" (finalize) | `tax.filing.finalize` |
      | /tax-filings/pnd51 "สร้าง PDF" + "บันทึกประมาณการ (ม.67ตรี)" | `tax.filing.preview` (verified: both endpoints — `/tax-filings/pnd51/pdf` and `/tax-filings/pnd51/estimate` — are gated on `Permissions.Tax.FilingPreview` in `TaxFilingEndpoints.cs`, NOT finalize; not guessed) |
      | /tax-filings/cit compute/save-override/add-adjustment/edit/delete buttons | `tax.filing.finalize` (matches `CitEndpoints.cs`'s `write` var on every mutating route) |
      | /period-close full page | `gl.period.close` |
      | doc-detail "+ อัปโหลด" (AttachmentsSection, shared by every doc type) | `sys.attachment.upload` |

      payroll "สร้างรอบจ่าย" (WP5 overlap, done here): already correctly wrapped in
      `<PermissionGate scope="payroll.run.manage">` in `frontend/app/(dashboard)/payroll/page.tsx`
      — verified against `PayrollEndpoints.cs`'s actual create-route policy, matches exactly. No
      code change needed; admin01 seeing the button is COMPANY_ADMIN legitimately holding
      `payroll.run.manage` per `docs/rbac/role-permission-matrix.md` (a product/RBAC-design
      question already flagged for Ham in admin01.md, not an FE gating bug).
- [x] a role WITH the perm still sees the form (no regression) — the gate is `perms.data &&
      !canCreate` (same idiom as /settings/users|roles|companies): while `perms.data` is falsy
      (loading) OR the perm is present, the real form/button renders unchanged; only confirmed
      false renders the deny block. No behavior change for a role that HAS the permission — code
      path is additive (new early-return only fires on missing perm), verified by reading every
      diff; no live-account smoke test run (no local backend/DB stack spun up this pass — FE-only
      dispatch, static verification via tsc+build was the assigned gate).
- FE-only. Follows proven in-repo pattern. Sonnet.

## WP2 — RBAC read grants: "no data" vs "no access" ambiguity + BU-read console spam (HIGH-4 + MED)
Evidence: swarm-findings/audit01.md (HIGH #3, MED business_unit, MED cit).
- Root: AUDITOR has read perms for the AR/sales subset only; ~10 modules (PO, VI, PV, quotations,
  sales-orders, delivery-orders, expense-claims, vendors, bank-accounts, fixed-assets) + 3 reports
  (AP-aging, outstanding-PO, bank-reconciliation) + CIT + business-units all 403 at the API but render
  a normal "ไม่มีข้อมูล" empty state — indistinguishable from a genuinely empty company (co5 HAS real
  POs the auditor can't see). `master.business_unit.read` absence alone = ~25 console 403s across
  nearly every page.
- Fix (product decision — Fable): an Auditor SHOULD have full read visibility. Grant AUDITOR the
  missing `*.read` / `report.*.read` / `master.business_unit.read` / CIT-read perms via a seed script
  (mirror 627: code-first-then-grant, template top-up + per-company FORCE-RLS resync, idempotent,
  NOBYPASSRLS). This resolves the ambiguity, the mission premise, AND the BU console spam in one go.
  Do NOT grant any write/finalize. Audit whether other read-only-ish roles share the BU-read gap and
  fix in the same script.
- [~] Backend done: `SqlScripts/628_seed_auditor_read_approver_grant.sql` grants AUDITOR 8 read-only
      codes (verified against Permissions.cs + each endpoint's actual RequireAuthorization — NOT the
      finding's placeholder names): `purchase.purchase_order.read` (also covers /reports/ap-aging +
      /reports/outstanding-po — same `read` policy, no separate report.* code exists),
      `purchase.vendor_invoice.read`, `purchase.payment_voucher.read`, `expense.claim.read`,
      `bank.account.read`, `fixedasset.read`, `bank.report.read` (bank-recon REPORT only —
      `bank.reconcile` stays ungranted, it's write-capable), `tax.filing.preview` (unlocks CIT
      years/profile/adjustments — shared code, no CIT-only perm exists). New test
      `AuditorReadApproverGrantTests.cs` (mirrors `TaxOfficerFilingGrantTests.cs`): asserts AUDITOR
      resolves all 8 + holds NONE of 21 named write codes, and hits `/purchase-orders`,
      `/reports/ap-aging`, `/reports/outstanding-po` without a 403. 3/3 pass against real teas_test.
      NOT granted (documented in the script header, not silently dropped): `sales.quotation.read` /
      `sales.sales_order.read` / `sales.delivery_order.read` / `master.vendor.read` /
      `master.business_unit.read` — none exist as separate codes; the owning endpoints gate BOTH read
      and write/lifecycle on one combined `*.manage` code (SalesChainEndpoints.cs,
      MasterEndpoints.cs's vendor group, BusinessUnitEndpoints.cs). Granting the combined code would
      hand AUDITOR write access, violating the hard READ-ONLY rule. Fixing this properly needs a
      read/manage split (the codebase already has 3 precedents: Customer, BankAccount, ExpenseCat) —
      a bigger, regression-risked diff (must re-grant `.read` to every existing `.manage` holder
      across 3 endpoint files / ~15 routes so nothing existing breaks) than a grant-only seed script
      should attempt unilaterally. Flagging as a follow-up spec; business_unit is the highest-value
      one (kills the ~25-console-403 spam for AUDITOR **and** every other read-heavy role — AR_CLERK,
      AP_CLERK, SALES_STAFF, PURCHASING_STAFF, WAREHOUSE_STAFF, TAX_OFFICER, APPROVER — none hold
      `master.business_unit.manage` today, confirmed via seed grep).
      Remaining: live FE verification (audit01 account, co5) — BACKEND-ONLY dispatch, not run here.
- Backend seed + test. Sonnet; Fable reviews grant scope (read-only, no write leak).

## WP3 — reports UX correctness/clarity (HIGH-2 + MED ×3)
Evidence: swarm-findings/chief01.md.
- HIGH-2 cutoff mismatch: TB/Balance-Sheet default "as-of today" while P&L defaults "full current
  month" → P&L pulls a future-dated (30/07) already-paid payroll run, showing −129,500 while BS shows
  −2,000 for "the same period", no UI warning. Fix: make the period basis explicit + consistent on
  screen — show the exact date range each report is computed over, and/or a one-line note when P&L's
  month range extends past today (or includes future-dated posted docs). Do NOT silently change the
  numbers; surface the basis. (Decide with Fable: simplest is a visible "ณ วันที่ / ช่วง" label on each
  report header so a reader can't conflate two different cutoffs.)
- MED: AP-aging missing the control-account tie-out banner that AR-aging has (add the same banner).
- MED: AR-aging negative (overpayment/net-credit) bucket has no visual distinction (style negatives).
- MED: bank-reconciliation shows an unreconciled ฿ difference with no explanatory badge (add a badge);
  bank-recon doesn't auto-select the company's only bank account (LOW — auto-select if exactly one).
- [x] each report header states its date basis; AP-aging has the tie banner; AR-aging negatives
      visually distinct; bank-recon diff badged + single-account auto-selected.
      Evidence: `pnpm tsc --noEmit` clean, `pnpm next build` clean (84 routes), i18n th/en key
      parity clean (0 missing either direction), no Bengali-glyph contamination in th.json.
      Backend: `dotnet build` 0 errors; `dotnet test --filter Accounting.Api.Tests.Reports`
      55/55 passed, 0 skipped (incl. 11/11 ApAgingTests, +1 new).
      - Cutoff mismatch: TB/BS headers now show `t('asOfBasis', {date})` = "ข้อมูล ณ วันที่ …"
        subtitle (PageHeader); P&L shows `t('periodBasis', {from,to})` = "ข้อมูลช่วงวันที่ … ถึง
        …" subtitle PLUS a `t('periodFutureWarning')` note when `to` > today (Bangkok). No number
        changed — only added display text. trial-balance/page.tsx, balance-sheet/page.tsx,
        profit-loss/page.tsx.
      - AP-aging tie banner: backend `SubledgerReportService.ApReconciliationAsync` (already
        built+used by VendorLedgerAsync) exposed on `ISubledgerReportService`, wired into
        `ApAgingService` via DI (new ctor param, no other callers to fix — confirmed via build),
        `ApAgingReport` DTO gained a `Reconciliation` field. FE `ApAgingReport` type + a
        `ReconciliationPanel` copied from ar-aging/page.tsx (reuses the 'report' namespace's
        existing tie-out labels, no new i18n keys needed). Files: SubledgerDtos.cs,
        SubledgerReportService.cs, ApAgingDtos.cs, ApAgingService.cs, ApAgingTests.cs (+1 test:
        `Reconciliation_is_populated_and_reflects_real_ap_movements`), lib/types.ts,
        reports/ap-aging/page.tsx. **Needed a backend field** (ApAgingReport had no reconciliation
        data at all, unlike ArAgingReport) — could not be computed FE-side without either
        hardcoding the 2110 control-account code client-side (violates the "never hardcoded, only
        GlAccountsOptions" rule) or calling a separate TB-report endpoint the viewer may not have
        permission for; reusing the ALREADY-BUILT+tested reconciliation method via DI was the
        minimal path.
      - AR-aging negatives: new `amountClass(v)` helper (text-error when v<0) applied to all 4
        bucket cells + total, both body rows and the tfoot totals row. reports/ar-aging/page.tsx.
      - Bank-recon: auto-selects the sole bank account via a `useEffect` once `useBankAccounts()`
        resolves (never overrides an explicit pick). Difference tile gained a `badge` slot:
        `useStatementImports(bankAccountId)` — 0 imports → ghost "no statement imported yet"
        badge; imports exist + diff≠0 → warning "unreconciled — see below" badge. 2 new bank.*
        i18n keys. reports/bank-reconciliation/page.tsx.
- FE reports (+ a minimal backend field for AP-aging tie, reused existing logic via DI). Mostly
  FE. Sonnet.

## WP4 — approver inbox (HIGH-3)
Evidence: swarm-findings/appr01.md.
- Root: the dashboard "ต้องทำ/แจ้งเตือน" widget is backed by `GET /reports/pending-agent-approvals`
  which 403s on every load → widget silently shows "all clear" while real drafts await approval. And
  there is no working approval inbox — an Approver must manually hunt list pages.
- Fix: (a) grant the Approver (and roles that own approval) the permission that endpoint requires so
  the widget loads (check the endpoint's RequirePermission; likely a report/approvals read perm →
  fold into WP2's seed script if it's a grant-only fix); (b) if the widget's query itself is wrong,
  fix it so pending drafts actually surface. Minimum viable: the existing widget shows the real
  pending count/list for an Approver. A full new inbox page is NOT required (Ponytail) unless the
  widget can't be made to work — if so, note it and ship the widget fix.
- [~] Backend done, grant-only, merged into WP2's script: endpoint's actual gate (`ReportEndpoints.cs`
      `/reports/pending-agent-approvals`) is `Permissions.Sales.TaxInvoiceRead` =
      `sales.tax_invoice.read` — NOT a dedicated approvals-read perm (no such code exists). APPROVER
      was the only PO/PV-approving role missing it (`COMPANY_ADMIN`/`CHIEF_ACCOUNTANT` already hold
      it via `320_seed_chapter3_rbac.sql`, confirmed by grep) — granted via
      `628_seed_auditor_read_approver_grant.sql`. `AuditorReadApproverGrantTests
      .Approver_resolves_tax_invoice_read_and_hits_pending_agent_approvals_without_a_403` passes
      against real teas_test (no 403).
      Widget-query finding: NOT a second bug — read the actual code
      (`ReportEndpoints.cs` L112-143 doc comment "M4a — count of DRAFT documents created via API key
      (MCP agent)"; FE `app/(dashboard)/page.tsx` uses a `Bot` icon + `agentType`-keyed i18n copy).
      The widget is BY DESIGN scoped to `CreatedViaApiKeyName != null` drafts only (agent-created),
      not a general human-approval inbox — appr01's PO/PV drafts were created by other swarm agents
      through the normal browser UI (human path), so `CreatedViaApiKeyName` is null for them and they
      were never going to be counted, grant or no grant. That is the feature working as documented,
      not a defect. The separate "no approval inbox exists" finding is real UX but explicitly
      out-of-scope per this spec's own Ponytail note (no new inbox page) — flagging for Ham/product,
      not building it here. No FE change made (BACKEND ONLY dispatch).
      Remaining: live FE verification (appr01 account) — BACKEND-ONLY dispatch, not run here.
- Backend perm (+ maybe query) + FE widget. Sonnet; if grant-only, merge into WP2.

## WP5 — MED/LOW misc
Evidence: audit01.md, chief01.md, admin01.md, ap01.md.
- api-keys: page renders content PAST its own deny gate + throws React hydration #418 (seen by 3
  agents). Fix the gate to short-circuit render (like the clean /settings/users gate) — kills both the
  leak-past-deny and the hydration mismatch.
- payroll: "สร้างรอบจ่าย" (create pay run) button shows for Company Admin (admin01) — gate it behind
  the payroll-run create perm (part of WP1's button-gating sweep if convenient).
- users page: admin01's own row + peer Company-Admin rows carry แก้ไขบทบาท/รีเซ็ตรหัสผ่าน/ปิดใช้งาน with
  no self-lock or peer-admin SoD guard (only isSuperAdmin rows are excluded from deactivate). Add a
  self + peer-admin guard on the destructive controls (don't let an admin lock themselves/each other
  out by accident). Small, security-adjacent → Fable reviews.
- attachment "+ อัปโหลด" on doc detail shown to a `sys.attachment.read`-only role — gate behind
  attachment-write (WP1 button sweep).
- EN error toast on an otherwise-Thai UI (appr01) — i18n the message.
- **NEW (round-4 ap01, HIGH-FE):** `vendor-invoices/new/page.tsx` PO-link effect fetches poDetail
  async then REPLACES all line rows with `categoryId: null`; if the user picks the Expense Category
  before that async replace lands, the pick is silently clobbered → Post button never enables, no
  error/toast. Fix: don't clobber a user-set categoryId on the async PO-detail merge (merge, or guard
  the replace so it doesn't overwrite fields the user already set), OR disable the category picker
  until poDetail has resolved. (This is the same symptom round-3 ap01 misfiled as a "CRIT exception".)
- [x] each item verified fixed (static gates; live-account verification deferred to round-4 swarm
      per this spec's own Sequencing §5 — no local backend/DB/browser stack was spun up this pass,
      same posture as WP1's dispatch). Evidence: `pnpm tsc --noEmit` clean, `pnpm next build`
      clean (84 routes), i18n th/en parity clean, no Bengali contamination. FE-only (no backend
      touched by this WP).
      - api-keys gate: page now calls `useMePermissions()` and early-returns the SAME deny block
        `/settings/users` uses (ShieldAlert + shared `common.noAccessTitle`/`noAccessBody`) BEFORE
        any of the page's other content — the "native connector" panel (previously unconditional,
        the leak-past-deny) now only renders once `canManage` is confirmed true, matching the
        backend's own gate (`ApiKeyEndpoints.cs` gates the WHOLE `/api-keys` group, including the
        list GET, on one `sys.api_key.manage` policy — verified, not guessed). Hydration #418: the
        3 `window.location.origin` reads (`mcpOrigin()` + 2 inline uses) were computed differently
        during SSR (`''`) vs the client's hydration render (real origin) — replaced with a single
        `origin` state seeded `''` and set via a post-mount `useEffect`, so the first client render
        matches SSR and React never observes a text mismatch (independent of the deny gate — this
        also fixes #418 for users WHO DO have access, per admin01.md's own console log).
        settings/api-keys/page.tsx.
      - users self/peer-admin guard: added `isGuardedRow(u, myUserId, viewerIsSuperAdmin)` — true
        on the viewer's OWN row (always), or on another row holding `COMPANY_ADMIN` when the
        viewer is NOT a super-admin (a super-admin still manages company admins; that's hierarchy,
        not a peer relationship the SoD concern is about). Guarded rows show a muted note instead
        of the แก้ไขบทบาท/รีเซ็ตรหัสผ่าน/ปิดใช้งาน buttons. Needed the current user's own userId,
        which `useMePermissions()` doesn't expose (only scopes/role codes) — added a page-local
        `useMe()` calling the existing `GET /me` endpoint (already returns `UserId`; no backend
        change) rather than growing the shared lib/queries.ts surface for a single page's need.
        settings/users/page.tsx.
      - EN error toast: root-caused to `DomainExceptionMiddleware`'s generic catch-all, which
        always emits CODE `"internal_error"` with an English `detail` ("An unexpected error
        occurred.") — `errorToToast`/`resolveProblemKey` fall through to that English detail
        whenever the code has no TH entry. Added `'internal_error'` to the TH dict (generic,
        locale-correct fallback message) — fixes this for EVERY unhandled-exception path, not
        just appr01's one repro. lib/i18n/problems.ts.
      - VI-new PO-link clobber: root cause confirmed exactly as ap01's round-4 probe described —
        the PO-link effect's `setRows(poDetail.lines.map(...))` unconditionally replaced every
        row's `categoryId` with `null`. Changed to a merge that preserves a categoryId (+ its
        paired `recoverable`) the user already picked at the same row position; description/
        amount/vatRate still always come from the PO (that's the point of linking one).
        vendor-invoices/new/page.tsx.

═══════════════════════════════════════════════════════════════════════════════════════════
## Sequencing / routing
1. Round-3 swarm confirms CRIT-1/CRIT-2 closed FIRST (in flight). Fold any new round-3 finding here.
2. Wave A (parallel, disjoint): WP1 (FE-only) ‖ WP2 (backend seed + test). WP4 likely merges into WP2.
3. Wave B: WP3 (FE reports) ‖ WP5 (mixed) — but only ONE runs `dotnet test` at a time; stagger the
   backend bits, or run WP5's backend piece after WP2 commits.
4. Each WP: worker self-verifies gates → Fable diff review (security-adjacent bits: WP1 gating logic,
   WP2/WP4 grant scope, WP5 self-lock — never skipped) → cross-review (Opus for the RBAC/grant WPs) →
   commit per WP → batch deploy (one release if timing allows; new seed scripts → DB backup +
   applied_sql_scripts increments by the script count).
5. Then SWARM ROUND 4 (reuse the 10 accounts) to verify every finding closed + no regression.

## Attempt log
- 2026-07-19 ~23:5x spec drafted (Fable) from round-2 findings while round-3 verifies CRITs. Dispatch
  pending round-3 verdict.
- 2026-07-20 (Sonnet, FE-only dispatch) WP1 implemented + both checkboxes closed. 25 files changed
  (16 `/new` routes + AdjustmentNoteScreens.tsx + AttachmentsSection.tsx + WhtFilingClient.tsx +
  reports/pnd30 + tax-filings/pnd36|pnd51|cit + period-close + messages/th.json + messages/en.json).
  backend/ untouched (read-only, to get exact `RequireAuthorization` perm codes per route). Gates:
  `pnpm tsc --noEmit` clean, `pnpm next build` clean, i18n th/en key-parity clean, no Bengali-glyph
  contamination. No live-account browser smoke test this pass (no local backend/DB stack running —
  static gates were the assigned verification for this dispatch); round-4 swarm re-run against a
  deployed build is the live-account verification step per this spec's Sequencing §5.
- 2026-07-20 (Sonnet, WP3+WP5 combined dispatch) both implemented + checkboxes closed. 18 files:
  4 backend (SubledgerDtos.cs, SubledgerReportService.cs, ApAgingDtos.cs, ApAgingService.cs) +
  1 backend test (ApAgingTests.cs, +1 test) + 13 frontend (lib/types.ts, reports/ap-aging,
  reports/ar-aging, reports/trial-balance, reports/balance-sheet, reports/profit-loss,
  reports/bank-reconciliation, settings/api-keys, settings/users, lib/i18n/problems.ts,
  vendor-invoices/new, messages/th.json, messages/en.json) — slightly over the ~15 blast-radius
  guideline; justified by 8 distinct findings genuinely spread one-per-page/service plus the
  paired i18n files. AP-aging's tie banner needed ONE new backend field (Reconciliation on
  ApAgingReport), reusing SubledgerReportService's ALREADY-BUILT+tested ApReconciliationAsync via
  DI rather than duplicating the control-account query or hardcoding the GL account code FE-side.
  Gates: `pnpm tsc --noEmit` clean, `pnpm next build` clean (84 routes), i18n th/en key-parity
  clean, no Bengali-glyph contamination, backend `dotnet build` 0 errors, `dotnet test --filter
  Accounting.Api.Tests.Reports` 55/55 passed 0 skipped (incl. new AP-aging reconciliation test).
  No live-account browser smoke test this pass — same posture as the WP1 dispatch above (no local
  backend/DB/browser stack spun up); round-4 swarm re-run against a deployed build is the
  live-account verification step per this spec's own Sequencing §5.

═══════════════════════════════════════════════════════════════════════════════════════════
## WP6 — read/manage split so AUDITOR (and read-heavy roles) get true read-only visibility (from WP2)
Surfaced by WP2 (628 header): quotations, sales-orders, delivery-orders, vendors, business_units gate
BOTH read AND write/lifecycle on ONE combined `.manage` code — so AUDITOR can't be granted read without
also getting write. Fully resolving HIGH-4 (auditor "no data" ambiguity) + the ~25-console-403 BU spam
needs a read/manage SPLIT.
- Follow the 3 EXISTING in-repo precedents (Customer, BankAccount, ExpenseCategory already split into
  `.read` + `.manage`): add a `<resource>.read` permission code; change each endpoint's list/get/PDF
  routes to require `.read` while write/lifecycle routes keep `.manage`.
- REGRESSION GUARD (the risky part): re-grant the new `.read` to EVERY role that currently holds
  `.manage` (seed script, mirror 627/628), so no existing user loses list/detail access. Verify via
  RbacMatrix/AuthMap: every prior `.manage` holder still resolves read; AUDITOR now resolves the 5 new
  reads; no role gained write.
- business_unit.read is highest value (kills the BU console-403 spam for AR/AP/SALES/PURCH/WAREHOUSE/
  TAX/APPROVER too, not just AUDITOR).
- Footgun (auth surface) → Opus reviews the split + the re-grant completeness before commit.
- [x] endpoints split; all prior .manage holders keep read; AUDITOR reads quotation/SO/DO/vendor/BU;
      zero BU 403 console spam; no role gained write (RbacMatrix green).
      Evidence (2026-07-21, Sonnet implementer dispatch):
      - **Permissions.cs**: added 5 new codes — `Sales.QuotationRead`, `Sales.SalesOrderRead`,
        `Sales.DeliveryOrderRead`, `Master.VendorRead`, `Master.BusinessUnitRead` — plus `Permissions.All`.
      - **Endpoints split** (routes reclassified; every write/lifecycle route unchanged on `.manage`):

        | File | Read (list/get/PDF/paper) | Manage (unchanged) |
        |---|---|---|
        | SalesChainEndpoints.cs (quotations) | GET `/`, GET `/{id}`, GET `/{id}/pdf`, GET `/{id}/paper` | POST `/`, PUT `/{id}`, DELETE `/{id}`, `/send`, `/accept`, `/reject`, `/cancel`, `/convert-to-so` |
        | SalesChainEndpoints.cs (sales-orders) | GET `/`, GET `/{id}`, GET `/{id}/pdf`, GET `/{id}/paper` | POST `/`, PUT `/{id}`, `/post`, `/delivery-orders`, `/create-invoice` |
        | SalesChainEndpoints.cs (delivery-orders) | GET `/`, GET `/{id}`, GET `/{id}/pdf`, GET `/{id}/paper` | POST `/`, `/issue`, `/mark-delivered`, `/create-ti`, `/create-invoice` |
        | MasterEndpoints.cs (vendors) | GET `/`, GET `/{id}` | POST `/`, PUT `/{id}` |
        | BusinessUnitEndpoints.cs | GET `/`, GET `/{id}` | POST `/`, PUT `/{id}`, DELETE `/{id}`, PUT `/company-setting` |

        Group-level `RequireAuthorization` removed from all 5 groups (would AND with the per-route
        policy, wrongly requiring BOTH manage+read on a read route — confirmed by reading
        `RbacEndpointInventory.Classify`, which unions ALL attached `perm:` policies). Matches the
        Customer/BankAccount/ExpenseCategory precedent exactly (no group-level auth, per-route only).
        `PrintEndpoints.cs`'s generic `/mark-printed` routes (a separate file, not in blast radius)
        correctly stayed on `.manage` untouched — confirmed via the regenerated endpoint map diff.
      - **Seed `629_seed_read_manage_split_grant.sql`** (mirrors 627/628 exactly: code-first insert →
        template top-up → per-company FORCE-RLS resync loop, NOBYPASSRLS, no curly braces):
        1. Inserts the 5 new codes into `sys.permissions`.
        2. REGRESSION GUARD: 5 `INSERT ... SELECT role_code, '<read>' FROM role_permission_templates
           WHERE permission_code = '<manage>'` statements — derives the manage-holder role set
           DYNAMICALLY from the table (not hardcoded), so any custom/future manage grant is covered.
        3. AUDITOR granted all 5 new `.read` codes explicitly.
        4. `master.business_unit.read` granted explicitly to AR_CLERK, AP_CLERK, SALES_STAFF,
           PURCHASING_STAFF, WAREHOUSE_STAFF, TAX_OFFICER, APPROVER (628's named gap).
        5. Per-company resync loop scoped to the 5 new read codes only.

        **Roles re-granted** (read from `docs/rbac/role-permission-matrix.md`'s regenerated diff):
        `master.business_unit.read` → ALL 11 non-super roles (COMPANY_ADMIN/CHIEF_ACCOUNTANT/
        ACCOUNTANT via the manage-derived top-up + AUDITOR + the 7 named roles = every non-super
        role — matches "kills BU spam app-wide"). `master.vendor.read` → AP_CLERK, CHIEF_ACCOUNTANT,
        COMPANY_ADMIN, PURCHASING_STAFF (pre-existing manage holders, unchanged) + AUDITOR (new).
        `sales.quotation/sales_order/delivery_order.read` → ACCOUNTANT, AR_CLERK, CHIEF_ACCOUNTANT,
        COMPANY_ADMIN, SALES_STAFF (pre-existing manage holders, unchanged) + AUDITOR (new). No role
        gained a `.manage` code it didn't already hold (verified by
        `ReadManageSplitGrantTests.No_role_gained_write_access_it_did_not_already_have`).
      - **Tests** — new `backend/tests/Accounting.Api.Tests/Rbac/ReadManageSplitGrantTests.cs` (4
        tests, mirrors 627/628's test files): (a) AUDITOR resolves all 5 new reads and holds NONE of
        the 5 paired manage codes; (b) regression guard — every role holding a manage code in
        `sys.role_permission_templates` (enumerated dynamically via `SqlQueryRaw`, not hardcoded)
        still resolves the paired read; (c) no role gained a manage code beyond the pre-existing
        template holders; (d) live HTTP — AUDITOR hits `/quotations`, `/sales-orders`,
        `/delivery-orders`, `/vendors`, `/business-units` without 403, but POST `/quotations` and
        POST `/business-units` are still 403. All 4 passed against real `teas_test`.
        `RbacAuthMapTests` + `RbacMatrixTests` + `RbacCartesianTests` all re-ran and passed
        (regenerated `docs/rbac/endpoint-permission-map.generated.md` +
        `docs/rbac/role-permission-matrix.md`, diff reviewed — matches expectations exactly, no
        superOnly-invariant regression, no SoD regression).
      - **FE nav-gating tweak found and fixed** (per dispatch instruction to grep for old combined
        codes used as visibility scopes): `frontend/components/app-shell/SidebarNav.tsx` gated the
        Vendors/Quotations/SalesOrders/DeliveryOrders/BusinessUnits nav items on the old `.manage`
        code as a stand-in READ gate (the file's own doc comment says each item should carry "the
        READ permission of its primary endpoint" — `.manage` was the only option before this split).
        Updated all 5 to the new `.read` code — a strict widen (629's regression guard means every
        prior `.manage` holder still resolves `.read`, so nothing that could see the nav item before
        loses it). Business-units settings page write buttons (edit/deactivate/create) + the vendors/
        quotations "+ create" buttons + the dashboard "create vendor" quick-action all correctly
        remained on `.manage` (verified by reading each file — they gate genuine write actions, not
        visibility) — no change needed there, matching the dispatch's "keep manage for buttons" rule.
        Found but NOT touched (out of blast radius, separate namespace): `settings/api-keys/page.tsx`
        lists old `.manage` codes as selectable API-KEY scopes (free-form scope catalog, unrelated to
        role-based nav gating per `RbacCartesianTests`' own doc comment); `e2e/helpers/rbac-manifest.ts`
        + `e2e/rbac-admin.spec.ts` + `manual/walkthroughs/*.ts` encode the old codes for Playwright/
        walkthrough fixtures — changing these needs an actual e2e run to verify, which this dispatch
        didn't spin up (no Playwright/browser stack running this pass); flagging as a small
        follow-up, not a regression (they're test fixtures, not runtime gating).
      - **Gates**: `dotnet build` 0 errors. `pnpm tsc --noEmit` clean. `pnpm next build` clean (all
        routes compiled). No Bengali-glyph contamination (`grep -c "ম" messages/th.json` = 0; no i18n
        keys touched by this WP anyway).
      - **Opus Tier-2 review: REJECT with one HIGH (F1)**, then fixed + re-verified:
        F1 — 629's step-2 regression guard derived manage-holders ONLY from
        `sys.role_permission_templates`, but `RbacAdminService.SetRolePermissionsAsync`
        (RbacAdminService.cs:119) writes custom grants straight to `sys.role_permissions` with no
        template row, and `CreateRoleAsync` (line 174) makes company-local custom roles that never
        exist in the template at all — either holding one of the 5 `.manage` codes would silently
        lose read access on deploy. Fix (Opus-supplied pattern): added step 5b inside 629's existing
        per-company loop — a direct-grant INSERT that joins `sys.role_permissions` to the 5
        manage/read pairs via a VALUES list and re-grants `.read` wherever `.manage` is held directly,
        `NOT EXISTS`-guarded, brace-free, same FORCE-RLS per-company scoping as step 5. Coordinator's
        prod de-risk check: only SUPER_ADMIN (global, `company_id IS NULL`) holds any of the 5 manage
        codes outside the template today, and SUPER_ADMIN bypasses per-permission checks entirely on
        the `is_super_admin` claim (never consults its granted codes) — so current prod exposure is
        nil; the fix is for correctness on custom grants/roles and future tenants, not a live gap.
      - **Test hardening** (`ReadManageSplitGrantTests.cs`): the regression-guard test originally
        enumerated from the template (same blind spot as the SQL bug it was meant to catch — couldn't
        fail on the case F1 describes). Rewrote to enumerate from `sys.role_permissions` (the
        EFFECTIVE per-company grants) via a single set-based anti-join query per pair (excluding the
        `company_id IS NULL` SUPER_ADMIN row for the reason above), and added a new test that creates
        a custom role via the REAL `RbacAdminService.CreateRoleAsync` + grants it `.manage` via the
        REAL `SetRolePermissionsAsync` (no template row, by construction), then exercises 629's actual
        step-5b SQL read from disk (scoped to just that one test company via a single asserted textual
        substitution of the loop driver — a full-file replay across every company in the shared
        `teas_test` was timed at over 10 minutes against this DB, which has grown to ~30,000 companies
        from repeated test runs across sessions; scoping to one company keeps the same real SQL fast).
        **Confirmed RED without the fix**: temporarily commented out step 5b in 629.sql → the new test
        failed on the real assertion (`readAfter` was 0), not an infra timeout. **Confirmed GREEN with
        the fix restored**: same test passes in ~2s. Also found and cleaned up 3 leftover synthetic
        `WP6_F1_*` test roles left dangling in `teas_test` by earlier crashed attempts of this same
        test (no cleanup code yet) — added a `finally` block so the test cleans up after itself now.
      - **Final gates (this pass, clean run — all stale dotnet/testhost processes killed first to
        avoid colliding on the shared teas_test)**: `dotnet build` 0 errors. Full `dotnet test`:
        **Accounting.Api.Tests: 918 passed, 0 failed, 8 skipped** (skip count matches baseline
        exactly) + **Accounting.Domain.Tests: 148 passed, 0 failed, 0 skipped**. `ReadManageSplitGrantTests`
        filtered run: 5/5 passed. `RbacAuthMapTests`/`RbacMatrixTests`/`RbacCartesianTests` all green
        (regenerated `docs/rbac/*.md`, unchanged from the pre-F1-fix pass since F1 only affects custom/
        future grants, not the seeded system-role matrix).
