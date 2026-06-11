# ภ.ง.ด.50 Phase C-D — p4/p5/p7 Schedules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement
> this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the ภ.ง.ด.50 fill from p1–p3+p6 to the p4/p5 detail schedules (รายการที่ 4–8)
and the p7 declaration header, sourced from GL expense classification + cit_adjustments,
with the same refuse-on-unrenderable + foot-to-ladder discipline as v2.

**Architecture:** Same single-source pipeline: `ComposeAsync` derives two new pure schedule
records (`Pnd50ExpenseSchedule` from per-account FY expense rows; `Pnd50DisallowedSchedule`
from positive cit_adjustments), both foot-checked against the p3 ladder (caller-bug throw →
collected refusal). `Pnd50FormFiller` prints them column-③-only with explicit zeros (v2
posture: blank box = lie). p4 sections print ladder-tied zeros (TEAS has no inventory/COGS,
no other income/expense split). p7 gets company name + period only — declaration answers and
signatures are the director's, never auto-filled.

**Tech Stack:** .NET 10 / xUnit / pymupdf (geometry recon) / existing `RdAcroFormFiller`.

**Scope decisions (spec: pnd50-cd-attachments-kickoff.md, Ham "ลุยเลย" 2026-06-12):**
- ใบแนบ ก–จ separate PDFs: NOT auto-filled in C-D v1 (market fills none; nothing trivially
  derivable beyond what p4/p5 already show). Stay attest-blank via `AcceptBlankSchedules`.
- ม.71ทวิ disclosure: informational refusal `pnd50.disclosure_required` when FY revenue
  > ฿200M — points user to manual TP-disclosure filing. Never auto-filled.
- รายจ่าย classification: account-code convention (below), unmapped → "อื่นๆ" line 22.
  Misclassification between lines never changes tax (total is the ladder row 8 invariant).

**Form structure facts (from _scratch recon, 2026-06-12):**
- p4 = รายการที่ 4 ต้นทุนผลิต (17 lines, margins 86–99) + รายการที่ 5 รายได้อื่น (7 lines,
  100–105) + รายการที่ 6 รายจ่ายอื่น (5 lines, 106–109). All amount boxes COMB:13.
- p5 = รายการที่ 7 รายจ่ายในการขายและบริหาร (24 lines, margins 110–129.1, line 24 = รวม)
  + รายการที่ 8 รายจ่ายที่ไม่ให้ถือเป็นรายจ่าย (7 lines, margins 130–134.1, line 7 = รวม).
  All COMB:13. 3 columns per row: x≈246 (ยกเว้น①), x≈354 (ต้องเสีย②), x≈462 (รวม③).
- p7 = แบบแจ้งข้อความของกรรมการ: Text36.11 (ชื่อบริษัท, plain), Text475/476/477 +
  Text478/479/480 (รอบบัญชี ตั้งแต่/ถึง: วันที่ COMB:2, เดือน COMB:2, พ.ศ. COMB:4),
  Group991 (Choice1=มี/Choice2=ไม่มี), Group992–995 ('1'=มี/'2'=ไม่มี), Text481–490
  (เพราะ/detail, plain), Text491–493 + 499–501 (sign dates), Text494–498 (auditor block).
  C-D fills ONLY Text36.11 + Text475–480.
- Column rubric: same as p3 — กรณีทั่วไป fill ช่อง ③ เท่านั้น (recon cont.89).

**รายการที่ 7 account-code convention (seeded CoA today: 5100 ค่าเช่า · 5200 ค่าบริการ ·
5300 โฆษณา · 5350 ภาษีซื้อขอคืนไม่ได้ · 5400 เงินเดือน · 5410 สปส.นายจ้าง):**

