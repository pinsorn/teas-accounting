# PLAN — fix round for the break-it swarm findings (v1.27.1)

## ⏸ STATE AS OF 2026-07-31 ~06:0x — PAUSED AT HAM'S 85% RULE, NOTHING IN FLIGHT

| Stage | Status |
|---|---|
| Break-it swarm (17 agents) | ✅ complete — `VERDICT-breakit-v1271.md`, evidence in `swarm-findings/breakit-v1271/` |
| Ham's 4 decisions | ✅ recorded below |
| Repttown non-VAT | ✅ confirmed by Ham → C6 backfill required on real books |
| **R1 design spec** | ✅ `specs/fix-breakit-r1-ledger-integrity.md` — Opus-designed, **Fable-reviewed APPROVED** |
| R1 implementation | ⬜ **not started** — deliberately deferred, see below |
| R2 / R3 / R4 design | ⬜ not started |
| co5/co7 wipe+reseed | ⬜ not started (now MANDATORY — see the C1 consequence) |

**Why paused:** 7-day quota 83% against Ham's hard stop at 85% (2026-07-31: stop completely, no Codex/AGY
fallback, not urgent, write everything down). R1 is ~40 source files across 5 work packages — starting it
now would strand it mid-flight. Nothing is running; no worker is warm; the tree is clean.

### Resume order (a fresh session can follow this without re-planning)
1. Read `VERDICT-breakit-v1271.md` §CRITICAL, then `specs/fix-breakit-r1-ledger-integrity.md`
   (it is approved — do **not** re-design it).
2. Answer the 5 open decisions below if Ham has replied; **WP-2 (the Repttown backfill) cannot be applied
   without decisions 3 and 4.** WP-1/3/4/5 need none of them and can start immediately.
3. Dispatch in the spec's stated order: **WP-1 → WP-2 (same warm worker) ; WP-3 → WP-4 → WP-5**
   (WP-3/4/5 share files, so that order is mandatory; only one worker may run the test suite at a time).
4. Per the spec's §10: ship WP-1/3/4/5 + WP-2's *code* as the release; run the Repttown `apply` afterwards
   as a separately authorised prod operation, never inside a code-release gate.
5. After R1 ships: wipe+reseed co5/co7, then design R2.

### ✅ Decisions 1 & 2 — RESEARCHED (Ham: "ไม่รู้เหมือนกัน ไปหามา", 2026-07-31)
Full findings with citations: **`specs/research-thai-prior-period-correction.md`** (delegated to agy;
it is research, not tax advice — every point needs the company's CPA to confirm before filing).

**The research CONTRADICTS the spec's WP-2 design, which is why it was worth asking.** The spec has
correcting entries land at their **true event dates**, i.e. inside closed periods. Thai practice is the
opposite: a prior-period error is corrected in the **CURRENT open period against opening retained
earnings** (กำไรสะสม), with comparatives restated for presentation only. Reopening a period whose
financial statements are already with the DBD is done only for very large errors or on DBD demand, and
is the CPA's call. (TFRS for NPAEs บทที่ 5 / TAS 8 ¶42 require retrospective *restatement*, which is a
presentation exercise — not re-posting into a locked ledger.)

**This makes WP-2 dramatically simpler — Fable's proposed redesign, for the implementer:**
- Sale already settled (any year): revenue was recognised at receipt, cash collected, AR is 0 today.
  **The correct state today is already the actual state → post nothing.** Only the *timing within closed
  years* was wrong, and that is a tax matter (amended return), not a ledger matter.
- Invoice unpaid, issued in a **closed** year: `Dr 1130 AR / Cr กำไรสะสม`, dated in the current open period.
- Invoice unpaid, issued in the **current open** year: `Dr 1130 AR / Cr Revenue`, dated at issue.

One entry per outstanding invoice, nothing touches a closed period, and it sidesteps the year-close
deadlock (H10), the closed-fiscal-year question entirely, and any change to statements already filed with
the DBD. **Decision 2 is therefore answered: corrections go in the current open period. Do not reopen.**

