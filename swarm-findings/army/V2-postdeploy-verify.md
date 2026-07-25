# V2 — Army post-deploy VERIFY leg, prod v1.22.12

Target: https://teas.kazaki-rio.com (footer confirms v1.22.12 on every screenshot). co5 (VAT
dummy) for WP-F + regression sanity; co6 (NON-VAT dummy, id=6) for WP-G + WP-H. co2/co3 never
touched. Agent: sonnet, raw `chromium.launch()` script (`frontend/army-V2.mjs`, deleted after
the run per dispatch — raw JSON evidence kept at `swarm-findings/army/V2-results.json`).

Blast radius: **1 new document** (1 Vendor Invoice, co5, id 18 / `07-2026-VI-0006`) — well
under the ≤6 cap. Every attempt to create a Payment Voucher on co6 was rejected by the server
BEFORE any row was persisted (see WP-G below), so 0 documents landed on co6.

## Per-item results

### WP-F (co5) — VI→PV prefill uses the dual-flag rate, lands exactly on outstanding — **CLOSED: YES**

Created a fresh VI on `ARMYAWS859829`: service line, ฿20,000, VAT manually set to 0% (the
foreign/no-VAT-D combo the original bug hit), posted clean as ap01 → VI #18, `07-2026-VI-0006`.
`GET /vendor-invoices/18`: `totalAmount=20000`, `settledAmount=0` → outstanding = **20,000**.

Clicked "ชำระด้วยใบสำคัญจ่าย" → `/payment-vouchers/new?fromVendorInvoiceId=18`. Prefilled line
amount input reads **20000** (displayed `20,000.00`), VAT badge shows `0% · ผู้ขายไม่จด VAT`,
totals box "จ่ายสุทธิ" (grand total) reads **20,000.00** — exactly the VI's outstanding, no
6.8%-short fabrication (the v1.22.11 bug: 18,691.59). WP-F's fix (reading `vendorVat`, the
dual-flag predicate, instead of the single-flag `vendor.vatRegistered` at the old L135) holds.
- Evidence: `V2-01-vi-form-filled.png`, `V2-02-vi-posted.png`, `V2-03-pv-prefilled.png`.
- Network: `POST .../vendor-invoices -> 201`, `.../vendor-invoices/18/post -> 200`.

### WP-G (co6) — no VAT control/summary on the PV form — **CLOSED: YES**

`/payment-vouchers/new` as nvadmin01 (co6, `system/info.vatMode=false`): the per-line VAT box
(`data-testid=pv-line-vat`) count = **0**, and the exact totals-box VAT label ("ภาษีซื้อ") count
= **0** — confirmed both programmatically and visually (screenshot shows the line row has only
รายการ/มูลค่าก่อนภาษี/ประเภทเงินได้(50ทวิ)/หัก ณ ที่จ่าย — no VAT column at all; totals box shows
only มูลค่าก่อนภาษี + หัก ณ ที่จ่าย, no VAT row). Matches the VI form's + expense-claims'
established company-VAT-mode treatment.
- Evidence: `V2-04-pv-new-co6-no-vat.png`.

### WP-G (co6) — money invariant (TotalPaid = gross, isRecoverableVat forced false) — **CLOSED: PARTIAL (see finding below)**

Filled the live form (vendor "ผู้ขาย NON-VAT ทดสอบ B2NV", category "สินทรัพย์ถาวร (capitalize)
(CAPEX)" — a non-⚠ i.e. VAT-recoverable-ish category, line net **1,000**), **without saving**.
FE-computed preview (never persisted): **subtotal ฿1,000.00, no VAT line rendered (=0, control
hidden), net paid (จ่ายสุทธิ) ฿1,000.00** — exactly the net typed, nothing silently under-paid,
for the real user flow (screenshot `V2-04b-pv-new-co6-filled-preview.png`).

Then attempted to actually **Save** (UI) and, separately, to **create via a direct API call**
mirroring WP-G's own backend regression-test shape (`vatRate:0.07, isRecoverableVat:true`
explicit, as an agent/API caller could still send even though the UI hides the control) — both
attempts got a clean **422 `period.closed`** toast/response
(`"Period 2026-07 is CLOSED. Reopen the period or correct doc_date."`), never a 500, never a
silent save. Screenshot `V2-04c-pv-new-co6-save-attempt.png` shows the toast with the form
still showing the correct ฿1,000.00 (not silently mutated).

**New finding (orthogonal to WP-G, worth flagging): a standalone PaymentVoucher — Draft or
Posted — currently cannot be created on co6 AT ALL, not just posted.**
`PaymentVoucherService.CreateDraftAsync` (`backend/src/Accounting.Infrastructure/Purchase/
PaymentVoucherService.cs` ~L179-182) pins `DocDate` to the server's real Bangkok-today
UNCONDITIONALLY (`_clock.TodayInBangkok()` — the client's `docDate` field is never read; this
is a deliberate, pre-existing §10 anti-backdating design, unrelated to WP-G) and calls
`_period.EnsureOpenAsync(docDate)` at **draft-create time**, not only at Post. Since co6's
period covering the real today (2026-07) was closed by the prior B2-ye leg, sending an FY2027
`docDate` in the request body cannot route around it — the field is discarded before the period
check even runs. This is a materially bigger blocker than "posting is closed, drafting is
fine" (which is what this leg's own dispatch briefing assumed) — confirmed identically via both
the UI Save button and a direct authenticated API POST.

