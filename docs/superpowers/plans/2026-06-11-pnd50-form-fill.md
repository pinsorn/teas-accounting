# ภ.ง.ด.50 v1 Form Fill (Phase C-C) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate the official RD ภ.ง.ด.50 (annual CIT return) PDF — page 1 header + page 2 รายการที่ 1 การคำนวณภาษี — from the CIT data layer, with the pnd51-§4 refuse-on-unrenderable posture.

**Architecture:** Mirror the proven ภ.ง.ด.51 pipeline exactly: embedded RD template + `RdAcroFormFiller` overlay (QuestPDF Thai shaping) + embedded `pnd50_cells.json` cell-centre geometry for the non-uniform comb boxes; a pure, unit-testable sheet builder (guard) + a thin `Pnd50FilingService` that assembles data from `CitYearDataService.ProfileAsync`, `cit_year_summaries`, `WhtReceivableRegister`, and `CitCalculator`; minimal-API endpoint + FE download button.

**Tech Stack:** .NET 10, PdfSharp+QuestPDF (existing `RdAcroFormFiller`), pymupdf (geometry/raster tooling), xUnit on real PG (`TEAS_TEST_PG`).

**Ground truth (do not re-derive):**
- Spec: `docs/superpowers/specs/pnd50-fieldmap-recon.md` (box map p1+p2, v1 scope)
- Radio map (RENDER-CONFIRMED, never guess): `docs/RD-Forms/pnd50/pnd50_radiomap.md`
- Field dumps: `docs/RD-Forms/pnd50/fieldmap/_pnd50_fields_p{1,2}.txt`
- Template: `docs/RD-Forms/pnd50/pnd50_050369.pdf` (7 pages, 478 widgets)
- pnd51 reference implementation: `backend/src/Accounting.Infrastructure/Pdf/Pnd51FormFiller.cs`,
  `backend/src/Accounting.Infrastructure/Tax/Pnd51FilingService.cs`,
  `backend/tests/Accounting.Api.Tests/TaxFilings/Pnd51WorksheetTests.cs`

**Environment briefing (§6 — MANDATORY for every executor):** run `dotnet` from `W:` (subst of `<repo>\backend`); kill the :5080 API before full solution builds; never `dotnet ef --no-build`; integration tests need `$env:TEAS_TEST_PG='Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true'` and run from `W:\tests\Accounting.Api.Tests`; do NOT `git commit` if you are a subagent — the main agent commits.

---

## v1 scope + legal guard (decided here, per spec §scope)

Fill: p1 header (taxid grid, name, address block, FY period, ยื่นปกติ, นิติบุคคลไทย Group00,
ม.71ทวิ Group06/07, จำนวนเงิน pair) + p2 รายการที่ 1 (THB, base radio by sign, rate radio
general/SME, boxes 661–672, Group7/8 by sign). Pages 3–7 stay blank in v1.

**Guard `pnd50.not_attestable` (ภ.ง.ด.50 §4 — a blank box asserts zero; REFUSE, never silently default):**
throw unless ALL hold:
1. caller attests `firstFiling` (ยื่นปกติ only in v1 — ยื่นเพิ่มเติม needs box 57 + Group1.Choice2)
2. caller attests `acceptBlankSchedules` (รายการที่ 2–9 will be completed manually before submission —
   v1 prints them blank)
3. `AdjustmentsTotal == 0` and `LossCarryIn == 0` (non-zero would make the blank p3 ladder a false zero)
4. not (ชำระไว้เกิน AND surcharge > 0) — box 60 เงินเพิ่ม cannot honestly coexist with an
   overpaid bottom line in the v1 rendering
5. THB books (always true today — no FX path is wired)

Sign semantics: `net = TaxBeforeCredits − CreditsTotal`; `net ≥ 0` → ชำระเพิ่มเติม
(Group7/8 = Choice1, p1 pair = `Text2000-1`+`Text3`), else ชำระไว้เกิน (Choice2, p1 pair =
`Text2000`+`Text3-2`). Base: `TaxableProfit > 0` → Group5 Choice1 + box 661 = TaxableProfit;
`TaxableProfit ≤ 0` → Group5 Choice2 + box 661 = |TaxableBeforeLoss| (loss case; tax = 0).
Surcharge (box 60/671) = `CitCalculator.UnderEstimatePenalty(estimate, actual, prepaid, schedule)`
when the store holds a ภ.ง.ด.51 estimate, else 0.

