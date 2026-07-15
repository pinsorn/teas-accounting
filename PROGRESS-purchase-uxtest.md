# PROGRESS — Purchase-side UX/UI test on prod + manual ch.5 refresh (2026-07-14)

Goal (Ham, away today): Claude Chrome test PRODUCTION purchase side, BU TEST company.
Click EVERY button, follow real work pipeline, post real docs (Ham approved full posting).
Judge UX/UI ease-of-use + spec compliance. Then refresh outdated manual (Thai, Markdown
+ screenshots in repo). Quota rule: never die without wakeup; 1hr loop allowed.

## Ham rulings (2026-07-14 morning)
- Chrome already logged into prod (session cookie live). Claude never handles passwords.
- Posting real documents in BU TEST = allowed, full pipeline.
- Manual: Thai, Markdown + screenshots, committed to repo (existing mkdocs pipeline).
- Prod URL: https://teas.kazaki-rio.com

## Plan
- [ ] Phase 1 — Fable-personal Chrome UX test on prod (BU TEST):
      vendor master → PO (create/edit/approve/PDF/list+filters) →
      vendor invoice (link-PO path + manual path, post) →
      payment voucher (WHT, approve, post, 50ทวิ PDF) →
      vendor ledger / GL / AP aging spot-check.
      Record findings: UX friction, spec deviation, manual-outdated notes.
