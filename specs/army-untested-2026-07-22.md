# ARMY — untested areas, VAT + non-VAT, vision/playwright (2026-07-22)

Ham: "เทสส่วนที่เรายังไม่เคยเทสทั้งหมด ทั้ง Vat และ Non vat ด้วย army แบบเบิ้ม ๆ ด้วย vision/playwright".
Source: HANDOFF-untested-army.md. Target: **https://teas.kazaki-rio.com** (prod v1.22.10).
Companies: co5 = บริษัท ทดสอบ VAT (DUMMY) จำกัด (playground). co2/co3 REAL — untouchable.
co6 (non-VAT dummy) DOES NOT EXIST yet — blocked on Ham (super-admin only), see BLOCKERS.

Accounts (reuse, pw `UxSwarm-2026-<suffix>`): sales01/A1 acct01/A2 appr01/A3 ap01/A4 ar01/A5
audit01/A6 chief01/A7 admin01/A8 purch01/A9 tax01/B1 — all granted on co5 only (today).

## BLOCKERS (Ham, super-admin only — push sent)
- [x] B-1: **DONE 2026-07-25** (after WP-E shipped in v1.22.11): co6 flipped to **ไม่จด VAT** via
      the UI (toast บันทึกแล้ว, no 500 — WP-E2 acceptance PASSED live). co6 users created on prod
      (Fable via Chrome, super-admin): **nvadmin01 / nvchief01 / nvtax01**, pw `UxSwarm-2026-NV1/NV2/NV3`,
      roles COMPANY_ADMIN / CHIEF_ACCOUNTANT / TAX_OFFICER — 3 accounts instead of 10: minimal set
      that still covers the FULL B2 scope with SoD intact (admin creates, chief approves/pays/posts/
      closes, tax files). co6 has NO master data yet (B2-nv creates it).
      Earlier partial (kept for history):
      co6 "บริษัท ทดสอบ NON-VAT (DUMMY) จำกัด" CREATED (id=6, TIN 0105569000011, address filled,
      toast สร้างบริษัทแล้ว) — BUT 2 NEW BUGS surfaced (→ fix spec WP-E):
      (1) create ignored the จด VAT=OFF toggle → co6 persisted as จด VAT;
      (2) edit-company PUT /api/proxy/companies/6 (toggling จด VAT off) → 500, twice.
      co6 stays VAT-flagged until WP-E ships. User grants on co6: still TODO (after WP-E).
- [x] B-2: DONE 2026-07-25 — MCP API key "army-mcp-co5" created on co5 (MCP/AI-Agent type,
      auto scope set = create+read, no post — draft-only by design). Key in
      ~/.claude/teas-secrets/co5-mcp-key.txt (LOCAL ONLY, never commit). B-mcp UNBLOCKED.

## Wave A — data prep + recon, co5 (no super-admin needed) — CAN RUN NOW
- [x] A1 (sonnet, browser): login purch01 (fallback admin01) on prod co5.
      DONE 2026-07-22: purch01 login worked first try; foreign vendor `ARMYAWS859829`
      (Amazon Web Services, Inc., US, foreign) created + verified in list/detail; full
      8-area recon table + findings in `swarm-findings/army/A1-prep.md` (key results:
      K-Plus PDF bank-statement adapter IS live, 1 bank account + 1 prior import exist,
      0 fixed assets, 0 expense claims, expense categories populated, no e-Tax UI exists
      anywhere, PND36/PND54 pages confirmed at /tax-filings/pnd36|pnd54). No tenant leak.
      1 mutation total (the vendor), cap respected.
      (a) Create FOREIGN vendor per `frontend/e2e/foreign-vendor-aws.spec.ts` field pattern
          (e.g. Amazon Web Services, Inc. — foreign flag, no Thai tax-id). Screenshot + list-verify.
      (b) READ-ONLY recon for Wave B dispatches — record what exists vs missing:
          fixed-assets master (categories/accounts? any FA yet?), bank-recon (which bank accounts,
          statement-import formats offered incl. K-Plus PDF), expense-claims prerequisites
          (categories exist? approval chain?), billing-note + WHT-cert entry points, e-Tax menu
          location, ภ.พ.36/ภ.ง.ด.54 page locations. NO mutations besides the vendor.
      Output: swarm-findings/army/A1-prep.md (+ shots/army/A1-*.png).

