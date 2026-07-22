# STATUS.md — orchestrator live board

## Now
- **v1.22.10 LIVE (2026-07-22 ~02:5x) — non-VAT F-A..F-D shipped+deployed; NEXT SESSION = army
  on untested areas, see HANDOFF-untested-army.md.** ExpenseClaim non-VAT 1170 guard (money,
  Fable-reviewed) + PO paper vendor address + control heights + VI non-VAT wording. Suite 921/0/8.
  API scripts unchanged 75, FE full, public probes 200. co5 address filled (.txt export unblocked).
- **ROUND-5 SWARM VERDICT: ALL FINDING FIXES CONFIRMED CLOSED on v1.22.9 (2026-07-22 ~00:5x).**
  10/10 agents: WP1 17/17 deny-pages clean; WP2+WP6 15 modules render real data for AUDITOR, BU-403
  spam = 0; WP3 4/4 report-UX items live; WP4 widget 200 4/4; WP5 api-keys #418 gone + users
  self-guard + VI-clobber fixed + Thai toast. CRIT regression: sales 27/27, ar 12/12, purch+appr
  approves all 2xx, TB Dr=Cr 9/9, tenant/SoD clean, zero 500/23505 everywhere. Residual LOW nits
  (file-response EN toast, statement-imports 403-vs-empty, 2 stray 403s, peer-admin guard untested
  no-data) logged in swarm-findings/round5/ for a future batch. Goal COMPLETE.
  - CRIT-1/CRIT-2: CLOSED + verified live (v1.22.7 on prod; round-4 10-agent swarm = zero
    500/23505 across every numbering path, TB Dr=Cr held, tenant/SoD clean).
  - Finding fixes committed to main, awaiting a v1.22.8 deploy:
    - WP1 `5c49234` — FE route-guard on 16 /new routes + CN/DN buttons + tax-filing/period-close/
      attachment (clean deny, backend still 403s).
    - WP2/WP4 `5c49234` — 628 seed grants AUDITOR module reads + APPROVER pending-approvals read
      (suite 912/0/8).
    - WP3/WP5 `043935c` — report date-basis labels + AP-aging tie banner + AR negatives + bank-recon
      badge; api-keys deny+#418, users self/peer-admin guard, EN-toast i18n, VI-new category clobber.
  - **RESUME (do NOT auto-start — Ham paused):** (1) WP6 read/manage split (BU/quotation/SO/DO/vendor
    — footgun auth, Opus review; Claude 7-day pool was 95%, consider Codex/wait). (2) deploy v1.22.8
    (API has 628 SqlScript → scripts +1, DB backup; FE full) + prod verify. (3) big Sonnet swarm
    round 5 to confirm every finding closed — needs Claude budget, gate on 7-day recovery.
  - Spec: specs/fix-swarm-findings-all.md (WP1/2/3/5 [x], WP6 open). Swarm evidence: swarm-findings/.
- **(prev) CRIT-1 REFIX — v1.22.7 LIVE + PROD-VERIFIED (2026-07-20 ~05:xx).** v1.22.6 fixed only the
  no-ambient-tx path (PO approve); round-3 swarm caught TI post still 500 3/3. opus-debugger found
  the REAL bug: off-by-one retry cap (`attempt < MaxAttempts` left the final-attempt doc_no collision
  uncaught → raw 500; in an ambient tx the escape rolls the seq bump back so it never climbs). Fix:
  catch every attempt + explicit savepoint after allocate/before SaveChanges + cap 5→50; tests now
  drive the REAL TaxInvoice/Receipt PostAsync (drift=8 + parallel) RED→GREEN. Fable diff-reviewed the
  one file; af5ab8a → CI green → v1.22.7 API deploy (scripts unchanged 73) → **real TI post on prod
  co5 = TI-0004 Posted** (the closing check that was skipped for v1.22.6). CRIT-2 already verified
  (round-3 ภ.พ.30 preview/PDF 200). NEXT (Ham /goal, after quota reset): swarm round 4 (concurrency
  proof) → fix ALL remaining findings (specs/fix-swarm-findings-all.md WP1-5) → swarm round 5.
- **(prev) SWARM CRIT-1/CRIT-2 FIX SHIPPED — v1.22.6 LIVE (2026-07-19 ~23:5x).** 10-role concurrent
  swarm on co5 exposed doc-number sequence drift (post/approve 500 → 23505 on *_doc_no) +
  TAX_OFFICER missing tax.filing grant. Root causes CONFIRMED from prod pm2 log + drift query.
  Fix: 626 reconcile SQL (GREATEST-only, per-company FORCE RLS) + retry-guard helper wired to
  14 alloc services + 627 tax grant. Sonnet impl → Opus Tier-2 APPROVE (5 lenses) → Fable
  reviewed 626/627/helper/GL/Receipt money paths personally → 3531052 → CI green → v1.22.6 API
  deploy: scripts 71→73 (+2), **seq drift co5 JV delta=0 (reconcile healed it)**, ภ.พ.30 alive,
  public probes 200. Suite 1053/1/8 (1 = documented Pnd50 flaky). Worker also fixed 2 rollout
  footguns (Receipt CashReceived vs 570-freeze ordering; ExpenseClaim first-attempt guard).
  NEXT: re-swarm round 3 to PROVE CRIT closed under concurrency (Ham /goal).
