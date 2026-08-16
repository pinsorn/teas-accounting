# PLAN — fix everything the 2026-08-15/16 local hard-test round found

One consolidated plan so the whole batch can be fixed in one pass instead of finding by finding.
Evidence for every item is in `PROGRESS-local-hard-test.md`; this file is the *fix* view: what changes,
in what order, who does it, and what would make each one wrong.

**Status of the round: 13 findings. 3 already fixed and shipped. 10 open.**
Nothing here is deployed — there is no server to deploy to until the migration.

| # | What | Severity | State | Fix unit |
|---|---|---|---|---|
| F5 | MCP api key could mint a Tax Invoice it had no scope for | 🔴 security | **fixed** `4988e52` | — |
| F2 | Raw 500s + leaked .NET text on VAT reports and CIT year endpoints | medium | **fixed** `4988e52` | — |
| F6 | Convert buttons rendered without the permission the backend demands | low-med | **fixed** `edcf9af` | — |
| F8 | SO→DO conversion drops discount, tax code, and the order-line link | 🔴 money/tax/control | open | **A** |
| F8b | Quotation→Tax Invoice drops the discount onto an immutable document | 🔴 money/tax | open | **A** |
| F13 | Tax invoices store tax code `V7`, which is not in the company's master | 🔴 tax/data | open | **A** |
| F9 | Payment-voucher preview overstates Grand Total and Net by the WHT | medium | open | **B** |
| F10 | 50 ทวิ issued with an all-zero payer tax ID, no warning | medium | open | **C** |
| F1 | A later demo-seed boot leaves tenants with no roles at all | medium | open | **D** |
| F11 | Tax-invoice header discount rollup stays zero | low | open | **E** |
| F12 | `/reports/profit-loss` defaults to excluding untagged activity → all zeros | low | open | **E** |
| F4 | A missing required query parameter returns 500 instead of 400 | low | open | **F** |
| — | `create_receipt_draft` reads a tax invoice under only `receipt.create` | low | open | **F** |

### 🔺 Non-VAT pass landed, and it UPGRADES Unit A — the wrong number DOES reach the ledger

The earlier write-up said the F8 discount error "never reached the GL" because delivery orders have no
`journal_entry_id`. **That was true of the delivery order and false of the chain.** On the non-VAT company
the same conversion dropped a 10% line discount (฿800 → ฿720 became ฿800), inflating the document from
฿1,719.99 to **฿1,799.99**, and the error then flowed DO → ใบแจ้งหนี้ → ใบเสร็จรับเงิน into journal
`08-2026-JV-0001`: **Dr ลูกหนี้การค้า 1,799.99 / Cr รายได้จากการขาย 1,799.99**. Balanced, no VAT touched —
and ฿80.00 higher than what the customer accepted on the quotation.

The VAT company's books stayed clean only because the swarm agent happened to notice the inflated figure
and corrected it in the still-editable invoice draft before it reached the tax invoice. That was a human
catching it, not a structural guard. **Unit A is therefore a live money defect that posts, not a
document-layer cosmetic issue, and its priority moves above Unit B.**

### Non-VAT guards — all hold, and this is worth keeping
- **Tax invoices are blocked server-side, not just hidden.** `/tax-invoices` and `/tax-invoices/new` show a
  clean ม.86/4 refusal, and a direct POST with a full valid payload still returns **422
  `ti.non_vat_blocked`**. Defense in depth, confirmed by going around the UI.
- **ภ.พ.30 is not offered at all** on the filings page — nothing to click, rather than a button that errors.
- **Withholding tax still works.** The 50 ทวิ income-type dropdown and the WHT fields are live on a
  non-VAT company, correctly treating WHT as income tax rather than something tied to VAT registration.
- **Labels are right** (ใบส่งของ / ใบแจ้งหนี้, never ใบกำกับภาษี) and **no tax code is written at all** —
  lines carry `taxCode: null`, which is cleaner than the VAT company's phantom `V7` (F13).

