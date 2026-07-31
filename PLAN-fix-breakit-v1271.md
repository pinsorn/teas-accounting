# PLAN — fix round for the break-it swarm findings (v1.27.1)

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

### ⚠️ C6 is almost certainly LIVE ON REAL BOOKS — Ham to confirm in one word
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

## Standing rules for every dispatch in this plan
- Workers never `git commit`. Fable runs the consolidated gate and reads the full diff before each commit.
- Money/compliance/schema work: Opus designs, Fable reviews the spec (never skipping a money-invariant
  section), a cheaper worker types the code, Opus reviews the diff.
- A money spec states the **invariant** (cash paid unchanged, AP clears exactly, Dr=Cr), never just the
  observable field values.
- One test-running worker at a time — the integration DB is shared.