- **(prev) USAGE-DRIVE FIX ROUND SHIPPED — v1.22.5 LIVE + VERIFIED (2026-07-19 ~16:4x,
  Ham "แก้สิ เราต้องการ Webapp สมบูรณ์").** Sonnet impl per
  specs/fix-usage-drive-findings.md → Fable diff read → d04b290 → CI green →
  PR #91 → v1.22.5 → full deploy (API DEPLOY_OK 7/7 probes incl. new
  ar_aging_auth, DB backup, scripts 71 unchanged; FE_DEPLOY_OK no-sudo;
  public probes 200/200/401). **F-1 VERIFIED LIVE**: AR aging shows C001
  −1,070 row, table total 4,280 = control banner. **F-3 VERIFIED LIVE**:
  QT→TI prefill carries ชิ้น/งาน. F-2 = not a code bug (invalidation already
  present pre-v1.22.3; prod sighting likely stale edge-cached bundle).
  F-4 = documented CDP screenshot tooling artifact, payroll code clean.
  Worker flagged: SO has NO prefill path to TI/DO at all (pre-existing gap,
  out of scope) — backlog candidate.
- **(prev) CO5 USAGE DRIVE COMPLETE (2026-07-19 ~15:1x, Ham "ลองใช้งานซื้อ/ขาย/Payroll/
  รายงาน").** Fable drove prod v1.22.3 live via Chrome on co5: purchase
  PO-0002→VI-0003→PV-COGS-0002, sales QT-0002→TI-0003→RC-0002 (direct-TI
  shortcut), payroll 09/2026 full cycle (create→approve→post→pay KBANK) +
  08/2026 PIT 7,008.33 = hand-calc exact. Reports: ภ.พ.30 July
  910/1,050/เครดิตยกไป 140 ✓, TB Dr=Cr + **RE-TEST (c) confirmed (new COGS
  VI → 5000)**, AR aging tie ✓. Dashboard "VAT 70 ขอคืนได้" sign-bug
  hypothesis REFUTED (was genuinely a credit). 4 minor findings logged in
  PROGRESS-vat-usage-drive.md (top: AR-aging table total 5,350 vs control
  4,280 — net-credit customer C001 hidden from rows). No code changes.
- **(prev) RESIDUAL NITS SHIPPED — v1.22.3 + v1.22.4 LIVE, ALL VERIFIED (2026-07-19
  ~12:2x, Ham "แก้เลย").** N-1 (CN/DN list shows TI doc-no via server JOIN) +
  N-2 (draft-only delete, 899/8/0 suite, RBAC wiring fixed) → v1.22.3 full
  deploy; verify pass found delete-toast raw key `common.deleted` (latent on
  Quotation too) → 2-line i18n fix → v1.22.4 FE-only deploy (API stays
  1.22.3 by design) → toast "ลบแล้ว" verified; test drafts cleaned, co5 =
  CN-0001 Posted only. New wiki: never sudo the FE deploy script.
- **S13 CLOSED-FOR-NOW (2026-07-19 ~13:0x, CF dashboard checked via Ham's
  login).** Bot Fight Mode OFF; Events(host=teas, 24h) = 7 scanner blocks
  (US IPs, Managed rules) and ZERO events from our heavy automation traffic
  → H1 bot-scoring REFUTED; WAF Skip rule = no-op, NOT applied. Leading
  hypothesis by elimination: CF edge↔origin connection race (intermittent;
  no recurrence since 07-18). Recurrence playbook appended to
  specs/fix-s13-cf-edge-503.md (check CF Events SAME-DAY — 24h retention).
- **(prev) F-11/F-12 FIX ARC SHIPPED — v1.22.2 LIVE (2026-07-19 ~02:3x).** Ham
  "เอาตามที่แนะนำเลย" → spec → sonnet impl (8 files) → Fable diff read →
  3fc7619 → CI green → PR #88 → tag → deployed (MinVer 1.22.2, DB backup,
  DEPLOY_OK scripts-unchanged 71, FE_DEPLOY_OK content anchors, public
  probes). **F-11 VERIFIED LIVE** on CN-0001 (Thai reason label on legal
  doc). F-12 live via content anchors; visual eyeball optional (TEAS session
  expired post-deploy). Remaining: S13 CF-dashboard check + WAF Skip rule =
  **waiting on Ham** (CF login in different Chrome profile than extension).
- **(prev) OPEN-ITEMS ROUND COMPLETE (2026-07-19 ~01:3x) — 4/4 tracks done.**
  (1) E3 create_vendor FIXED ac048e8 — stale test fixture (taxId null vs WP1
  domestic-VAT validation 65b9b2b); suite 897/8/0; CI-green confirmation
  pending (watch bg). (2) S13 CF-edge 503 ROOT-CAUSED db29473 — origin fully
  ruled out; top hypothesis CF Bot Fight Mode blocking the automation browser;
  **NEEDS HAM: CF dashboard checklist** (specs/fix-s13-cf-edge-503.md, 8 items;
  proposed scoped WAF Skip rule drafted, not applied). (3) statement-import +
  CN→ภ.พ.30 on co5 PASS af89e0d — KBiz CSV import→match RC/PV exact→recon
  ผลต่าง 0.00; CN-0001 → ภ.พ.30 ขาย 770/เครดิตยกไป 70, TB 172,440 tie; new
  F-11 (CN reason raw enum key on UI+printed doc) + F-12 (low UX batch).
  (4) manual บท 6 posted-state 99f3a8f — Post-JE/Pay-JE/opening-YTD/dup-guard,
  facts code-cited, mkdocs clean (screenshots deferred 🚧). Next candidates:
  F-11/F-12 fix batch, TI PDF + สปส.1-10 PDF visual re-checks.
