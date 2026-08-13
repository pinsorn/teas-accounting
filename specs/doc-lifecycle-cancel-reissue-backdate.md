# SPEC — document lifecycle: cancel + reissue, settable doc date, receipt-only settlement

Ham /goal, 2026-08-06:
> "ต้องการให้มันแก้ invoice ใบเสร็จ ใบกำกับภาษี ที่ Post ไปแล้วได้ อาจจะเป็นการยกเลิกแล้ว Reissue ใหม่ก็ได้
> (พร้อมทำให้มัน Tracable) พร้อมทำให้เอกสารทุกอย่างสามารถกำหนดวันที่ก่อน Post ได้ แล้วก็ Invoice อะ
> เอาปุ่มลูกค้าจ่ายแล้วออกไปเลย แล้วไปใช้ใบเสร็จเป็นตัวระบุว่าชำระแล้ว"

Three features, one spec because they share the document lifecycle and two of them collide with the
in-flight R1 release. **Design only — nothing here is implemented.**

Status: **DRAFT, needs Fable/Opus design review + Ham's answers to §6 before any dispatch.**

---

## 0. Facts established in code (verified 2026-08-06, not assumed)

| Fact | Where | Why it matters |
|---|---|---|
| **Neither TaxInvoice nor Receipt has any cancel/void path.** Only BillingNote, Quotation, PaymentVoucher and PurchaseOrder have `CancelAsync`. | `BillingNoteService.cs:320`, `QuotationChainServices.cs:249`, `PaymentVoucherService.cs:427`, `PurchaseOrderService.cs:199` | Feature A is **new construction** on the two documents that matter most legally, not a relaxation of an existing guard. |
| **`DocDate` is server-pinned to `clock.TodayInBangkok()` everywhere**, and *re-pinned on edit* on VI and PO. | `BillingNoteService.cs:61,107,173` · `VendorInvoiceService.cs:68,292` · `PurchaseOrderService.cs:52,136` · `PaymentVoucherService.cs:143,181` · `JournalService.cs:50` | Feature B removes the pin. Note `VI:292` and `PO:136` carry a deliberate "§10 — re-pin on edit" rule that a previous round asked for; Feature B **reverses a past decision** and must say so out loud. |
| ⚠️ **AMENDED 2026-08-13 — the PaymentVoucher is re-pinned a SECOND time, at POST**, not only at draft-create. This row was missing and its absence would have left the bug below unfixed. | `PaymentVoucherService.cs:496-498` (`postDate = _clock.TodayInBangkok(); pv.DocDate = postDate; pv.PostingDate = postDate;`) | **In scope for Feature B, but it is NOT a simple deletion — it is a genuine conflict of two tax points, and the design must resolve it explicitly.** The re-pin is deliberate: its own comment cites §4.3 / **ม.78** so that a draft created last month and posted today lands in *this* month's period bucket and PV/WT number sequence, and the 50ทวิ `CertDate` follows it. But ภ.พ.36's tax point is **ม.83/6 — the PAYMENT**, and `WhtFilingService.cs:267` filters the reverse-charge return on this same `DocDate`. Net effect today: pay an overseas provider 30 June, post the voucher 3 July, and the liability is declared on **July's** return (due 7 August) instead of June's (due 7 July) — one month late, with เงินเพิ่ม 1.5%/month accruing. v2.0.0 made this strictly worse: before `1e46a35` the period followed the VendorInvoice's `DocDate`, which a user could set. Surfaced by the R2 Tier-2 review (L1) and traced in `specs/fix-pnd36-payment-detection.md` §1.7 / §11. **Do not "fix" it by adding a separate actual-payment-date column** — that recreates the silent GL-vs-tax divergence the F1 spec exists to close. |
| **The document number is derived FROM `DocDate`** — `SubPrefixNumberAsync("IV", bn.BusinessUnitId, bn.DocDate, …)` — and numbers are monthly (`07-2026-IV-0001`). | `BillingNoteService.IssueAsync` | **This is the trap in Feature B.** Backdating into a previous month makes the allocator mint a number in *that* month's sequence, appended after numbers already issued there — i.e. chronologically out of order. |
| **`MarkSettledAsync` exists** and is reachable from the UI. | service `BillingNoteService.cs:333` · endpoint `BillingNoteEndpoints.cs:50` · FE button `bn-mark-settled` in `invoices/[id]/page.tsx:131` + its confirm dialog | Feature C deletes exactly this. |
| Real settlement already flows from the receipt. | `BillingNoteService.cs:18` comment, `ReceiptService.cs:477` | Feature C removes a second, weaker path — it does not have to build the real one. |
| The manual-JV date contract is `docDate <= TodayInBangkok()` → `je.future_date`. | `JournalService.cs:158` | Feature B should be consistent with it unless Ham decides otherwise (§6). |

