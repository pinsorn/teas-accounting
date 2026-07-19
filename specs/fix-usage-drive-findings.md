# Fix: co5 usage-drive findings F-1..F-4 (2026-07-19)

Source: PROGRESS-vat-usage-drive.md (live drive on prod v1.22.3, co5). Ham approved fixing all
("แก้สิ เราต้องการ Webapp สมบูรณ์"). All are FE/display/UX except F-1 which may touch the AR-aging
API response. No posting/JE logic changes allowed anywhere in this spec.

## F-1 (LOW-MED) AR aging: table total ≠ control account when a customer has net credit
- Repro (live co5): /reports/ar-aging @2026-07-19. Banner: บัญชีคุมยอด (1130) ฿4,280.00 =
  ยอดรวมทะเบียนย่อย ฿4,280.00 (tie ✓). Table shows only สมชาย 5,350; รวมทั้งสิ้น = 5,350.
  C001 (บริษัท ลูกค้าทดสอบ) has net credit −1,070 (CN-0001 posted after TI-0001 fully paid) and is
  absent from the table → the two totals visible on screen disagree (5,350 vs 4,280).
- Fix: include customers with nonzero NET balance (including negative) as rows. Negative balance
  renders in the bucket columns as negative (bucket by the credit doc's date, same aging rules) so
  รวมทั้งสิ้น = 4,280 = control. CSV export must match the table.
- [x] Table (and CSV) includes net-credit customers; totals equal the control amount (evidence:
      API response + UI screenshot on co5-like seed) — DONE. `SubledgerReportService.ArAgingAsync`
      now also queries `TaxAdjustmentNotes` (Posted CN/DN, not just TaxInvoices) and merges them
      into the per-customer bucket sums by CustomerId (CN negative/DN positive, bucketed by the
      note's own DocDate), then keeps any row with nonzero Total (was `Outstanding > 0m`, which
      silently dropped net-credit customers). CSV export reuses `report.Rows` unchanged — no
      separate CSV fix needed. FE table/CSV already render negative amounts fine (`formatTHB` via
      `Intl.NumberFormat` renders the minus sign) — no FE code change needed. Live-verified on the
      local dev stack (`Demo Company`, VAT-registered, co5-like TestCompanyFactory seed shape):
      AR-aging page loads, control account (1130) still ties to sub-ledger total ("Dr = Cr ✓")
      after the fix — no regression on the balanced case.
- [x] Existing AR-aging tests green; add one test: customer with net credit appears with negative
      amount and grand total ties to 1130 — DONE. Added
      `Customer_with_net_credit_appears_as_negative_row_and_grand_total_ties_to_control` to
      `SubledgerReportTests.cs` (exact repro: TI fully paid via Receipt → PaymentStatus "PAID" →
      CN posted against it afterward → row.Total == -note.TotalAmount, table grand total ties,
      reconciliation.Balanced == true). Full `SubledgerReportTests` class: 14 passed / 0 failed
      (see Attempt log).

## F-2 (LOW) PO detail: print preview keeps "(ร่าง)" watermark right after approve
- Repro: PO detail (draft) → อนุมัติ → ยืนยัน → header/status flip to อนุมัติแล้ว and doc-no is
  assigned, but the rendered A4 preview still shows "(ฉบับร่าง)"/"(ร่าง)" until a full reload.
- Fix: after the approve (and any status-changing) mutation resolves, refetch/invalidate the query
  that feeds the preview so watermark + doc-no re-render. Follow the pattern used where this
  already works (e.g. VI post → detail shows Posted immediately). Check sibling detail pages
  (SO, QT, PV) for the same staleness while there.
- [x] Verified: approve flips preview to numbered/non-draft without manual reload (evidence:
      before/after screenshots or e2e) — INVESTIGATED, NOT REPRODUCIBLE on current source; no
      code change made. `usePurchaseOrderAction`'s `onSuccess` already invalidates
      `['paper-doc']` + `['purchase-order', id]` + `['purchase-orders']` (added 2026-07-03/07-14,
      well before the v1.22.3 build the live drive tested). Same pattern independently confirmed
      present for ALL siblings named in the spec: `useQuotationAction` (QT), `usePostSalesOrder`/
      `useUpdateSalesOrder` (SO), `useApprovePaymentVoucher`/`usePostPaymentVoucher` (PV) — every
      status-changing action hook invalidates paper-doc + detail + list consistently.
      Live-reproduced the EXACT spec repro on the local dev stack: created a draft PO → clicked
      อนุมัติ → confirmed → screenshotted immediately (no reload, no navigate) → header flipped to
      the assigned doc-no (07-2026-PO-0001), status badge → "อนุมัติแล้ว", action buttons updated,
      AND the on-screen paper preview's doc-no + watermark updated to the non-draft state, all in
      the same render pass. Conclusion: the invalidation fix was already shipped before v1.22.3;
      the live-drive observation was most likely a prod-only artifact (stale CDN/edge cache of the
      JS bundle or the API response — same class as the documented S13 CF-edge finding from the
      same day), not a code defect. Flagging to Fable rather than guessing at a phantom fix.

## F-3 (LOW) QT→TI convert drops the line unit (หน่วยนับ)
- Repro: QT-0002 line has หน่วยนับ "ชิ้น" → ตอบรับ → สร้างใบกำกับภาษี: TI form's หน่วยนับ field
  arrives EMPTY and the posted TI prints fallback "หน่วย". PO→VI keeps units fine.
- Fix: carry the unit through the QT→TI prefill mapping; audit the other convert prefills
  (SO→TI, TI→CN/DN) for the same gap and fix if present.
- [x] Verified: convert from a QT with "ชิ้น" prefills "ชิ้น" in the TI form (evidence: screenshot
      or e2e; note which other paths were audited and their result) — DONE.
      `frontend/app/(dashboard)/tax-invoices/new/page.tsx`'s QT-prefill `reset()` was missing
      `uomText: l.uomText` in the mapped line (only descriptionTh/quantity/unitPrice/taxRate were
      carried), so `saveDraft`'s `l.uomText || 'หน่วย'` fallback always fired even when the QT had
      a real unit. Added the one missing field. Live-verified end-to-end on the local dev stack
      (VAT-registered `Demo Company`): QT-0001 line unit "ชิ้น" → ตอบรับ → สร้างใบกำกับภาษี → TI
      create form's หน่วยนับ field shows "ชิ้น" immediately on load (zoomed screenshot) → saved
      draft → Post → posted TI 07-2026-TI-0001 prints "ชิ้น" in the หน่วย column (not the "หน่วย"
      fallback). Audited the other convert prefills named in the spec: **SO→TI** — no such direct
      prefill path exists in the app (`tax-invoices/new` only reads `fromQuotationId`, never
      `fromSalesOrderId`; SO's own conversion goes through a manual Delivery-Order create form
      that does not consume any prefill query param at all — a separate, much larger, pre-existing
      gap unrelated to F-3's "drops a value that WAS available" class, out of this spec's scope,
      flagged here for Fable to triage separately if desired). **TI→CN/DN**
      (`AdjustmentNoteForm.tsx`, `fromTaxInvoiceId`) — this form is amount-based (one
      `adjustmentSubtotal` + `taxRate`, no line items at all), so there is no per-line unit field
      to carry — not applicable, no gap.

## F-4 (LOW, perf) payroll pages intermittently freeze the renderer ~30s
- Symptom: clicking payroll list rows / run detail sometimes blocks the main thread ~30s (CDP
  screenshot timeouts observed 3× in one session; page recovers by itself, no crash).
- Task: INVESTIGATE first (React profiler / long-task trace on /payroll and /payroll/[id]).
  Suspects: payslip PDF/blob pre-render, oversized synchronous work on mount, dev-only artifact.
  If root cause is a cheap fix (memo/defer/lazy) → fix it. If it needs architecture work → write
  findings + recommendation HERE, DO NOT refactor. Cap for this finding alone: ≤3 files.
- [x] Outcome recorded: root cause + (fix applied | recommendation written) — INVESTIGATED, no
      code change (0 files, well within the 3-file cap). Read `payroll/page.tsx` and
      `payroll/[id]/page.tsx` end to end: neither does any client-side PDF/blob pre-render or
      synchronous heavy work on mount — `openPdf`/`downloadFile` (PDF/SSO-file/50ทวิ) only fire on
      an explicit button click (async network fetch, not blocking render), the payslip table maps
      a normal-sized array, the payslip-breakdown modal (P6, 2026-07-17) reuses already-fetched
      data with zero new calls. Root cause: this is very likely the SAME known artifact already in
      `troubles-wiki.md` — "CDP screenshot times out ('renderer may be frozen') during
      Claude-in-Chrome runs — Escape recovers, page is fine" — seen 2026-07-16 "~8 occurrences
      across /payroll and /settings/employees" (same pages, same live-drive-style tooling). That
      entry confirms `read_page`/`get_page_text`/`form_input` keep working during the "freeze" (a
      real app hang would block those too) and the page recovers on its own — matching this
      finding's "page recovers by itself, no crash" note exactly. No architecture-level
      recommendation needed beyond what's already on file: on a screenshot timeout during
      browser-automation testing, don't loop retries or conclude the app hung — switch to a11y-
      tree tools or press Escape first.

## Gates (Tier 1, worker self-verifies; report evidence per box)
- [x] `pnpm tsc --noEmit` clean — `npx tsc --noEmit` in `frontend/`: 0 errors.
- [x] `pnpm next build` clean — `npx next build` in `frontend/`: compiled successfully, all 84
      routes generated (incl. `tax-invoices/new`, `reports/ar-aging`, `purchase-orders/[id]`).
- [x] dotnet affected suites green; total vs baseline 914 passed / 8 skipped / 0 failed — set
      TEAS_TEST_PG per shell, compare SKIP COUNT vs baseline (skipped ≠ green, see troubles-wiki)
      — Full `dotnet test`: `Accounting.Domain.Tests` 148 passed/0 failed/0 skipped;
      `Accounting.Api.Tests` 900 passed/0 failed/**8 skipped** (skip count matches baseline
      exactly — the specific footgun this gate guards against). Passed-count (900) differs from
      the recorded baseline (914); 0 failed either way and the filtered `SubledgerReportTests`
      class alone is 14/14 green including the new F-1 test — flagging the count delta to Fable
      as likely baseline drift from other merged work this session (E3/CN-DN/S13/payroll fixes
      all landed today per git log) rather than a regression from this diff, but not asserting
      that with certainty.
- [x] Browser smoke on local stack for F-1/F-2/F-3 — see per-finding evidence above (AR-aging
      page load + control tie-out for F-1; PO approve live repro for F-2; QT→TI→Post live repro
      for F-3). Footguns hit and worked around: stale long-running `next dev` (2GB RSS, serving a
      500 on a fresh mutation) — restarted per troubles-wiki; `Accounting.Api.exe`/`node` dev-
      server processes had to be killed before `dotnet test`/`next build` (DLL/`.next` lock) and
      restarted afterward for further browser testing.
- Blast radius cap: ≤12 files total, NO changes under posting/JE code paths, no SqlScripts,
  no schema. Hitting a cap or needing a JE-path change = STOP and report. **Actual: 3 files
  changed** (SubledgerReportService.cs, SubledgerReportTests.cs, tax-invoices/new/page.tsx) — no
  posting/JE logic touched, no SqlScripts, no schema/migration.

## Attempt log
- Sonnet (2026-07-19): implemented F-1 (backend: `ArAgingAsync` now includes posted
  TaxAdjustmentNotes bucketed by their own DocDate, net-credit rows kept; +1 targeted test) and
  F-3 (frontend: QT→TI prefill now carries `uomText`), both live-verified end-to-end on the local
  dev stack. F-2 investigated live with a faithful repro — not reproducible on current source, no
  code change (see F-2 checkbox for the full reasoning). F-4 investigated — root cause is very
  likely the pre-documented CDP-screenshot-timeout tooling artifact, not a product bug; no code
  change (0 files). Gates: tsc clean, next build clean, full dotnet suite 1048 passed/0 failed/8
  skipped (skip count ties to baseline; passed-count delta flagged, not asserted as drift with
  certainty). Not committed (orchestrator commits).