**Consequence:** no PV was ever persisted on co6 this leg, so I could not independently
re-verify LIVE the exact stored `subtotalAmount` / `vatAmount` / `totalPaid` /
`lines[0].isRecoverableVat`, nor drive one to Post to inspect the JE for a missing 1170 line.
That specific money-invariant (VAT folded into the expense debit, not zeroed; no Input-VAT-account
debit; `TotalPaid == gross`) is **not re-proven live in this leg** — it falls back to the
existing, already Tier-2-**APPROVED** evidence already in `specs/fix-army-findings-2026-07-22.md`
(WP-G section): `PaymentVoucherNonVatCompanyTests.
NonVatCompany_StandalonePv_FoldsVatIntoCost_NoInputVatLine` /
`NonVatCompany_ViLinkedPv_SettlesViInFull_NoInputVatLine`, which assert exactly this shape
against a real Postgres run. I did not re-derive it independently this session — flagging that
explicitly rather than claiming a live re-proof I didn't actually get.
- Evidence: `V2-04b-pv-new-co6-filled-preview.png`, `V2-04c-pv-new-co6-save-attempt.png`,
  `V2-results.json` (`apiCreateStatus:422`, `apiCreateBody.title:"period.closed"`).
- **Recommend Ham/Fable decide**: reopen co6's 2026-07 period, or accept co6 is fully frozen
  for new Purchase-side drafts until FY2027 begins. Not a WP-F/G/H regression — a side-effect of
  the B2-ye period-close action stacking with the pre-existing DocDate-pinning design.

### WP-H (co6) — nvtax01 (TAX_OFFICER) payroll filing access — **CLOSED: YES**

Resolved co6's posted payroll run (id **9**) and an employee id (**6**) as nvchief01 first, then
logged in as nvtax01 and probed all 5 filing artifacts directly (no proxy network logger needed —
status read straight off each response):

| Endpoint | Status |
|---|---|
| `GET /payroll/runs/9/pnd1/pdf` | **200** |
| `GET /payroll/pnd1a/pdf?year=2026` | **200** |
| `GET /payroll/runs/9/sso/pdf` | **200** |
| `GET /payroll/runs/9/sso/file` | **200** |
| `GET /payroll/employees/6/wht50tawi/pdf?year=2026` | **200** |
| `GET /payroll/runs` (administration list) | **403** |

All 5 RD/SSO filing artifacts now return 200 for nvtax01 (were 403 pre-deploy, per B2-pr leg).
Payroll **administration** (`GET /payroll/runs` list) is still correctly denied (403) —
confirms H1's fix widened only the filing endpoints (OR-ed `tax.filing.preview`), not
administration.

**UI residual gap confirmed as described by the implementer**: nvtax01's sidebar has **no**
"พนักงาน (Payroll)" link at all (`a[href="/payroll"]` count = 0; compare co6's admin sidebar,
which does show it) — nvtax01's new backend access is reachable only via direct API call, no
filing-only UI exists yet. This matches the implementer's own note in the spec (H1 residual
gap) — recorded here as a residual UX gap, not a bug.
- Evidence: `V2-05-nvtax01-sidebar.png`, `V2-results.json`.

## Regression sanity

- **co5 Trial Balance**: `/reports/trial-balance` badge reads **"Dr = Cr ✓"**.
- **5xx responses observed across the entire run**: 0.
- **Tenant-leak hits** (co2/co3/เรปทาวน์/พงศ์สันต์/repttown) across every screen visited: 0.
- Version footer confirmed **v1.22.12** on every screenshot.
- Evidence: `V2-06-trial-balance-co5.png`.

## Summary

| Item | Result |
|---|---|
| WP-F VI→PV prefill = 20,000.00 exactly | **CLOSED: YES** |
| WP-G no VAT control/summary on co6 PV form | **CLOSED: YES** |
| WP-G money invariant (fold-not-zero, no 1170 line) | **PARTIAL** — form preview confirms no silent under-pay live; the deeper posted-JE invariant could not be re-driven live this leg (co6 PV *drafting*, not just posting, is currently fully blocked — new finding, see WP-G section) and instead relies on the existing Tier-2-approved dotnet test evidence |
| WP-H nvtax01 filing PDFs (5/5) | **CLOSED: YES** |
| WP-H administration still denied | **CLOSED: YES** |
| WP-H UI path for nvtax01 | residual gap, as flagged by implementer — not a bug |
| Regression (TB, 5xx, tenant leak) | **CLOSED: YES** |
