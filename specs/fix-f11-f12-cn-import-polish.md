# Fix F-11 + F-12 — CN reason i18n + import/CN UX polish

Source: REPORT-vat-dummy-test.md §Open-items round (2026-07-19). Ham approved
"เอาตามที่แนะนำเลย". Origin round evidence: co5 prod v1.22.1, CN-0001, import #1.

## F-11 (Medium) — CN reason code renders as raw enum key, incl. on the printed legal doc
- [x] F-11.1 Reason dropdown on /credit-notes/new shows raw enum keys
      (Typo / AmountError / CustomerInfo / Return / PriceReduce / Cancel) on the
      Thai UI. Map to Thai labels (suggested, verify against ม.86/10 common
      practice already used elsewhere in repo if any):
      Typo → พิมพ์ผิด/คำนวณผิด, AmountError → จำนวนเงินผิด,
      CustomerInfo → ข้อมูลลูกค้าผิด, Return → รับคืนสินค้า,
      PriceReduce → ลดราคา/ส่วนลดภายหลัง, Cancel → ยกเลิกรายการ.
      EN locale keeps readable English labels (not bare enum keys).
      Evidence: `frontend/messages/{th,en}.json` `note.reasons.*` (9 keys —
      the 6 CN codes + 3 DN-only codes, `Typo` shared). Dropdown in
      `AdjustmentNoteForm.tsx` now renders `t(\`reasons.${r}\`)` instead of the
      raw code `r`. Same dropdown/form serves both `/credit-notes/new` and
      `/debit-notes/new` (noteType prop) — both fixed together.
- [x] F-11.2 The POSTED document line prints "เหตุผล (PriceReduce): <text>" —
      the raw key must not appear on the legal document. Use the Thai label
      (or omit the code entirely and keep only the free-text reason — pick
      whichever the existing document template convention supports with the
      smaller diff; the reason CODE must still be stored unchanged in DB).
      Check BOTH renderers: FE live preview AND the PDF/print output — they
      must match.
      Evidence: both renderers already shared ONE source —
      `TaxAdjustmentNoteService.BuildPaperAsync` (Read.cs) builds the
      `PaperDocModel` consumed by (a) `GET /paper` → FE `AdjustmentNoteScreens`
      posted-detail view and (b) `BuildPdfAsync` → `PaperDocumentPdf.Render`
      for the PDF — same model, same string, so fixing the one line fixes
      both by construction (no separate PDF-only template exists). Changed
      `เหตุผล ({d.ReasonCode}): ...` → `เหตุผล ({DocumentLabels.AdjustmentReasonLabel(d.ReasonCode)}): ...`.
      Added `DocumentLabels.AdjustmentReasonLabel` (display-only lookup dict,
      falls back to the raw code defensively) — `TaxAdjustmentNote.ReasonCode`
      DB column is untouched (still stores the enum name). Also updated the
      one e2e test (`credit-note-corrects-tax-invoice.spec.ts`) that had been
      asserting the RAW key `'AmountError'` was visible on the posted CN
      detail page — that assertion was literally pinning this bug; now
      asserts the Thai label `จำนวนเงินผิด` shows and `AmountError` does not.
- [x] F-11.3 Same check for debit notes (ใบเพิ่มหนี้) — if the DN form/doc
      shares the reason-code component/template, fix it in the same pass
      (shared component = shared fix; do NOT fork a separate copy).
      Evidence: DN uses the exact same `AdjustmentNoteForm.tsx` (dropdown fix)
      and the exact same `TaxAdjustmentNoteService.BuildPaperAsync` (document
      line fix, `noteType` switches only the title/legal-ref, not the reason
      composition) — one shared fix covers both, no fork. `DocumentLabels`
      label dict also covers the 3 DN-only codes (PriceIncrease,
      AdditionalCharge, ScopeExpansion).

## F-12 (Low) — UX polish batch
- [x] F-12.1 Statement-import modal (/bank-accounts/[id]): add a one-line hint
      of the accepted formats: "รองรับไฟล์ CSV จาก KBiz (KBank) และ PDF จาก
      K PLUS" — derive the exact wording from the registered adapters
      (KBizCsvAdapter .csv, KPlusPdfAdapter .pdf); keep it generic enough that
      a future adapter doesn't make it wrong (e.g. list adapters dynamically
      only if an endpoint already exposes them — otherwise static text is fine,
      Ponytail).
      Evidence: confirmed no endpoint exposes the adapter list
      (`Accounting.Application/Bank/StatementAdapterContracts.cs` +
      DI registration only, no `GET`) → static text per spec's own Ponytail
      call. Added `bank.importFormatHint` key (TH exact wording from spec; EN
      "Accepts CSV from KBiz (KBank) or PDF from K PLUS") + one `<p>` line in
      `StatementImportSection.tsx`'s upload modal.