| Code range | Line (margin) | Form label |
|---|---|---|
| 5400–5499 | 1 (110) | รายจ่ายเกี่ยวกับพนักงาน |
| 5100–5199 | 6 (115) | ค่าเช่า |
| 5300–5349 | 9 (118) | ค่านายหน้า ค่าโฆษณา ค่าส่งเสริมการขาย |
| 5350–5399 | 11 (120) | ค่าภาษีอากรอื่นๆ |
| 5200–5299 | 19 (126) | ค่าธรรมเนียมอื่นๆ |
| any other / unparseable | 22 (129) | รายจ่ายอื่นที่นอกเหนือจาก 1. ถึง 21. |

**รายการที่ 8 mapping (positive cit_adjustments only, by LegalRefCode/Label keyword):**

| Match (LegalRefCode or Label contains) | Line (margin) |
|---|---|
| ภาษีเงินได้ | 1 (130) |
| ค่ารับรอง / ม.65ตรี(4) | 2 (131) |
| หนี้สูญ | 3 (132) |
| เงินสำรอง / ม.65ตรี(1) | 4 (133) |
| anything else | 6 (134.1) อื่นๆ |

Line 5 (134, รายจ่ายตามรายการที่ 7 ข้อ 23) = 0 (TEAS records no double-deduction expenses;
ladder rows 18/19 CharityExcess/EducationExcess are separate and already 0-printed).
Total = Σ lines 1–6 and must equal ladder row 11 (DisallowedExpenses = Σ positive adj).

---

### Task 1: Geometry consolidation — extend pnd50_cells.json + map docs

**Files:**
- Create: `docs/RD-Forms/pnd50/fieldmap/geo_cd.py`
- Modify: `docs/RD-Forms/pnd50/fieldmap/pnd50_cells.json` (script output, append p4/p5/p7)
- Modify: `backend/src/Accounting.Infrastructure/Pdf/Templates/pnd50_cells.json` (copy)
- Create: `docs/RD-Forms/pnd50/fieldmap/pnd50_p4_map.md`, `pnd50_p5_map.md`, `pnd50_p7_map.md`

- [ ] **Step 1: Write `geo_cd.py`** — reuse `probe_cd.py`'s `vbounds`/`centers` verbatim; for
  pages 4/5/7 collect every COMB text widget's cell centres, merge into the existing
  `pnd50_cells.json` dict (error on key collision), and print a per-page row table
  (y, line-label guess from nearest text, ①/②/③ field names) for the map docs.
- [ ] **Step 2: Run it; verify** Σ new keys ≈ 87 (p4) + 93 (p5) + 12 (p7); spot-check 3 cell
  lists have 13 ascending x values (p4/p5) and 2/2/4 (p7).
- [ ] **Step 3: Write the three map docs** from the printed table joined with the recon label
  list (in this plan header + `_scratch/text_out.txt`). Each row: line no · margin no ·
  Thai label · field ① · field ② · field ③. Mark which fields C-D fills (③ only).
