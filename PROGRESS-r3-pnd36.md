# PROGRESS — R3/F1 ภ.พ.36 payment detection

Updated 2026-08-13 17:45. v2.0.0 is live; this is the first R3 work package.
Quota at last reading: 5-hour 21%, 7-day 42%.

## Why this exists
ม.83/6 keys on the PAYMENT. v2.0.0 (`1e46a35`) correctly made ภ.พ.36 source rows from posted
PaymentVouchers only — but nothing forces a payment through a voucher. Clearing a foreign vendor's
payable with a manual JV (`Dr 2110 AP / Cr Bank`) leaves that purchase declared in **no period at all**.
Under-declaring a tax return carries เงินเพิ่ม 1.5%/month. Ham ruled "fix it" rather than accept the
documented gap. Live exposure is zero today (foreign reverse-charge invoices exist only on co5; neither
real tenant has any; ภ.พ.36 has never been finalized) — which is why there was room to design it properly.

## Design (Opus, `specs/fix-pnd36-payment-detection.md`, 807 lines)
**Detect and require sign-off — never refuse.** Both refusal designs are rejected in-spec so they are
not relitigated: an AP-account blocklist recreates the dead end `cb2e362` had to remediate, and
requiring a JV→invoice link is *unbuildable* because `JournalLine` carries no vendor tag. Per filing
month it compares actual AP debits against what posted PaymentVouchers explain, surfaces the difference,
and gates only `finalize` behind an acknowledgement tick.

## Implementation — code-complete, 14 files, cap 15
Backend: `TaxFilingDtos.cs` · `WhtFilingService.cs` (`DetectUnreconciledAsync`) · `TaxFilingEndpoints.cs`
· `JournalDtos.cs` · `JournalService.cs` (advisory on the manual-JV path) · `Sprint9WhtComplianceTests.cs`
(T1–T6, T8, T9). Frontend: `types.ts` · `queries.ts` · pnd36 page · `ManualJournalForm.tsx` ·
`en.json`/`th.json`. Docs: `troubles-wiki.md` · the spec.
`problems.ts` was handled by Fable (`pnd36.unreconciled_not_acknowledged`), as always.

## What Fable verified personally, because the tests cannot catch any of it
- **Parameter order** — `bool acknowledgeUnreconciled = false` sits AFTER `ct` on **both** the interface
  and the implementation. Putting it before `ct` would rebind the trailing `default` at 4+ call sites,
  compile clean, and fail no test. The warning comment is preserved in the source.
- **Sign discipline** — `unexplained = Sum(DebitAmount) - expectedApDebits`. `CreditAmount` is carried
  into the display list but never into the arithmetic, and `RequiresAcknowledgement: unexplained > tol`,
  so a net credit can never raise a warning. T3 exists to catch a sign flip and passed without the sign
  logic being touched.
- **I3 (declared amount unchanged)** — `rows` still derives from `pvRows` alone; the only addition is
  `PaymentDate`. The advisory lives in a separate diagnostic field, never in `Rows`.
- **The money formula itself**, verified at design review: the AP debit for a VI-linked voucher is
  `pv.SubtotalAmount + pv.VatAmount` (`GlPostingService.cs:225`) and `AppliedAmount` is assigned the
  identical expression (`PaymentVoucherService.cs:634`). Same figure; WHT reduces neither side.

## Gates
Frontend: `tsc` 0, vitest 65/65, glyph scan clean. Backend full suite: **running** (Domain 188/0/0 done).
Tier-2 Opus review: **running**, briefed to attack the subtraction's comparability first (partial
settlements, multi-invoice vouchers, reversed vouchers, month-boundary splits) and to judge whether the
acknowledgement gate is a real control or theatre.

## Open, not blocking
- **E1** the acknowledgement gate is a UX call — Ham approved shipping it.
- **E2 (CPA)** — an employee paying the overseas provider personally. **Researched 2026-08-13 at Ham's
  request and the answer runs AGAINST the comfortable assumption**: ป.104/2544 ข้อ 3 identifies the
  ผู้จ่ายเงิน as "ผู้รับบริการในราชอาณาจักร", so the duty likely stays with the company. Recorded as a
  known uncovered liability, not a harmless edge. Still needs a CPA or an RD ข้อหารือ.
- **E3** — CPA confirmation of the PV-only rule itself, carried over from R2 §10 E1.
- **§3.6 finding** — `RdHttpEfilingClient` forwards the whole filing DTO raw, which now includes the
  advisory fields. Pre-existing shape, unreachable today (Mock provider only, TODO skeleton). Opus is
  judging whether the widened payload crosses a line the existing fields did not.