---

## 1. Feature A — cancel a posted document and reissue it, traceably

### 1.1 The distinction that must not be blurred

Thai VAT practice separates two different situations, and the system must not let one be used to do the
other's job:

- **A defect in the document itself** — wrong customer name, wrong address, wrong tax id, wrong item
  description. → **cancel the document and issue a replacement.** The original is kept, marked ยกเลิก, and
  the replacement references it.
- **A change in the value of the sale after the fact** — a discount given later, goods returned, a price
  correction. → **ใบลดหนี้ / ใบเพิ่มหนี้ (credit/debit note)**, which the system already has.

**If cancel+reissue can change amounts, it becomes a way to rewrite history and CN/DN stops being used.**
So: the replacement document may correct *descriptive* fields freely, but a change in **total amount**
must either be refused outright, or be allowed only with a loud, separately-permissioned confirmation.
**→ Ham decides, §6 Q1.**

### 1.2 States and links

Add to TaxInvoice and Receipt (and align BillingNote, which already has `Cancelled`):

- `Status = Cancelled`, plus `CancelledAt`, `CancelledBy`, `CancelReasonCode`, `CancelReason` (free text, required).
- `ReplacedByDocumentId` (nullable) — set on the cancelled document when a replacement is issued.
- `ReplacesDocumentId` (nullable) — set on the replacement, pointing back.

Rules:
- **A cancelled document keeps its number forever.** Numbers are never freed and never reused. (This is
  also why H1 — duplicate running numbers from the branch-scoped unique index — must be fixed **before or
  with** this feature; otherwise cancel+reissue multiplies an existing numbering defect.)
- The replacement is created as a **new draft**, pre-filled from the cancelled document, and gets a **new
  number** from the current sequence when it is issued. It never inherits the old number.
- Both links are shown on screen **and printed on the PDF**: the cancelled one says it was replaced and by
  which number; the replacement says which number it replaces.

### 1.3 The ledger side

The original journal entry is immutable and is never edited or deleted.

- Cancelling posts a **reversing journal entry** — the exact mirror of the original, referencing both the
  original document and the cancellation reason.
- **Which period does the reversal land in?** Consistent with the decision already taken for R1's backfill
  (`specs/research-thai-prior-period-correction.md`): if the original document's period is still **open**,
  reverse in that period; if it is **closed**, reverse in the **current open period**. Never reopen a
  closed period to do it, and never post into one.
- The replacement posts its own entry normally when issued.
- Invariant: **after cancel + reissue of an unchanged document, the net ledger effect equals the original**
  — the reversal and the replacement cancel out to the original's position, and cash never moves.

### 1.4 Guards

Cancelling a posted document is refused when:
- a **posted Receipt** applies to it (unapply or cancel the receipt first),
- a **posted CN/DN** references it,
- for a Receipt: it has already been used to settle something that has since been closed — enumerate at design review,
- the caller lacks the new scope (below).

### 1.5 Permission

A new scope, e.g. `sales.tax_invoice.cancel` / `sales.receipt.cancel`, **not** folded into `.manage` —
cancelling an issued tax document is a materially bigger action than editing a draft. Per Ham's standing
ruling, separation of duties stays permission-based (no hard creator≠canceller constraint), but the action
must be in the activity log with actor, timestamp and reason.

### 1.6 Open compliance questions — do NOT decide these in code

- **How a cancelled ใบกำกับภาษี is reported in ภ.พ.30** — netted within the same month, versus handled by
  ใบลดหนี้ across months. This has an exact RD answer that we do not have yet.
- Whether the physical/PDF original must be retained and marked in a particular way to satisfy an audit.
- Whether a cancellation that crosses a filed ภ.พ.30 requires an amended filing.

