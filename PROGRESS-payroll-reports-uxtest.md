# PROGRESS — payroll + reports UX test (2026-07-16)

Goal (Ham /goal): Chrome test PROD teas.kazaki-rio.com — เงินเดือน (payroll) + รายงาน (reports),
UX-focused, อย่างละเอียด → REPORT findings → ถ้าไม่มีปัญหา blocking → ทำ Manual ใหม่.

## Ground rules
- Prod URL: https://teas.kazaki-rio.com — Chrome session logged in (Repttown company).
  Claude NEVER handles passwords; logged out → checkpoint + wakeup + notify Ham.
- **Payroll = DRAFT-ONLY.** Posting a payroll run creates immutable JEs on Repttown's REAL
  books (no void). Create draft → test UX → delete draft if deletable. NEVER post/approve.
- Reports = read-only, test all 14 pages fully.
- v1.21.4 live (footer confirmed).

## Scope map (from FE source)
- Payroll: /payroll (list), /payroll/[id] (detail). Sidebar perm payroll.run.manage.
  List page has: year filter (2026), ภ.ง.ด.1ก (ปี) print, สร้างรอบจ่าย, search, status filter.
- Reports (14): tax-summary, trial-balance, balance-sheet, profit-loss, general-ledger,
  bank-reconciliation, ar-aging, customer-statement, sales-summary, pnd30 (vatOnly),
  outstanding-po, ap-aging, vendor-ledger, wht-receivable.

## Checklist
- [x] Login state verified (Repttown, /payroll loads, empty list ปี 2026)
- [x] P1 payroll list UX — ภ.ง.ด.1ก no-data → 422 + BLANK toast (PR-1); period input OK
- [x] P2 create รอบจ่าย draft — created OK; dup-period POST → 422 rejected, modal stays (good);
      calc verified: salary 30,000, hire 2026-07-12 → PIT 0 (correct: 6 remaining periods
      → annualized 180k < threshold), SSO 0 (ssoApplicable=false)
- [x] P3 payroll detail UX — no payslip breakdown/edit (PR-4); posted-state features untested
- [x] P4 delete draft — works (confirm modal, redirect to list); ran twice (#1, #2)
- [x] Employees settings tested — salary edit saved (via 503-but-applied!), stale-form bug
      found (PR-8), i18n leak common.yes/no (PR-7)
- [x] R1..R14 all 14 report pages tested (2 transient ChunkLoadError crashes, both recovered
      on reload; cross-report number consistency verified end-to-end)
- [x] REPORT-payroll-reports-uxtest.md written (9 payroll + 11 report findings + S13 chain)
- [x] specs/fix-payroll-reports-findings-2026-07-16.md created — PENDING HAM APPROVAL
- [ ] Manual ใหม่ — DEFERRED: goal condition "หากไม่มีปัญหา" not met (พบ bug จริงหลายตัว);
      ทำหลัง fix round ปิด มิฉะนั้น manual จะ document พฤติกรรมที่กำลังจะเปลี่ยน

## Findings log
- PR-1 (bug, app-wide): openPdf/downloadFile (frontend/lib/api.ts:171,181) throw
  `ApiError(status,'open_failed',res.statusText)` — HTTP/2 statusText is EMPTY and the
  problem+json body is discarded → error toast renders BLANK (seen live: ภ.ง.ด.1ก year
  2026 → 422 → empty red toast). Affects every PDF/print/download error path app-wide.
- PR-2 (UX): payroll create modal วันที่จ่าย native date input has no BE/format hint —
  sales fix round added hints to QT form + list filters; payroll modal missed.
- PR-3 (UX): zero-salary employee pulled into run silently (BUTEST-EMP, ฿0.00 all cols);
  no warning "ยังไม่ได้ตั้งเงินเดือน" / no link to settings/employees.
- PR-4 (UX): payroll detail — no per-payslip breakdown view/edit in UI (rows not
  clickable; only payslip PDF). No recalc / add-remove employee on draft.
- PR-5 (observed once, S13 family): RSC prefetch `/payroll/1?_rsc=` → 503 (twice) at edge;
  matches known CF/edge 503 issue. Also one non-repro renderer freeze (print-preview-like
  state) recovered by Escape.
- Note: period input HAS placeholder + regex validation + digit-strip (OK). Search on
  payroll list = docNo only; drafts have docNo "—" so unsearchable (minor).
- Untestable without POSTing (won't touch real GL): posted-state buttons ภ.ง.ด.1 PDF,
  สปส.1-10 file/PDF, 50ทวิ per employee, post→pay flow. Manual must cover from source.
- PR-6 (INFRA, S13 smoking gun): PUT /api/proxy/employees/2 22:35:08 ICT → origin nginx
  log shows **204**, browser received **503** = "503-but-applied" REPRODUCED with full
  evidence chain. Also GET employees/2 503'd 4× in browser while origin saw only one
  request (200) → CF edge generating 503s without contacting origin. Origin access log
  (proxy-host-13) has ZERO 503 entries today. All client IPs 172.68.x (CF).
- PR-7 (bug, i18n): `common.yes`/`common.no` keys MISSING in th.json+en.json — employees
  page SSO column + its filter dropdown render raw "common.no". Only expenseCategory ns
  has yes/no. (settings/employees/page.tsx:93 tc('yes')/tc('no'))
- PR-8 (bug, data-loss risk): employee edit modal seeds ONCE from React Query CACHE
  (page.tsx:58 populate-once `if detail.data && edit===null`); after background refetch
  the form keeps STALE values — reopening the modal after an edit shows the OLD salary
  (seen live: list ฿30,000, reopened form 0). Saving would silently revert.
- PR-9 (UX): employee detail fetch fails → pencil click does NOTHING (no toast, no
  spinner, no retry; RQ error state cached so subsequent clicks don't even refetch until
  full reload).
- PR-10 (a11y): icon-only pencil edit button has no aria-label (a11y tree: "no name").
- PR-11 (minor): success toast on create payroll run says generic "บันทึก"; delete-run
  confirm button is primary orange, not destructive red, despite variant:'destructive'.
- Note: /api/proxy/attachments/3/download (310KB) fetched on dashboard page load
  (company logo?) — verify caching later, not payroll-specific.
- Automation note: CDP screenshot intermittently times out (Escape recovers); NOT
  user-facing evidence; correlated loosely with modal-open/nav moments.

## Resume steps (if cut by quota)
1. Read this file + REPORT-payroll-reports-uxtest.md
2. tabs_context_mcp → new tab → https://teas.kazaki-rio.com/payroll
3. Continue at first unchecked item
