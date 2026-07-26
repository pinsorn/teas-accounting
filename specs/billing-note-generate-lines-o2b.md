# O2b — linking tax invoices generates the billing-note lines (design, Fable 2026-07-26)

Ham's decision, recorded 2026-07-26: of the three options put to him in
`specs/fix-army-findings-2026-07-22.md` §O2b, he chose **(1) linking TIs auto-generates the BN lines,
and manual lines are then an override**. Do NOT implement option (2)'s reconcile-and-block or option
(3)'s rename — they were alternatives, not additions.

The bug this closes (army leg evidence): a billing note listed ฿6,955 of linked tax invoices while its
own total read ฿107.00, because the totals are computed from manual lines only and never look at the
join table. A Thai ใบวางบิล bills the sum of the invoices it lists, so today's document asserts
something commercially false.

## Facts established in code (Fable, 2026-07-26) — do not re-derive
1. **The link already resolves amounts.** `BillingNoteService.BuildTaxInvoiceLinksAsync` turns
   `req.TaxInvoiceIds` into `BillingNoteTaxInvoice` join rows whose `AppliedAmount` defaults to the
   TaxInvoice's `TotalAmount` at link time, silently skipping TIs outside the tenant.
2. **The generate-from-a-source-document pattern already exists twice.**
   `CreateFromDeliveryOrderAsync` (~line 127) and `CreateFromSalesOrderAsync` (~line 191) both build BN
   lines directly, setting `LineAmount` / `TaxAmount` / `TotalAmount` per line and accumulating
   `bn.SubtotalAmount += l.LineAmount; bn.VatAmount += l.TaxAmount; bn.TotalAmount += l.TotalAmount;`.
   **Mirror that.** It is the proven shape and it copies amounts rather than recomputing them.
3. **`UpdateDraftAsync` already rebuilds everything on save** — it clears `bn.Lines`, zeroes the three
   totals, calls `ApplyLinesAsync(bn, req.Lines, ct)`, then clears and rebuilds `bn.TaxInvoiceLinks`.
   That is the single seam where generation belongs; `CreateDraftAsync` needs the same treatment.

## Design
### D1 — when to generate: TIs linked AND no manual lines supplied
No new flag, no new endpoint, no `generateLines` boolean. The rule is:

> if `req.TaxInvoiceIds` is non-empty **and** `req.Lines` is empty → generate one line per linked TI.
> Otherwise the caller's lines win, untouched.

That is exactly "generate for me, but let me override": link invoices and lines appear; edit them and
your edits are what get saved, because they are now non-empty on the next save. It needs no state and
cannot silently clobber an edit. Say this in a comment at the seam — the next reader will otherwise
wonder why generation is conditional.

Consequence to accept, not to fix: a draft with linked TIs whose lines the user deleted outright will
regenerate on the next save. A billing note that lists invoices and bills nothing is the defect this
item exists to remove, so regenerating is the correct behaviour.

### D2 — one line per tax invoice, at document granularity
A ใบวางบิล lists the invoices it bills, one row each — it does not restate every line of every
invoice. So generate **one BN line per linked TI**, described by the invoice
(its `DocNo`, and its `DocDate` if the line description has room), not per TI line item.

### D3 — MONEY: copy the invoice's own three amounts; never recompute VAT
Each generated line takes the TI's `SubtotalAmount` → `LineAmount`, `VatAmount` → `TaxAmount`,
`TotalAmount` → `TotalAmount`, verbatim. **Do not route generated lines through the tax-code
calculation path** (`ApplyLinesAsync`'s recompute): a tax invoice's total is VAT-inclusive already, so
recomputing VAT from it would bill VAT on VAT. Build the `BillingNoteLine` objects directly, the way
`CreateFromDeliveryOrderAsync` does.

**INVARIANT — state it, test it:** after generation,
`bn.SubtotalAmount == Σ TI.SubtotalAmount`, `bn.VatAmount == Σ TI.VatAmount`, and
`bn.TotalAmount == Σ TI.TotalAmount`, each exact at 2dp, over exactly the TIs that ended up in
`bn.TaxInvoiceLinks` (not over what the caller requested — cross-tenant ids are skipped by fact 1, and
a skipped id must not leave a line behind). The฿107-vs-฿6,955 divergence must be arithmetically
impossible after this change, not merely unlikely.

A non-VAT company's TIs carry `VatAmount == 0`; the same copy rule then yields `bn.VatAmount == 0`
without any special case. Do not add one.

### D4 — Draft only, and both write paths
Generation runs in `CreateDraftAsync` and `UpdateDraftAsync` only — every other BN edit is already
Draft-gated and an issued note is immutable. Do not touch `IssueAsync`, `CancelAsync`,
`MarkSettledAsync`, the PDF builder, or the two `CreateFrom…` methods.

### D5 — FE
`frontend/components/forms/BillingNoteForm.tsx`: when the user picks tax invoices and the line grid is
empty, save produces the lines — so after the save round-trip the grid shows them and is editable as
normal. No new control is required; if the form currently blocks saving with an empty line grid, relax
that check for the case where at least one TI is linked, and say in the UI (one short line, i18n keys
in BOTH `th.json` and `en.json`) that the lines will be generated from the selected invoices.

## Tests
- link 2 TIs, no manual lines → 2 lines; the three BN totals equal the sums of the two TIs exactly.
- link TIs **and** supply manual lines → the manual lines survive untouched; nothing is generated.
- a non-VAT company's TIs (VatAmount 0) → `bn.VatAmount == 0`, total still equals Σ TI totals.
- a requested TI id belonging to another tenant → skipped from links AND no line generated for it;
  totals still tie to the surviving links.
- generated lines on an Issued note → rejected (the existing Draft guard, pinned so it stays).
- regression: a BN with no linked TIs behaves exactly as today.

## Gates
`dotnet build`; targeted billing-note tests; `tsc` + `next build` for D5. **Fable runs the full Api
suite.** No schema change, no migration, no new endpoint, no new dependency.
Cap: `BillingNoteService` + `BillingNoteDtos` if the request shape needs it + `BillingNoteForm.tsx` +
i18n + tests. Anything beyond = stop and re-spec.
