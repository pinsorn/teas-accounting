# V4 — O8 payroll proration verified LIVE on prod v1.23.0 (Fable, 2026-07-26)

The army's original finding (`swarm-findings/army/B2-pr.md` F1): an employee hired mid-month or
terminated mid-month was paid a FULL month's salary and full PIT — **in the GL journal and in the
printed ภ.ง.ด.1/1ก**. This leg re-drives that exact scenario against the deployed fix.

Target: prod **v1.23.0** (confirmed via `/system/info` → `"version":"1.23.0"`), company **co7**
(`บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด`, id=7, `vat_mode:false`). Driven by Fable directly over the
authenticated BFF (`/api/auth/login` cookie jar), no worker, no browser.

## Setup — 3 employees, identical salary ฿60,000, July 2026 (31 days)
| code | employment | expected days | expected gross |
|---|---|---|---|
| `O8FULL` | hired 2020-01-01, active | 31/31 | **60,000.00** (must be untouched) |
| `O8MID` | hired **2026-07-15** | 17/31 | **32,903.23** |
| `O8OUT` | terminated **2026-07-10** | 10/31 | **19,354.84** |

## Result — payroll run 10 (`07-2026-PR-0001`), all three exact
```
O8FULL  gross=60000.00   pit=372.92  sso=875.00  net=58752.08
O8MID   gross=32903.23   pit=0.00    sso=875.00  net=32028.23
O8OUT   gross=19354.84   pit=0.00    sso=875.00  net=18479.84
run total gross = 112,258.07
```
- `O8FULL` is **exactly 60,000.00** — the full-month short-circuit really does return `BaseSalary`
  untouched (no rounding drift), which was the #1 regression risk in the design.
- `O8MID` = 60,000 × 17/31 and `O8OUT` = 60,000 × 10/31 — **both match the army's hand-calcs to the
  satang**, computed independently by the army leg weeks before this code existed.
- PIT is 0.00 for both partial employees: ม.50(1) projects annual income from the (smaller) part-month
  gross, which lands under the taxable threshold. That is the design's documented, Ham-approved
  behaviour (it self-corrects in later months; a leaver settles on ภ.ง.ด.91).
- SSO is 875.00 for all three because every prorated wage here still exceeds the live wage ceiling.
  The below-ceiling path (a prorated wage under the ceiling paying proportionally less) is covered by
  the unit tests, not by this run — noted so nobody reads this as proof of that branch.

## GL — the actual money proof
Posted JE **176** (`07-2026-JV-0002`):
```
Dr 5400 Salaries              112,258.07   ← prorated total, NOT 180,000
Dr 5410 Employer SSO            2,625.00
Cr 2153 PIT payable (ภ.ง.ด.1)     372.92
Cr 2160 SSO payable             5,250.00
Cr 2170 Net wages payable     109,260.15
Dr total 114,883.07 = Cr total 114,883.07
```
Pre-fix this journal would have debited **180,000.00** (3 × 60,000) — a **67,741.93** overstatement of
salary expense on three employees in one month.

## The printed form — the regression the finding actually named
`GET /payroll/runs/10/pnd1/pdf` → 200, 304,081 bytes (saved as
`swarm-findings/army/pdfs/V4-o8-pnd1-prorated.pdf`). Text extraction of the main page:
```
1. 40(1) … 3 คน   112,258.07   372.92
5. 40(2) … 3 คน   112,258.07
```
**The form prints 112,258.07 and contains no `180,000` anywhere** — the printed artifact now agrees
with the GL and with the payslips. (The ใบแนบ per-employee page could not be text-verified: Thai font
subsetting makes those cells extract as dot-leaders, the same caveat recorded for the C2 vision wave.
Its rows aggregate to the main page total, which is exact.)

## Verdict
**O8 CLOSED and verified end-to-end on production**: payslip → GL journal → printed ภ.ง.ด.1 all carry
the same prorated figures, and a full-month employee is bit-identical to pre-fix.

## State left behind
co7 now has 3 employees and one POSTED payroll run + JE 176. co7 is the non-VAT playground (co6 is
frozen by its year-end close), so this is intentional test data — but note a posted payroll run means
co7's July 2026 now has GL activity, and its period is still OPEN (deliberately, per the O14 spec's
acceptance plan).