**Decision 1 (tax) — the ledger fix does NOT settle the tax side; it is a separate, parallel task:**
- The mechanism exists: **ภ.ง.ด.50 เพิ่มเติม**, filed per accounting period (e-Filing or the area office).
- **เงินเพิ่ม 1.5%/เดือน (ม.27) cannot be waived or reduced** — it is statutory, and it is capped at the
  tax due. **เบี้ยปรับ can be waived to 0%** for a voluntary correction made **before** the RD issues a
  summons (ท.ป. 81/2542); once an audit starts it is 100% (50% at best if you cooperate). **So filing
  early is worth real money — the cost grows 1.5% per month either way.**
- Assessment window: **2 years** from filing (ม.19), extendable to **5** where evasion is suspected;
  a 10-year civil limitation (ม.193/31) exists beyond that. Practical scope: correct everything within 5 years.
- **No materiality threshold exists in tax law** — understated revenue is understated revenue.
- Recognising revenue on cash receipt **is** an incorrect filing: ม.65 + ท.ป. 1/2528 mandate เกณฑ์สิทธิ
  (accrual). Timing matters even though lifetime revenue is identical, because tax is assessed per
  12-month period.
- **Book-to-tax:** once the ledger correction is booked to retained earnings in the current year, the
  current year's ภ.ง.ด.50 must **back that revenue out**, or it gets taxed twice — it was already taxed
  via the amended prior-year return.

**What still needs a human, not more research** (agy flagged these itself): negotiating the เบี้ยปรับ
waiver with the area RD office · whether to re-file the statements with the DBD (materiality — the CPA
who signed them decides) · anything older than 5 years.

**→ For Ham: this is now an action for the company's accountant, in parallel with the code fix.** The
preview endpoint gives the exact per-year revenue figure to hand them. My recommendation: run the
preview first, take those numbers to the CPA, and let the amended-return work start while R1 is built —
the 1.5%/month clock is already running.

### 🔴 3 product decisions still waiting on Ham
3. **Receipt against an already-invoiced delivery order (PRODUCT).** Must be refused to stop
   double-counting revenue (`rc.do_already_invoiced`); the wording and the UX around the refusal is a
   product call, not the implementer's.
4. **Payroll `PayDate <= Bangkok-today` bound (PRODUCT).** Stops the 2099 case, but also blocks posting a
   run *before* payday, which some businesses do. The looser alternative is one line.
5. **PV/VI carry the identical C5 account-type hole, deliberately deferred (PRODUCT).** A payment voucher
   legitimately debits non-expense accounts (asset purchase, loan repayment), so the allowed account set
   has to be defined by Ham before it can be pinned. C1's precision guard still protects the ledger
   meanwhile. Needs a `troubles-wiki.md` entry at diff review or it will be rediscovered.


Source of truth for findings: `VERDICT-breakit-v1271.md` · raw evidence: `swarm-findings/breakit-v1271/` (17 files).
~48 findings → **4 themed releases**. Nothing is descoped; LOWs land in R4.

## Ham's decisions (2026-07-31)
1. **C6 non-VAT basis → ACCRUAL.** Post revenue + AR when the invoice is issued, mirroring the VAT path.
2. **C3 payroll period escape → reuse the existing O14 monthly reopen.** No new feature; the guard must
   return an error that *names the way out*, not a bare `period.closed`.
3. **Release shape → themed, 3–4 releases** (not CRIT-first).
4. **Test data → wipe+reseed BOTH co5 and co7 after the fixes land**, giving a clean baseline to verify on.

### Consequence of decision 3 — flagged, needs a yes/no if a deadline is near
Themed batching puts two compliance CRITs in **R2, not R1**: **C2** (ภ.พ.36 double-counts → over-remits VAT
to the RD) and **C4** (ภ.ง.ด.1 totals on the ม.40(2) non-resident row). Both are wrong-number-on-a-filed-form
defects. R1 is designed to carry everything that writes bad data into the **immutable ledger**, which is the
only class that gets worse with time. **If a filing deadline lands before R2 ships, say so and C2/C4 move to R1.**