---

### Task 1: Cell-centre geometry → embedded `pnd50_cells.json`

**Files:**
- Create: `docs/RD-Forms/pnd50/fieldmap/geo.py`
- Create: `backend/src/Accounting.Infrastructure/Pdf/Templates/pnd50_main.pdf` (copy of `docs/RD-Forms/pnd50/pnd50_050369.pdf`)
- Create: `backend/src/Accounting.Infrastructure/Pdf/Templates/pnd50_cells.json`
- Check/Modify: `backend/src/Accounting.Infrastructure/Accounting.Infrastructure.csproj` (templates are embedded via `<EmbeddedResource Include="Pdf\Templates\*" />`-style glob — verify pnd50 files are covered; add explicit entries if the glob lists files individually)

- [ ] **Step 1: Write `geo.py`** — generalisation of root `_pnd51_geo.py` (same `vbounds`/`centers` functions verbatim), but:

```python
import fitz, json, sys, collections
src = 'docs/RD-Forms/pnd50/pnd50_050369.pdf'
OUT = 'docs/RD-Forms/pnd50/fieldmap/pnd50_cells.json'
PAGES = (0, 1)           # v1 fills p1+p2 only
# v1 box fields that REQUIRE cell-centre placement (comb grids):
WANT = {'1',                                  # p1 taxid 13-cell grid
        'Text2000-1', 'Text3', 'Text2000', 'Text3-2',   # p1 amount pairs
        'Text661', '662', '663', '664', '665', '666', '667', '668',
        '669', '670', '671', '672'}           # p2 รายการที่ 1 combs

# ... vbounds(page, rect) + centers(b) copied VERBATIM from _pnd51_geo.py ...

d = fitz.open(src)
flat, dupes = {}, collections.Counter()
for pi in PAGES:
    page = d[pi]
    for w in (page.widgets() or []):
        if w.field_type != fitz.PDF_WIDGET_TYPE_TEXT or w.field_name not in WANT:
            continue
        b = vbounds(page, w.rect)
        if not b: continue
        has_l = any(abs(x - w.rect.x0) < 3 for x in b)
        has_r = any(abs(x - w.rect.x1) < 3 for x in b)
        if not (has_l and has_r): continue
        c = centers(b)
        if not c: continue
        dupes[w.field_name] += 1
        flat[w.field_name] = c
missing = WANT - set(flat)
dup = [k for k, n in dupes.items() if n > 1]
print('missing (no printed grid found — OK only for plain underline fields):', sorted(missing))
print('DUPLICATE names across pages (FATAL if any):', dup)
assert not dup, 'cellCenters is keyed by field name — duplicate names would collide'
json.dump(flat, open(OUT, 'w'), ensure_ascii=False, indent=1)
print({k: len(v) for k, v in flat.items()})
```

- [ ] **Step 2: Run it** — `cd Y:\ClaudePlayground\TEAS-Project; python docs/RD-Forms/pnd50/fieldmap/geo.py`
Expected: `'1'` → 13 cells; each of `Text661`,`662`…`672` → 14 cells; `Text2000*`/`Text3*` → their printed cell counts; no duplicates. If a WANT field has no grid (plain box), drop it from WANT (it will render via /Rect like pnd51 address fields) and note it in the commit message.
- [ ] **Step 3: Copy artifacts** — `pnd50_cells.json` → `backend/src/Accounting.Infrastructure/Pdf/Templates/`; `pnd50_050369.pdf` → `…/Templates/pnd50_main.pdf`. Verify embedding: `Select-String -Path backend/src/Accounting.Infrastructure/Accounting.Infrastructure.csproj -Pattern 'Templates'` — if files are listed individually, add both; if a glob, nothing to do.
- [ ] **Step 4: Prove the resources embed** — `dotnet build W:\src\Accounting.Infrastructure -v q` then a quick check the names appear: `[System.Reflection.Assembly]::LoadFrom('W:\src\Accounting.Infrastructure\bin\Debug\net10.0\Accounting.Infrastructure.dll').GetManifestResourceNames()` should contain `Accounting.Infrastructure.Pdf.Templates.pnd50_main.pdf` + `…pnd50_cells.json`.
- [ ] **Step 5: Commit** — `git add docs/RD-Forms/pnd50/fieldmap/geo.py backend/src/Accounting.Infrastructure/Pdf/Templates/pnd50_main.pdf backend/src/Accounting.Infrastructure/Pdf/Templates/pnd50_cells.json backend/src/Accounting.Infrastructure/Accounting.Infrastructure.csproj` → `git commit -m "feat(pnd50): template + cell-centre geometry (p1+p2 combs)"`

