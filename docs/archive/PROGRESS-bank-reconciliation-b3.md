# PROGRESS — Bank Reconciliation Stage B3 (K-Plus PDF adapter)

Worker checkpoint (quota-guard insurance, not an orchestrator PROGRESS file — no commit access
as a worker). Written at ~87% quota mid-B3, so a session cutoff doesn't lose the design work.

## Done so far
- `Directory.Packages.props` — added `PackageVersion Include="PdfPig" Version="0.1.15"`.
- `backend/src/Accounting.Infrastructure/Accounting.Infrastructure.csproj` — added
  `PackageReference Include="PdfPig"`. Restored successfully.
- Verified NuGet package id is `PdfPig` (not `UglyToad.PdfPig`) via `dotnet package search` —
  namespace is `UglyToad.PdfPig`, package id is `PdfPig`, 25.6M downloads, correct library.
- Explored the REAL `STM_SA5476_01FEB26_08JUL26.pdf` (password 06121996, read-only, via a
  throwaway file-based C# app in the scratchpad — never committed, script deleted from repo,
  lives only in scratchpad) to ground the D9 positional-parsing design in real word positions.
  Key findings (structure only):
  - 17 pages confirmed. EVERY page repeats the IDENTICAL header block (same X/Y positions,
    verified pages 1/2/17) and starts its transaction section with its own `ยอดยกมา` anchor row.
  - Header wraps across sub-lines: "เวลา/" + "วันที่มีผล" (time/value-date), "ยอดคงเหลือ" +
    "(บาท)" (balance) are each split across 2 Y-bands ~3-13pt apart.
  - Column X-positions derived and validated against real transaction rows: date, time, type
    ("รายการ"), amount (combined ถอนเงิน/ฝากเงิน), balance, channel ("ช่องทาง"), detail
    ("รายละเอียด") — in that LEFT-TO-RIGHT order (NOT the order the spec's prose lists them —
    real data proves channel comes AFTER balance, not between amount and type; confirms D9's
    "derive from header positions programmatically" design was correct — a hardcoded column
    order would have been wrong).
  - Column boundary derivation: midpoint between adjacent header-label anchor CENTERS, word
    classified by its own CENTER-X. Validated against every real transaction row for the
    FINANCIALLY CRITICAL columns (date/time/type/amount/balance/direction) — all classify
    correctly, some with tight-but-correct margins (~1.6pt in one case). ONE known soft edge:
    the channel/detail boundary is empirically fragile for a few detail-column lead-in words
    (e.g. "เพื่อชำระ") because the "รายละเอียด" header text sits unusually far right of where
    its own data column actually starts (data starts ~404, header text center ~469) — a
    real PDF-template quirk, not a bug in my derivation. This ONLY affects the free-text
    Channel/Description fields (cosmetic — neither feeds D10 nor B4 matching, which matches on
    amount/date only per D4). Documented, not silently swept under the rug.
  - Row clustering: ROLLING-anchor Y-tolerance (~3.5pt) correctly chains same-row word
    fragments (verified a 3.2pt spread within one real row) while cleanly separating distinct
    rows/continuation-lines (~10-12pt gaps, confirmed real).
  - Multi-line channel/description wrap CONFIRMED real (e.g. "เครื่องรูดบัตร (EDC)/" on the
    main row + "E-Commerce" wrapped ~11-14pt below, roughly midway to the NEXT transaction row)
    — requires the "does this row-band have a DATE word? no → it's a continuation, append to
    the PREVIOUS core row's channel/detail" rule (not naive Y-tolerance banding alone).
  - D9 Amount semantics resolved: `ParsedStatementLine.Amount` = the PDF's own PRINTED
    amount-cell value (parsed independently, same as KBiz CSV), NOT the delta itself — D9's
    "the parsed amount cell must equal that delta within 0.005" is describing D10's shared
    `BankStatementIntegrity.Validate` catching a REAL mismatch; if Amount were defined AS the
    delta by construction, that check would be vacuous. Direction is derived from the delta's
    SIGN only (MoneyIn if balance increased).
  - Metadata (AccountNo/period/totals/closing) label-matched the same way as KBiz CSV: for each
    known label word, search words to its right within a Y-tolerance (~6pt, validated against
    real 3.2-5.7pt label/value baseline offsets) and join in X order; first token = count
    (for the two totals rows), last token = amount/value.

## Next (not yet written to disk)
1. `backend/src/Accounting.Infrastructure/Bank/Pdf/PositionedWord.cs` (or same file as extractor)
   — the word+position DTO.