- [x] F-12.2 CN post-confirm dialog identifies the referenced doc as "TI #1"
      (internal id). Show the doc number (07-2026-TI-0001) instead — the form
      already holds the selected TI's display label.
      Evidence: `AdjustmentNoteForm.tsx` now calls `useTaxInvoice(originalTiId)`
      (existing hook, `lib/queries.ts`) and feeds
      `originalTi.data?.docNo ?? \`TI #${originalTiId}\`` into the
      `PostConfirmDialog` summary (falls back to the old id-based string only
      while the TI detail hasn't loaded yet). Shared form → fixes CN AND DN
      confirm dialogs together (same bug, same file).
- [x] F-12.3 Recon match-confirm toast is a bare "บันทึก" — change to
      "จับคู่รายการแล้ว" (match) / keep unmatch toast consistent
      ("ยกเลิกจับคู่แล้ว" if it's also bare).
      Evidence: `imports/[importId]/page.tsx` — unmatch was indeed bare
      `tc('save')` ("บันทึก"); added `bank.matchSuccess` ("จับคู่รายการแล้ว")
      and `bank.unmatchSuccess` ("ยกเลิกจับคู่แล้ว"), wired into the
      match-confirm (`SuggestModal.confirm`) and `doUnmatch` toasts
      respectively. `doIgnore`/`doUnignore`/postJournal toasts intentionally
      left as generic `tc('save')` — not named in scope, smaller diff.

## Gates
- [x] tsc --noEmit + next build pass — both clean (see attempt log)
- [x] grep "ম" over all changed files = 0 hits
- [x] If BE files changed: dotnet build + affected test class(es) green —
      build 0 warn/0 err; CnDnGlBalanceTests + Pnd30CorrectnessTests +
      SubledgerReportTests 19/19 passed, 0 skipped; broader
      Sales/Note/Pdf-tagged sweep 164/164 passed, 0 skipped
- [x] i18n: no raw enum key visible in TH or EN locale for CN/DN reason —
      dropdown now keys off `note.reasons.*`; th.json/en.json key-path parity
      verified 0/0 diff (script in attempt log)
- [x] Attempt log below

## Attempt log
- Located the reason enum: `backend/src/Accounting.Domain/Enums/AdjustmentReasonCode.cs`
  (`CreditNoteReasonCode`, `DebitNoteReasonCode`, both stored as `string?
  ReasonCode` on `TaxAdjustmentNote` — DB storage untouched by this fix).
- Traced BOTH F-11.2 renderers back to ONE shared source:
  `TaxAdjustmentNoteService.BuildPaperAsync` (Read.cs) — the same
  `PaperDocModel` feeds `GET /paper` (FE `AdjustmentNoteScreens` detail view)
  AND `BuildPdfAsync`/`PaperDocumentPdf.Render` (PDF). One-line fix,
  no template fork.
- Found the create-form dropdown + CN/DN post-confirm dialog are BOTH the
  same shared `AdjustmentNoteForm.tsx` (`noteType` prop switches CN/DN) — so
  F-11.1, F-11.3, and F-12.2 all land in one file with no duplication.
- Confirmed no `GET /adapters`-style endpoint exists for F-12.1 → static hint
  text is the correct (Ponytail) choice per the spec's own fallback clause.
- Found `frontend/e2e/credit-note-corrects-tax-invoice.spec.ts` line 31 was
  asserting `toContainText('AmountError')` on the posted CN detail page —
  i.e. a regression test that had been PINNING the F-11.2 bug. Updated it to
  assert the Thai label instead + assert the raw key is gone, so it now
  guards the fix rather than the bug.
- Backend: added `DocumentLabels.AdjustmentReasonLabel(string?)` (private
  Dictionary<string,string> + public lookup, defensive fallback to the raw
  code for any unmapped value — never blank). Kept it in the existing
  `DocumentLabels` value object (already the single pure resolver for
  VAT-mode-dependent doc labels; same file already `using`'d by the caller).
- Frontend i18n: added `note.reasons.*` (9 keys: 6 CN + 3 DN-only, `Typo`
  shared) to both `th.json`/`en.json`; added `bank.importFormatHint`,
  `bank.matchSuccess`, `bank.unmatchSuccess` to both.
- F-12.2: used the existing `useTaxInvoice(id)` query hook (already in
  `lib/queries.ts`) rather than threading new state through
  `TaxInvoicePicker` — smaller diff, always in sync with `originalTaxInvoiceId`
  however it was set (URL prefill or manual pick).
- Gate evidence:
  - `npx tsc --noEmit` → clean, no output/errors.
  - `npx next build` → "Compiled successfully", all 84 routes generated
    including `/credit-notes/new`, `/credit-notes/[id]`, `/debit-notes/*`,
    `/bank-accounts/[id]`, `/bank-accounts/[id]/imports/[importId]`.
  - `grep -rn "ম"` over all 8 changed files → exit 1 (0 matches).
  - `node -e "JSON.parse(...)"` on both message files → OK.
  - th.json/en.json key-path diff script (recursive key flatten + Set diff)
    → `only in th.json: 0`, `only in en.json: 0` (full parity, not just the
    new keys).
  - Backend: `dotnet build src/Accounting.Api/Accounting.Api.csproj` → Build
    succeeded, 0 warnings, 0 errors.
  - `dotnet test --filter "FullyQualifiedName~CnDnGlBalanceTests|
    FullyQualifiedName~Pnd30CorrectnessTests|FullyQualifiedName~SubledgerReportTests"`
    (with `TEAS_TEST_PG` set per troubles-wiki.md's current connection
    string — Postgres 18, port 5432, user `accounting`) → 19 passed, 0
    failed, 0 skipped.
  - Broader safety sweep `dotnet test --filter
    "FullyQualifiedName~Sales|FullyQualifiedName~Note|FullyQualifiedName~Pdf"`
    → 164 passed, 0 failed, 0 skipped.
- No live browser smoke test performed (no dev stack was already running;
  spinning up Postgres+API+Next+login+seeded CN/DN/bank-import data was judged
  disproportionate to a label/i18n-only change already covered by tsc, next
  build, an updated e2e assertion, and 164 green backend tests touching the
  exact code paths changed). Flagging this explicitly rather than silently
  skipping it, per verification-before-completion discipline — orchestrator
  may want a follow-up live pass before this reaches a user-facing demo.