- **(prev) VAT DUMMY ROUND COMPLETE — FIX LOOP CLEAN (2026-07-19 ~00:2x).** v1.22.1 live; all
  F-findings fixed + VERIFIED-LIVE on co5 (F-3 opening-YTD 7,008.33 exact, F-6 Pay JE
  TB-exact, F-9 COGS→5000, F-8 PND1 column, F-4/7/10 UI-i18n). Re-test found ZERO new
  issues. Open items: E3 create_vendor CI-red (pre-existing), S13 CF-edge 503s,
  statement-import flow + CN + manual posted-state (PROGRESS §Next).
- **(prev) VAT DUMMY ROUND — MAIN ARC (2026-07-18 ~14:2x).** F-1 CRITICAL (company
  creation broken since RLS-600: CreateAsync missed by Family-B inventory, 42501 +
  no tx → half-created tenant) → fixed 4b92efd (tx + LOCAL company_id pin, red→green
  RLS test, Opus APPROVE) → **v1.21.6 deployed** (DEPLOY_OK 10/10, backup, scripts 69)
  → orphan co4 deleted → dummy co5 seeded FULL via UI (1/25/12/15/19/11). Then the
  never-run-before paths, all on co5: **payroll Post #1 in history** (07-2026-PR-0001,
  JE tie exact 127,500, docs ภ.ง.ด.1/สปส.1-10 txt+PDF/สลิป/50ทวิ/ภ.ง.ด.1ก, Pay,
  month-2 continuity, dup-guard 422) + **full VAT sales chain** QT→SO→DO→IV→TI→RC
  (7,490 every hop) + TI-0002 unpaid + **purchase chain** PO→VI→PV (10,700) +
  **ภ.พ.30 real data exact** (ขาย 840 / ซื้อ 700 / สุทธิ 140) + sales-summary/
  tax-summary/AR aging (1130 tie ✓)/TB (169,230 Dr=Cr)/bank-recon (diff 0.00) —
  ALL money math ties to hand-calc. Findings F-1..F-10 in REPORT-vat-dummy-test.md;
  **awaiting Ham: F-3** (PIT onboarding-year under-withholding 1,408 vs 6,075 —
  recommend opening-YTD/ยอดยกมา feature) + **F-6** (Pay = status-only, no settlement
  JE, 2170 never clears). Batchable: F-4/F-7/F-8 (tax-summary ภ.ง.ด.1 col blank)/
  F-9 (COGS→5200, no COGS acct in default CoA)/F-10. Then fix→retest loop per Ham.
- **PAYROLL+REPORTS GOAL COMPLETE (2026-07-17 ~11:3x).** Full arc done: UX test (20
  findings) → Ham "แก้ทั้งหมดเลย แก้หมดแล้วค่อยทำ manual" → fix round W1 7bb293d (error
  infra: blank-toast fix in openPdf/downloadFile, global-error boundaries w/ chunk
  auto-retry, employees modal stale-seed + silent-fail fixes, i18n common.yes/no +
  report.total, P&L dev-note removal, BE hint, destructive confirm) + W2 ce9aba1
  (zero-salary badge/banner, payslip breakdown modal + ม.50(1) explainer, CE period
  hint) + W3 c71c13b (BE dates on statements, TB/BS/P&L CSV + financial-statements PDF
  wiring, shared FE csvCell w/ OWASP formula guard ×5 exports, month/year presets +
  defaults, GL picker code-prefix resolve, bank-recon empty link, sales-summary basis
  footnote — basis confirmed posted TaxInvoices only) → **Manual ใหม่ บท 6 เงินเดือน +
  บท 8 รายงาน** 3ee4433 (hand-authored, facts verified against code; mkdocs nav wired).
  All gates green every WP (tsc, next build, ম grep); Fable read every diff.
  **v1.21.5 DEPLOYED + VERIFIED LIVE (2026-07-18, Ham "ทำเลย ๆ"):** release PR #84
  admin-merged → tag @ 2d786c8 → built from tag worktree (MinVer 1.21.5 ✓) → API
  `DEPLOY_OK` (probes 10/10 incl. new financial-statements route probe; sql_scripts 69
  unchanged; DB backup) + `FE_DEPLOY_OK` (content-check anchors P1/R1/P6/W3) → public
  E2E green → **Chrome smoke test 7/7 PASS** (common.no→ไม่, TB/BS/P&L exports, P&L
  presets+default, GL code-resolve, payroll BE hint + breakdown modal + destructive
  confirm, sales-summary footnote, outstanding-po header). Scripts archived
  publish/v1.21.5/ (ab056f8). **OPEN:** (1) R10 picker/modal first-click intermittent
  (investigate-only); (2) S13 CF log pull — Ham (evidence §Infra, two windows now);
  (3) payroll POSTED-state e2e ยังไม่เคยรัน (ต้อง test company แยก).
