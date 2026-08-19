# UX SWARM ROUND 5 — verify all finding fixes (WP1-6) closed on v1.22.9 (2026-07-21)

Ham /goal: "แก้ Finding อื่นทั้งหมด แล้วจัดกองทัพ sonnet เบิ้มๆ รุม". Finding batch WP1-6 shipped
= v1.22.9 LIVE. This round re-runs the 10-role swarm to PROVE each fix closed + regression + CRIT stays closed.

Target: **https://teas.kazaki-rio.com** (prod v1.22.9), company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด).
Accounts EXIST (REUSE): sales01/acct01/appr01/ap01/ar01/audit01/chief01/admin01/purch01/tax01,
pw `UxSwarm-2026-<suffix>` (A1 sales A2 acct A3 appr A4 ap A5 ar A6 audit A7 chief A8 admin A9 purch B1 tax).

## PRIMARY ASSERTIONS (per-role, this round's reason to exist)
- **audit01 (WP1+WP2+WP6 — the big one):** (a) WP1: the 16 `/…/new` routes + CN/DN list "+ สร้างเอกสาร"
  + tax-filing finalize + /period-close must now show a CLEAN full-page deny "ไม่มีสิทธิ์เข้าถึง", NOT a
  rendered write form (round-4 they rendered fully). (b) WP2+WP6: the previously-403 modules — PO, VI,
  PV, quotations, sales-orders, delivery-orders, expense-claims, vendors, bank-accounts, fixed-assets,
  AP-aging, outstanding-PO, bank-recon, CIT, business-units — must now RENDER REAL DATA for AUDITOR
  (co5 has real docs the auditor previously couldn't see). Console 403 spam (esp. business-units) must
  be GONE. Still NO write (POST still 403 — defense-in-depth intact). This single role verifies 3 WPs.
- **appr01 (WP4):** dashboard "ต้องทำ/แจ้งเตือน" widget must no longer 403 (was false "all clear").
  NOTE the widget is agent-created-draft-scoped by design — a grant-only fix; browser-created drafts
  still won't appear (that's intended, documented). Just confirm no 403 + it loads.
- **chief01 (WP3):** each report header now states its date basis (TB/BS "ณ วันที่ …", P&L "ช่วง …");
  AP-aging shows the control-account tie-out banner (like AR-aging); AR-aging negative buckets visually
  distinct; bank-recon diff has an explanatory badge + auto-selects the sole account. No numbers wrong.
- **admin01 (WP5):** /settings/api-keys renders clean deny (no leak-past-gate, no React #418 in console);
  /settings/users own row + peer COMPANY_ADMIN rows have the destructive buttons guarded (self/peer SoD).
- **ap01 (WP5 VI-clobber):** VI-new from a PO link — pick the Expense Category immediately (before the
  async poDetail settles) → the pick must STICK and Post must enable (round-4 it clobbered to null).
  Trigger any error toast → must be Thai, not EN (WP5 i18n).
- **CRIT regression (sales01/ar01/purch01):** the numbering paths (QT issue, TI/RC post, PO approve)
  must STILL be 2xx zero 500/23505 under concurrency on v1.22.9.

## HARD RULES (unchanged)
1. co5 ONLY. Other company's data = CRITICAL tenant-leak, screenshot + stop that thread.
2. FORBIDDEN: ยืนยัน/ปิดงวด ภ.พ.30, year-end close, payroll mutations, delete/edit EXISTING master or users.
   Creating NEW docs/products = fine (playground).
3. A fix that DIDN'T close (form still renders for unauthorized role / module still 403s for AUDITOR /
   widget still 403 / label missing / clobber still happens / any 500) = finding, screenshot + evidence.
4. Playwright headless from Y:\ClaudePlayground\TEAS-Project\frontend, temp swarm5-<user>.mjs, DELETE
   after. No repo source edits, no git, no builds.
5. Output ONLY: swarm-findings/round5/<user>.md + shots/round5/<user>-*.png. Sections: Done /
   Fix-verify (explicit per WP: closed? yes/no + evidence) / Regressions / Findings.
6. ~25-min timebox. Human-paced. Capture console errors (esp. 403 count for audit01 BU check).

## Missions
- **audit01**: THE verifier — sweep all 16 /new (expect deny), CN/DN nav button (expect hidden),
  all previously-403 modules (expect real data now), count console 403s (expect ~0 BU). Direct-API POST
  probe on 2 doc types (expect still 403). This one role closes WP1+WP2+WP6.
- **chief01**: all reports — verify WP3 (date-basis labels, AP-aging tie banner, AR negatives style,
  bank-recon badge+autoselect). Cross-report consistency.
- **admin01**: WP5 — api-keys clean deny + no #418; users self/peer-admin guard; create new master data.
  Company switcher co5-only.
- **appr01**: WP4 widget no-403 + race-approve drafts (CRIT regression 2xx).
- **ap01**: WP5 VI-clobber (pick category fast, must stick) + VI/PV post 2xx (CRIT regression) + Thai toast.
- **sales01**: CRIT regression — QT→SO→DO→IV cycles 2xx.
- **ar01**: CRIT regression — TI→post→RC cycles 2xx + AR aging tie.
- **purch01**: CRIT regression — PO approve 2xx.
- **acct01**: TB Dr=Cr held under load + regression.
- **tax01**: CRIT-2 regression — ภ.พ.30 preview/PDF 200 (+ note .txt 422 address-gap still stands, known).

## Consolidation (Fable)
- [x] 10 round5 files → verdict: every WP fix CONFIRMED closed? any regression? Fold survivors into a
      new fix arc. Post-swarm sanity: TB tie, no cross-tenant, prod pm2 zero-500 for the window. —
      closed by triage 2026-08-19 (round5/ committed; survivors → fix-swarm-round5-lows.md)
- [x] cleanup leftover frontend/swarm5-*.mjs. — closed by triage 2026-08-19 (no swarm5-*.mjs found
      in repo)