2. `backend/src/Accounting.Infrastructure/Bank/Pdf/KPlusPdfTextExtractor.cs` (B3.2) — PdfPig
   `PdfDocument.Open(stream, new ParsingOptions{Password=...})` wrapped in try/catch(Exception)
   → on ANY failure throw a FRESH `DomainException("bank.pdf_password", "Could not open the
   statement PDF — check the password.")` with NO inner exception, NO logging of the caught
   exception (verified `DomainExceptionMiddleware.cs` surfaces `ex.Message` verbatim to the
   client on both /api/v1 and BFF paths — confirms a hardcoded, caller-independent message is
   the only safe design; the generic catch-all `Exception` branch ALSO leaks `ex.Message` +
   InnerException chain in Development, which is another reason to never let the raw PdfPig
   exception escape uncaught).
3. `backend/src/Accounting.Infrastructure/Bank/Pdf/KPlusPdfLineAssembler.cs` (B3.3, PURE, no IO)
   — the algorithm designed above (column anchor derivation, row clustering, core-row vs
   continuation-band classification, metadata parsing, D9 delta-direction assembly). This is
   the biggest remaining file.
4. `backend/src/Accounting.Infrastructure/Bank/Adapters/KPlusPdfAdapter.cs` (B3.4) — thin wire:
   `AdapterCode "KPLUS_PDF"`, `CanHandle` on `.pdf`, calls Extractor then Assembler.
5. DI registration: `AddScoped<IBankStatementAdapter, KPlusPdfAdapter>()` in
   `DependencyInjection.cs` (one line, alongside the existing KBizCsvAdapter registration).
6. B3.5 — ALREADY SATISFIED by B2's own plumbing (password form field + FE conditional password
   input already exist from B2.6/B2.7; KBizCsvAdapter already ignored it, KPlusPdfAdapter will
   now consume it). No new code needed — just confirm/document in the checklist.
7. Tests (B3.6):
   - `backend/tests/Accounting.Api.Tests/Bank/KPlusPdfLineAssemblerTests.cs` — T3, SYNTHETIC
     positional word arrays (hand-built `PositionedWord` lists), covering: per-page header
     skip, ยอดยกมา carry-forward re-anchor, MoneyIn/MoneyOut via delta for both directions,
     multi-line channel/detail join, interest-with-NO-WHT, D10 balance-integrity holds end to
     end (feed through `BankStatementIntegrity.Validate` too).
   - `backend/tests/Accounting.Api.Tests/Bank/KPlusPdfTextExtractorTests.cs` — T4, decryption
     smoke test: generate a tiny password-protected PDF with PDFsharp
     (`doc.SecuritySettings.UserPassword = "..."`) in test setup — VERIFIED WORKING via a
     throwaway probe (PDFsharp-encrypted PDF opened cleanly by PdfPig with the right password;
     wrong password threw from PdfPig, confirmed via the same probe — exact PdfPig exception
     type not pinned in test assertions, only that SOME exception is thrown and my adapter
     maps it to the generic `bank.pdf_password` DomainException). Assert: right password
     extracts words successfully; wrong password → `DomainException` with code
     `bank.pdf_password` and a message that does NOT contain the attempted password string;
     capture log output (if any hook exists) and assert no leak — likely via NSubstitute
     `ILogger` capture or just asserting the extractor logs nothing at all (simplest: my design
     doesn't log anything in the catch block, so "no logger call happened" is trivially true —
     may just assert on the exception message/no thrown-exception's ToString() containing the
     password, which is the meaningful check per §Security).
8. Gates: `dotnet build`; `dotnet test --filter "FullyQualifiedName~Bank"`; full suite
   (baseline 852/0/8 + new B3 tests); frontend `tsc`+`build` only if the import modal needs a
   touch (currently believe NO FE change needed — B2.7 already conditionally shows the
   password field for `.pdf`; verify this still renders/works, no code change expected).
9. Update `specs/bank-reconciliation.md` B3.1-B3.6 checkboxes + attempt log with real-PDF
   verification evidence (structure only, no data — per the coordinator's explicit ask).
10. Report to coordinator: files, dependency, test counts, verification evidence. No commit
    (per dispatch instruction — orchestrator commits).

## Scratchpad exploration files (NOT part of the deliverable, never committed)
`Z:\temp\claude\...\scratchpad\pdfprobe.cs`, `pdfprobe2.cs`, `pdfencrypt.cs` — throwaway PdfPig/
PDFsharp exploration scripts. Safe to delete; not referenced by production code or tests.

## Files touched so far (this stage)
- `backend/Directory.Packages.props` (edit — PdfPig version pin)
- `backend/src/Accounting.Infrastructure/Accounting.Infrastructure.csproj` (edit — PdfPig ref)