- [ ] **Step 4: Copy merged json over `Pdf/Templates/pnd50_cells.json`** (embedded resource;
  verify `<EmbeddedResource>` glob already covers it — it's the same file path v1 embedded).
- [ ] **Step 5: Commit** `docs(pnd50): p4/p5/p7 comb geometry + field maps (C-D recon consolidation)`.

### Task 2: Data layer — per-account FY expense rows

**Files:**
- Modify: `backend/src/Accounting.Application/Tax/ICitYearDataService.cs`
- Modify: `backend/src/Accounting.Infrastructure/Tax/CitYearDataService.cs`
- Test: `backend/tests/Accounting.Api.Tests/TaxFilings/CitYearDataServiceTests.cs` (existing class)

- [ ] **Step 1: Read `CitYearDataService.ProfileAsync`** to find the exact FY P&L query basis
  (the one producing `AccountingNetProfit`). The new method MUST use the same posted-entry,
  same date-window query so Σ expense rows == RevenueFullYear − AccountingNetProfit.
- [ ] **Step 2: Add to the interface:**

```csharp
public sealed record ExpenseAccountRow(string AccountCode, string AccountNameTh, decimal Amount);
// in ICitYearDataService:
/// <summary>Posted FY expense totals per account (same basis as ProfileAsync's net profit) —
/// feeds the ภ.ง.ด.50 รายการที่ 7 schedule. Σ Amount must reproduce RevenueFullYear − AccountingNetProfit.</summary>
Task<IReadOnlyList<ExpenseAccountRow>> ExpenseByAccountAsync(int fiscalYear, CancellationToken ct);
```

- [ ] **Step 3: Failing integration test** (TestIds year discipline — mirror `FreshYearAsync`
  pattern): post a JE with two expense accounts in a fresh year, assert rows contain both
  accounts with the right amounts and Σ == profile-derived expenses.
- [ ] **Step 4: Implement** (group the ProfileAsync expense-side query by account code/name).
- [ ] **Step 5: Run** the test class 2× on `teas_test` (env `TEAS_TEST_PG`, run from `W:`): PASS ×2.
- [ ] **Step 6: Commit** `feat(cit): per-account FY expense rows for pnd50 รายการที่ 7`.

### Task 3: Pure builders — Pnd50ExpenseSchedule + Pnd50DisallowedSchedule

**Files:**
- Modify: `backend/src/Accounting.Infrastructure/Pdf/Pnd50FormFiller.cs` (records live with
  Pnd50Model/Pnd50Ladder — same file, same pattern)
- Modify: `backend/src/Accounting.Infrastructure/Tax/Pnd50FilingService.cs` (static builders)
- Test: `backend/tests/Accounting.Api.Tests/TaxFilings/Pnd50ScheduleTests.cs` (new)

- [ ] **Step 1: Records** (next to `Pnd50Ladder`):

```csharp
/// <summary>p5 รายการที่ 7 (margins 110–129.1) — column ③ only; line 24 = Total.</summary>
public sealed record Pnd50ExpenseSchedule(
    decimal Employee,            // 1  (110) 5400–5499
    decimal DirectorComp,        // 2  (111)
    decimal Utilities,           // 3  (112)
    decimal Travel,              // 4  (113)
    decimal Freight,             // 5  (114)
    decimal Rent,                // 6  (115) 5100–5199
    decimal Repairs,             // 7  (116)
    decimal Entertainment,       // 8  (117)
    decimal Marketing,           // 9  (118) 5300–5349
    decimal SbtTax,              // 10 (119)
    decimal OtherTaxes,          // 11 (120) 5350–5399
    decimal FinanceCost,         // 12 (121)
    decimal Bookkeeping,         // 13 (121.1)
    decimal AuditFee,            // 14 (122)
    decimal PoliticalDonation,   // 15 (122.1)
    decimal CharityDonation,     // 16 (123)
    decimal EducationSport,      // 17 (124)
    decimal Consulting,          // 18 (125)
    decimal OtherFees,           // 19 (126) 5200–5299
    decimal BadDebt,             // 20 (127)
    decimal Depreciation,        // 21 (128)
    decimal Other,               // 22 (129) fallback
    decimal DoubleDeduct,        // 23 (129.1) always 0 in C-D
    decimal Total);              // 24 = Σ 1–23 == ladder row 8

/// <summary>p5 รายการที่ 8 (margins 130–134.1) — positive cit_adjustments classified.</summary>
public sealed record Pnd50DisallowedSchedule(
    decimal IncomeTax,           // 1 (130)
    decimal Entertainment,       // 2 (131)
    decimal BadDebt,             // 3 (132)
    decimal Provisions,          // 4 (133)
    decimal FromItem7Line23,     // 5 (134) always 0 in C-D
    decimal Other,               // 6 (134.1) fallback
    decimal Total);              // 7 = Σ 1–6 == ladder row 11
```

- [ ] **Step 2: Failing unit tests** (`Pnd50ScheduleTests`, no DB): classification per the
  convention table (one row per range + unparseable code → Other), foot guard throws
  `InvalidOperationException` when Σ != ladderRow8 / != ladderRow11, disallowed keyword
  mapping (ค่ารับรอง→131, หนี้สูญ→132, เงินสำรอง→133, ภาษีเงินได้→130, else→134.1),
  negative adjustments ignored.
- [ ] **Step 3: Implement** static `Pnd50FilingService.BuildExpenseSchedule(rows, ladderRow8)`
  and `BuildDisallowedSchedule(adjustments, ladderRow11)` — pure, mirror `BuildLadder`'s
  caller-bug-throw posture (`InvalidOperationException`, not DomainException, when foots break).
- [ ] **Step 4: Run** `Pnd50ScheduleTests` → PASS; full `Pnd50*` filter still green.
- [ ] **Step 5: Commit** `feat(pnd50): pure p5 schedule builders with ladder foot guards`.

### Task 4: Filler — p4/p5/p7 rendering

**Files:**
- Modify: `backend/src/Accounting.Infrastructure/Pdf/Pnd50FormFiller.cs`
- Test: `backend/tests/Accounting.Api.Tests/TaxFilings/Pnd50FormFillerTests.cs`

- [ ] **Step 1: Extend `Pnd50Model`** with `Pnd50ExpenseSchedule ExpenseSchedule` and
  `Pnd50DisallowedSchedule Disallowed` (non-null, like Ladder).
- [ ] **Step 2: Fill p5** — column ③ field names from `pnd50_p5_map.md` (Task 1 output), one
  `Amt(name, value)` per line 1–24 (รายการที่ 7) and 1–7 (รายการที่ 8); explicit zeros.
- [ ] **Step 3: Fill p4** — column ③ all rows explicit 0 EXCEPT ladder-tied rows:
  รายการที่ 4 line 17 = `L.CostOfSales`; รายการที่ 5 line 7 = `L.OtherIncome`;
  รายการที่ 6 line 5 = `L.OtherExpenses`; intermediate รวม rows (ร4 lines 4/14/15) = 0
  consistently. Field names from `pnd50_p4_map.md`.
- [ ] **Step 4: Fill p7 header** — `new("Text36.11", m.CompanyName)`; dates Text475/476/477 =
  PeriodStart day/month/พ.ศ., Text478/479/480 = PeriodEnd — copy the exact d/m/พ.ศ.
  formatting used for p1 fields 17–22 (same source values). Nothing else on p7.
- [ ] **Step 5: Update structural tests** — existing `Pnd50FormFillerTests.Model(...)` helper
  gains the two schedules; add assertions: every map-doc ③ field present exactly once, p7
  date strings match พ.ศ., no Group991–995 radio ever emitted.
- [ ] **Step 6: Run** filler tests → PASS. **Step 7: Commit**
  `feat(pnd50): fill p4/p5 schedules + p7 declaration header`.

### Task 5: Service wiring — compose, refusals, attestation, preview

**Files:**
- Modify: `backend/src/Accounting.Infrastructure/Tax/Pnd50FilingService.cs`
- Modify: preview DTO + attestation records (wherever `Pnd50PreviewDto`/`Pnd50Attestation` live
  — locate via grep; extend, don't move)
- Test: `backend/tests/Accounting.Api.Tests/TaxFilings/Pnd50FilingServiceTests.cs`

- [ ] **Step 1: `ComposeAsync`** — fetch `ExpenseByAccountAsync`; build both schedules inside
  the same try/collect pattern as the ladder (foot-throw → refusal code
  `pnd50.schedule_breaks_ladder`); add informational refusal `pnd50.disclosure_required`
  when `profile.RevenueFullYear > 200_000_000m` (ม.71ทวิ). Composition carries the two
  schedules (nullable, like Ladder).
- [ ] **Step 2: `BuildPnd50Async`** — model gains the schedules; null schedule = refusal path
  (same as null ladder). `AcceptBlankSchedules` doc-comment narrows to ใบแนบ + p7
  declaration/answers (p4/p5 now render).
- [ ] **Step 3: Preview** — DTO gains `expenseSchedule` + `disallowedSchedule` (all line
  values + total), refusal list unchanged in shape.
- [ ] **Step 4: Integration tests** — compose-level: schedule totals foot to ladder rows 8/11
  for a seeded FY; >200M revenue year (TestIds fresh year) → `pnd50.disclosure_required`
  present but PDF still renders when attested; preview JSON carries the schedules.
- [ ] **Step 5: Run** `Pnd50FilingServiceTests` 2× on teas_test → PASS ×2.
- [ ] **Step 6: Commit** `feat(pnd50): C-D compose/preview wiring + ม.71ทวิ disclosure refusal`.

### Task 6: Visual gate

**Files:**
- Modify: `backend/tests/Accounting.Api.Tests/TaxFilings/Pnd50VisualEmit.cs`
- Output: `_review/pnd50cd/` crops

- [ ] **Step 1: Extend the worked cases** — profit case gets a realistic expense schedule
  (employee 1,200,000 · rent 240,000 · marketing 60,000 · other 12,345.67 footing to the
  case's SellingAdminExpenses) + disallowed (ค่ารับรอง excess 15,000 + อื่นๆ 5,000 footing
  to DisallowedExpenses); rebuild case figures through the REAL builders so everything foots.
- [ ] **Step 2: Emit** (`PND50_EMIT_DIR=_review/pnd50cd` + filter `Pnd50VisualEmit`), raster
  p4/p5/p7 at 200dpi, crop: p5 ร7 rows with values + รวม row, ร8 rows, p4 the three section
  totals, p7 name+dates. Read every crop yourself: digits land in cells, no bleed,
  พ.ศ. correct, columns ①② empty.
- [ ] **Step 3: Send crops to Ham** (SendUserFile, proactive) — same rule as v1/v2: รอยืนยัน
  ก่อนยื่นจริง; build continues meanwhile.
- [ ] **Step 4: Commit** `test(pnd50): C-D visual-gate worked cases (p4/p5/p7)`.

### Task 7: FE + openapi

**Files:**
- Modify: `frontend/app/(dashboard)/tax-filings/cit/*` (preview card), `frontend/messages/{th,en}.json`
- Modify: `docs/api/openapi.yaml` (preview schema delta)

- [ ] **Step 1:** Extend the CIT dashboard preview types with the two schedules; add a
  "รายจ่ายขายและบริหาร (รายการที่ 7)" card listing nonzero lines + total, and a
  รายการที่ 8 block when total > 0; new refusal key `pnd50.disclosure_required` +
  `pnd50.schedule_breaks_ladder` i18n (th primary + en parity).
- [ ] **Step 2:** `tsc --noEmit` → 0; visual check on :3000 (restart dev server first —
  stale-chunk gotcha).
- [ ] **Step 3:** openapi: extend `/tax-filings/pnd50/preview` response schema; YAML parses.
- [ ] **Step 4: Commit** `feat(pnd50): CIT dashboard schedule cards + openapi preview delta`.

### Task 8: Full gates + records

- [ ] Build solution 0 warnings-as-errors regressions; kill :5080 first, restart after.
- [ ] Api full suite ≥ 302 passing, 0 fail, 2× on the schedule/filing classes.
- [ ] `grep -rln "ম"` over changed files → clean (Bengali glyph trap).
- [ ] `progress.md` prepend cont.92 entry · `plan.md` tick C-D items · NEXT-SESSION.md update
  (mark item 6 progress; note ใบแนบ decision + ภ.พ.01/09 next).
- [ ] Commit docs + **push everything** (policy: push after every commit).

---

## Self-review notes

- Spec coverage: p4–p5+p7 fill ✓ (Tasks 1,3,4) · ใบแนบ ✓ (scoped out, recorded) · ม.71ทวิ ✓
  (Task 5 refusal) · preview/FE ✓ (Tasks 5,7) · visual gate ✓ (Task 6).
- Field names for p4/p5 column ③ intentionally come from Task 1's generated map docs —
  geometry-derived, not inventable ahead of running the join; the plan pins the procedure,
  counts, and acceptance instead.
- Disallowed keyword table must be validated against real `LegalRefCode` usage in Task 3
  Step 2 (grep validation list first; adjust match keys if a fixed vocabulary exists).
