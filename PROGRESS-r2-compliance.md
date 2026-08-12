# PROGRESS — R2 compliance filings (release 2 of 4)

Updated 2026-08-12 22:45. R1 shipped and live (v1.28.0). **R2 wave 1 is committed; wave 2 is in flight.**
Quota at last reading: 5-hour 53%, 7-day 32% — Ham's full-stop rule not in play.

Spec: `specs/fix-breakit-r2-compliance.md` (Opus-designed, 8 WPs). Findings: `VERDICT-breakit-v1271.md`.
Plan: `PLAN-fix-breakit-v1271.md`.

## Wave 1 — COMMITTED

| commit | WP | what |
|---|---|---|
| `5a9b1b0` | WP-6 | ภ.ง.ด.50/51 reject a nonsense CE year with 422 instead of an unmapped 500 |
| `0f59fab` | WP-5 | สปส.1-10 / ภ.ง.ด.1 refuse a blank employer account and an unfilable name |
| `934a561` | WP-1 | **C4 RETRACTED — never a defect**; both field maps rewritten from measurement |
| `e9e9e90` | — | routing log |

Gates for the wave: full suite **1151 passed / 0 failed / 14 skipped** (Domain 188/0/0), `tsc --noEmit`
0 errors, vitest 65/65. The numbers reconcile exactly: +22 passed = WP-5's 4 + FilingNameRules' 6 +
WP-6's 12; skipped 8 → 14 = WP-1's 6 new `TEAS_DIAG`-gated diagnostics. Nothing was silently disabled.

### What wave 1 actually settled
- **C4 is not a defect** and is marked RETRACTED in the verdict. The swarm's "totals print on row 5,
  รวม blank" came from `pdftotext -layout`, whose line reconstruction attributes row 6's text to row 5's
  printed line when rows 2–5 are empty. Reproduced on demand, then refuted from the rendered image:
  ภ.ง.ด.1 row 6 รวม `1 / 125,000.00 / 1,408.33` and row 8 `1,408.33`; ภ.ง.ด.1ก row 6 รวม
  `1 / 965,000.00 / 52,450.00`; rows 2–5 empty on both. **Stages C/D/E and Ham's image gate cancelled** —
  there was no coordinate to change. The general lesson (a placement claim must come from a rendered
  image, and a second agent agreeing reproduces the tool's artifact) is folded into the implementer agent.
- **The committed pnd1 field map was wrong** in two places (`Text1.21` as sheet-count; `Text1.11`/`.13`
  address swap) while the code was right. `pnd1a_fieldmap.md` did not exist and now does. **ใบแนบ was
  NOT re-measured** — both maps say so explicitly.
- **Tier-2 (Opus) REJECTed WP-5** on a defect nobody else saw: `errorToToast` returns
  `resolveProblemKey(code) ?? detail`, so adding an i18n entry **replaces** the backend detail rather
  than titling it — which deleted the employee id and the U+xxxx code point the user needs to find a
  lookalike character. Verified in source, then fixed by removing the entry (the backend message is
  already Thai-first). A comment in `problems.ts` records why that key must stay absent.
- Opus's F2 (test setup nulling company 1's `DefaultDocNotes` on the never-reset `teas_test`) fixed;
  `PayrollRunServiceTests` re-run 38/0/0.
- **F3 closed against prod, not assumed:** prod `appsettings` carries **no** `Payroll:Sso:EmployerAccountNo`
  key (only Rate/WageFloor/WageCeiling/MaxAllowanceForPit), so the H8 guard genuinely fires there — no
  tenant can file สปส.1-10 under another entity's account number via the config fallback.
- WP-6's year bound arbitration (Fable): `< 2000 or >= 9999` accepted. A tighter ceiling would 422 the
  repo's own green tests, which file years 3098/3099 and [2500, 7499] as a shared-DB collision-avoidance
  convention. Residue — `year=3000` still renders an empty filing — is a nonsense answer, not a crash;
  **deferred to R3**, where the seam is API-request validation.

## Wave 2 — IN FLIGHT (3 parallel implementers, nothing committed)
- **WP-2 — C2 ภ.พ.36 declares the payment.** Owns `teas_test`. Removes the vendor-invoice rows from
  `GeneratePnd36Async` so one foreign-service purchase is declared once, at the ม.83/6 payment tax point.
  Carries a **blocking pre-check**: if any route other than `PaymentVoucher` can settle a
  `VendorInvoice`, it must STOP — removing the invoice rows would then under-remit, which is worse.
