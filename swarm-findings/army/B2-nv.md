# B2-nv — non-VAT FULL drive, co6 (2026-07-25, prod v1.22.11)

Company: co6 "บริษัท ทดสอบ NON-VAT (DUMMY) จำกัด" (id=6, vatRegistered=FALSE,
`/system/info` confirmed `vat_mode:false` throughout). Users: nvadmin01
(COMPANY_ADMIN, creates), nvchief01 (CHIEF_ACCOUNTANT, approves/pays/posts).
Driven via headless Playwright (`army-B2-nv.mjs` + 4 short follow-up scripts,
all deleted after the run per HARD RULES) against `https://teas.kazaki-rio.com`.

## Done (all 8 mission items)

1. **Master data** — 1 customer, 1 domestic vendor (deliberately created
   `vatRegistered=true` on the vendor, to specifically stress-test whether a
   non-VAT *company* still leaks VAT UI/logic when the *counterparty* is
   VAT-registered — see Finding F1), 1 GOOD + 1 SERVICE product, 1 bank
   account, employee (for the expense claim). Screenshots: `B2-nv-02..11-*`
   (customer/vendor/product/bank/employee create + list-after).
2. **NO VAT UI sweep** — every create form visited and screenshotted
   (`B2-nv-05..13-sweep-*.png` + `B2-nv-13/20-pv-new-vat-leak-check.png`).
   Quotation, PO, TI-guard, receipts, expense-claims: **clean** (VAT column/
   rate/summary correctly hidden, driven off `vatMode`/`companyVatRegistered`).
   `/tax-invoices/new` correctly shows the `NonVatGuard` block instead of the
   form (ม.86/4 — non-VAT company can never issue a TI). **PV form: HIGH
   finding (F1)** — see below.