### A new company created through the UI proves the real tenant path is sound
Company **id 4, "NV-ร้านนอนแวต2 เดโม"** was created through the Super Admin screen, so it went through
`CompanyService.CreateAsync`. It came up with **30 chart-of-accounts rows and all 11 system roles** —
the same shape as co3. That is the counterpart to **F1**: companies created through the app get their
roles, and only the raw-SQL seed path does not. Two new low-severity UX findings came with it: the
create-company form reports only *"Request validation failed (1 field(s))"* with no field named when the
tax-ID checksum fails, and turning จด VAT off leaves the VAT rate, VAT registration date and ภ.พ.30 filing
mode fields fully editable.

**Unfinished, and cheap to finish:** company 4 has no customers yet, so posting one sale there (and then
checking its stored tax code and its ใบกำกับภาษี refusal) was not completed. Roughly ten tool calls, and
nothing depends on it — co3 already answered the same questions.

---

## ⏸ CHECKPOINT — 2026-08-16, paused at the 5-hour quota cliff

**Quota at the latest pause (2026-08-16, second checkpoint): 5-hour 99%, 7-day 85%.**
The 5-hour window resets around epoch **1786865400**. The pause is because of the **5-hour** pool, which
is the one carrying the 85% gate. A wakeup is chained; the stack (API :5080, FE :3000) was still up and
the working tree was clean, with everything committed through `a6d8d20`. No work is in flight — the
non-VAT agent reported before the cliff and its findings are already folded in above.

**Start with Unit A, not Unit B** — the non-VAT pass showed the lost discount reaching the ledger, so it
outranks everything else. Unit A needs an Opus design first; Unit B is a one-line fix worth taking as a
quick win while that design is being written.

**Corrected rule (Ham, 2026-08-16): the 85% gate is on the SESSION / 5-hour pool. The 7-day pool may run
to 95%.** An earlier note here and in memory had this backwards and would have stopped a resume at 7-day
85% for no reason. On resume, read the **5-hour** figure: below 85%, carry on; at or above it, checkpoint
and pause again. The 7-day figure only matters as it approaches 95%.

### State — everything is committed, nothing is in the working tree
Commits this round: `4988e52` (security + 500 family), `edcf9af` (convert buttons), `a32d682` (this plan),
plus the finding write-ups `d742929` `be2ea79` `5aac414` `e3e14a0` `4c1ed0a` and status `4a2de4c`.
Working tree clean at pause.

### One thing was in flight
A sonnet agent was testing the **non-VAT** path: first company 3 (ร้านนอนแวต เดโม), then — added mid-run at
Ham's request — **creating a brand-new non-VAT company through the UI** and re-running the decisive checks
there, because a company created through `CompanyService.CreateAsync` takes the real tenant path while co3
was inserted by a raw SQL seed, so any difference between them is itself a finding.

A subagent cannot be paused, only finished or killed, and a killed one loses its findings entirely — so it
was left running. **If its report arrived, fold the findings into the table at the top of this file before
starting any fix.** If the session ended first, that pass is simply lost and needs re-running; nothing else
depends on it, and it does not block Unit B or D.

What it was asked to check on the new company: whether it gets a usable chart of accounts, tax codes and
roles compared to co3; whether it can be worked in; one simple sale posted end to end with VAT 0.00
throughout; **what tax code its lines store and whether that code exists in its own master** (the VAT
company writes `V7`, which does not — finding F13); and whether the ใบกำกับภาษี route is refused.

### Resume, in order
1. Read the 7-day quota. At ≥85%, stop — see above.
2. If the non-VAT report landed, fold it into the findings table.
3. Start at **Unit B** (the payment-voucher preview — one line, one file, a wrong number on the screen an
   accountant approves payments from). Then **Unit D**, then design **Unit A**.
4. The local stack may no longer be running. Restart it with the recipe in memory
   `local-stack-boot-recipe` — the two env overrides matter, and the database must be seeded in one boot
   or every tenant ends up with no roles.

---

## Unit A — the document-chain conversion defects (F8 + F8b + F13)
**Do these together. They are one bug wearing three hats, and fixing them separately would mean touching
the same DTO and the same two screens twice.**

