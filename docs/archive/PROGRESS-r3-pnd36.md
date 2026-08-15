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

---

# ✅ COMMITTED — `18f6fcc` (2026-08-13). Not yet released.

Suite **1180 / 0 / 14** (baseline 1170, +10 = the 8 original tests plus the 2 remediation tests).
Frontend `tsc` 0, vitest 65/65, both locale files valid JSON, codepoint scan clean with a positive control.

## Tier-2 REJECTed the first cut. Three of the five findings changed what the filer signs
1. **The prescribed remedy did not exist, and the attestation it forced was false.** The UI said
   "reverse the journal entry" — there is no user-invocable reversal anywhere in the backend
   (`ReversalOfId` is written only by year-close, filtered to revenue/expense, and can never reach AP).
   A filer improvising a mirror entry credits AP, which debits-only refuses to net, so the warning
   persisted at double and they then had to tick *"ยืนยันว่าไม่มีรายการใดเป็นการชำระเงินให้ผู้ให้บริการต่างประเทศ"* —
   false — into an immutable filing record, just to file. Fixed by rewriting both strings so the remedy
   is achievable and the attestation stays true. **NOT** by excluding equal-and-opposite Dr/Cr pairs;
   that is netting, which §3.2 forbids.
2. **An acknowledgement survived a change of the month picker** — finalize a period on a sign-off for
   entries never rendered. The tick now binds to the figure the filer saw; a stale figure is refused by
   name, with the I4 exit intact (re-preview → re-tick → finalize, same screen, same permission).
3. **The advisory could 500 an already-committed journal post**, and `journals/manual` has no
   idempotency key, so the retry creates a second AP debit — manufacturing the very state it warns
   about. Now swallows its own failures.
Plus: the load-bearing invariant had no real test (both sides were hand-seeded), and the MCP-drafted
post path had no visible advisory. Both closed.

## Two calls Fable made that the workers did not
- **No `problems.ts` entry for `pnd36.unreconciled_figure_changed`.** `errorToToast` returns
  `resolveProblemKey(code) ?? detail`, so a dictionary entry REPLACES the backend message, and that
  dictionary cannot interpolate — an entry would delete the two figures (reviewed vs current) that are
  the only reason the refusal is actionable. The backend message was rewritten Thai-first instead. A
  comment in `problems.ts` records why the key must stay absent, same as `sso_batch.unencodable_name`.
- **Rejected the worker's handback rather than applying it**, for that reason.

## Self-inflicted, recorded in troubles-wiki
Fable edited backend source **while the consolidated suite was running**, which locked the DLL and
failed the next build — and, more importantly, meant the green result did not cover the edit. Recovered
by rebuilding and re-running the affected filters (30/30) after the run finished.

## Next
- **Not released.** F1 ships in the next release together with whatever else R3 gathers.
- **E1** shipped as designed (Ham approved). **E2** (employee-paid, researched — likely DOES count) and
  **E3** (CPA confirmation of the PV-only rule) remain open with a CPA; neither blocks code.
- L1 (ภ.พ.36's period follows the voucher's POSTING day) belongs to Feature B, whose spec was amended
  2026-08-13 to cover the post-time re-pin it had been missing.
- Still unstarted in R3: duplicate tax-doc numbers · the 500 family · conversion routes checking the
  wrong scope · attachment IDOR · year-close deadlock · the year=3000 bound.