**→ These need the same treatment the prior-period question got: research + the company's CPA, before
implementation.** They are listed again in §6.

---

## 2. Feature B — set the document date before posting

### 2.1 What changes

`DocDate` becomes a caller-supplied, editable field **while the document is a draft**, on every document
type. Once issued/posted it is frozen. The "re-pin to today on edit" rule (`VI:292`, `PO:136`) is removed.

⚠️ **AMENDED 2026-08-13 — the PaymentVoucher's POST-time re-pin (`PaymentVoucherService.cs:496-498`) is
also in scope, and it is the hard part of this feature.** "Frozen once posted" is not enough on its own:
today Post *overwrites* whatever `DocDate` the draft carried, so a user who correctly backdates a
voucher to the day they actually paid an overseas provider still has that date replaced at Post. Every
downstream consumer then reads the posting day — including ภ.พ.36 (`WhtFilingService.cs:267`), whose
tax point is **ม.83/6, the payment**. Pay 30 June, post 3 July → declared on July's return, one month
late, เงินเพิ่ม 1.5%/month.

The re-pin is not an oversight, which is why this must be designed rather than deleted. Its comment
cites **ม.78** and it exists so a stale draft cannot mint a document number in a month whose sequence
has moved on, and so the 50ทวิ `CertDate` follows the post. Removing it naively re-opens the
out-of-order numbering trap this spec already identifies as its own headline risk (§0, the
`SubPrefixNumberAsync` row).

So the design must state, explicitly, **which date the document number is derived from once `DocDate`
is user-settable** — they no longer have to be the same date, and pretending they do is what forces
the conflict. Do NOT resolve this by adding a separate "actual payment date" column: that recreates
the silent GL-versus-tax divergence that `specs/fix-pnd36-payment-detection.md` exists to close. One
date, one meaning, with the numbering derived from whichever date the design nominates.

### 2.2 The bounds — this is where the design lives

Three separate constraints, all required:

1. **The date must fall inside an OPEN period.** Reuse `EnsureOpenAsync`, with the same
   two-message error contract R1 defines for payroll: a *closed* period names the reopen route; a
   never-opened future month says something else, because reopen cannot help it.
2. **Not in the future** — consistent with the manual-JV contract (`JournalService.cs:158`).
   **→ Ham confirms or overrides, §6 Q2** (a business that dates an invoice for tomorrow is not unheard of).
3. **The number-sequence consequence must be handled, not discovered.** Because the number is derived from
   `DocDate`, backdating into an earlier month appends a number to *that month's* sequence, out of
   chronological order (June gets a new number after June was finished). Options:
   - **(a) Restrict backdating to inside the current open period only.** Simple, no numbering anomaly,
     and coherent — an open month is by definition still being worked on. **Recommended.**
   - (b) Allow any open period and accept out-of-order numbering within a past month.
   - (c) Allow any open period but number from the issue date rather than the document date — decouples the
     two, at the cost of a number that no longer matches the printed date.
   **→ Ham decides, §6 Q3.** My recommendation is (a).

### 2.3 Dependency

**H1 (duplicate running numbers — the branch-scoped unique index vs a branch-blind allocator) must be
fixed first.** Backdating exercises the allocator far harder than today's always-today behaviour does, and
building on a known-broken allocator will produce defects that look like this feature's fault.

---

## 3. Feature C — delete "customer has paid"; the receipt is the only proof of settlement

### 3.1 What gets deleted

- `BillingNoteService.MarkSettledAsync` (`:333`)
- its endpoint (`BillingNoteEndpoints.cs:50`) and the `IBillingNoteService` declaration (`BillingNoteDtos.cs:73`)
- the FE button `bn-mark-settled` and its confirm dialog (`invoices/[id]/page.tsx:131`, `:208-211`)
- the now-unused i18n keys

Settlement continues to flow only from `ReceiptService`, which already sets it.

### 3.2 Why this is not a UX tidy-up — it closes a money hole R1 opens

R1 (C6) makes an issued invoice **accrue AR**. Once that ships, `MarkSettled` would flip an invoice to
settled **without crediting AR and without debiting cash** — leaving accounts receivable overstated with no
cash entry and no audit trail of a payment that never happened. The R1 spec already flagged this path as a
known hole and put it out of scope.