### What is actually wrong
`ChainLineDto` (`frontend/lib/types.ts:1061-1065`) — the line shape the sales-order and quotation detail
endpoints return — carries `lineNo, productId, productCode, descriptionTh, quantity, uomText, unitPrice,
lineAmount, taxAmount, totalAmount`. It does **not** carry `lineId`, `discountPercent` or `taxCode`.
Two screens then have to rebuild a create-request from it and invent the missing values:

- `sales-orders/[id]/page.tsx:56-78` sends `salesOrderLineId: null`, `discountPercent: 0`,
  `taxCodeId: 1`, `taxCode: vatMode ? 'VAT7' : 'VAT0'`.
- `tax-invoices/new/page.tsx:92-110` + `:139-151` never sets `discountPercent` in the prefill, then sends
  `discountPercent: l.discountPercent ?? 0`, `taxCodeId: 1`, `taxCode: 'V7'`.

Consequences, all confirmed: a delivery order overstated by ฿401.25; a tax invoice — **immutable once
posted** — that silently loses its discount; `delivered_quantity` that never moves so the
`do.over_delivered` guard can never fire; an exempt line re-charged at 7%; and a stored tax code (`V7`)
that does not exist in the company's master, on a row whose `tax_code_id` says otherwise.

### The shape of the fix — decide this before writing code
Ten of the twelve conversion paths are already clean, and they are clean for one of two reasons: they
POST **no line payload at all** and let the server copy from the tracked entity, or the request is
**amount-based** so the already-discounted `lineAmount` carries through untouched.

There is a working example of the exact broken request in this repo: the MCP tool
`create_delivery_order_draft` (`TeasMcpTools.cs:655-691`) builds the same `CreateDeliveryOrderRequest`
from `SalesOrder.Lines` and carries `DiscountPercent`, `TaxCodeId`, `TaxCode`, `TaxRate` and
`ProductType` faithfully.

So the likely answer is **make these two conversions server-side like their ten siblings**, rather than
widening `ChainLineDto` and trusting the client to echo it back. Widening the DTO leaves the client free
to send a wrong discount next time; removing the client payload makes that impossible. But this is
exactly the judgement the design must settle, because the tax-invoice screen is also a normal *create*
form, not only a conversion target — it must still accept hand-entered lines.

**Routing: Opus design first, then Sonnet implements from the spec, then Opus reviews.** Money + tax +
a DTO shared across the whole document chain is footgun-zone on every axis, and F8b lands on a legally
numbered immutable document. Fable co-authors the design and never skips the money sections of the
review.

### Traps for whoever designs it
- **`SalesLineBackstop.Resolve` protects the rate but not the code.** It ignores a client-supplied rate
  and derives from the code, so sending the wrong *code* is the whole exploit. Any fix that keeps a
  client-supplied code must validate it against the company's tax-code master.
- **`tax_code_id` and `tax_code` are stored side by side and can disagree** — they already do on
  TI-0001. Whatever writes them must write a matched pair, and it is worth deciding whether the string
  should be stored at all rather than derived from the id.
- **Existing rows carry `V7`.** Decide explicitly whether to leave them, or repair them, and if repairing
  then remember these are posted tax documents: renumbering-style caution applies, and the GL must not
  move. The ledger currently ties out, so a repair that changes an amount is a regression.
- **Do not "fix" the ten clean paths.** They are clean by construction; changing them adds risk for
  nothing.

---

## Unit B — the payment-voucher preview (F9)
One line, one file. `payment-vouchers/new/page.tsx:319` passes `total: subtotal + vat` to the paper
renderer, but `PaperFoot.tsx:34-39` documents that when `summary.wht` is set, `total` **is the net**
and Grand is derived as `total + wht`. The page already computes the correct value at line 187 as `net`.
Passing `net` satisfies the contract in all three cases — no WHT, normal WHT, and self-withhold, where
`wht` is passed as null and the vendor is paid in full.

**Routing: Sonnet, with a test.** The contract has a pinned test (`PaperFoot.test.ts`) against a backend
fixture, so add a case for the caller rather than re-deriving the rule. Trap: `PaperFootPlan.cs` is the
single source of truth and the comment records that this contract was inverted once before — do not
"fix" the renderer to match the caller.

