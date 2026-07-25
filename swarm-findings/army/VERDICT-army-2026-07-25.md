# ARMY VERDICT — untested areas, VAT + non-VAT (2026-07-22 → 2026-07-25)

Ham's goal: "เทสส่วนที่เรายังไม่เคยเทสทั้งหมด ทั้ง Vat และ Non vat ด้วย army แบบเบิ้ม ๆ ด้วย vision/playwright".
Spec: `specs/army-untested-2026-07-22.md`. Fix arc: `specs/fix-army-findings-2026-07-22.md`.
Prod went **v1.22.10 → v1.22.11** mid-run. Companies: co5 (VAT dummy), **co6 (non-VAT dummy, created
this run)**. co2/co3 never touched. Every leg's own report + screenshots live in this folder.

## Coverage — all 11 never-tested areas driven live
| # | Area | Leg | Verdict |
|---|---|---|---|
| 1 | ภ.พ.36 + ภ.ง.ด.54 reverse charge | B-rc | ภ.พ.36 exact; ภ.ง.ด.54 blocked by a CRITICAL → fixed → re-verified ฿3,529.41 |
| 2 | non-VAT company FULL cycle | B2-nv | PASS (zero-VAT sales, VAT-to-cost exact, no 1170) + 2 HIGH found |
| 3 | expense claims full cycle | B-ec | PASS (JE #117 ties) + 2 UX findings |
| 4 | fixed assets lifecycle | B-fa | PASS (numbering, depreciation, disposal all hand-calc exact) |
| 5 | year-end closing | B2-ye | **full PASS** (closing JE exact, post-close deny, reopen+reclose) |
| 6 | bank reconciliation FULL | B-br | PASS + HIGH: real K-Plus PDF 500 → fixed |
| 7 | ภ.ง.ด.1 / 1ก edge cases | B2-pr | run+filings PASS; proration UNBUILT (O8) + RBAC bug (WP-H) |
| 8 | e-Tax pipeline | B-et | DISABLED-by-config (Phase-1 scaffolding, not a defect) |
| 9 | billing notes + 50ทวิ certs | B-bn | PASS (auto-issue, immutable) + HIGH stuck-PV → fixed |
| 10 | MCP agent surface | B-mcp | **PASS end-to-end** (agent draft → widget → human approve → actor identity) |
| 11 | vision vs official forms | C1, C2 | pnd54 98-100%, pnd1 98%, 50ทวิ ~85%, สปส.1-10 not submittable (O11) |

## Bugs found and SHIPPED in v1.22.11 (all re-verified live afterwards, 10/11 in leg V1)
- **CRITICAL** — VI-linked PV posting always 422'd `gl.unbalanced` for any foreign self-withhold
  vendor: the VI-settlement GL branch never booked the self-withhold gross-up debit. The entire
  "pay a foreign vendor + file ภ.ง.ด.54" pipeline was unusable. `e17d232`
- **HIGH** — PV form derived VAT from a single vendor flag, fabricating VAT the vendor never
  charged (20,000/0% VI → 18,691.59 + 1,308.41). `e17d232`, second code path `479baae`
- **HIGH** — a WHT line with no Income-Type passed draft-save AND approve, then 422'd at post,
  leaving the PV permanently stuck (no edit, no cancel) — and a legacy bad draft could block
  period close forever. Now validated at the single create seam + a Draft/Approved→Voided escape
  hatch. `3835e96`
- **HIGH (latent, found by review not by test)** — `PaymentVoucher.Version` was configured as a
  concurrency token but never incremented, so a cancel-vs-post race could mark a POSTED voucher
  Voided (payment vanishes from bank-rec + ภ.พ.36 while its JE stays, and the VI becomes
  re-settleable → double payment). `3835e96`
- **HIGH** — real multi-page K-Plus statement 500'd on import: a left-margin watermark bridged two
  transaction rows and a page footer became a fake row. Plus any unmapped parse error now returns
  a clean 422 instead of a raw 500. `b71e5cd`
- **HIGH** — super-admin editing another company's tax fields → raw 500: the audit-log insert ran
  under the caller's own RLS company pin. `a8d54b4`
- **MEDIUM** — raw i18n keys `status.Submitted`/`status.Paid`; false "success" toast on a repeat
  depreciation run; generic error instead of a permission-named deny on 403. `aaf62c5`
- **LOW** — malformed MCP `tools/call` was swallowed into a generic SDK error (it cost this army a
  whole false-CRITICAL leg) → now `[mcp.arguments] …`. `a8d54b4`

## Fixes committed, awaiting the NEXT release
- **WP-F** `479baae` — PV VI-prefill dual-flag (second code path of the same class).
- **WP-G** (in review) — the PV path had **no company-VAT-mode gate at all**: on a non-VAT company a
  standalone PV really persisted recoverable input VAT and would have debited 1170 on post. Tier-2
  rejected the first attempt (zeroing the VAT under-paid the vendor and stranded AP on the VI-linked
  path) — the correct shape is the single `IsRecoverableVat = false` flag, letting the existing GL
  fold the VAT into cost. **The wrong acceptance criterion was Fable's own spec error.**
- **WP-H** (in flight) — no read-level payroll permission exists, so ภ.ง.ด.1/1ก PDFs sit behind
  `payroll.run.manage` and a TAX_OFFICER is 403'd out of the tax forms.

## Open — Ham's scope calls (nothing here is a crash)
| # | Gap | Where |
|---|---|---|
| O8 | **no day-based payroll proration** — mid-month hire/leaver both got a full month of salary + PIT, in the JE and in the printed ภ.ง.ด.1/1ก. Biggest functional gap the army found. | B2-pr |
| O9 | no employee termination/end-date field in the UI at all (prerequisite for O8) | B2-pr |
| O10 | no negative adjustment / deduction path (overpayment clawback); `OtherDeductions` is a dead stub | B2-pr |
| O11 | สปส.1-10 ส่วนที่ 2 (per-employee schedule) unbuilt → the form prints, but SSO won't accept it | C2 |
| O12 | nowhere to store the 10-digit SSO employer account number (blocks O11) | C2 |
| O1 | fixed-asset acquisition posts no GL and the UI never warns, yet disposal credits the full cost | B-fa |
| O4 | expense-claim edit for Draft/Rejected — backend PUT wired, zero FE surface | B-ec |
| O5 | ภ.พ.36 has no PDF export (only ภ.ง.ด.3/53/54 do) | B-rc |
| O6 | 50ทวิ "ลำดับที่ … ในแบบ ภ.ง.ด.53" always blank — the cert is issued before the monthly filing exists and is immutable by law | C1 |
| O7 | the agent-approval widget shows APPROVER rows it has no permission to open | B-mcp |
| O2/O3 | billing-note TI aggregation rollup + the PDF dropdown button — need one manual re-check each | B-bn |

## Process notes worth keeping
- Two Tier-2 reviews rejected work that had a green full suite. Both times the defect was
  **invisible to the tests as written** (dead concurrency token; assertions that passed on
  under-paid money). Reviewer-on-money is not ceremony.
- One leg reported a false CRITICAL ("the whole MCP write surface is broken") that was its own
  malformed request. Reading the prod server log settled it in one command — cheaper than any
  amount of re-probing.
- AGY's vision pass produced one genuine finding (สปส.1-10 unsubmittable) and one false positive
  (the 50ทวิ dual header is how the official RD template prints). Filter model critique against
  the actual artifact, every time.