### 🔴 CONFIRMED BY HAM (2026-07-31): Repttown IS non-VAT — C6 is live on REAL books
This is now the most serious item in the round. A real operating company's ledger has **no accounts
receivable at all**, recognises revenue only when cash arrives, while its purchases accrue normally — the
books run on two different bases, and any ภ.ง.ด.50 filed off them understates revenue by the invoices
issued-but-unpaid at period end.

**C6 becomes work package 1 of R1, and a backfill migration is REQUIRED, not optional.** The backfill is
the hard half, and it is prod-data surgery on a real tenant:
- Historical JEs are immutable — corrections must be new postings, never edits.
- Past receipts on that company **already recognised revenue at receipt**, so naively posting
  Dr AR / Cr Revenue at each invoice's issue date would **double-count**. Total revenue per sale must
  come out unchanged.
- Affected periods are largely **CLOSED**, and reopening inside a closed fiscal year is refused (and a
  year-close deadlock exists, H10). Where the correcting entries land is a design decision with tax
  consequences.
- **If a correction moves revenue across a fiscal-year boundary it touches a year whose ภ.ง.ด.50 was
  already filed → that is a tax decision for Ham, not an engineering one.** The spec must separate the
  cases that can be corrected silently from those needing an amended filing.
- Requires: DB backup, a dry-run report listing what WOULD post before anything is written, per-company scoping.

Invariant for the whole work package: **total revenue recognised for any given sale is unchanged; AR clears
exactly; Dr=Cr on every correcting entry; cash never moves.**

Reseeding co5/co7 (decision 4) does **not** touch this — Repttown is real data.

<details><summary>Original open question (now answered) — kept for the record</summary>
I went looking for whether any real tenant runs non-VAT. **Two independent documents say Repttown does:**
- `HANDOFF-untested-army.md:9-10` — "**NO non-VAT dummy company exists yet** — create one first (Step 0) so
  non-VAT tests **don't touch Repttown**." (i.e. Repttown was the only non-VAT company available to test on)
- `PROGRESS-vat-dummy-test.md:6` — "ก่อนหน้านี้เทสแค่ **non-VAT/Repttown**"

If that holds, C6 is not a dummy-company gap: **a real company's books currently have no accounts receivable
at all, and recognise revenue only when cash arrives** — while its purchases accrue normally. Its P&L and
balance sheet are on two different bases, and any ภ.ง.ด.50 filed off them understates revenue for invoices
issued-but-unpaid at period end.

**Ham: confirm whether Repttown (co2/co3) is non-VAT.** I did not query the prod database to settle it —
one word from you is cheaper and safer than me touching real data.

Consequences if confirmed:
- **R1 gains a backfill migration** (post the missing AR/revenue for historical issued-unpaid invoices),
  which is schema-and-money work — Opus design, not a side task.
- The severity ordering changes: C6 stops being "a company type is incomplete" and becomes "real financial
  statements are wrong right now", which would justify pulling it ahead of everything else in R1.
- Reseeding co5/co7 (decision 4) does **not** clean this up — Repttown is untouchable real data.

If Repttown is in fact VAT-registered, none of the above applies and C6 stays a forward-only fix.
</details>

---

## R1 — Ledger integrity (everything that writes wrong data into an immutable ledger)
Footgun tier: money + a new GL posting path → **Opus design spec, Fable reviews it, Sonnet implements,
Opus reviews the diff, Fable reads the full diff before commit.** Acceptance-tester pass before Tier-2.