- **WP-3 — H16 ภ.พ.30 VAT-registrant-only.** Under TEST-DB HOLD. Also briefed on the false-positive
  lens (a deregistered company still owing a final return) and that **ภ.พ.36 must NOT be blocked** for a
  non-VAT company.
- **WP-7 — delete the "customer has paid" button.** Under TEST-DB HOLD; FE work unblocked. Removes one
  public endpoint — the only public-API change in R2. Told NOT to run Playwright.
- Both held workers hand their i18n keys back to Fable rather than touching `problems.ts` (the
  serialisation that avoided a three-way collision in wave 1).

## Live PRE-deploy baseline (captured by Fable through the browser, read-only, nothing clicked)
Captured on prod so the Tier-4 leg after R2 ships has something to compare against.
- **WP-7** — `https://teas.kazaki-rio.com/invoices/3` (co2 `07-2026-IV-LAB-0001`, ฿8,400, status Issued)
  renders the button **"ยืนยันชำระครบแล้ว"** immediately beside "สร้างใบเสร็จ". That is the button being
  deleted. **After R2: it must be gone and only "สร้างใบเสร็จ" remains**, with the invoice still
  reachable and still Issued.
- Route note for future browser work: **there is no `/th` locale prefix** on this app — pages live at
  the root (`/invoices/3`, `/payroll`, `/tax-filings`). `/th/...` returns a Next.js 404, and because it
  is client-side routed the URL bar can keep showing the path you asked for while the body is the 404
  page. Don't read the URL as proof the page loaded.
- co2 has **zero payroll runs**, so the H13/WP-4 draft-run baseline cannot be captured there — use co5
  or co7 for that leg (both are test playgrounds).

## Remaining in R2 after wave 2
**WP-4 — H13 filing artifacts require a Posted run.** Not yet dispatched. Its E3 product question is
already answered inside the spec (refuse from `Draft`, allow `Approved`+`Posted`, as a separately
revertable commit), so it needs no further input. Note the spec says "run WP-4 BEFORE WP-5" — that
ordering is now moot since WP-5 shipped; the dispatch must warn the worker not to undo WP-5's guards in
`SsoFilingService.cs` / `Pnd1FilingService.cs`.

## Still escalated (not blocking any dispatch)
E4 the two blank `สปส.1-10/1` pages (product) · E5 entry-time name validation (scope; the deferral ships)
· **E6 ภ.พ.36 / ภ.ง.ด.2 PDF templates and E8 the official ส่วนที่ 2 template — asset asks, Ham must
supply the official PDFs.** (E1, E2, E3, E7 are all resolved.)

## Not R2
R3 (duplicate tax-doc numbers · the 500 family · conversion routes checking the wrong scope · attachment
IDOR · year-close deadlock · **the year=3000 nonsense-filing bound** · **point-in-time VAT status for
filing guards**, below) · R4 (documents/reports + LOW cluster) · doc-lifecycle features A and B — Ham's
answers recorded in `specs/doc-lifecycle-cancel-reissue-backdate.md` §6.

**New R3 candidate, surfaced by WP-3 and worth a spec of its own.** Every VAT filing guard reads
`VatMode` as **current** state, never as the state during the period being filed. So a company that
deregisters from VAT is blocked from filing even its final ภ.พ.30 for a pre-deregistration period.
This is **pre-existing, not a WP-3 regression** — `TaxInvoiceService.EnsureVatRegisteredAsync`, the
sibling WP-3's guard mirrors, has had the identical property all along, so WP-3 shipping does not make
it worse. But it is exactly the class of guard-with-no-exit this project has been bitten by twice, and
the fix is cross-cutting (a point-in-time registration history), not a drive-by edit.

## Also outstanding from R1
wipe+reseed co5/co7 — confirmed necessary (co5 has 1 REVENUE and co7 3 EXPENSE sub-satang lines, exactly
what year-close aggregates, so neither can be year-closed). Deferred to just before the swarm re-run,
which wants clean companies anyway. co7's bogus `Finalized` ภ.พ.30 filing row clears in the same pass.

## Environment notes worth keeping
- `pnpm` is NOT on PATH here. Use `frontend\node_modules\.bin\tsc.cmd`, `...\vitest.cmd`, or
  `corepack pnpm` (corepack IS on PATH).
- Full-suite command that works: set `TEAS_TEST_PG` and `TEAS_REPO_ROOT` in the SAME invocation;
  `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password`.
- Prod read-only access: `ssh -i ~/.ssh/repttown_deploy -o BatchMode=yes ubuntu@158.69.197.154`,
  app at `/opt/npm-sites/teas.kazaki-rio.com/api`, DB `teas` via `sudo -u postgres psql`.