### Task 2: `RdRadio` on-state support (pnd50 radios are Choice-named, not index-guessable)

The render-confirmed pnd50 map speaks AcroForm **on-state names** (`Group5=Choice2`), while
`RdRadio` today only supports widget index (sorted top→bottom/left→right). Selecting by on-state
removes a whole class of x-order bugs (the pnd51 flip lesson).

**Files:**
- Modify: `backend/src/Accounting.Infrastructure/Pdf/RdAcroFormFiller.cs` (record `RdRadio` line ~21 + `BuildRadioCells` line ~91 + `ReadFieldRects`/`Walk` widget collection — widgets must carry their /AP /N state keys)
- Test: `backend/tests/Accounting.Api.Tests/TaxFilings/Pnd50FormFillerTests.cs` (created in Task 4 — the on-state path is exercised there; this task adds the capability + a temporary smoke check)

- [ ] **Step 1: Extend the record** —

```csharp
/// <summary>… (existing doc) … When <paramref name="OnState"/> is given the widget is selected by its
/// AcroForm appearance on-state name (e.g. "Choice2" on ภ.ง.ด.50) instead of positional index —
/// immune to x-order ambiguity; <paramref name="WidgetIndex"/> is ignored then.</summary>
public readonly record struct RdRadio(string Name, int WidgetIndex, string? OnState = null)
{
    public RdRadio(string name, string onState) : this(name, -1, onState) { }
}
```

- [ ] **Step 2: Carry on-state names through widget collection.** In `ReadFieldRects`'s `Walk`, for each widget annotation also read its appearance dictionary: `/AP` → `/N` (a dictionary whose keys are the on-state names + `Off`). Store per-widget `string? OnStates[]` (the non-`Off` keys) next to the rect in the existing structure (`allRects` already groups same-named widgets — extend its tuple/record with the states).
- [ ] **Step 3: Resolve in `BuildRadioCells`.** When `radio.OnState != null`: pick the widget in the group whose on-states contain `radio.OnState` (exact match); throw `InvalidOperationException($"Radio '{r.Name}' has no widget with on-state '{r.OnState}'")` when absent (refuse > silently mis-tick). Otherwise keep the existing index path untouched.
- [ ] **Step 4: Build + existing tests still green** — `dotnet build W:\Accounting.sln -v q` 0/0; run `dotnet test --no-build --filter "FullyQualifiedName~Pnd51|FullyQualifiedName~Pnd1|FullyQualifiedName~Wht50"` from `W:\tests\Accounting.Api.Tests` (with `TEAS_TEST_PG`) — all pass (index path untouched).
- [ ] **Step 5: Commit** — `git commit -m "feat(pdf): RdRadio can select widgets by AcroForm on-state name"`

### Task 3: Pure sheet builder + §4 guard (TDD)

**Files:**
- Create: `backend/src/Accounting.Infrastructure/Pdf/Pnd50FormFiller.cs` (model records only in this task; `Fill` comes in Task 4)
- Create: `backend/src/Accounting.Infrastructure/Tax/Pnd50FilingService.cs` (static `BuildSheet` only)
- Create: `backend/src/Accounting.Application/Tax/IPnd50FilingService.cs`
- Test: `backend/tests/Accounting.Api.Tests/TaxFilings/Pnd50SheetTests.cs`

- [ ] **Step 1: Define the contracts** —