- [x] Phase 2 — manual ch.5 refresh: DONE 2026-07-14 ~13:30. Sonnet worker updated
      05.01/05.02/05.03 (05.04–06 verified no-drift), 05.02 expanded to 8 steps,
      6/6 capture pass, gen-markdown + mkdocs green, off-by-one step refs fixed in
      follow-up, Fable diff-reviewed, committed 518e1ed + pushed (rebased onto
      v1.20.1 release commits; autostash conflict in specs/mcp-document-chain.md
      resolved keeping both blocks, Ham's uncommitted edits preserved).
- [ ] Final: findings report for Ham + commit manual.

## Key context
- Manual pipeline: frontend/manual/run-capture.spec.ts (Playwright, personas, local dev)
  → docs/manual/captures/05/*.json+png → frontend/manual/gen-markdown.mjs →
  docs/manual/generated/chapter-05.md. Walkthroughs 05.01–05.06 exist (captured Jun 14-16).
- Purchase chain per manual: PO → บันทึกใบกำกับภาษีซื้อ (VI) → ใบสำคัญจ่าย (PV) + 50ทวิ.
- v1.20.1 live on prod (MCP document chain incl. purchase-side draft tools).
- Claude-in-Chrome screenshots are inline-only (can't save PNG) → manual PNGs come from
  the Playwright pipeline, not from the UX test session.

## Findings log (Phase 1)
- F1 (UX/edge): session expired → dashboard renders stale cached shell, nav sections
  (ขาย/ซื้อ) show as EMPTY headers instead of redirecting to login. Confusing state.
- F2 (UX/i18n): date filter + form date inputs = native mm/dd/yyyy (CE) while tables/
  preview show Thai BE (14/07/2569). Inconsistent locale on /purchase-orders list + create form.
- F3 (UX): PO list column "หน่วยธุรกิจ" shows "#1" (raw id) not BU name.
- F4 (UX): vendor picker modal in PO form has no inline "สร้างผู้ขายใหม่" — must leave form.
- F5 (spec?): PO totals have NO VAT row (ยอดก่อนภาษี/ส่วนลด/รวม only) — manual 05.01 says
  VAT 7% auto-computed. Verify: vendor non-VAT vs UI change. → later resolved, see below.
- F6 (UX, significant): PO DRAFT cannot be edited — no แก้ไข button, /edit route = 404.
  Only อนุมัติ/ยกเลิก. Typo = cancel + recreate whole doc.
- OK: PO create form matches manual structure (①ผู้ขาย ②ข้อมูลเอกสาร ③รายการ), live A4
  preview, BU TEST selectable, line calc correct (2,500 + 1,620 = 4,120, discount 10% OK),
  draft saved as #2 (no doc number pre-approval, per spec), activity log records creator.

- F7 (UX): PO อนุมัติ = one click, NO confirmation dialog; doc number issued immediately.
- F8 (UX/flow): approved-PO CTA = "สร้างใบสำคัญจ่าย" only — no "บันทึกใบกำกับภาษีซื้อจาก PO"
  button; pushes user to skip the VI hop (chain per manual: PO → VI → PV).
- F9 (UX): "ส่ง PO ให้ vendor" = instant mark-sent (stamps date), no dialog/email/undo;
  label implies sending.
- F10 (UX minor): right panels (ประวัติกิจกรรม, เอกสารอ้างอิง) don't refresh after
  อนุมัติ/mark-sent actions — need full page reload.
- F11 (UX minor): activity log entries are English event codes ("MarkedSent → Sent") in Thai UI.
- F12 (a11y): vendor form (and most forms) inputs lack accessible labels (a11y tree shows
  bare textboxes).
- F13 (validation): vendor can be saved as จดทะเบียน VAT with NO 13-digit tax ID — VAT
  invoice needs vendor tax id for ภ.พ.30 claim.
- Doc numbering now <MM>-<YYYY>-PO-<BU>-<seq> (07-2026-PO-TEST-0001) — manual says
  PO-NNNN → outdated.
- F5 RESOLVED as feature change/regression: PO totals never include VAT (even
  VAT-registered vendor: 3,000 stays 3,000). Manual 05.01 shows VAT auto-computed → update
  manual, and flag to Ham whether PO-side VAT display is intended to be gone.
- F14 (money UX): VI "เชื่อมกับใบสั่งซื้อ" pulls line with อัตรา VAT = 0 even for
  VAT-registered vendor — user must remember to set it; risk of under-claimed input VAT.
- F15 (MONEY BUG, UX): VI line "อัตรา VAT" field is a FRACTION (0.07), but UI gives no
  hint; typing "7" (natural %) → VAT ฿21,000 on ฿3,000 base (700%) accepted silently, no
  validation/warning, live preview happily shows ฿24,000 total. Must fix: percent input or
  hard validation (0..1) + default from vendor VAT status.
- F16 (MAJOR UX/auth): access token lifetime ~30-40 min, no auto-refresh; expiry mid-form
  = POST 401 with NO user-visible error (save button stuck disabled, no "session expired"
  redirect/modal, form data at risk). Also POST went to /api/proxy/vendor-invoices/ (trailing
  slash) 401 then retry POST stuck "pending". Session-expiry handling needed globally.
- STATE at blocker: VI draft for INV-BUTEST-7001 (PO-TEST-0002, COGS, 3,000 + 210 VAT)
  UNKNOWN if saved (pending POST) — check /vendor-invoices for duplicates before recreating.

- F17 (UX minor): re-login resets company context to default (personal co), not last-used.
- F18 (UX, significant): /vendor-invoices list has NO "สร้าง" button — direct create form
  exists at /vendor-invoices/new but is unreachable from UI; list subtitle still says old
  flow "สร้างจากใบสำคัญจ่าย (PV → บันทึก)". Approved-PO page also lacks a create-VI CTA (F8).
- F19 (UX): server validation error surfaces as English-only auto-dismissing toast
  ("Line 1: no expense account (category 'COGS' has no default)") — technical wording,
  easy to miss; session-expired 401 shows NO toast at all.
- F20 (data/config bug): Repttown company: expense category COGS (and possibly others)
  has no default GL expense account → VI save 422-blocked. Related to known co2/co3
  seed gap (expense categories auto-seeded without account mapping?). Need config UI/seed fix.
- F21 (FE bug): after a failed save, one of the double-fired POSTs stays pending forever →
  save buttons dead, must reload page (repro: VI form, save with COGS → 422 → change field →
  save clicks do nothing). Also: save fires POST to both /api/proxy/vendor-invoices/ and
  without trailing slash (double request pattern) — worth backend/proxy check.
- F14 refined: fresh manual VI line defaults อัตรา VAT = 0.07 correctly; only PO-pulled
  lines default to 0.
- STATE: VI #2 draft saved OK (INV-BUTEST-7002, OFFI, 1,000+70). PO-linked VI variant still
  untested end-to-end (blocked earlier by COGS; retry with OFFI later). Next: post VI #2 →
  PV settle → PV+WHT (use individual vendor BUTEST-VEND for ภ.ง.ด.3) → 50ทวิ → reports.

- F25 (UX/policy): PV page says "ผู้อนุมัติต้องไม่ใช่ผู้สร้าง (SoD)" but creator
  ham_chatsang approved own PV fine (admin exempt? not enforced?) — text contradicts behavior.
- F26 (blocked/unconfirmed): PV "บันทึกเอกสาร (Post)" click = total no-op (no request, no
  modal, no console error) — BUT session token was dying at that moment; likely the silent-401
  family (F16). Retest with fresh session before filing as separate FE bug.
- Session TTL measured: login #2 ~12:05 → dead ~12:35 (~25-30 min). Browser E2E on prod not
  sustainable without babysitting logins → Phase 1 PAUSED at PV post step.

- F5/F14 RE-RESOLVED (worker evidence, 2026-07-14): PO VAT row is gated by
  `vatMode && vendor.vatRegistered` — co2 (VAT-registered) shows VAT 7% normally incl.
  PO-linked VI vatRate 0.07. Repttown = non-VAT company (vatMode=false) → no VAT row +
  PO-pull rate 0 is BY DESIGN, not regression. (troubles-wiki has the full entry.)
- F27 (money/compliance, NEW — for Ham/backend): on the non-VAT company (Repttown), the VI
  form still ACCEPTS อัตรา VAT 0.07 and posted it as recoverable input VAT
  (VI 07-2026-VI-TEST-0001: vatAmount 70, isRecoverableVat=true) — a non-VAT-registered
  company cannot claim input VAT; should be forced non-recoverable (expense) or blocked.
  F15 (fraction field, no 0..1 validation) unchanged and still the top UX-money fix.

## Resume (2026-07-14 ~15:00, Ham logged in, session live)
- PV #2 POSTED → 07-2026-PV-TEST-OFFI-0001 (PV number embeds expense category).
  F26 RESOLVED = silent-401 (F16 family), not a separate FE bug.
- F28 (NEW): PV post = ONE click, NO confirm modal (VI post has one) — inconsistent,
  and it books an immutable JE. PV posted doc shows "ไม่สมบูรณ์/ขาดไฟล์ใบเสร็จจากผู้ขาย".
- F15 addendum: PV field "หัก ณ ที่จ่าย %" auto-fills 0.03 for 3% — label says %, value
  is fraction; same fraction-vs-percent mess as VI VAT field.
- OK: PV line VAT box shows "0% · ผู้ขายไม่จด VAT" for non-VAT vendor (good messaging);
  gross-up toggle "ผู้รับเงินไม่ให้หัก ณ ที่จ่าย (ออกภาษีให้เอง)" present per manual 05.04.
- PV #3 (WHT test) DRAFT saved: BUTEST-VEND (บุคคลธรรมดา), PROF, ค่าจ้างออกแบบโลโก้
  20,000, WHT ค่าบริการ(บุคคลธรรมดา) 3% = 600, net 19,400, Transfer, BU TEST.
  NEXT: approve → post → check 50ทวิ ภ.ง.ด.3 at /wht-certificates → PDF; then PO "ปิด"
  on PO-0001, reports spot-check (tax summary/AP aging/vendor ledger via MCP), optional
  PO-linked VI. Note: PV #3 detail page find showed no อนุมัติ button — scroll up/reload.
- Quota hit 85% ~15:05 (resets ~17:30). Insurance checkpoint done (this write + commit +
  chained wakeup). Continue browser work freely; new Claude dispatches avoided.

## Final (2026-07-14 ~15:20, quota 90% → wrapped)
- PV #3 posted → 07-2026-PV-TEST-PROF-0001; 50ทวิ auto-issued 07-2026-WT-0001:
  Pnd3 ✓ (individual), ม.40(8) ค่าบริการ(บุคคลธรรมดา), 20,000 × 3.00% = 600 ✓,
  refs to PV correct, พิมพ์/PDF available. WHT flow VERIFIED end-to-end.
- AP settlement VERIFIED via MCP: VI-TEST-0001 settledAmount 1,070 = PAID, settlingPv
  PV-OFFI-0001 Posted; vendor ledger VI cr 1,070 → PV dr 1,070 → closing 0; control
  account 2110 reconciliation balanced=true. Money loop closes.
- F29 (minor): PO "ปิด" button click = no visible effect, no confirm/toast (fresh session,
  so NOT the 401 family) — untested further due to quota; retest + define semantics.
- Minor i18n on /wht-certificates list: แบบยื่น shows "Pnd3" (EN code), ม.40 column shows
  raw "8" — detail page renders both properly.
- DROPPED (quota): PO-linked VI browser E2E (mechanism verified in form + on co2 by
  worker), reports UI pages (AP aging/tax summary UI — data verified via MCP instead).
- Approve/Post on PV: NO confirm dialog at either step (F7/F28 pattern confirmed twice).

## Prod test-doc state (BU TEST, Repttown co.)
- PO 07-2026-PO-TEST-0001 (non-VAT vendor BUTEST-VEND, 4,120) — approved + marked sent.
- PO 07-2026-PO-TEST-0002 (VAT vendor BUTEST-VEND2, 3,000) — approved.
- VI 07-2026-VI-TEST-0001 (INV-BUTEST-7002, OFFI, 1,000+70) — POSTED, ไม่สมบูรณ์ (no file).
  NOTE: not linked to PO (manual-entry path); PO-linked VI still to be tested.
- PV #2 (draft→approved, 1,000 + VAT, settle VI-0001, Transfer) — approved, POST PENDING
  (blocked by session death).
- Vendor created: BUTEST-VEND2 "BU TEST บจก. เพ็ทแคร์ ซัพพลาย (ทดสอบ)" (Corporate, VAT, no tax id).
- Remaining prod steps (need fresh login): PV post → JE + settle check → 50ทวิ WHT PV
  (use BUTEST-VEND individual → ภ.ง.ด.3) → wht-certificates page → PO "ปิด" button →
  vendor ledger/AP aging/tax summary spot-check → PO-linked VI (OFFI).

## Phase 2 (manual) — local pipeline
- Recipe: docs/superpowers/plans/2026-06-14-manual-build-all-modules-INSTRUCTIONS.md
  (stack :5080 + :3000 + co2 manual-demo seed; capture -g "05."; gen-markdown; mkdocs).
- Dispatched sonnet-implementer to update walkthroughs 05.01–05.06 per findings + recapture
  chapter 5 (see ROUTING-LOG 2026-07-14). No commit by worker; Fable reviews diff.
- [x] DONE 2026-07-14 13:10 — stack brought up locally (backend :5080 Dev, frontend :3000
  next dev), Playwright chromium installed (was missing — see troubles-wiki). Verified
  co2 master data intact (no ch3 reseed needed).
  - 05.01: doc-numbering fixed to MM-YYYY-PO-BU-NNNN, new action bar (สร้างใบสำคัญจ่าย/
    ส่ง PO ให้ vendor/ปิด/ยกเลิก) documented. **VAT-on-PO finding (F5) does NOT reproduce
    on co2** — code (`vendorVat = vatMode && vendor.vatRegistered`) + live capture both
    confirm co2's VAT-registered vendor still shows VAT 7% on PO normally. Kept accurate
    caption + added a conditional-VAT note instead of the blanket "no VAT" claim — see
    report to Fable, flag for Ham re: whether BU TEST/Repttown company is legitimately
    configured non-VAT (vatMode=false) or that's a separate bug.
  - 05.02: added PO-link demo step (VAT rate pulled = 0.07 for co2, NOT 0 as F14/F15
    found on prod — same vatMode nuance), explicit "อัตรา VAT is a fraction" warning
    (admonition + highlighted field capture), attach-file step (resolves "ไม่สมบูรณ์"),
    ม.86/4 / ม.86/12 post-confirm wording, "ชำระด้วยใบสำคัญจ่าย" CTA documented. 5→8 steps.
  - 05.03: added SoD-hint + date-lock caption note (text present but not enforced for
    admin, per F25 — noted honestly).
  - 05.04/05.05: verified via dry-run capture — no drift found, unchanged.
  - 05.06: verified via dry-run capture — no drift found, unchanged (script itself
    untouched; captures refreshed).
  - gen-markdown + mkdocs build both clean (0 errors, only pre-existing rbac-ui-guide
    warning). All 6 walkthroughs green on final gate run.
  - Caveat: one intermediate run used an unanchored `-g "05."` filter that also
    re-captured 02.05/03.05/04.05/07.05 (substring match) — harmless (idempotent
    re-run of already-correct scripts, fresh captures per project rule #1) but
    inflated the docs/_site diff. Full report in session transcript.

## Attempt log
- 2026-07-14 11:12 session start, wakeup scheduled (1hr heartbeat). Ham answered 3 setup Qs.
- 11:28 oriented: manual pipeline located, plan written.

## CONFIRM ROUND on prod v1.21.0 (2026-07-15 ~11:00, Fable-personal Claude Chrome, BU TEST @ Repttown)
Full chain re-run: PO #1 (draft) → edit (BU→TEST) → approve → VI from PO CTA →
save+post → PV from VI → approve → post. New docs: 07-2026-PO-TEST-0003 (Closed),
07-2026-VI-TEST-0002 (Posted, settled 214/214 = PAID), 07-2026-PV-TEST-COGS-0001 (Posted).
Vendor NOT created (validation test only, form abandoned).

### CONFIRMED FIXED (browser + server evidence)
- F15/WP1.1 ✓ VI อัตรา VAT = percent UI (chips 0%/7%): type 7 → 7% → ฿14 on ฿200; type 700 →
  clamped 30%. PV หัก ณ ที่จ่าย %: type 3 → 3% → ฿6.42 on ฿214. Stored fraction 0.07 (API-verified).
- F27/WP1.2 ✓ non-VAT co: FE advisory ×2 shown; totals put VAT in ภาษีซื้อต้องห้าม (฿14), เครดิตได้ ฿0;
  server: hasInputVat=false? (header vatAmount=0), line isRecoverableVat=false, nonRecoverableVat=14.
- F20/WP1.5 ✓ COGS selectable (19 categories, none disabled), VI saved+posted with COGS; 623 backfill live.
- F13/WP1.4 ✓ vendor จด VAT + no taxId → inline Thai error "ผู้ขายจด VAT ต้องระบุเลขผู้เสียภาษี 13 หลัก", save blocked; asterisk on label.
- F16/WP2.1 ✓ POST /api/auth/refresh → 200 ok, expires_at = now+8h (8h TTL live). Keep-alive endpoint proven.
- F21/WP2.3 ✓ VI save = single POST /api/proxy/vendor-invoices (no slash) 201; PV same. Zero 308, zero dup.
- F19/WP2.4 ✓ Thai toasts (over-receipt warning in Thai, sticky).
- F18/WP3.1 ✓ VI list create button + new subtitle.
- F8/WP3.2 ✓ approved-PO CTA "บันทึกใบกำกับภาษีซื้อ" BEFORE สร้างใบสำคัญจ่าย; prefills vendor+PO+BU.
- F6/WP3.3 ✓ PO draft แก้ไข → /edit route, PUT persists (BUT see R2).
- F29/WP3.4 ✓ close (auto-closed on full VI receipt, ค้างเหลือ shows -14 over-receipt), เปิดใหม่ button
  on Closed, reopen with posted VI correctly BLOCKED (but see R3).
- F24/WP3.5 ✓ PV from VI prefill: vendor/BU/category/line "ชำระ <docNo>" (see R4 edge).
- F7+F28/WP3.6 ✓ confirm dialogs on PO approve, PV approve, PV post — totals + immutable warning.
- F9/WP3.7 ✓ "บันทึกว่าส่งแล้ว" relabel (button present on approved PO).
- F2/WP4.1 ✓ BE hints "= dd/MM/2569" under date inputs (PO edit, VI new).
- F11/WP4.3 ✓ activity log Thai (สร้างเอกสาร → ฉบับร่าง, อนุมัติ → อนุมัติ).
- F17/WP4.4+F-C ✓ (indirect) fresh morning login landed on Repttown (last-used), not personal co.
- F10/WP4.5 ✓ side panels refresh live after approve/post (no reload), all hops.
- F12/WP4.6 ✓ vendor form 16/16 inputs label-associated (DOM el.labels check).
- F22/WP4.7 ✓ post-confirm title "ยืนยันการบันทึกใบกำกับภาษีซื้อ" + ม.86/4/ม.86/12 warning.
- F23/WP4.8 ✓ PV subtitle "ชำระใบกำกับภาษีซื้อเลขที่ 07-2026-VI-TEST-0002" (docNo).
- F25/WP4.9 ✓ SoD hint + pvApprove dialog both "ผู้อนุมัติควรเป็นคนละคนกับผู้สร้าง — super-admin ข้ามได้".
- WP4.10 ✓ WHT list: แบบยื่น "ภ.ง.ด.3", ประเภทเงินได้ "40(8)".
- F26 ✓ dead: PV post works on live session (was F16 family).

### NOT RE-TESTABLE THIS ROUND
- WP2.2 session-expired modal + form preserve: 8h token can't be expired in-browser (HttpOnly).
  Live-verified at implementation (local dev); refresh endpoint + Thai 401 toast code paths deployed.
- F14 VAT-registered-co PO-pull derivation: needs co2; prod BU TEST co is non-VAT (rate 0 = by design).

### NEW FINDINGS (confirm round)
- R1 (bug, was F3/WP4.2 "already fixed" claim): PO LIST หน่วยธุรกิจ column shows "#3"/"#1" raw id on
  prod — business-units fetch 200 and VI list resolves names fine; PO list page alone broken.
- R2 (bug, data): PO draft EDIT-SAVE silently resets docDate to today (12/07 → 15/07 verified
  server-side docDate=2026-07-15). Only BU was changed in the edit.
- R3 (i18n gap): po.reopen_blocked toast English-only ("Cannot reopen: a posted Vendor Invoice is
  already linked to this Purchase Order.") — code missing from lib/i18n/problems.ts.
- R4 (money edge, minor): PV-from-VI prefill under-settles when VI carries VAT but vendor re-derives
  0% (non-VAT vendor whose VI had manual 7%): VI outstanding 214 → PV total 200. User-adjustable.
- R5 (cosmetic): VI post-confirm dialog shows "VAT ฿0.00" while the ฿14 non-recoverable VAT is
  inside Total ฿214 — no ภาษีซื้อต้องห้าม row in dialog.
- R6 (cosmetic): activity entries "อนุมัติ → อนุมัติ"/"บันทึกแล้ว → บันทึกแล้ว" redundant; PO auto-close
  on VI post writes NO activity entry.
- R7 (positive, note): over-receipt guard toast in Thai "รับเกินใบสั่งซื้อ: รวม VI 214.00 > PO 200.00
  (เกิน 105%) — โปรดตรวจสอบ" fired on VI post; PO auto-closes on full receipt.