| # | Finding | Fix |
|---|---------|-----|
| C1 | Sub-satang amounts reach the GL via **3 paths** (JV draft + MCP, expense claims 4dp, payroll deduction) | Guard at the **posting seam** every path funnels through — not per-validator patching. Validators fail fast on top. Decide the rounding contract (reject vs round) and apply it once. |
| C5 | Expense claim accepts ANY account type (bank/AP/revenue/equity/1170); category-default branch skips validation entirely | Validate account **type** (expense/cost only) on **both** branches of `BuildLinesAsync` (ExpenseClaimService.cs:94-97) + at `POST /expense-categories`. Add an update/deactivate route for categories (today a poisoned one is permanent). |
| C3 | Payroll post/pay skip `EnsureOpenAsync`; no future-date guard; unbounded (JE dated 2099) | Inject `IPeriodCloseService`, guard both paths, add a future-period bound. Error message must name the O14 reopen path. **Ship together — the guard alone bricks payroll on a company whose only open period already has a run.** |
| C6 | Non-VAT companies never accrue revenue/AR (`BillingNoteService.IssueAsync` posts no JE) | New GL posting on invoice-issue for non-VAT companies, mirroring `TaxInvoiceService.PostAsync:545`. Receipt must then settle AR instead of recognising revenue — **check the receipt path does not double-count**. |

**R1 exit gate:** full suite green · a test proving each of the 3 precision paths rejects >2dp · a test that a
non-VAT invoice-issue moves 1130 and the receipt does not re-recognise revenue · Tier-4 live leg on a
freshly-reseeded company.

## R2 — Compliance & filings (documents that go to the government)
Footgun tier: compliance → Opus design for C2/C4, Sonnet implements, Opus reviews.