- **PAYROLL + REPORTS UX TEST COMPLETE (2026-07-16 ~23:1x, Chrome on prod, Repttown).**
  Goal (Ham /goal): เงินเดือน + รายงาน ละเอียด, UX-first → Finding report → Manual ถ้าไม่มีปัญหา.
  Payroll draft lifecycle tested end-to-end (create/calc/dup-guard/delete — NEVER posted, zero
  GL impact); all 14 report pages tested, cross-report numbers tie everywhere checked.
  **20 findings** → `REPORT-payroll-reports-uxtest.md`; fix spec
  `specs/fix-payroll-reports-findings-2026-07-16.md` **PENDING HAM APPROVAL**. Highlights:
  ChunkLoadError white-screens (2×, CF 503 on _next chunks, no error boundary); blank error
  toasts app-wide (openPdf statusText on HTTP/2); employee edit modal stale-cache seed =
  silent data-revert risk; i18n leaks (common.yes/no, report.total); P&L dev note on prod.
  **S13 SMOKING GUN CAPTURED**: PUT employees/2 → origin 204 / browser 503; origin log has
  ZERO 503s all day → all 5xx generated at CF edge (evidence chain in report §Infra, window
  22:10–22:40 ICT for Ham's CF log pull). **Manual ใหม่ DEFERRED** — เงื่อนไข "ไม่มีปัญหา"
  ไม่ผ่าน; ทำหลัง fix round. Cleanup: no payroll runs left; BUTEST-EMP salary now ฿30,000
  (test fixture, harmless).
- **v1.21.4 DEPLOYED TO PROD (2026-07-16 ~21:37, Ham-approved "deploy เลยลุย").** Sales
  fix round LIVE — 83e47f9 (WP-A backend), e71f3e3 (WP-B FE flow), 996d91a (WP-C polish),
  all 16 S1-S16 findings. release-please PR #83 admin-merged (checks never fire on that
  branch, per wiki) → tag v1.21.4 @ f09cfcf. Built from a fresh worktree
  `Z:\temp\claude\wt-teas-v1214-build` (real path, not subst — MinVer confirmed `1.21.4`
  in Accounting.Api.deps.json before shipping). Code-only release: no EF migration, no
  new SqlScript — `applied_sql_scripts` stayed 69. API `DEPLOY_OK` (DB backup
  `teas-pre-v1.21.4-deploy-*.sql.gz` 281820B; all probes PASS incl. new S15-B probe
  `sales_order_put_route_exists=401` for the new `PUT /sales-orders/{id}` endpoint —
  unauthenticated, route confirmed present without touching any real doc). FE
  `FE_DEPLOY_OK` (content-check on S13a proxy-timeout anchor). Public E2E through
  teas.kazaki-rio.com GREEN: login 200, /mcp 401, /.well-known 200, PUT
  /api/proxy/sales-orders/999999999 401 through the full CDN→NPM→app path. S13b
  (quotation-send double-call safety) verified by the 17 new backend tests
  (SalesUxFixesWpATests) pre-deploy, no live probe per plan. Scripts + DEPLOY-README
  archived + **committed** to publish/v1.21.4/ (aef6b91 — first time a deploy archive
  was actually git-tracked; publish/ is gitignored, prior v1.21.0-v1.21.3 archives were
  left untracked on disk despite STATUS.md claiming otherwise — fixed via `git add -f`).
  **PROD SPOT-CHECK PASSED (2026-07-16 ~21:5x, fresh Ham login, Fable via Chrome):**
  footer v1.21.4 ✓; BU columns show names on /quotations + /sales-orders +
  /delivery-orders incl. via SPA-nav (R8-lesson repro method) ✓; BU + status filters
  populated/Thai (S3/S4) ✓; QT detail BU badge (S10) ✓; S11 confirm dialog live on
  QT #8 send (warning ออกเลขทันที + totals; cancelled, no number consumed) ✓; S1 nav
  no longer flashes empty section headers ✓. Sales fix round CLOSED end-to-end.
  Remaining for Ham (unchanged): CF 5xx log pull (S13), LE cert rate-limit cleanup,
  prod test-doc cleanup (QT #5/#8 drafts), MCP connector re-auth.
- **PURCHASE-SIDE REGRESSION CHECK on v1.21.4 PASSED (2026-07-16 ~22:1x, Chrome, BU
  TEST):** /purchase-orders list BU names + Thai status filter ✓; PO #4 /edit docDate
  still locked w/ R2-B hint + new BE hint on วันที่คาดว่าจะส่ง ✓; PO detail BU badge +
  CTAs + Thai activity ✓; VI-from-PO form INTACT — อัตรา VAT percent field + chips
  still present on non-VAT co (LineItemsTable vatMode change did NOT hide purchase VAT
  fields), PO prefill + ล็อกวันนี้ + งวด ม.82/4 all ✓. No docs posted (rendering-level
  regression only; purchase backend untouched by v1.21.4). Observation: SPA
  transitions intermittently slow (5-10s) tonight — one transient Next router wedge
  when navigating away mid-load (recovered by hard nav, NOT reproducible) — likely
  S13/edge-latency family; fold into the CF log review.
- Phase: **SALES-SIDE UX TEST COMPLETE (2026-07-16 ~06:1x).** Full chain E2E on prod
  BU TEST: QT (create/edit-preserves-docDate/send/accept) → SO post → INV issue →
  RC post (confirm dialog ✓) → Settled ✓ → AR aging balanced ✓ → PDF ✓.
  FINAL report: REPORT-sales-uxtest.md (16 findings). Fix spec ready for Ham go:
  specs/fix-sales-ux-findings-2026-07-16.md. Manual ch.4 refreshed (420a4c0).
  **Top findings: S13 prod intermittently 503s on writes that still APPLY (proxy layer
  — investigate first, dup-risk); S4 QT/SO/DO list DTOs lack BusinessUnitId (BU column
  dead ×3 pages, needs API deploy); S11 no confirm dialogs on QT send (issues number!)
  / SO post / INV issue — only RC has one.** Test docs left: QT #5/#8 drafts +
  QT/SO/IV/RC-TEST-0002 chain (settled). MCP connector TEAS-Repttown still needs
  re-auth (Ham).
- **FIX ROUND DONE (2026-07-16 evening, Ham-approved "แก้ทุก finding"):** S1–S16 all
  landed on main — 83e47f9 (WP-A backend), e71f3e3 (WP-B FE flow), 996d91a (WP-C
  polish). Gates green (17 new BE tests, suite baseline+2; FE tsc + 61 vitest;
  runtime-verified local both VAT/non-VAT cos). S13 = Cloudflare edge (origin clean).
  **NEXT = Ham decisions: (1) release/deploy — backend changed, needs API deploy w/
  DB backup (no schema change); (2) CF dashboard 5xx pull 13:02-13:12 ICT 07-16;
  (3) LE cert rate-limit cleanup (npm-1/9/11/23); (4) prod test-doc cleanup.**
- Prior phase: **v1.21.2 DEPLOYED (2026-07-15 ~20:25) — R2 Option B + BU-column batch LIVE.**
  Ham decisions executed: (B) PO draft edit now PRESERVES DocDate (§10 amended,
  UpdateDraftAsync; Create/Approve pinning untouched; new integration test) + BU-column
  stale-memo fix on all remaining 8 list pages. Commits 0258260 (BU×8) + 6368451 (Option B),
  release v1.21.2 @ ec8b33c (PR #81), API DEPLOY_OK (backup taken, sql-scripts unchanged 69,
  version=1.21.2) + FE_DEPLOY_OK, public login 200. Scripts archived publish/v1.21.1/
  (deploy-*-v1212.sh). Local live-verification: edit preserved docDate 2026-06-22 on PO #8,
  BU names on 2/8 pages fresh-mount. **Prod Chrome spot-check PENDING a fresh login by Ham**
  (8h session expired mid-check): PO #4 /edit → hint "ล็อกตามวันที่สร้างเอกสาร" + docDate
  15/07; create form → "ล็อกเป็นวันนี้"; /vendor-invoices + /invoices fresh mount → BU names.
  Still open for Ham: MCP connector TEAS-Repttown re-auth; other doc types still re-pin
  docDate on draft edit (PO-only per decision scope — batch follow-up if wanted).
- **PROD SPOT-CHECK RESULT (2026-07-15 ~20:45, fresh Ham login):** footer v1.21.2 ✓;
  R2-B on prod ✓ (PO #4 /edit shows stored docDate + hint "ล็อกตามวันที่สร้างเอกสาร";
  create form shows "ล็อกเป็นวันนี้"); /vendor-invoices BU names ✓.
  **NEW FINDING R8: /invoices (billing notes) BU column STILL "#3"/"#1" on prod** —
  business-units fetch 200, cache-bust (?cb=1) did NOT help, /vendor-invoices works in the
  same session → page-specific defect, NOT the deployed-build/cache. The reviewed diff for
  invoices/page.tsx looked identical to the working pages — root cause TBD (suspect: the
  billing-note list row's BU field differs, or its buName lookup misses; diagnose locally).
  **R8 RESOLVED — v1.21.3 deployed (2026-07-15 ~23:40).** TRUE root cause (2nd bug stacked
  under R1): TanStack Table `row._valuesCache` caches the accessorFn result PERMANENTLY per
  row (invalidated only on a new data reference) — a row built before business-units
  resolved keeps "#id" forever; the R1 memo-deps fix couldn't override it. Deterministic
  repro: create→save→SPA redirect to list (3/3 fail pre-fix). Fix: BU column `cell` resolves
  from `row.original.businessUnitId` every render (accessorFn kept for faceted filter) —
  applied to ALL 9 list pages (f6a8356), release v1.21.3 @ 82ac750 (PR #82), FE-only deploy
  FE_DEPLOY_OK, prod-verified via Chrome: /invoices names ✓ + SPA-nav → /purchase-orders ✓.
  Footer shows v1.21.2 = API version (API not redeployed, FE-only — expected).
  troubles-wiki updated (R8 + next-build-vs-dev corruption footgun).
- Prior: **v1.21.1 DEPLOYED + R1-R4 PROD-VERIFIED (2026-07-15 ~18:20) — pipeline COMPLETE.**
  R1/R3/R4 (731e775) + R2-FE locked docDate (526a55b) → release v1.21.1 @ b08387e (PR #80,
  admin-merged per wiki) → FE-only deploy FE_DEPLOY_OK (deploy-fe-v1211.sh archived in
  publish/v1.21.1/, no API/DB change) → all four re-verified on prod via Chrome incl. R4
  full chain (VI-TEST-0003 321 → PV prefill 321 exact → posted → settled PAID). Details:
  PROGRESS-purchase-uxtest.md §R1-R4 FIX ROUND.
  **OPEN for Ham:** (1) backend §10 docDate re-pin on draft edit — keep (current; FE now
  shows locked-today honestly) vs preserve-on-edit (backend change + API deploy);
  (2) same stale-memo BU-column bug latent in 8 other list pages (troubles-wiki entry) —
  batch-fix candidate; (3) MCP connector TEAS-Repttown needs re-auth (token expired).
- Prior phase: **BU TEST CONFIRM ROUND on prod v1.21.0 DONE (2026-07-15 ~11:30, Claude Chrome).**
  Full purchase chain re-run on prod (PO-TEST-0003 → VI-TEST-0002 → PV-TEST-COGS-0001, all
  posted, VI settled 214/214 PAID). 24 findings CONFIRMED FIXED incl. all money/compliance
  (F15 percent-UI, F27 non-recoverable forced server-side, F20 COGS, F13 taxId, F16 8h+refresh,
  F21 single-POST). 4 new issues found: R1 PO-list BU column still #id (F3 not actually fixed
  on that page), R2 PO edit-save resets docDate to today (data bug), R3 po.reopen_blocked
  toast EN-only, R4 PV-from-VI under-settles a VAT-carrying VI of a non-VAT vendor (edge).
  Full log: PROGRESS-purchase-uxtest.md §CONFIRM ROUND. MCP connector token expired —
  Ham must re-auth the claude.ai TEAS-Repttown connector.
- Goal (2026-07-14, Ham away): purchase-side UX/UI test on PROD (BU TEST) via
  Claude Chrome + refresh outdated manual ch.5. PROGRESS-purchase-uxtest.md =
  full findings log (F1–F27; top: F15 VAT-fraction field accepts "7"→700%,
  F16 ~25-30min token TTL w/ silent-401 UX, F20 COGS no default GL on Repttown,
  F27 non-VAT company can post recoverable input VAT).
- Phase: **v1.21.0 DEPLOYED TO PROD (2026-07-15 ~10:35) — purchase-findings fix batch LIVE.**
  Tag v1.21.0 @ bf36ba3 (release-please #79). API + FE both deployed. 623 backfill ran
  under RLS on prod (expense_null_defaults 2→0), applied_sql_scripts 68→69, version=1.21.0,
  all regression + v1.20.0-migration probes PASS, DB backed up. FE build OK, teas-web online.
  Public E2E through teas.kazaki-rio.com GREEN (login 200 / mcp 401 / wellknown 200). Cookie
  TTL 15min→8h config live (effective on next login). Scripts + DEPLOY-README archived to
  publish/v1.21.0/. Build worktree removed. Ham: rotate the leaked password; the pre-existing
  McpServerSmokeTests.E3_create_vendor failure (not from this batch) still needs triage.
- Prior phase: **PURCHASE-FINDINGS FIX PIPELINE — WP1–WP4 landed on main (a86de78).**
  Prod UX test (F1–F29, commit 0106b0b) + manual ch.5 refresh (518e1ed) + all four fix
  work-packages shipped: WP3+WP4 (d88ee51), WP1 money/compliance (65b9b2b), WP2 auth/session
  (d5a9c69), WP3.4 PO close/reopen + WP4.9 SoD (a86de78). Every money/security diff got an
  Opus Tier-2 review (WP1 money APPROVE-WITH-FIXES → F-1/F-2/F-3 applied; WP2 security
  APPROVE-WITH-FIXES → F-A absolute-cap-bypass fixed). Fable read every money/security diff
  personally before each commit. Ham decisions D1–D7 all confirmed + implemented.
  **NOT DEPLOYED — all on main, no prod release cut.** Pending for a release:
  (1) EF: no new migration (623 is a startup SqlScript, Closed enum pre-existed) — but the
  623 backfill runs at API boot on prod → DB backup + per-company row-count probe mandatory.
  (2) Residuals (tracked in spec): F-C (post-modal re-login company context, fails safe),
  F-D (proxy Location, acceptable), F-5 (move rate bound into BuildLinesAsync, hardening),
  WP3.4 reopen has no PV-downstream check (PVs settle VIs not POs, so VI-Posted check suffices).
  (3) Manual ch.5: remove the F15 fraction-VAT admonition + re-capture 05.02 once percent-UI
  is visible in prod. (4) Pre-existing broken test found (NOT from this work):
  McpServerSmokeTests.E3_create_vendor_returns_id_code_name — baseline fails identically,
  needs separate triage. (5) Ham: rotate the password leaked in logs early this session.
  Ham's uncommitted edits (CLAUDE.md, specs/fix-codex-review, specs/mcp-document-chain) left
  intentionally untouched.
- Prior phase (superseded):
  Fix spec: specs/fix-purchase-ux-findings-2026-07-14.md (Opus design §Design + Ham
  decisions D1–D7 all confirmed). Prod UX test + manual ch.5 both DONE earlier.
  - WP3+WP4 (FE flow/polish, F2–F24) merged d88ee51 — Fable diff-reviewed, tsc green.
  - WP1 (money/compliance, F13/F15/F20/F27) merged 65b9b2b — non-VAT non-recoverable,
    percent-UI, vendor taxId rule, RLS-safe category backfill 623 + auto-seed. Opus
    Tier-2 money review APPROVE-WITH-FIXES (F-1/F-2/F-3 applied), 262 backend + 40 FE
    tests green. **623 SqlScript NOT deployed** — prod deploy needs DB backup +
    per-company row-count probe (design has both).
  - **RESUME AFTER QUOTA RESET (5h window, resets ~17:40 / ts 1784041200):**
    (1) WP2 auth/session — Sonnet implement from §Design WP2.1–2.4 (D6 sliding re-issue
    Option A: POST /auth/refresh + BFF route + FE keep-alive hook, absolute cap 8-12h +
    idle timeout; WP2.2 global 401 modal preserving form state; WP2.3 trailing-slash 308
    root cause — VERIFY-before-fix with Network panel, then app-wide slash removal +
    AbortController timeout + proxy Location hardening; WP2.4 Thai error toasts by code).
    Auth = security → Opus Tier-2 review after, then Fable diff + commit + ff main.
    (2) WP3.4 PO Closed status (D3 confirmed: Approved→Closed, no further VI/PV linking,
    drops from open-PO lists, activity-logged, reopen if no posted downstream) — needs
    backend status + /close endpoint. (3) WP4.9 SoD text-align (D4, trivial FE i18n).
    (4) F-5 residual hardening (move rate bound into BuildLinesAsync) — optional later.
    Ham's uncommitted edits (CLAUDE.md, specs/fix-codex-review, specs/mcp-document-chain)
    are intentional — DO NOT commit them.
  - Quota paused at 82% (block 95%) with WP2 a large footgun dispatch pending; wakeup
    chained to reset per Ham's "1hr loop = safe" rule.
- Prior goal: MCP document chain cycle (Ham approved 2026-07-13 morning, autonomous
  while away). Spec: specs/mcp-document-chain.md — §A carries ALL Ham rulings
  (per-hop draft tools, data-driven skip-DO, full-qty only, purchase side in,
  workflow guide + instructions, approvalLinkMarkdown, verify-then-advance).
- Phase: **DEPLOYED — v1.20.0 LIVE on prod** (2026-07-13 ~17:13, tag v1.20.0 @
  76e2467). PR #75 merged, release #76. EF migration `McpDocumentChain`
  (20260713032419) applied cleanly at boot: 3 additive nullable FK columns
  (tax_invoices.sales_order_id/delivery_order_id, billing_notes.sales_order_id)
  + 3 partial indexes + 3 FKs, zero data risk (all nullable, no backfill). API
  27/27 probes PASS (23 carried-forward regression + 4 new migration probes),
  FE 4/4 PASS (incl. sales-orders/[id] create-invoice route), public E2E green
  (login 200, /mcp 401, wellknown 200, sales-orders 307). DB backed up twice
  pre-migration (~/backups/teas-pre-v1.20.0-*.sql.gz on prod, 178895-178896B).
  Footgun found + wiki'd: this project's EF migrations-history table is
  `sys.__ef_migrations`, NOT the EF default `__EFMigrationsHistory` — a probe
  written against the default name 42P01s on prod. Scripts archived to
  publish/v1.20.0/ (deploy-api-v1200.sh, deploy-fe-v1200.sh, DEPLOY-README.md).
  Next: chain E2E at Repttown (real create_invoice_draft/document-chain walk
  through the live MCP connector), then close out this cycle.
- Prior: **v1.19.0 LIVE on prod** (2026-07-13 ~02:15): MCP error
  surfacing + 4 resolver tools + bank-match FE warning (#72). Deploy 23/23
  API probes + FE 3/3 + public E2E green. Post-deploy MCP probes CONFIRMED
  client now sees `[mcp.validation]`/`[mcp.domain_rule] ... (ม.86/4) ...`
  instead of the old generic swallow. Full night's story:
  PROGRESS-mcp-butest-sweep.md (Sana report → zero backend defects → SDK
  swallow bug fixed E2E). PRs #74 + release #73; wiki entry on the MCP
  client-SDK WhenWritingNull test footgun committed (5f52e1e).
- **HOTFIX v1.20.1 DEPLOYED to prod** (2026-07-13 ~22:15, tag v1.20.1 @
  14a1461, API-ONLY — no EF migration, no new SqlScripts, no FE change, FE
  deploy skipped entirely). Fixes H1 BLOCKER (direct-BN settlement never
  flipped Settled + no over-collection amount guard — double revenue
  possible on a settled BN) AND H2 (chain resolver missing the new
  mcp-document-chain SO↔BN/TI forward-FK edges — a chain anchored on a
  skip-DO/DO-direct TI or invoice never resolved its upstream SO/DO/Q).
  H3 (web MarkSettled Issued→Issued no-op logging) not in this release.
  Opus spot-reviewed the hotfix diff APPROVE (2 low-severity residuals
  accepted: F1 concurrent-partial-receipt race, web-only, low; F2 unused
  var, harmless) — see specs/mcp-document-chain.md HOTFIX section.
  Built from a fresh worktree `Z:\temp\claude\wt-teas-v1201-build` (NOT the
  dev worktree `wt-teas-v1201`, left untouched on `fix/bn-settlement-flip`),
  MinVer stamp confirmed `1.20.1`. API 27/27 probes PASS incl. all v1.20.0
  migration-state probes reasserted UNCHANGED (`total_sql_scripts=68`,
  `mcp_chain_migration_still_applied_once=1` against `sys.__ef_migrations`).
  Public E2E green (login 200, /mcp 401, wellknown 200 — bare paths, no
  `/api` prefix, backend has no ingress of its own). DB backed up twice
  pre-deploy (`~/backups/teas-pre-v1.20.1-*.sql.gz` on prod, 203440-203443B).
  md5-verified tar+script local==remote before deploying. Scripts archived
  to publish/v1.20.1/ (deploy-api-v1201.sh, DEPLOY-README.md).
- **CYCLE CLOSED** (2026-07-13 ~22:25): H2 verified LIVE
  (get_document_chain(QT-7) resolves full Q→SO→IV→RC across skip-DO edge).
  H1 verified by transition-exercising tests + Opus tx trace (live browser
  probe blocked by post-restart session expiry — login is Ham-only). Pre-fix
  data corrected: BN 4 (07-2026-IV-TEST-0001) flipped ISSUED→SETTLED via
  one-off SQL, matches financial reality (RC-5 covered it in full; backups on
  prod). Lesson folded into implementer template: state-transition tests
  must EXERCISE the transition, not seed the target state. H3 open-low.
  Phase: IDLE.
- For Ham (morning): (1) new MCP tools appear after the connector's next
  session/reconnect (tool list caches per session); (2) review the 19
  expense categories + account mapping I seeded for co2 (delete-me:
  `DELETE FROM sys.expense_categories WHERE company_id=2`); (3) BUTEST test
  drafts await your delete/void: PV 1, vendor-invoice 1, expense-claim 1 +
  BUTEST-EMP employee; (4) co2 still has NO bank account (bank rec unusable)
  and no real employees; (5) product decision pending: auto-seed expense
  categories at company creation + expense-category/employee CRUD UI (none
  exists — SQL was the only path tonight); (6) push the tightened
  implementer/gate-runner "poll, never turn-end-wait" rule upstream to
  minions-assemble.
- Ham RULED (2026-07-10 evening): #4 = keep as documented limitation (closed,
  no code). #7 = FE warning on >7d gap (sonnet implementing on
  feat/match-window-warning). PV SelfWithhold = investigated by Opus → NOT A
  BUG (TotalPaid conditional on payer mode, single computation site :219,
  worked examples tie out; only a stale comment at PaymentVoucher.cs:52 —
  cosmetic). Findings in specs/fix-codex-review-2026-07-10.md.
- In-flight: none. #7 warning DONE — PR #72 merged to main (CI green, Fable
  diff review pass); rides the next release, no separate deploy.
- Next: PARKED by Ham 2026-07-11 — no next cycle yet. Backlog candidates when
  ready (phase-2s): per-bank GL posting (unlocks DocReconciliationLimited),
  receipt OCR via MCP/AI, bank feed API, tax-vs-book depreciation, FA category
  master; plus the "not now" list in PLAN-feature-cycle-2026-07.md and small
  debts (PaymentVoucher.cs:52 comment, mobile-viewport smoke, PR #72 rides the
  next release).

## Recently done (2026-07-10 evening)
- v1.18.0 DEPLOYED — (1) MCP expansion v2: 14 read/draft-create tools for bank
  rec, expense claims (+list_employees, PII-slim), fixed assets; scopes in
  McpScopes.All + FE picker; no state-changing tool (test-asserted). (2) Codex
  fix round: all 10 accepted findings fixed (bank-rec report scoping per Opus
  addendum, override validations, double-match unique indexes, draft-edit 409s,
  CSV injection, parser strictness) + 24 targeted tests. Suite 957/0/8.
  API DEPLOY_OK 21/21 (incl. match_target_unique_indexes=2, total_sql_scripts=68
  prod-baseline), FE_DEPLOY_OK (api-keys route + 3 regressions), public E2E
  green (login 200, proxies 401, /mcp 401). PRs #69/#71, release #70.
  Pre-deploy dedup gate ran clean (prod had 0 matched lines).
- Codex cross-family review of v1.14.0..v1.17.0 delivered 11 findings (2
  BLOCKING) that three layers of Claude-family review missed — cross-family
  review now proven twice (Cycle B + this round).

## Recently done
- 2026-07-10 v1.17.0 DEPLOYED — Cycle C expense claims (submit/approve/pay,
  self-contained JE, no WHT) + Cycle D fixed assets (register, straight-line
  depreciation with dual-direction final-month plug, disposal/write-off,
  period-close hook). API DEPLOY_OK 21/21 probes (seeds 616-622 first try,
  fanout exp=20 fa=22, fa_accounts=10, RLS true), FE_DEPLOY_OK (3 new routes),
  public E2E green (login 200, proxy 401s, pages 307). PRs #66/#68, release #67.
- 2026-07-10 deploy false-fail lesson: total_sql_scripts probe expected repo
  file count (88) but prod ledger has 68 (pre-squash scripts baked into EF
  migrations, never individually recorded) — auto-rollback fired on a healthy
  deploy; fixed expectation, re-ran, DEPLOY_OK. → troubles-wiki.
- 2026-07-10 quota cliff mid-D-implement (session limit) — checkpoint+wakeup
  protocol worked; resumed clean at reset.

## Recently done
- 2026-07-09 v1.16.0 DEPLOYED — bank reconciliation live: bank master, KBiz CSV +
  K-Plus PDF (password) adapters, matching + inline JE, reconciliation report.
  API DEPLOY_OK 13 probes (bank_tbl=3 scripts=2 perms=5 fanout=30 rls=true; seeds
  passed FIRST TRY on prod — post-42501 RLS-safe patterns held), FE_DEPLOY_OK,
  public E2E green (proxy 401s, pages 307). PR #64 (4+1 commits), suite 882/0/8.
- 2026-07-09 review chain earned its cost AGAIN: Opus Tier-2 (B4, 2 findings fixed),
  Fable diff review caught SPEC-level tie-out sign flip, sonnet cross-review caught
  cumulative-window bug + CI-only storage-path test failure. 3 money bugs, 0 reached prod.
- 2026-07-09 v1.15.1 DEPLOYED — Cycle A (year-end closing, period close UI, ar-aging
  CSV, docType i18n) after 42501 seed-RLS hotfix.

## Blocked / waiting
- Ham to confirm: .gitignore had no other entries beyond codex-out//agy-out/ (reset
  --hard incident, restored 2026-07-09 — see PROGRESS-cycle-a retro).
- Carryover: FE browser smoke of prod (Ham login at Chrome tab) — now covers v1.16.0.
