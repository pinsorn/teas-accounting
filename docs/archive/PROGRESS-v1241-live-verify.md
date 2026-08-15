# PROGRESS — v1.24.1 live prod verification (2026-07-29, Ham asleep)

Checkpoint written at 94% quota. prod = **v1.24.1**, main = `b790906` + uncommitted findings below.

## Done — 3 live legs on prod, all evidence in `swarm-findings/v1241/`

### Leg A — O14 period reopen on co6 · **PASS 7/7**
- **D3 ledger-safety invariant HELD**: reopening a month while the fiscal year was still closed was
  refused, `POST /periods/2026/2/reopen` → **422 `period.year_closed`** with the Thai "reopen the year
  first" message. This was the single most dangerous thing in the release and it behaves correctly.
- **co6 is UNFROZEN and usable again** — after reopen-year → reopen-month, PV `07-2026-PV-MISC-0001`
  posted; Trial Balance **Dr = Cr ฿17,640.00**, bank +฿1,000.00.
- Negative case → 422 `period.not_closed`, clean (no 500/blank).
- LOW finding: no UI-reachable audit view shows period reopen events.

### Leg B — O10 deductions + O11-alt on co7 · **PASS**
- JE `08-2026-JV-0001`: **Dr 121,750.00 = Cr 121,750.00**, with **`Cr 2180` = ฿500.00** exactly.
- Net fell exactly ฿500 (58,752.08 → 58,252.08); gross/PIT/SSO untouched.
- Cap refused: `จำนวนเงินหักของพนักงาน O8FULL ต้องไม่เกิน…58,752.08 บาท` — no negative net persisted.
- **ภ.ง.ด.1 isolation holds**: gross 60,000.00 / PIT 372.92 = the pre-deduction figures.
- Payslip prints `หัก รายการหักอื่น ๆ (เรียกคืนเงินจ่ายเกิน) -500.00`. Posted run immutable.
- O11-alt totals tie to ส่วนที่ 1 on two runs; prorated joiner shows ฿32,903.23, not ฿60,000.
- BLOCKED: print-preview step froze the tab on a native OS print dialog — needs a human eyeball.

### Leg X — co7 employee names are corrupted Thai (Fable, reviewing Leg B)
Leg B called `???????` "placeholder text, not a bug". Wrong — `octet_length` proves it:
co6's names are 15 bytes (3/char, intact), co7's are 4 bytes (1/char, literal `?`).
Cause is the CLIENT, not the app: co6 was created via the UI, co7 via the API from PowerShell, which
degrades non-ASCII to `?` silently. **Not a product defect**, but co7 cannot be used to verify name
rendering on ภ.ง.ด.1 / สปส.1-10. Fix (repair 3 names) deliberately NOT applied — prod data write while
Ham slept. See `swarm-findings/v1241/legX-co7-employee-names-corrupt.md`; wiki entry added.

### Leg C — O2b on co5 · **CRITICAL: headline feature unreachable in the UI**
- Override half works: linked TIs + a manual line → manual line survives, nothing generated. Issue →
  `07-2026-IV-0006`, Thai paper renders, issued doc immutable. Steps 4–7 PASS.
- **Steps 2–3 BLOCKED**: with the grid left empty and two TIs linked, save is refused **client-side**
  ("ต้องมีรายการอย่างน้อย 1 รายการ" / "กรุณากรอกข้อมูลให้ครบถ้วน"). **Zero network requests fire** — the
  request never reaches the backend, so the generation logic is unverified on prod.
- LOW: console spams `MISSING_MESSAGE: common.remove (th)` on the invoice form.
- INFO: a ~20-25s Cloudflare 521 occurred mid-session, self-recovered, unrelated to any write.
- Note: the billing note is surfaced in the UI as **ใบแจ้งหนี้** at `/invoices` (API `billing-notes`).

## ROOT CAUSE of the O2b block — diagnosed, fix NOT yet written
`frontend/components/forms/BillingNoteForm.tsx`:
- line 168: `if (v.lines.length === 0 && selectedTis.length === 0)` — the relaxation only applies when
  the array is EMPTY.
- But the form always renders **one default blank row whose delete button is disabled**, so
  `lines.length` is never 0. The blank row then fails the per-line zod schema → "กรอกข้อมูลให้ครบถ้วน".
- line 418: the "lines will be generated" hint is gated on the same `lines.length === 0`, so it never
  shows either — which is why the leg saw no indication the feature existed.

Codex removed `.min(1)` from the zod array as instructed, but that was never the binding constraint.
**My dispatch caused this**: I wrote "if the form blocks submit on an empty line grid, relax that
check" without noticing the form cannot reach an empty grid.

**Intended fix (not applied):** treat rows that are entirely blank as absent — filter them out before
validating/submitting, and allow the field array to reach zero rows (or skip per-row validation for a
single untouched blank row) when `selectedTis.length > 0`. Then both the guard and the hint work.

## Next steps on resume
1. Fix `BillingNoteForm.tsx` per above (FE-only; the backend generation logic is already tested and
   unchanged). Sonnet is fine — normal implementation.
2. Gates: `tsc` + `next build` (run alone — never alongside `dotnet test`), then the full Api suite
   only if any backend file is touched (it should not be).
3. Commit. **Do NOT deploy without Ham** — prod still carries the broken form. A redeploy means a new
   release tag + the usual DB-backup-free FE-only path (no schema in this fix).
4. Re-run Leg C's steps 2–3 on co5 after deploying, to close the CRITICAL properly.
5. Optional, ask Ham: repair co7's three employee names; human eyeball of the SSO print preview.

## Environment reminders
- Gate command needs `TEAS_TEST_PG` + `TEAS_REPO_ROOT` in the SAME shell call, absolute paths.
- Never run `tsc`/`next build` concurrently with `dotnet test` (killed the build twice with EPERM).
- Live prod logins: co5 `admin01`/`UxSwarm-2026-A8` · co6 `nvadmin01`/`UxSwarm-2026-NV1` ·
  co7 `nvadmin02`/`UxSwarm-2026-NV4`.
