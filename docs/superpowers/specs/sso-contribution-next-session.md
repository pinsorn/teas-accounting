# SSO contribution file — NEXT SESSION kickoff (P-D #4)

> Read order: CLAUDE.md → progress.md (top) → this. Payroll core + ภ.ง.ด.1/1ก SHIPPED. This is the
> last payroll output: the monthly Social-Security (ม.33) contribution submission to สำนักงานประกันสังคม
> (SSO) — a SEPARATE system from the RD. Reuse the ภ.ง.ด.1 build playbook end-to-end.

## Goal
Monthly **สปส.1-10** (แบบรายการแสดงการส่งเงินสมทบ) + ใบแนบ **สปส.1-10/1** (รายชื่อผู้ประกันตน) from a posted
`PayrollRun`. Two likely deliverables — confirm which SSO accepts first:
1. **Fill the official สปส.1-10 / 1-10/1 PDF** (if AcroForm) via `RdAcroFormFiller` — same as ภ.ง.ด.1.
2. **e-Service upload text file** (SSO e-payment "ส่งข้อมูลเงินสมทบ") — fixed-width or delimited; SSO has
   its own layout spec (NOT the RD FORMAT กลาง). Research the exact field layout from sso.go.th.

## Data already in place (don't rebuild)
- `Payslip.SsoEmployee` (=employee 5%) + `SsoEmployer` (=employer 5%, equal), per employee per run.
- `PayrollRun.TotalSsoEmployee` / `TotalSsoEmployer`.
- `Employee.SsoNumber` (เลขประกันสังคม), `NationalId` (13-digit PIN), `SsoApplicable`, name.
- `SsoOptions` (`Payroll:Sso`): Rate 0.05 · WageFloor 1650 · WageCeiling 15000 (⚠️ 2569 in flux). Contribution
  base + clamp already computed by `SsoContribution.Monthly` at run draft — the file just READS the payslips.
- Aggregation pattern: copy `Pnd1FilingService.BuildPnd1MonthlyAsync` (load run + payslips + employer header).

## Source PDFs to check FIRST
`docs/RD-Forms/os4/` already has `os4_150861.pdf`, `os4k_150861.pdf`, `os4kh_160861.pdf`, `os4_guide.pdf`,
`sd10.pdf` — **verify whether these are the สปส./SSO forms** (os4 ≈ แบบขึ้นทะเบียน? sd10? ). If สปส.1-10 /
1-10/1 are NOT among them, download from sso.go.th. Then run the field-discovery dump (below).

## Field-discovery methodology (PROVEN on ภ.ง.ด.1 — reuse verbatim)
1. Probe the PDF for an AcroForm: a throwaway xUnit test walking `doc.AcroForm.Elements["/Fields"]`
   (PdfSharp) — dump field names + `/Rect` (top,x) grouped by row + radio widget order (see the deleted
   `_Pnd1aDump`/`_Pnd1RadioDump` in git history `5b16160`/`4488bbc` for the exact dump code).
2. To map generic `Text{block}.{idx}` names → boxes: render markers OR read `/Rect` coords + cross-check
   against a Playwright screenshot (`file:` is blocked → serve the PDF via a tiny `node http` server, then
   `browser_navigate` + `browser_take_screenshot` + `Read` the PNG).
3. Write `Pdf/Templates/sps_1_10_fieldmap.md`, then build the filler.

## Build plan
1. If AcroForm: embed `sps_1_10.pdf` + `sps_1_10_1.pdf` as `EmbeddedResource` (csproj wildcard already copies).
2. `Pdf/SpsFormFiller.FillMonthly(SpsModel)` via `RdAcroFormFiller` (comb for SSO/PIN; ✓ check mark;
   landscape ⇒ engine reads page W/H automatically). Rows = employees, ≤N/sheet, PdfSharp page-merge.
3. `Application/Payroll/ISsoFilingService.BuildMonthlyAsync(runId)` + impl in `Infrastructure/Payroll`
   (aggregate the run's payslips: SsoNumber/NID/name/wage/empContrib/erContrib + totals).
4. Endpoint `GET /payroll/runs/{id}/sso/pdf` (perm `payroll.run.manage`) + (if text file)
   `…/sso/file`. FE: button on `/payroll/[id]` via `openPdf` (no print dialog — Ham pref).
5. Test: golden %PDF + `≥N` pages (mirror `Pnd1_monthly_fills_the_official_acroform`). Live render +
   self-verify screenshot + send sample for Ham's zoom-check (expect ~1-2 review rounds on field map).

## Open items / confirm with Ham
- Which output SSO accepts: filled PDF, e-service upload text, or both? (default: do the PDF first.)
- Exact สปส.1-10 layout (the wage column = contributory wage, capped 15,000) + whether ใบแนบ needs the
  employee's wage per row or just the contribution.
- 2569 wage-ceiling effective ฿ (config `Payroll:Sso:WageCeiling`) — same open item as ภ.ง.ด.1.
- Rate config is SSO-only (5%+5%); provident fund is a generic deduction (out of scope, locked earlier).
