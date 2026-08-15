# PROGRESS — General Ledger release v1.12.0 — ✅ COMPLETE (2026-07-08 ~00:30+07)

Morning report for Ham. Everything shipped while you slept.

## Shipped to prod
- **v1.12.0** live on teas.kazaki-rio.com: General Ledger report (บัญชีแยกประเภท) + JE detail
  drill-down + PDF/CSV export + RBAC perm `report.general_ledger.read` (seed 590 applied on
  prod at API startup — verified perm row exists).
- Trail: feat PR #47 → CI backend FAILED (CSV CRLF, Linux-only) → fix PR #49 (explicit
  `\r\n`, RFC 4180) merged green → release PR #48 → tag `v1.12.0` → build
  (MinVer `1.12.0+e0421fe`, 15 .so) → scp upload (md5 verified all 4 artifacts) →
  `deploy-api-v1120.sh`: DB_BACKUP_OK, DEPLOY_OK (gl/je routes 401-gated, seed590=1) →
  `deploy-fe-v1120.sh`: BUILD_OK, FE_DEPLOY_OK (login 200, gl/je 307) →
  public re-check from dev machine: /reports/general-ledger → 307 to login, /login → 200.
- Deploy used SSH key `repttown_deploy` (works on this VPS — found after teas_deploy step
  wasn't completed). No password handled by Fable. Rollback never triggered; `unpacked.old`
  cleaned by the script on success.

## For Ham to check (5 min)
1. Login on prod as a company user → เมนู รายงาน → บัญชีแยกประเภท → เลือกบัญชี+ช่วงวันที่ →
   ตารางถูก → คลิกเลขเอกสาร → หน้า JE detail → ปุ่ม PDF/Excel โหลดได้.
2. Confirm the sidebar shows the menu only for roles that should see it (same set as trial
   balance: ACCOUNTANT/CHIEF_ACCOUNTANT/AUDITOR/TAX_OFFICER/COMPANY_ADMIN).

## Incident + lessons (already filed)
- Merged #47 on red: `gh pr checks --watch` exit 0 despite backend FAIL → now in
  troubles-wiki ("read statuses, never trust watch exit code"). Root failure itself
  (AppendLine platform newline) also in troubles-wiki.
- Quota-cliff session death (no wakeup scheduled): ScheduleWakeup was queued AFTER extras;
  cliff killed the turn first → memory `quota-cliff-wakeup-first` (checkpoint = PROGRESS +
  ScheduleWakeup ONLY, in one response).
- Pending template folds for minions-assemble (next /minions maintenance pass):
  gh-watch lesson + haiku glyph-gate exact-codepoint instruction (U+09AE ম vs U+0E21 ม).

## Housekeeping done
- specs/general-ledger.md → Status DONE with evidence. troubles-wiki +2 entries.
- Memory updated: teas-prod-deploy-plink (key auth), quota-cliff-wakeup-first (new).
- Dev servers stopped (:3000 killed; :5080 died earlier on its own).
- Local branches: main synced to e0421fe; feature/fix branches deleted on remote by merges.