```csharp
// IPnd50FilingService.cs
namespace Accounting.Application.Tax;

/// <summary>ภ.ง.ด.50 §4 attestation — v1 prints รายการที่ 2–9 blank; the filer must accept that
/// + confirm a first (not amended) filing. Mirrors Pnd51Attestation.</summary>
public sealed record Pnd50Attestation(bool FirstFiling, bool AcceptBlankSchedules);

public interface IPnd50FilingService
{
    /// <summary>Build the v1 ภ.ง.ด.50 PDF for the fiscal year. <paramref name="isSme"/> null ⇒ auto
    /// from CitProfile (paid-up ≤5M ∧ revenue ≤30M). Throws pnd50.not_attestable per §4 guard.</summary>
    Task<byte[]> BuildPnd50Async(
        int year, bool? isSme, bool hasRelatedPartyOver200M,
        Pnd50Attestation? attest, CancellationToken ct);
}
```

```csharp
// in Pnd50FormFiller.cs — the page-2 รายการที่ 1 figures, all derived by Pnd50FilingService.BuildSheet
public sealed record Pnd50Sheet(
    decimal BaseAmount,      // box 48-49 (Text661): TaxableProfit, or |TaxableBeforeLoss| on the loss path
    bool    IsLoss,          // Group5: false→Choice1 กำไรสุทธิ, true→Choice2 ขาดทุนสุทธิ
    decimal TaxComputed,     // box 50-51 (662) = CitComputation.TaxBeforeCredits
    decimal WhtCredit,       // box 54 (665)
    decimal Pnd51Prepaid,    // box 55 (666)
    decimal CreditsTotal,    // รวม (669) = 665 + 666 (663/664/667/668 are 0 in v1 scope)
    decimal NetAmount,       // box 58-59 (670) = |TaxBeforeCredits − CreditsTotal|
    bool    PayMore,         // Group7/Group8 Choice1 ชำระเพิ่มเติม vs Choice2 ชำระไว้เกิน + p1 pair
    decimal Surcharge,       // box 60 (671) — ม.67ตรี UnderEstimatePenalty (0 when none)
    decimal TotalAmount,     // box 61-62 (672) = NetAmount + Surcharge (PayMore) / NetAmount (overpaid)
    bool    IsSme);          // Group21: false→Choice1 ทั่วไป, true→Choice2 + Group6 Choice1 SMEs
```

```csharp
// in Pnd50FilingService.cs
public static Pnd50Sheet BuildSheet(
    CitComputation cit, decimal surcharge, bool isSme, Pnd50Attestation? attest)
{
    if (attest is not { FirstFiling: true, AcceptBlankSchedules: true })
        throw new DomainException("pnd50.not_attestable",
            "ภ.ง.ด.50 v1 prints รายการที่ 2–9 blank (a blank box asserts zero) — the filer must attest "
          + "firstFiling + acceptBlankSchedules, or complete the full form manually.");
    if (cit.AdjustmentsTotal != 0m || cit.LossApplied != 0m || cit.TaxableBeforeLoss != cit.TaxableProfit)
        throw new DomainException("pnd50.not_attestable",
            "Non-zero ม.65ทวิ/ตรี adjustments or loss carry-forward require the รายการที่ 2 ladder "
          + "(page 3), which v1 does not render — blank would assert zero.");

    var net     = cit.TaxBeforeCredits - cit.CreditsTotal;
    var payMore = net >= 0m;
    if (!payMore && surcharge > 0m)
        throw new DomainException("pnd50.not_attestable",
            "เงินเพิ่ม (ม.67ตรี) with an overpaid bottom line is not renderable in v1.");

    var isLoss = cit.TaxableProfit <= 0m && cit.AccountingProfit < 0m;
    return new Pnd50Sheet(
        BaseAmount:   isLoss ? Math.Abs(cit.TaxableBeforeLoss) : cit.TaxableProfit,
        IsLoss:       isLoss,
        TaxComputed:  cit.TaxBeforeCredits,
        WhtCredit:    /* whtSuffered component */ cit.CreditsTotal - /* prepaid */ 0m, // see Step 2 note
        Pnd51Prepaid: 0m,
        CreditsTotal: cit.CreditsTotal,
        NetAmount:    Math.Abs(net),
        PayMore:      payMore,
        Surcharge:    payMore ? surcharge : 0m,
        TotalAmount:  Math.Abs(net) + (payMore ? surcharge : 0m),
        IsSme:        isSme);
}
```