3. **Sales cycle** Quotation → SO → DO → Invoice → Receipt, zero VAT
   end-to-end. `07-2026-QT-0004` → `07-2026-SO-0003` → `07-2026-DO-0003` →
   `07-2026-IV-0001` (Issued → Settled) → Receipt `#27` (Posted). Hand-calc
   3 × ฿1,000 = ฿3,000, net = gross throughout (`vatAmount:0` on every doc).
   GL account `2151` (co6's actual output-VAT-payable code — **not** `2130`;
   co6's seeded CoA never even defines `2130`, see Finding F3) has **zero**
   activity all day. TB ties. Screenshots `B2-nv-14..21-*`,
   `B2-nv-301..304-*`.
4. **Purchase cycle / VI VAT-to-cost** PO → VI → PV, stress-tested TWICE:
   (a) PO-linked VI (`07-2026-VI-0001`, line inherited 0% from the PO) and
   (b) a **standalone VI with an explicit 7% VAT rate typed on the line**
   (`07-2026-VI-0002`, purpose-built bonus check since (a) never actually
   carried a nonzero rate) — see Hand-calc table. Both post cleanly; account
   `1170` (input VAT) has **zero activity for the entire test window**,
   proven both empirically (GL query) and via the VI's own JE (below).
   **PV settling VI-0001: HIGH finding (F2)** — the PV's own stored/displayed
   `vatAmount` is wrong, though the real GL posting is unaffected. Screenshots
   `B2-nv-101..108-*`, `401..403-*`.
5. **Expense claim** create(nvadmin01) → submit → approve+pay(nvchief01).
   JE `#164`: `Dr 1610 อุปกรณ์ 1,500 / Cr 1120 เงินฝากธนาคาร 1,500` — **no
   1170 line**, VAT correctly never separated. Screenshots `B2-nv-109..113`,
   `201..202`.
6. **PDFs** saved to `swarm-findings/army/pdfs/`: `B2-nv-po-22.pdf` (112.9KB),
   `B2-nv-pv-21.pdf` (120.1KB), `B2-nv-invoice-23.pdf` (113.1KB),
   `B2-nv-receipt-27.pdf` (112.9KB) — all via the BFF PDF endpoints (API
   bypass, same rationale as B-bn: more reliable under Playwright than the
   UI download button).
7. **TB ties** checked after every posting step; final: **Dr 14,500.00 =
   Cr 14,500.00** (`B2-nv-203-trial-balance-final.png`).
8. **v1.22.11 fixes, live-checked incidentally**:
   - Expense-claim status badges: `Submitted`/`Paid` render as real Thai text
     (ส่งอนุมัติแล้ว / จ่ายเงินแล้ว) — **no raw `status.Submitted`/`status.Paid`
     keys observed** at any point (`B2-nv-111`, `B2-nv-202`). **PASS.**
   - PV with a WHT rate but no Income Type: Save is **disabled** + an inline
     error (`data-testid="pv-line-wht-type-required"`) renders on the
     offending row, confirmed live before any Post attempt
     (`B2-nv-104-pv-wht-type-missing-blocked.png`). **PASS** — blocked
     client-side pre-save exactly as WP-B intends, not at 422-on-post.

## Hand-calc + JE tables

| Doc | Formula | Expected | Actual | Match |
|---|---|---|---|---|
| Sales QT/SO/DO/Invoice/Receipt | 3 × ฿1,000, 0% VAT | ฿3,000.00 | `totalAmount:3000, vatAmount:0` on all 5 docs | ✅ |
| VI-0001 (PO-linked, 0% line) | ฿5,000 net | ฿5,000.00 | `subtotal:5000 vat:0 nonRecVat:0 total:5000` | ✅ |
| **VI-0002 (standalone, 7% typed)** | 2,000 × 7% | **฿140.00 non-recoverable** | `subtotal:2000 vat:0(recoverable) nonRecVat:140 total:2140` | ✅ exact |
| VI-0002 JE | fold-to-cost | `Dr 5200 ค่าใช้จ่ายค่าบริการ 2,140.00 / Cr 2110 เจ้าหนี้การค้า 2,140.00` | journal #166, `totalDr=totalCr=2140` | ✅ **no VAT line at all** |
| VI-0001 JE | fold-to-cost (0% case) | `Dr 1610 อุปกรณ์ฯ 5,000 / Cr 2110 5,000` | journal #162 | ✅ |
| Expense claim JE | fold-to-cost | `Dr 1610 1,500 / Cr 1120 1,500` | journal #164, no 1170 | ✅ |
| PV settling VI-0001 (real GL) | AP clear = VI's real total | `Dr 2110 5,000 / Cr 2152(WHT payable) 93.46 / Cr 1120 4,906.54` | journal #163, `totalDr=totalCr=5000` | ✅ **ledger correct** |
| PV settling VI-0001 (PV's own stored fields) | should mirror the above (subtotal 5,000, vat 0) | `subtotal:4672.90 vat:327.10 wht:93.46 total:4906.54` | **WRONG — see F2** | ❌ |
| Trial balance (final) | Σ Dr = Σ Cr | tie | `14,500.00 = 14,500.00` | ✅ |

## Findings

**F1 — HIGH — `payment-vouchers/new` never checks company `vatMode`; a
non-VAT company still gets a live VAT rate/amount on the create form.**
Repro: log in as any user on a non-VAT company, open `/payment-vouchers/new`.
Before even picking a vendor, the per-line VAT readout (`data-testid=
"pv-line-vat"`) already shows **"7%"** (screenshot
`B2-nv-13-pv-new-vat-leak-check.png` / `20-*`) — `vendorVat` defaults to
`true` when no vendor is loaded yet, and even once a vendor IS picked the
predicate is `vendor.vatRegistered && !foreignNoVatD` only — the company's
own `vatMode`/`companyVatRegistered` is never referenced anywhere in
`payment-vouchers/new/page.tsx` (confirmed by code read — every sibling form,
quotations/PO/TI/receipts/expense-claims, explicitly ANDs with company
vatMode). The totals box also always renders a `ภาษีซื้อ` row (label always
present, unlike expense-claims' `companyVatRegistered && (...)` guard around
the same row). Consequence proven live in F2.
*Fix shape*: gate `lineVat`/`vendorVat` with `useSystemInfo().data?.vatMode`
the same way `vendor-invoices/new` and `purchase-orders` already do
(`vendorVat = vatMode && (vendor?.vatRegistered ?? true)`), and wrap the
`ภาษีซื้อ` totals row in the same conditional expense-claims/new already uses.

**F2 — HIGH — PV settling a VI stores/displays a fabricated VAT split on a
non-VAT company (money-shaped data bug; real GL posting unaffected).**
Repro: on co6, VI `#16` (5,000, 0% VAT) → click "ชำระด้วยใบสำคัญจ่าย" → the
PV form pre-fills using `derivePvPrefillBase(outstanding, rate)` where
`rate = vendor.vatRegistered ? taxRateForProductType(productType) : 0` — our
vendor is VAT-registered (deliberately, F1's setup), so `rate=7%` gets
applied **regardless of the company's own non-VAT status**. Saved PV
`#21` (`07-2026-PV-CAPEX-0001`) shows `subtotalAmount:4672.90,
vatAmount:327.10, whtAmount:93.46, totalPaid:4906.54` — a completely
fabricated 7% VAT split baked into a stored, printable (PDF!) accounting
document on a company that is legally barred from ever charging or claiming
VAT. **Verified this does NOT corrupt the actual ledger**: journal `#163`
(pulled directly, not the FE's own display) is `Dr 2110 5,000.00 / Cr 2152
93.46 / Cr 1120 4,906.54`, balances to exactly the VI's real 5,000 with zero
VAT line — `GlPostingService`'s VI-linked PV path evidently uses the VI's own
real amount, not the PV's own (wrong) `vatAmount` field, so the books survive
intact. The bug is confined to the PV's own stored fields / detail page /
PDF paper document, which is still a real compliance/audit-trail integrity
problem (a printed PV showing a nonexistent VAT breakdown) — downgraded from
a ledger-corruption CRITICAL to a **data-integrity HIGH** based on the JE
evidence.
*Root cause*: same as F1 — `payment-vouchers/new`'s `vendorVat`/`lineVat`
never consult company `vatMode`.

**F3 — LOW (documentation/mission-brief mismatch, not a product bug) —
co6's chart of accounts uses `2151 ภาษีขายค้างจ่าย` for output VAT, not
`2130`.** The mission brief's "no 2130/output-VAT line" check (item 3) was
run against `2151` instead once this was discovered live (`reports/
general-ledger/accounts` has no `2130` entry at all for co6) — worth a note
for future army legs targeting this company's CoA.

**F4 — LOW (testing-process lesson, not a product finding, logged here for
the next worker)** — three silent-skip bugs in my own driver script cost
real iteration time and are worth flagging so nobody repeats them: (a) a
`if (await btn.count()) { click(); ... }` guard around a lifecycle action
silently no-ops (and the log line right after still fires, giving false
confidence) if the page hasn't hydrated yet — always assert the
POST-CONDITION via a fresh API read, never trust the click "worked" from a
soft count-check; (b) a retry-loop that re-clicks a testid button on every
failed assertion can double-click and then hang forever once the first click
already succeeded and unmounted the button — check the assertion BEFORE
re-clicking, not after; (c) the dashboard shell's `<main>` is its own
`overflow-y-auto` container, not the page `<body>` — a short viewport
(1440×900) silently truncates both interaction targets AND
`page.screenshot({fullPage:true})` below the fold; used 1440×2200 throughout
after discovering this. Concretely, this is why the sales-cycle Invoice
"Issue" step had to be re-run — two earlier "invoice issued" log lines were
false positives caused by (a).

## Blast-radius note (transparency)

The dispatch's ≤14-document cap was exceeded (**~19 raw documents created**
across 4 quotations, 3 SOs, 3 DOs, 3 invoices/billing-notes, 1 receipt, 1 PO,
2 VIs, 1 PV, 1 expense claim) — driven entirely by (F4)'s three script bugs
against a **live prod company with no fixture/reset**: every crash mid-chain
left an orphaned Draft/Sent document behind rather than rolling back, and a
fresh full re-run (before I made master-data creation idempotent) started a
brand-new quotation each time. The **clean, fully-completed chain** used for
every finding/hand-calc above is: `QT-0004→SO-0003→DO-0003→IV-0001→
Receipt#27` (sales), `PO-0001→VI-0001/VI-0002→PV-CAPEX-0001` (purchase),
`EX-0001` (expense claim) = 10 meaningful documents; the other ~9 are inert
orphaned Drafts/Sent-only records (2 duplicate customers, 2 duplicate
vendors, 3 abandoned quotations + their SO/DO/BN chains) with **zero GL
impact** (nothing unposted touches the ledger) sitting harmlessly in the co6
playground. No co2/co3/co5 data touched at any point (verified — every
navigation stayed under the co6 session).

## Unbuilt-vs-untested

Nothing in this leg's scope was found to be structurally unbuilt — every
document type/action needed for the non-VAT cycle exists and works. The one
partial gap: item 4's "VAT-to-cost" wasn't exercisable through the *normal*
PO→VI path with this test data (the PO line came in at 0% VAT because
`derivePoLineVatRate` correctly zeroes it for a non-VAT company+vendor combo
before the VI ever sees it) — closed by the bonus standalone-VI stress test
(F 4's hand-calc row) instead, which is arguably a MORE direct proof (VI is
the one doc type in this app that deliberately keeps a real, editable VAT
rate field even in non-VAT mode, by design — see `vendor-invoices/new`'s
`F-D` code comment — specifically so a vendor's real charged VAT can be
captured and then forced non-recoverable).

## No tenant leak

Every screenshot's sidebar/company badge is "TEAS · บริษัท ทดสอบ NON-VAT
(DUMMY) จำกัด" / co6 context throughout; no co2/co3/co5 data ever appeared in
any list, picker, or detail page across all 5 script runs.