| # | Finding | Fix |
|---|---------|-----|
| C2 | ภ.พ.36 double-counts reverse-charge VAT (VI + settling PV both flagged, `Concat` with no dedup) | Flag belongs on ONE side; query picks it. Fix the already-double-counted history on any real tenant. |
| C4 | ภ.ง.ด.1 / 1ก totals print on the ม.40(2) non-resident row, "6. รวม" blank | **Needs Ham: pin the correct field ids against the official template** — the field map carries a "Ham visual-validation pending" note. Reproduced on 2 companies. |
| H16 | Non-VAT company can render AND **finalize** a ภ.พ.30 (`filingId 1` now on co7) | `non_vat_blocked` guard on the route, both render and finalize. Back out the bogus filing. |
| H13 | ภ.ง.ด.1 / สปส.1-10 / payslips render from an unapproved DRAFT run (`journalId: null`) | Filing artifacts only from posted runs. |
| H8 | Every สปส.1-10 ships employer account `0000000000` | Refuse export on a missing mandatory field, mirroring ภ.พ.30's `pp30_batch.missing_address`. |
| H9 | SSO files ship `?????` names; cp874 encoder uses a silent replacement fallback | Validate payee names before a government file; make the encoder fail loudly, not silently. |
| — | สปส.1-10 **ส่วนที่ 2 prints entirely blank** (no national IDs) while ส่วนที่ 1 certifies 3–4 people | Fill the section. |
| — | ภ.พ.36 and ภ.ง.ด.2 have **no PDF route at all** | Add both. |
| — | `pnd50`/`pnd51` PDF → HTTP 500 on out-of-range year | Range-validate (folded into R3's mapping pass if cheaper). |

## R3 — Guards, scopes & robustness
| # | Finding | Fix |
|---|---------|-----|
| H2 | The 500 family: double-post race on JV/TI/RC · attachment >5MB · bad `bankAccountId` · period endpoints' year/month · over-length JV fields · pnd50/51 year | **One exception-mapping pass.** Mirror `PaymentVoucherService`'s `DbUpdateConcurrencyException`→409 into TI/RC/JV; map Postgres 22001/23505 globally; range-validate inputs. |
| H3 | **Systemic:** all 5 conversion routes authorize on the SOURCE doc's scope, never the TARGET's | One shared helper requiring both. The grep that found it becomes the regression test. |
| H4 | `GET /attachments/{id}/download` skips `ParentGuard` (IDOR, intra-tenant) | Call `ParentGuard` after resolving the parent. |
| H17 | Same SO billable twice (`so.invoice_exists` doesn't traverse SO→DO→Invoice) | Traverse the link that already exists (`DO.billingNoteId`). |
| H1 | Duplicate running numbers on tax documents (index is `(Company,Branch,DocNo)`, allocator is branch-blind) | Pick one: branch-aware allocator · company-wide uniqueness · branch segment in the number. **Design decision — Opus.** Plus: `/reports/number-gaps` must detect REUSE, not just gaps. |
| H10/H11 | Year-close three-way deadlock; period reopen unauditable (enables silent back-dating) | Break the deadlock; expose the reopen audit trail through an API and stop NULLing `ClosedAt/By/Notes`. |
| — | MCP `.post` denylist bypassable via U+200B; `/mcp` auth is kind-agnostic | Call the existing `McpScopes.Normalize` allowlist at mint; pin `/mcp` to `kind == mcp`. |
| — | `SALES_STAFF` with no report scopes downloads the full AR aging CSV | Re-gate that route. |

## R4 — Documents, reports & the LOW cluster
| # | Finding | Fix |
|---|---------|-----|
| H15 | Non-VAT PV/VI print a Grand Total their lines don't add up to (`PaperFootPlan.cs:31-41`) | Show lines at gross on non-VAT, or keep a reconciling row. VI additionally reads `vatAmount` instead of `nonRecoverableVatAmount`. |
| H5 | Voided/draft PV prints "ต้นฉบับ" (hard-coded, ignores status) | Call `PaperDocConfig.Watermark`; same in `PurchaseOrderService.cs:325`. |
| H6/H14 | Payslip YTD frozen at run creation → contradicts 50ทวิ/ภ.ง.ด.1ก, non-monotonic | Recompute YTD at render. |
| H7 | AR/AP aging ignore `asOf` entirely | Add the `DocDate <= asOf` filter and an as-of paid figure — the pattern is already in the same file. |
| H12 | TB/BS default as-of to UTC, aging to Bangkok → the two disagree 00:00–07:00 | One timezone contract. |
| — | sales-summary excludes CN/DN → disagrees with ภ.พ.30 and TB | Include them. |
| — | Line numbers ≥10 wrap vertically on every template · CE years on a ภ.ง.ด.50 attachment · no JV/expense-claim PDF · unformatted issuer tax ID | Template fixes. |
| — | LOW cluster: stale SoD comments claiming a dropped DB constraint · `ApproveAsync` missing the concurrency wrapper · PV `CreatedBy` never stamped · MIME spoofable · no expense amount cap · attachments mutable on a PAID claim · no 200-line cap on the JV draft path · VI over-bills a PO to 200% with only an advisory chip · TB silently ignores unknown query params · no server-side logout | Batch. |

## After R4
1. **wipe+reseed co5 and co7** (decision 4) — clears the skewed TBs, the 2099 JE, the poisoned category, the
   bogus ภ.พ.30 filing, and co5's unclosable FY2026 in one move.
2. **Re-run the break-it swarm** against the reseeded companies to confirm each finding is closed and nothing
   regressed. The 17 dispatch briefs are reusable as-is.
3. Correct the stale notes in `STATUS.md`: O8 proration is FIXED; co7's `???` names are a client-side
   artifact, not a server bug.

## Quota rule for this whole fix round (Ham, 2026-07-31)
**7-day pool ≥85% → STOP completely. Do NOT fall back to Codex or AGY.** Not urgent — the instruction is
"บันทึกทุกอย่างเอาไว้": write the state down and pause. At the stop point, leave behind (a) this plan with
every work package marked done / in-flight / not-started, (b) any spec files produced so far, (c) a resume order,
(d) a checkpoint commit. The 5-hour window rule is unchanged: checkpoint + ScheduleWakeup and continue the
same day.

## Standing rules for every dispatch in this plan
- Workers never `git commit`. Fable runs the consolidated gate and reads the full diff before each commit.
- Money/compliance/schema work: Opus designs, Fable reviews the spec (never skipping a money-invariant
  section), a cheaper worker types the code, Opus reviews the diff.
- A money spec states the **invariant** (cash paid unchanged, AP clears exactly, Dr=Cr), never just the
  observable field values.
- One test-running worker at a time — the integration DB is shared.