**Note for Step 2:** `CitComputation` only exposes `CreditsTotal` — pass the two credit components
(`whtSuffered`, `pnd51Prepaid`) into `BuildSheet` as explicit parameters and fill `WhtCredit`/`Pnd51Prepaid`
from them (signature: `BuildSheet(CitComputation cit, decimal whtSuffered, decimal pnd51Prepaid, decimal surcharge, bool isSme, Pnd50Attestation? attest)`;
assert `whtSuffered + pnd51Prepaid == cit.CreditsTotal` inside — mismatch = caller bug → `InvalidOperationException`).

- [ ] **Step 2: Write the failing tests** (`Pnd50SheetTests.cs`, plain `[Fact]`s — pure function, no DB; mirror `Pnd51WorksheetTests` style). Cases (use `CitCalculator.Compute` to build inputs — never hand-roll a `CitComputation`):
  1. `No_attestation_throws` / `Partial_attestation_throws` (Theory over both flags) → code `pnd50.not_attestable`
  2. `Nonzero_adjustments_throws` (Compute with adjustmentsTotal: 1000m)
  3. `Loss_carry_forward_throws` (Compute with lossCarryIn: 500m on a profitable year)
  4. `Clean_profit_pay_more_foots`: profit 1,000,000, WHT 5,000, prepaid 10,000, general →
     `IsLoss=false`, `BaseAmount=1_000_000`, `TaxComputed=200_000`, `CreditsTotal=15_000`,
     `NetAmount=185_000`, `PayMore=true`, `TotalAmount=185_000+surcharge`
  5. `Clean_loss_overpaid`: accounting −200,000, WHT 3,000 → `IsLoss=true`, `BaseAmount=200_000`,
     `TaxComputed=0`, `NetAmount=3_000`, `PayMore=false`, `Surcharge=0`, `TotalAmount=3_000`
  6. `Overpaid_with_surcharge_throws` (loss + surcharge 100) → `pnd50.not_attestable`
  7. `Credit_component_mismatch_is_a_caller_bug` → `InvalidOperationException`
- [ ] **Step 3: Run to verify they fail** — from `W:\tests\Accounting.Api.Tests`: `dotnet build W:\Accounting.sln -v q; dotnet test --no-build --filter "FullyQualifiedName~Pnd50SheetTests"` → all FAIL (types missing → write skeletons; then asserts fail).
- [ ] **Step 4: Implement `BuildSheet` until green** (final signature from the Step-1 note).
- [ ] **Step 5: Run 2× consecutive** — same filter, twice; both green (pure tests — trivially stable, the discipline still applies).
- [ ] **Step 6: Commit** — `git commit -m "feat(pnd50): Pnd50Sheet builder + §4 refuse-on-unrenderable guard (TDD)"`

### Task 4: `Pnd50FormFiller.Fill`

**Files:**
- Modify: `backend/src/Accounting.Infrastructure/Pdf/Pnd50FormFiller.cs`
- Test: `backend/tests/Accounting.Api.Tests/TaxFilings/Pnd50FormFillerTests.cs`

- [ ] **Step 1: Model** —

```csharp
public sealed record Pnd50Model(
    string TaxId, string CompanyName,
    DateOnly PeriodStart, DateOnly PeriodEnd,
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Road,
    string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? Website, string? Email,
    bool HasRelatedPartyOver200M,     // Group06 (มี) vs Group07 (ไม่มี/≤200M)
    Pnd50Sheet Sheet);
```