---

## Unit C — the 50 ทวิ with no payer identity (F10)
A withholding certificate is generated and marked บันทึกแล้ว with the payer's tax ID printed as
`0-0000-00000-00-0`, because the company profile has none. The numbers on it are right; the document is
useless to the vendor, who cannot substantiate the credit.

The precedent to follow is v2.0.0's WP-3/WP-5, which refuse to produce a filing when the identity behind
it is unusable. **Decide with Ham** whether this should refuse outright or warn loudly at generation —
refusing is consistent with the existing guards, but it would block a demo tenant that has never filled
in its profile. **Routing: Sonnet once the behaviour is decided; the decision is Ham's, not the worker's.**

---

## Unit D — seed ordering leaves tenants with no roles (F1)
`510_per_company_roles_reconcile.sql` fans per-company roles out over `master.companies` once and is then
recorded as applied forever, so any company created by raw SQL afterwards has no roles and none of its
users can log in (`401 auth.no_company_assignment`). Real tenants are safe — `CompanyService.CreateAsync`
calls `sys.seed_company_roles` directly (`MasterDataServices.cs:388`) — so this is the demo/seed path and
anything that restores or imports a company by SQL.

Simplest fix that closes the class: a new numbered SQL script that calls `sys.seed_company_roles` for
every company that is missing roles. It is idempotent by construction and repairs an already-broken
database as well as preventing the next one. **Routing: Sonnet. Trap: it must be idempotent and must not
touch the global SUPER_ADMIN row.** Worth doing before the server migration, since a restore is exactly
the scenario that trips it.

---

## Unit E — reporting truthfulness (F11 + F12)
Both are "the number is right but the report can mislead", and both are one-liners.
- **F11**: `sales.tax_invoices.discount_amount` stays 0 while the line carries the real discount. Either
  populate the header rollup or document the field as unused — but decide, because a printed document or
  an export reading the header reports no discount on a document that gave one.
- **F12**: `GET /reports/profit-loss` defaults `includeUnspecified` to false and returns all zeros for a
  company that traded, while both shipped callers (the screen at `reports/profit-loss/page.tsx:21` and
  the MCP tool at `TeasMcpTools.cs:1077`) pass true. Flip the endpoint default to match its own callers.

**Routing: Sonnet, one dispatch for both.**

---

## Unit F — the small contract gaps
- **F4**: a malformed or missing required query parameter surfaces as 500 from model binding rather than
  400. Framework-level; needs a decision about a global binding-error handler, so it is a design question
  rather than a patch.
- **`create_receipt_draft`** reads a tax invoice's status and amounts in settlement mode while gated only
  on `sales.receipt.create`, with no `sales.tax_invoice.read`. A read under a neighbouring scope, not a
  mint under the wrong one, and the document it creates always matches its policy — lowest priority.

---

## Suggested order
1. **B** (one line, contained, removes a wrong number from a screen an accountant approves payments from)
2. **D** (cheap, and de-risks the server migration, which is the next big project item)
3. **A** (the big one — design first; everything else is smaller than this)
4. **C** (needs Ham's decision on refuse-versus-warn)
5. **E**, then **F**

## Gates for the whole batch
The full suite is **1233 passed / 0 failed / 14 skipped** (Api.Tests) plus **188/188** (Domain) as of
`4988e52`; the skipped count is the baseline, and a jump means `TEAS_TEST_PG` did not apply and the run is
fake-green. Frontend gate is `npx tsc --noEmit`, never `next build` while the dev server is live.

Unit A additionally needs a **live re-run of the two broken conversions** on the local stack — a quotation
with a discounted line converted to a tax invoice, and a sales order with a discounted line converted to a
delivery order — checking the stored `discount_percent`, `tax_code`, `sales_order_line_id` and
`delivered_quantity` in the tables, not the figures on the screen. The ledger must still tie out
afterwards: trial balance balanced, AR and AP subledgers reconciling to their control accounts with zero
difference.
