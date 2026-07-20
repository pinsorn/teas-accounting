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
- [ ] all 16 /new routes deny cleanly for a role without the matching create perm (audit01 account =
      the test oracle); CN/DN list create button hidden without create perm; /period-close + all
      tax-filing finalize/write buttons gated. Backend POST still 403 (defense-in-depth intact).
- [ ] a role WITH the perm still sees the form (no regression) — verify with a Sales/AP account.
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
- [ ] AUDITOR resolves the added read perms (RbacAuthMapTests, TEAS_REPO_ROOT set); the 10 modules +
      3 reports now render real data for audit01; zero BU 403s in console.
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
- [ ] each report header states its date basis; AP-aging has the tie banner; AR-aging negatives
      visually distinct; bank-recon diff badged + single-account auto-selected.
- FE reports (+ maybe a report-service field for AP-aging tie). Mostly FE. Sonnet.

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
- [ ] logged in as appr01, the dashboard widget shows the real pending PO/PV drafts (not false
      "all clear"); no 403 on pending-agent-approvals.
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
- [ ] each item verified fixed on the relevant role account.

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