- [ ] **Step 2: `Fill(Pnd50Model m)`** — mirror `Pnd51FormFiller.Fill` structure exactly (Lazy `CellCenters` from `pnd50_cells.json`, `Template("pnd50_main.pdf")`, `Cells` test hook). Field mapping (spec §p1/§p2 + radiomap; amounts = `Amt`-style 14-cell comb `{baht:0}{satang:00}` right-justified, **comma-free**):
  - p1 text: `1`=TaxID digits (cellCenters) · `2`=CompanyName · address `3`..`15`+`Text10.1` —
    take the box→role assignment from `_pnd50_fields_p1.txt` label join (อาคาร/ห้อง/ชั้น/หมู่บ้าน/
    เลขที่/หมู่/ตรอกซอย/ถนน/ตำบล/อำเภอ/จังหวัด/รหัสไปรษณีย์/โทรศัพท์), and TREAT AS PROVISIONAL
    until the Task-5 raster confirms each one (pnd51 lesson: the whole block was off by one) ·
    `17/18/19`=start D/M/พ.ศ. · `20/21/22`=end D/M/พ.ศ. (BE year = CE+543, day/month "00" format) ·
    `166.1`=Website · `166.2`=Email
  - p1 radios (ALL by OnState): `Group1`→`Choice1` ยื่นปกติ · `Group00`→`Choice1` นิติบุคคลไทย ·
    `m.HasRelatedPartyOver200M ? Group06→Choice1 : Group07→Choice1` (tick exactly ONE of the two groups)
  - p1 amount pair from `Sheet`: PayMore → `Text2000-1`=baht + `Text3`=satang; else `Text2000`+`Text3-2`
    (fill exactly one pair, both Right: true)
  - p2 text: `Text661`=BaseAmount · `662`=TaxComputed · `665`=WhtCredit · `666`=Pnd51Prepaid ·
    `669`=CreditsTotal · `670`=NetAmount · `671`=Surcharge (only when > 0) · `672`=TotalAmount
    (boxes 663/664/667/668 stay blank — v1 guard guarantees they are truly zero… **NO**: blank ≠ 0 on
    this form — but these are "หัก" sub-lines whose blank-when-unused is how RD examples render the
    form; the รวม box 669 carries the true total. This matches the pnd51 precedent for unused credit lines.)
  - p2 radios (by OnState): `Group4`→`Choice1` บาท · `Group5`→ IsLoss ? `Choice2` : `Choice1` ·
    IsSme ? (`Group21`→`Choice2` + `Group6`→`Choice1`) : `Group21`→`Choice1` ·
    `Group7`/`Group8`→ PayMore ? `Choice1` : `Choice2`
- [ ] **Step 3: Structural tests** (`Pnd50FormFillerTests.cs`, plain `[Fact]` — no DB):
  1. `Fill_renders_nonempty_pdf` — model with full sheet → bytes start `%PDF`, length > 50_000
  2. `Cells_geometry_loads_and_has_the_v1_combs` — `Pnd50FormFiller.Cells` contains `"1"` (13 centres) and `"Text661"`,`"662"`,`"670"`,`"672"` (14 centres each)
  3. `Loss_overpaid_variant_renders` — IsLoss/overpaid sheet → renders (different radio set)
  4. `Sme_variant_renders` — IsSme=true
  5. `Unknown_onstate_throws` — temporary model fill with a deliberately wrong OnState via direct
     `RdAcroFormFiller.Render(template, [], [new RdRadio("Group5", "ChoiceX")], null)` → `InvalidOperationException` (proves Task-2 refuse path)
- [ ] **Step 4: Build + run** — filter `Pnd50`, 2× green.
- [ ] **Step 5: Commit** — `git commit -m "feat(pnd50): form filler p1 header + p2 รายการที่ 1 (v1)"`

### Task 5: Visual gate (render-confirm EVERY tick/box before the service ships)

**Files:**
- Create: `docs/RD-Forms/pnd50/fieldmap/render_check.py` (raster + crops)
- Create: `_review/pnd50/` PNGs (gitignored review artifacts)

- [ ] **Step 1: Emit a worked-case PDF** — tiny xunit fact (or `dotnet script`) calling
  `Pnd50FormFiller.Fill` with the Task-3 case-4 numbers + a fully-populated address, writing
  `_review/pnd50/pnd50_case4.pdf` (and the loss variant `pnd50_case5.pdf`). Distinct digits per box
  (e.g. base 1,234,567.89) so cell alignment errors are visible.
- [ ] **Step 2: Raster** — `render_check.py`: pymupdf opens each PDF, renders p1+p2 at 200 dpi full +
  crops per box/radio (taxid grid, address block, period, each 66x box, every ticked radio,
  p1 amount pair) → `_review/pnd50/*.png`.
- [ ] **Step 3: READ every crop** (the executor uses the Read tool on each PNG): digits centred in
  their printed cells, ticks inside the right circle per `pnd50_radiomap.md`, address strings in the
  right boxes. Fix the provisional p1 address mapping here if the label-join was off (expected
  failure mode — pnd51 was off by one).
