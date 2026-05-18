# Report-Backend5 — Sprint 4 Wrap (Receipt + Credit/Debit Note slice)

**Date:** 2026-05-16
**Sprint:** 4 (Receipt + CN/DN vertical slice + e2e expansion)
**Prev:** [Report-Backend4.md](./Report-Backend4.md) ·
[Answer-Sana-Question-Backend4.md](./Answer-Sana-Question-Backend4.md)
**Author:** Claude Code · **Owner:** Ham (via Sana)

---

## 1. Executive Summary

All 6 ordered Sprint-4 steps done + verified. One mid-sprint escalation
(Question-Backend4 — legal-doc model shape) resolved by Sana before any improvisation;
then built to the answers. Verification again paid off — caught a global JSON bug that
would have broken every enum-bodied endpoint.

| Gate | Result |
|---|---|
| Backend build | 0 / 0 (NU1902/1903 hard errors) |
| Backend tests | Domain 32/32 · Api 10/10 · **0 regression** |
| Frontend `tsc` | exit 0 |
| `next build` | ✓ Compiled (9 new routes) |
| Playwright e2e | **4 / 4 PASS** via system Edge (no chromium download) |
| Screenshots | 5 captured, `frontend/screenshots/` |

---

## 2. What shipped

**Backend**
- `CreditNoteReasonCode` {Typo,AmountError,CustomerInfo,Return,PriceReduce,Cancel} +
  `DebitNoteReasonCode` {PriceIncrease,AdditionalCharge,ScopeExpansion,Typo} (Q3).
- `TaxAdjustmentNote.ReasonCode` column + EF migration
  `20260516074551_AddAdjustmentReasonCode`; DTO field; validator enforces a valid code
  *for the note type*; service maps it. (`Reason` free text still legally mandatory.)
- Read surface for Receipt + CN/DN: `list` (cursor) / `detail` / `pdf` (QuestPDF) via
  `*.Read.cs` partials; endpoints extended; perms reuse existing CN/DN assertions and
  `Sales.ReceiptCreate`.
- **`JsonStringEnumConverter`** registered (`ConfigureHttpJsonOptions`) — see §3.
- Per Q1/Q2: Receipt stays **application-based** (TI-mandatory, standalone deferred);
  CN/DN stay **amount-based** (no line-level redesign).

**Frontend** (DaisyUI `teas`, RHF+Zod, TanStack Query, next-intl th/en)
- Sidebar extended (Receipts / Credit Notes / Debit Notes).
- `/receipts` list+new+detail; `/credit-notes` & `/debit-notes` list+new+detail via a
  shared `AdjustmentNoteForm` (amount-based, reasonCode dropdown, `?fromTaxInvoiceId=
  &reason=` prefill) + `AdjustmentNoteScreens` (list/detail). Receipt form is
  application-based (apply to posted TI(s)). PostConfirm reused with the legal-warning
  copy Sana specified.

## 3. Bugs caught by verification

1. **JSON enums (significant).** ASP.NET default serialises enums as **int**; the
   frontend sends `"Transfer"` / `"Credit"`. Receipt & CN create returned **400**
   (`expect(dialog)` never opened in e2e). Fixed once, globally:
   `ConfigureHttpJsonOptions(... JsonStringEnumConverter)`. This had been latent since
   the first enum-bodied DTO — no prior flow exercised it. Pure `tsc`/unit-green would
   never reveal it; the full-stack e2e did.
2. **`reason_code` migration wiring** — needed entity + config + a new EF migration;
   caught at build/migration time.
3. **Over-strict e2e assertions** (CN detail shows the original TI's *doc number*, not
   `#id`, because the TI is posted) — fixed the test, not the app.

## 4. Files

**Backend new:** `Domain/Enums/AdjustmentReasonCode.cs`,
`Application/Sales/AdjustmentReadDtos.cs`,
`Infrastructure/Sales/{ReceiptService,TaxAdjustmentNoteService}.Read.cs`,
`Migrations/20260516074551_AddAdjustmentReasonCode*.cs`.
**Backend modified:** `TaxAdjustmentNote.cs`, `TaxAdjustmentNoteDtos.cs`,
`TaxAdjustmentNoteService.cs`, `TaxAdjustmentNoteConfiguration.cs`,
`ReceiptDtos.cs`, `ReceiptService.cs`, `ReceiptEndpoints.cs`,
`TaxAdjustmentNoteEndpoints.cs`, `Program.cs` (JSON enum).
**Frontend new:** `components/forms/AdjustmentNoteForm.tsx`,
`components/AdjustmentNoteScreens.tsx`,
`app/(dashboard)/{receipts,credit-notes,debit-notes}/{page,new/page,[id]/page}.tsx`,
`e2e/{_helpers,issue-receipt,credit-note-corrects-tax-invoice,screenshots}.spec.ts`.
**Frontend modified:** `lib/{types,queries}.ts`, `components/app-shell/SidebarNav.tsx`,
`messages/{th,en}.json`, `playwright.config.ts` (system-browser channel).

## 5. Screenshots (Answer-Sana §5.4 — visual-fidelity check)

`frontend/screenshots/`: `01-dashboard.png`, `02-tax-invoice-create.png`,
`03-credit-note-create.png`, `04-number-gaps.png`, `05-tax-invoices-mobile.png`.

Eyeballed dashboard + CN-create: DaisyUI `teas` theme renders cleanly — blue primary,
readable Thai (Sarabun), card/sidebar layout per `Design(UI).md`, locked doc-date and
query-prefill working. **No visual clash to flag** (per Answer-Sana §5.4 I flag, don't
re-theme). Mobile TI-list captured at 390×844 for the responsive check.

## 6. Honest gaps / flags

1. **e2e = 4 critical paths only** (Sprint-3 discipline; no gold-plate). Not click-
   asserted: TI list filters, PDF/XML/receipt-PDF download, resend, TaxIdInput
   validation, locale toggle, DN happy path (CN tested = DN same code path, per Sana).
2. **Receipt standalone** deferred (Q1=b) — UI/model are application-based; a no-TI
   receipt path is future work (needs the advance-payment GL account decision).
3. **CN/DN amount-based** (Q2=a) — not line-level; richer per-line CN is a future slice
   if you want that UX.
4. **TH copy** Sprint-4 additions are first-pass (`rc.*`, `note.*`) — Sana's pre-merge
   sweep still recommended; no `TODO(tr)` needed (standard terms).
5. Long-path workaround still in force (`subst U:`/`W:`); `code/` canonical.

## 7. Questions for Ham / Sana

1. Next sprint: Vendor-Invoice / Payment-Voucher UI slice (reuses the same infra), or
   broaden e2e + polish (TaxId validation, downloads, DN path), or Phase-2 backend?
2. Standalone (no-TI) Receipt — schedule it? If yes I still need the advance-payment GL
   account decision from Question-Backend4 Q1(a).
3. Sana side-items (FYI, non-blocking): openapi for receipts/CN/DN + reasonCode;
   `schema.sql` `reason_code` column note.
4. e-Tax cert / ETDA registration — unchanged (~4–6 wk), inert.

## 8. Status

Sprint 4 **done done** — built, verified, e2e 4/4 on real stack, screenshots captured.
Backend 0/0 + 42 tests; frontend tsc 0 + prod build + e2e green. e-Tax inert (XAdES
round-trip green since Sprint 1). Escalation discipline intact (Question-Backend4).
Mirror synced; `code/` canonical.