**→ Therefore Feature C must ship WITH R1, or immediately after it. It cannot wait for a later release.**
This is the one part of this spec that is genuinely urgent.

### 3.3 Existing data

Before deleting the path, **report** on billing notes currently `Settled` with no receipt behind them —
on Repttown (real data) there may be some. That report decides whether history is left as-is (with a note)
or needs a corrective receipt. Do not delete the endpoint before the report is read.

---

## 4. How this interacts with the R1 release already specced

| This spec | Interaction with `specs/fix-breakit-r1-ledger-integrity.md` |
|---|---|
| Feature A (cancel+reissue) | R1's WP-1 adds `billing_note.cannot_cancel_posted` — a blanket refusal to cancel a posted BN. Feature A later **relaxes that in a controlled way** (reversal + replacement). Sequence matters: R1 blocks the naive path first, then this feature adds the correct one. |
| Feature B (doc date) | Depends on **H1** (numbering), which is scheduled in R3. Cannot start before it. |
| Feature C (kill MarkSettled) | **Must ship with R1.** R1 turns it from a weak path into an active hole. |

---

## 5. Proposed release placement

- **Feature C → fold into R1** as an additional work package (it is small: delete a path, plus the
  pre-flight data report). Do not let it drift.
- **Feature A → its own release after R2**, because its open compliance questions (§1.6) need research and a
  CPA answer first, and it touches the same filing surface R2 is already fixing.
- **Feature B → after R3**, gated on H1.

---

## 6. ANSWERED by Ham — 2026-08-12. Binding.

1. **A reissued document may NOT change the total amount.** Descriptive corrections only (name, address,
   tax id, item description). A change in value goes through ใบลดหนี้/ใบเพิ่มหนี้, which already exists —
   otherwise cancel+reissue quietly replaces the credit-note mechanism and history becomes rewritable.
2. **Document date: backdating allowed only INSIDE an open period; future dates forbidden.** The document
   number is derived from `DocDate` and numbering is monthly, so backdating across a closed month would
   append a number out of chronological order. Confining it to an open period removes the anomaly
   entirely. No-future matches the manual-JV contract.
3. **Delete the "customer has paid" button NOW — it goes in R2, not later.** v1.28.0 made it an active
   money hole: pressing it marks an invoice settled without crediting AR or debiting cash. Small change
   (service + endpoint + button), but it must be preceded by a check for invoices already marked settled
   with no receipt behind them.
4. **RD form box positions: Ham validates from a rendered image.** Render the real form from prod data,
   send the picture, Ham says whether each box sits in the right place. This is the only reliable route —
   the ภ.ง.ด.1 field map already carries a "Ham visual-validation pending" note, and that unvalidated map
   is exactly what produced the wrong-row defect.
5. *(superseded)* The "settled without a receipt" data question folds into item 3's pre-flight check.

## 7. What we do next (the actual to-do list)

**Blocked right now:** 7-day quota is at 93% against Ham's own 85% full-stop rule; it resets in ~2 days.
Nothing below is dispatched yet.

1. **Ham answers §6** (5 questions; 1–3 have recommendations and can be a one-word yes).
2. **Run the research for §6 Q4** (ภ.พ.30 treatment of a cancelled tax invoice) — same pattern as the
   prior-period research: delegate the web work, Fable filters it, the CPA confirms before anything ships.
3. **Fold Feature C into the R1 spec** as a new work package, including the pre-flight "settled without a
   receipt" report. This is the only urgent piece.
4. **When quota resets — R1 implementation goes first**, in the order its spec already fixes:
   WP-1 → WP-2 (same warm worker) ; WP-3 → WP-4 → WP-5.
5. **Then** R2 (compliance filings) → R3 (guards, incl. H1 numbering) → R4 (documents/reports).
6. **Feature A design spec** after R2, once §6 Q4 is answered.
7. **Feature B implementation** after R3's H1 fix lands.

**Still running in parallel, no code needed:** the Repttown tax track — amended ภ.ง.ด.50 for the
understated years. Voluntary filing before an RD summons waives เบี้ยปรับ; เงินเพิ่ม 1.5%/month is
statutory and accrues now. Details: `specs/research-thai-prior-period-correction.md`.