- [ ] **Step 4: Send crops to Ham** (SendUserFile, proactive) + hold the endpoint task until visual
  sign-off **or** (overnight mode) record "pending Ham visual review" in progress.md and proceed —
  the §4 guard + structural tests make the service safe to wire; only RD submission needs the sign-off.
- [ ] **Step 5: Commit** — `git add docs/RD-Forms/pnd50/fieldmap/render_check.py` → `git commit -m "test(pnd50): visual gate tooling + worked-case rasters"`

### Task 6: `Pnd50FilingService` + endpoint + OpenAPI + FE

**Files:**
- Modify: `backend/src/Accounting.Infrastructure/Tax/Pnd50FilingService.cs` (add the async service around `BuildSheet`)
- Modify: `backend/src/Accounting.Infrastructure/DependencyInjection.cs` (register `IPnd50FilingService` next to `IPnd51FilingService`)
- Modify: `backend/src/Accounting.Api/Endpoints/TaxFilingEndpoints.cs` (after the pnd51 endpoints, line ~117)
- Modify: `docs/api/openapi.yaml` (mirror `/tax-filings/pnd51/pdf` entry at line ~963)
- Modify: `frontend/app/(dashboard)/tax-filings/cit/page.tsx` (or its client component — find the pnd51
  download button and add the pnd50 one beside it) + `frontend/messages/th.json` + `frontend/messages/en.json`
- Test: `backend/tests/Accounting.Api.Tests/TaxFilings/Pnd50FilingServiceTests.cs`

- [ ] **Step 1: Service** — constructor `(AccountingDbContext db, ITenantContext tenant, ICitYearDataService citData, IWhtReceivableReportService whtReport)`:

```csharp
public async Task<byte[]> BuildPnd50Async(
    int year, bool? isSme, bool hasRelatedPartyOver200M, Pnd50Attestation? attest, CancellationToken ct)
{
    var c = await db.Companies.AsNoTracking()
        .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct)
        ?? throw new DomainException("company.not_found", "Company not found.");
    var prof = await db.CompanyProfiles.AsNoTracking()
        .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct);

    var startMonth  = (int)c.FiscalYearStartMonth;
    var periodStart = new DateOnly(year, startMonth, 1);
    var periodEnd   = periodStart.AddMonths(12).AddDays(-1);

    var profile = await citData.ProfileAsync(year, ct);          // SME flag, adjustments Σ, loss c/f, accounting NP
    var years   = await citData.ListYearsAsync(ct);
    var summary = years.FirstOrDefault(y => y.FiscalYear == year);
    var prepaid  = summary?.Pnd51Prepaid ?? 0m;
    var estimate = summary?.Pnd51EstimatedProfit;

    var whtFy = (await whtReport.GetRegisterAsync(periodStart, periodEnd, ct)).TotalWht;

    var sme      = isSme ?? profile.IsSme;
    var schedule = sme ? CitRateSchedule.Sme() : CitRateSchedule.General();
    var accountingNp = summary?.EffectiveNetProfit ?? profile.AccountingNetProfit;

    var cit = CitCalculator.Compute(accountingNp, profile.AdjustmentsTotal, profile.LossCarryIn,
                                    prepaid, whtFy, schedule);
    var actualTaxable = cit.TaxableProfit;
    var surcharge = estimate is { } est
        ? CitCalculator.UnderEstimatePenalty(est, actualTaxable, prepaid, schedule) : 0m;

    var sheet = BuildSheet(cit, whtFy, prepaid, surcharge, sme, attest);

    var model = new Pnd50Model(
        TaxId: prof?.TaxId ?? c.TaxId, CompanyName: prof?.LegalName ?? c.NameTh,
        PeriodStart: periodStart, PeriodEnd: periodEnd,
        Building: prof?.RegBuilding, RoomNo: prof?.RegRoomNo, Floor: prof?.RegFloor,
        Village: prof?.RegVillage, HouseNo: prof?.RegHouseNo, Moo: prof?.RegMoo,
        Soi: prof?.RegSoi, Road: prof?.RegStreet, SubDistrict: prof?.RegisteredSubdistrict,
        District: prof?.RegisteredDistrict, Province: prof?.RegisteredProvince,
        PostalCode: prof?.RegisteredPostalCode,
        Website: prof?.Website, Email: prof?.ContactEmail,     // verify property names on CompanyProfile; null is fine
        HasRelatedPartyOver200M: hasRelatedPartyOver200M,
        Sheet: sheet);
    return Pnd50FormFiller.Fill(model);
}
```