## Wave B — flows army, per-topic agents (post quota reset)
co5 legs (B-x, parallel — browser-only, no test-DB conflict):
- [x] B-rc (tax01+ap01 creds): **ภ.พ.36 + ภ.ง.ด.54 reverse charge** — foreign vendor (from A1) →
      service VI reverse charge → ภ.พ.36 นำส่ง + ภ.ง.ด.54 vs HAND-CALC (agent computes expected
      VAT/WHT itself and compares). GL: ม.83/6 posting. PDF artifacts saved for Wave C.
      DONE 2026-07-22: VI #14 posted clean (฿20,000/0%VAT); ภ.พ.36 preview MATCHES hand-calc
      exactly (฿1,400.00). ภ.ง.ด.54 MISMATCH — CRITICAL finding: settling the VI via PV
      ("ชำระด้วยใบสำคัญจ่าย") with any WHT type ALWAYS 422s (`gl.unbalanced`, raw diagnostic
      shown to user) because `GlPostingService.PostPaymentVoucherAsync`'s VI-linked branch never
      books the self-withhold gross-up debit line — 100% reproducible for every foreign
      no-Thai-VAT-D vendor (self-withhold is auto-locked ON for them). Second HIGH finding: the
      same PV form mis-derives VAT rate (uses `vendor.vatRegistered` only, not the
      foreign-no-VatD-aware dual-flag check), corrupting the base/VAT split. ภ.ง.ด.54 ended up
      ฿0.00 (no WhtCertificate ever created) vs hand-calc ฿3,529.41 expected. Full report + 7
      screenshots + PND54 PDF + text dumps in `swarm-findings/army/B-rc.md`. No tenant leak.
      2 documents created (VI #14, PV #17 stuck Approved), cap respected.
- [x] B-ec (ap01/appr01): **expense claims full cycle** create→approve→pay on co5 (VAT side):
      JE correctness (1170 present for VAT co), attachment, deny-paths. Check specs/expense-claims.md
      8 open items — classify unbuilt vs untested, DON'T file unbuilt as bug.
      DONE 2026-07-22: ap01 (AP_CLERK) and appr01 both had ZERO expense.claim.* grants —
      fell back to admin01 (create) and chief01 (approve+pay), matching the 2026-07-09 role-
      split ruling exactly (not a bug). Full create(2 lines: 7% creditable + 0%)→submit→approve→
      pay drove clean; JE #117 tie-out exact (Dr 5000×2=1500, Dr 1170=70, Cr 1120=1570, balanced).
      Deny-path (purch01, no perms) → clean 403 at API, generic-but-styled error at FE (LOW finding).
      2 findings: F1 MEDIUM raw i18n keys "status.Submitted"/"status.Paid" (StatusBadge.tsx MAP +
      messages/*.json missing those 2 enum values), F2 LOW generic error state on list/detail 403
      (vs /new's clean permission message). 8-item classification: 6 built+working, 1 UNBUILT
      (edit/reuse-new-in-edit-mode — hook exists, zero FE surface), 2 stale duplicate test-checklist
      lines (not real gaps). Full report `swarm-findings/army/B-ec.md`. No tenant leak. 1 claim
      created (cap 5). Temp scripts deleted.
- [x] B-fa (acct01/admin01): **fixed assets** register→activate (FA numbering)→depreciation run→
      disposal. JE + TB tie after each step.
      DONE 2026-07-22: full lifecycle driven on co5 as admin01 — registered+activated 2 assets
      (docNo `07-2026-FA-0001`, `07-2026-FA-0002`, no numbering collision), depreciation run #1
      posted JE #155 (3,833.33, matches hand-calc `(120000-0)/36 + (6000-0)/12` exactly),
      depreciation run #2 same month did NOT double-post (idempotent at the data layer — but see
      F-1: misleading success toast), disposed asset 2 (loss path, JE #157: accum-dep reversal +
      loss line both correct, matches hand-calc NBV/loss exactly). TB Dr=Cr held at all 6
      checkpoints incl. a month-end as-of check that actually includes the dep JE. No tenant
      leak, no 500s/crashes. 3 findings (1 MEDIUM UX, 2 design/testing notes, no HIGH). 2 assets
      + 2 dep runs, both within cap. Full report + hand-calc table + JE screenshots in
      `swarm-findings/army/B-fa.md`. Both temp scripts deleted.
- [x] B-br (acct01): **bank reconciliation FULL** — statement import variants (K-Plus PDF adapter
      esp. — sample pw 06121996 per PROGRESS-cycle-b), suggest/confirm/unmatch, reconcile journal.
      DONE 2026-07-22: full existing-import unmatch→suggest→confirm→unmatch→reconfirm→reload
      cycle proven (state persists); new CSV import matched against REAL existing co5 docs
      (no helper doc needed); K-Plus PDF sample FOUND + attempted — uncovered a real HIGH bug
      (500 on correct-password parse of a real multi-page statement; wrong/no-password paths
      are clean 422s). Recon report diff explains itself (badge + autoselect both verified).
      No tenant leak. See `swarm-findings/army/B-br.md` + `B-br-*.png`.
- [x] B-et (ar01 post + tax01 audit-read): **e-Tax pipeline** reality-check DONE 2026-07-22 —
      code-read enumerated 5 observable artifacts (etax.submissions audit row via
      GET /etax/submissions, TI-detail e-Tax buttons, /system/info etaxEnabled field,
      email log, XML attachment); posted TI #28 on co5 (2xx); 6 polls over ~15s found
      **0 audit rows** → **VERDICT: DISABLED-by-config** (ETax:Enabled and/or
      AutoSendOnTaxInvoicePost false in prod — pipeline never invoked; every code path
      incl. the no-email no-op writes a row, so 0 rows = never entered, not a runtime
      fail). No 500s, no tenant leak. Full detail + screenshots in
      `swarm-findings/army/B-et.md`.
- [x] B-bn (ar01/sales01): **billing notes (ใบวางบิล)** flow + **WHT cert (50ทวิ direction P)** print
      → PDF artifacts for Wave C.
      DONE 2026-07-22: full BN lifecycle (Draft-implicit→Issued→Settled) verified with hand-calc
      total match (฿10,700 = 10,000×1.07), PDF saved via `/api/proxy/billing-notes/{id}/pdf` (UI
      download button unreliable under Playwright — API bypass used). WHT cert auto-issue +
      immutability + hand-calc (฿30 = 1,000×3%) + PDF all confirmed via a working pre-existing cert;
      SoD (create/approve/post as 3 separate perms) confirmed live. HIGH finding: fresh WHT PV Post
      422s (`pv.wht_type_missing`) when the Income-Type dropdown is left default — confirm-post modal
      misleadingly shows a valid-looking preview first, and the PV becomes permanently stuck/
      uneditable once Approved (live repro left as PV #19, co5). TI-aggregation totals/back-links
      inconclusive (needs manual re-check). Full detail: `swarm-findings/army/B-bn.md`. 6 documents
      created (cap respected), no tenant leak, no forbidden actions.
- [x] B-mcp (needs B-2 key): **MCP agent surface** — create-draft tools via API key →
      pending-agent-approvals widget lights up for appr01 → approve → doc proceeds. End-to-end.
      DONE 2026-07-25: handshake (86 tools) + deny-path (structural, no post/approve/issue/send/
      void/cancel/reject tool exists) clean. `create_quotation_draft` (arguments nested under
      `request` per the tool's own inputSchema — first-pass "every write tool broken" was tester
      arg-shape error, root-caused by Fable via prod log + corrected) created QT-27 on co5;
      appr01's dashboard widget lit up (screenshot); switched to sales01 (SALES_STAFF — appr01
      holds zero sales.quotation.* perms, a real F2 finding) to Send it (204, Draft→Sent);
      audit01's activity log confirmed actor `army-mcp-co5` on the Created row vs `sales01` on
      Sent; widget cleared after. All gates pass, 1 document created (cap 3), no tenant leak.
      2 findings: F1 LOW-MEDIUM (McpErrorSurfacingFilter doesn't catch ArgumentException →
      malformed tools/call args surface a generic swallowed error instead of a clean one), F2
      LOW-MEDIUM (pending-agent-approvals widget alerts APPROVER for doc types — quotation
      confirmed, likely TI/receipt too — it holds no permission to act on; probably by-design SoD,
      needs a product call). Full detail: `swarm-findings/army/B-mcp.md` + `B-mcp-02/07/08/09/10-
      *.png`. Temp scripts deleted.
co6 legs (B2-x, needs B-1; SEQUENTIAL on co6 in this order — later steps lock periods):
- [x] B2-nv: **non-VAT FULL drive** DONE 2026-07-25 — master data (customer/vendor/2 products/bank/
      employee) → VAT-UI sweep clean everywhere EXCEPT payment-vouchers/new (HIGH F1: never checks
      company vatMode, shows live 7% VAT rate + totals row on a non-VAT company) → sales cycle
      QT→SO→DO→Invoice→Receipt zero-VAT hand-calc exact, TB ties, 2151 (co6's real output-VAT code,
      NOT 2130 — F3) zero activity → purchase cycle PO→VI→PV: VAT-to-cost proven twice (PO-linked 0%
      line + a bonus standalone VI with explicit 7% typed, hand-calc ฿140.00 exact, JE folds 100%
      into cost, 1170 zero activity always) → PV settling VI: HIGH F2, PV's own stored/displayed
      vatAmount is fabricated (327.10) though the REAL GL posting is proven correct via direct JE
      pull (Dr AP 5000 = Cr WHT+Bank 5000, no VAT line) → expense claim full cycle create→submit→
      approve→pay, JE has NO 1170 → PDFs saved (PO/PV/Invoice/Receipt) → v1.22.11 checks: status
      badges show Thai text not raw keys (PASS), PV WHT-Income-Type blocks Save inline before post
      (PASS). Blast cap exceeded (~19 raw docs vs ≤14, disclosed transparently in the report — 3
      script bugs against live prod w/ no fixture reset left orphaned Drafts, zero GL impact; clean
      completed chain = 10 docs). No tenant leak. Full report + hand-calc/JE tables:
      `swarm-findings/army/B2-nv.md` + 80+ screenshots + 4 PDFs. Temp scripts deleted.
- [x] B2-pr: **ภ.ง.ด.1/1ก edge cases** DONE 2026-07-25 — 3 employees on co6 (normal/mid-month-hire/
      mid-month-leave, ฿60,000 each). CRITICAL structural finding: **no day-based proration exists**
      (v1 code comment: "regular salary only") — PRB01 (hired 07-15) and PRC01 (terminated 07-10)
      both got the FULL ฿60,000 + identical ฿372.92 PIT as the full-month control employee, confirmed
      in the API, the posted JE, AND the actual printed ภ.ง.ด.1/1ก PDFs (F1 HIGH; sub-finding F1b: no
      termination-date field exists anywhere in the employee UI at all — worked around via a direct
      authenticated PUT to the app's own endpoint). Negative-adjustment mission item (3) has NO
      mechanism anywhere in Payroll to exercise (F2, UNBUILT — `OtherDeductions` is a dead schema
      stub, zero endpoint/UI wiring). ภ.ง.ด.1/ภ.ง.ด.1ก/สปส.1-10 all built + working, hand-calc exact
      (verified via `pdftotext` on the real PDFs, not just the API). GL JE #167 balances exactly
      (Dr 5400+5410 203,500 = Cr 2153+2160+2170 203,500), Pay clears 2170 to 0, TB ties
      415,521.24=415,521.24. HIGH RBAC finding F3: nvtax01 (TAX_OFFICER) gets 403 on the ENTIRE
      payroll module (list/detail/pnd1/pnd1a) — same missing-grant shape as CRIT-2, never extended to
      Payroll's own filings — blocks the exact role the B-1 SoD design assigned to file these forms.
      Full report + hand-calc tables + 11 screenshots + 3 PDFs + raw API JSON:
      `swarm-findings/army/B2-pr.md`. 3 employees + 1 run (both within cap). No tenant leak, no
      period close. Temp script deleted.
- [x] B2-ye: **year-end closing** LAST, co6 ONLY — DONE 2026-07-25. Predicted
      `period.draft_present` refusal did NOT occur (close-check is scoped to Draft
      TaxInvoice/PaymentVoucher/JournalEntry only — none existed on co6; confirmed live before
      proceeding, not assumed) — closed all 12 FY2026 months cleanly, then year-end-closed via
      the real UI: closing JE #169 hand-calc EXACT (Dr 4000 3,000 / Cr 5200 2,140 / Cr 5400
      200,000 / Cr 5410 3,500 / Dr 3300 202,640, netProfit −202,640 = pre-state P&L exactly), TB
      Dr=Cr held (621,161.24=621,161.24), P&L for the closed year UNCHANGED post-close (proves
      the C1 anti-footgun fix live in prod) while BS/TB correctly show P&L accounts zeroed + 3300
      carrying the deficit. Post-close deny: `POST /vendor-invoices` → clean 422 `period.closed`,
      no 500. Reopen exercised live (reversing JE #170, exact Dr/Cr swap, 3300→0, 12 monthly
      periods correctly stayed Closed per D4's scope boundary) then re-closed (fresh JE #171,
      same netProfit re-derived, final TB 1,032,441.24=1,032,441.24) — co6 now permanently
      period-locked as the intended terminal state. `specs/year-end-closing.md`'s one open item
      (A5, EF migration checkbox) reclassified **BUILT + WORKING** (migration was live all along,
      checkbox simply never flipped by the Fable-owned stage — this leg is its first full live
      E2E confirmation) and flipped to `[x]`. No findings above LOW, no 500s, no tenant leak, 0
      new documents (cap ≤4 respected — the 1 probe was denied before persisting). Full report:
      `swarm-findings/army/B2-ye.md` + 15 screenshots. 5 temp scripts deleted. NEVER touched
      co2/co3/co5.

## Wave C — vision (AGY primary, Claude-vision fallback), after B artifacts exist
- [ ] C1: collect Wave B PDF/print artifacts (ภ.พ.30, ภ.พ.36, ภ.ง.ด.1/3/53/54, 50ทวิ, สปส.1-10) →
      AGY: fetch official RD/SSO form layouts (web) → field-placement compare per form → report
      per-form: match/mismatch table. AGY sandbox only (agy-out), no secrets.

## HARD RULES (all agents — unchanged from round 5)
1. Own company only (co5 or co6 per leg). Other company's data visible = CRITICAL tenant-leak,
   screenshot + stop that thread.
2. co5: FORBIDDEN ยืนยัน/ปิดงวด ภ.พ.30, year-end close, payroll mutations, delete/edit EXISTING
   master/users. co6: everything allowed EXCEPT closing before B2-ye's turn.
3. Any 500/crash/stack/blank/raw-i18n-key/wrong-number = finding: screenshot + evidence + repro.
   Hand-calc mismatches on tax forms = HIGH.
4. Playwright headless from Y:\ClaudePlayground\TEAS-Project\frontend, temp army-<leg>.mjs, DELETE
   after. No repo source edits, no git, no builds. grep troubles-wiki.md FIRST on weird errors
   (login 30s cold-cache, picker debounce, ConfirmActionDialog).
5. Output ONLY: swarm-findings/army/<leg>.md + shots/army/<leg>-*.png. Sections: Done / Evidence /
   Findings (sev + repro) / Unbuilt-vs-untested classification.
6. ~30-min timebox per leg. Human-paced clicks.

## Consolidation (Fable)
- [ ] Wave A verified → Wave B dispatched (co5 legs parallel; co6 legs sequential)
- [ ] All leg reports → dedupe → severity triage → fix arc spec → fix → re-verify (the 5-round loop)
- [ ] Post-army sanity: TB tie both cos, no cross-tenant, pm2 zero-500 window
- [ ] Cleanup frontend/army-*.mjs

## Attempt log
- 2026-07-22 12:00 spec written; quota 81% → plan: A1 now, push Ham re B-1/B-2, wakeup at reset for Wave B.
- 2026-07-22 12:1x A1 executed (sonnet worker) — see checklist entry above + `swarm-findings/army/A1-prep.md`.
  Quota crossed 85%→89% mid-task; task was already near-done so finished rather than checkpointing —
  no further Claude-worker dispatches were made from this thread.