(`CitRateSchedule`/`CitCalculator` live in `Accounting.Domain.Tax` — same usings as `Pnd51FilingService`.
If `CompanyProfile` has no Website/Email properties, pass `null` — boxes stay blank, legally neutral.)

- [ ] **Step 2: Endpoint** (TaxFilingEndpoints, after pnd51; same auth/permission style as the pnd51 GET — copy its `RequireAuthorization` policy verbatim):

```csharp
app.MapGet("/tax-filings/pnd50/pdf", async (
    int year, bool? isSme, bool? hasRelatedParty,
    bool? firstFiling, bool? acceptBlankSchedules,
    IPnd50FilingService svc, CancellationToken ct) =>
{
    var attest = (firstFiling == true || acceptBlankSchedules == true)
        ? new Pnd50Attestation(firstFiling ?? false, acceptBlankSchedules ?? false)
        : null;
    var pdf = await svc.BuildPnd50Async(year, isSme, hasRelatedParty ?? false, attest, ct);
    return Results.File(pdf, "application/pdf", $"pnd50-{year}.pdf");
}).RequireAuthorization(/* same policy as pnd51/pdf */);
```

- [ ] **Step 3: Integration tests** (`Pnd50FilingServiceTests.cs`, `[Collection(nameof(PostgresCollection))]` + `StubTenant` Provider pattern copied from `Pnd51FilingServiceTests`):
  1. `Pnd50_renders_valid_pdf_for_attested_clean_year` — seed nothing exotic: company 1, a fiscal year with zero adjustments; call with attest → `%PDF` bytes
  2. `Pnd50_without_attestation_throws_not_attestable`
  3. `Pnd50_with_adjustments_throws_not_attestable` — create an adjustment via `ICitYearDataService.CreateAdjustmentAsync` for a **fresh fiscal year** (use a far-future year per the `FreshYearAsync` pattern in `PayrollRunServiceTests` — shared DB!), expect refuse, then delete it
- [ ] **Step 4: OpenAPI** — copy the pnd51 path object; document `pnd50.not_attestable` 422 + the five params.
- [ ] **Step 5: FE** — in the CIT tax-filings page, add "ดาวน์โหลด ภ.ง.ด.50 (PDF)" next to the pnd51 button: year selector reuse, two attestation checkboxes (ยื่นครั้งแรก / รับทราบว่ารายการที่ 2–9 ต้องกรอกเอง), related-party checkbox, link `GET /api/proxy/tax-filings/pnd50/pdf?...`. Add th/en message keys (`taxFilings.pnd50.*`). `tsc --noEmit` → 0.
- [ ] **Step 6: Full gates** — kill :5080 if running → `dotnet build W:\Accounting.sln` 0/0 → Api.Tests full suite 2× on `TEAS_TEST_PG` → Domain suite → FE `tsc --noEmit` 0.
- [ ] **Step 7: Commit** — `git commit -m "feat(pnd50): filing service + GET /tax-filings/pnd50/pdf + FE download (v1 p1+p2)"` → push.

### Task 7: Bookkeeping

- [ ] Tick NEXT-SESSION.md item 2 + prepend `progress.md` (test counts, visual-gate status "pending Ham review"), update `plan.md`.
- [ ] `grep -rln "ম"` on touched files (Bengali glyph check) before the commit.

---

## Self-review notes

- Spec coverage: geometry (spec method §2) → Task 1; filler (§3) → Tasks 3-4; visual gate (§4) → Task 5; service+endpoint+FE (§5) → Task 6. Radio on-state capability is an addition required by the Choice-named pnd50 map.
- The p1 address box→role mapping is intentionally PROVISIONAL until Task-5 rasters confirm (pnd51 off-by-one lesson) — that is the designed verification, not a placeholder.
- Type check: `BuildSheet(CitComputation, decimal whtSuffered, decimal pnd51Prepaid, decimal surcharge, bool isSme, Pnd50Attestation?)` is the single signature used in Tasks 3 and 6.
