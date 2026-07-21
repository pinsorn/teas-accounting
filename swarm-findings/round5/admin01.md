# admin01 (Company Admin) — UX swarm findings round5 — co5 prod v1.22.9
Run: 2026-07-21 (headless Playwright/msedge, `frontend/swarm5-admin01.mjs`, deleted after use)

## Done (สิ่งที่ทำ+ผล)
- Login เป็น admin01 (pw UxSwarm-2026-A8, REUSE) สำเร็จ, dashboard โหลดผ่าน nav-gates sentinel
  (shots/round5/admin01-01-login-dashboard.png)
- GET /api/proxy/me → status=200 `userId=18 username=admin01 companyId=5 isSuperAdmin=false
  companyName=บริษัท ทดสอบ VAT (DUMMY) จำกัด allowedCompanies=[{"id":5,...}]`
- Company switcher DOM: count=0 (ไม่ render — ถูกต้อง, admin01 ไม่ใช่ super-admin)
- Tenant-leak probe: `POST /api/auth/switch-company {companyId:1}` → **HTTP 403** (deny ถูกต้อง)
- ตรวจ /settings/api-keys — screenshot + console/pageerror capture
  (shots/round5/admin01-02-settings-api-keys.png)
- ตรวจ /settings/users — self row + full user list via `/api/proxy/admin/rbac/users`, screenshot
  ทั้งหน้า + screenshot เจาะแถวของ admin01 เอง (shots/round5/admin01-03-settings-users.png,
  admin01-04-settings-users-self-row.png). **ไม่ได้กดปุ่มใดๆ จริง** ตาม hard rule.
- สร้างสินค้าใหม่ **P990777** (สินค้าทดสอบ swarm5 990777) — เห็น row ใหม่ในตาราง
  (shots/round5/admin01-05-product-created.png)
- สร้างลูกค้าใหม่ **C990777** (ลูกค้าทดสอบ swarm5 990777) — เห็น row ใหม่, VAT unchecked ก่อน submit
  (shots/round5/admin01-06-customer-created.png)
- สร้างผู้ขายใหม่ **V990777** (ผู้ขายทดสอบ swarm5 990777) — toast "บันทึกผู้ขาย" + เห็น row ใหม่
  (shots/round5/admin01-07-vendor-created.png)
- หมายเหตุ debug: การรัน 2 ครั้งแรกของ script (ก่อนปรับ wait) สร้าง P435664/C435664/V435664 และ
  P718181/C718181/V718181 ทิ้งไว้ด้วย (ทุกโค้ดเป็น code ใหม่ล้วน ไม่กระทบ master data เดิม, อนุญาต
  ตาม hard rule "Creating NEW docs/products = fine (playground)") — เกิดจาก list ใช้เวลา refetch
  หลัง navigate นานกว่าที่ script รอครั้งแรก (customer/vendor list ค้าง "กำลังโหลด…" ชั่วคราว) ไม่ใช่บั๊ก
  ของแอป แค่ timing ของ script เอง, แก้โดยรอ `waitFor({state:'visible'})` แทน fixed timeout แล้วผ่านทุก
  รายการ
- ลบไฟล์ temp script (`frontend/swarm5-admin01.mjs`) และ debug json หลังใช้งานเสร็จ
- Session จบตาม timebox, เขียนสรุปแล้ว

## Fix-verify (WP5 — admin01 scope, explicit closed?)

### (a) /settings/api-keys — clean deny / no React #418
**Status: #418 regression CLOSED — confirmed. "Clean deny" not applicable to admin01 (see note).**
- Checked the role×permission matrix (`docs/rbac/role-permission-matrix.md`) first:
  `sys.api_key.manage` is granted to **COMPANY_ADMIN** (✓) and SUPER_ADMIN only — AUDITOR and
  every other role are blank. admin01 = COMPANY_ADMIN, so admin01 **legitimately has** manage
  rights and is NOT expected to see a deny screen at this route. The spec's admin01 mission line
  ("api-keys clean deny") appears to generalize the WP5 description rather than admin01's actual
  grant — flagging this as a spec-wording note, not a finding. The deny-gate path is the correct
  thing for audit01 (AUDITOR, no grant) to exercise, not admin01.
- What IS in scope and verified: the page rendered its full content (API Keys table "ไม่มีข้อมูล" +
  the native MCP connector panel) with **zero console errors and zero `pageerror` events** —
  confirming the round3/4 regression ("Minified React error #418" hydration mismatch, seen on
  every load of this page) is now **GONE**. `results.apiKeys = {denyShown:false,
  nativePanelShown:true, pageErrors:[], has418:false}`.
  (shots/round5/admin01-02-settings-api-keys.png)
- Zero page-errors were observed across the **entire session** (not just this page) — strong
  evidence the fix (moving `window.location.origin` into a post-mount `useEffect`, per the code
  comment in `api-keys/page.tsx` WP5) is holding.

### (b) /settings/users — self + peer COMPANY_ADMIN SoD guard
**Status: CLOSED — confirmed for the self-guard half. Peer-guard half not independently testable
(no peer Company Admin exists in co5's user set) but same code path, see below.**
- admin01's own row (`userId=18`) now shows the guard note
  **"จัดการบัญชีนี้จากหน้านี้ไม่ได้ (ป้องกันการล็อกตัวเองหรือผู้ดูแลระบบบริษัทคนอื่น)"** in place of the
  3 destructive buttons — confirmed via both the live DOM (`data-testid="user-guarded-18"`
  present, `user-edit-18`/`user-reset-18`/`user-active-18` all **count=0**) and visually in the
  screenshot: every OTHER row (acct01/ap01/appr01/ar01/audit01/chief01/purch01) still shows the
  full 3-button set (แก้ไขบทบาท / รีเซ็ตรหัสผ่าน / ปิดใช้งาน), only admin01's own row is guarded.
  **This is the exact round3/4 LOW finding, now closed.**
  (shots/round5/admin01-03-settings-users.png, admin01-04-settings-users-self-row.png)
- Fetched the full co5 user list via `GET /api/proxy/admin/rbac/users` (the same endpoint the
  page uses) to look for a peer COMPANY_ADMIN row: **none exists** — admin01 is the only
  COMPANY_ADMIN-roled user in co5's 10-account swarm set. The peer half of the guard
  (`isGuardedRow`'s `u.roles.some(r => r.roleCode === 'COMPANY_ADMIN')` branch) could not be
  independently exercised from this account/dataset. Since the guard is a single shared function
  applied per-row regardless of WHICH condition triggers it, and the self-condition fired
  correctly, this is a data-availability gap, not a suspected code gap — noting for the
  consolidation pass in case another role's dataset surfaces a second Company Admin to confirm.
- No destructive button was clicked on any row, per hard rule.

## Regressions (carried from round3/4, out of this round's WP5 scope but re-observed)
| severity | พื้นที่ | อาการ | status this round |
|---|---|---|---|
| LOW | /login | console 404 on some static resource load at page open | **still present**, cosmetic, unrelated to WP5, not investigated further (out of scope) |

Not re-checked this round (out of admin01's WP5 mission): /payroll mutate-button (was a MED
finding round3/4, no read-only fix mentioned in this round's WP1-6 batch) — deferred, not
verified either way this round.

## CRIT / tenant-scope verification (admin01 scope)
- **Company switcher = co5 ONLY: YES.** `/api/proxy/me` → `companyId=5`,
  `allowedCompanies=[{"id":5,...}]` only. Switcher DOM not rendered (correct, non-super-admin).
  Direct probe `POST /api/auth/switch-company {companyId:1}` → **403 denied**. No tenant leak.
- CRIT-1/CRIT-2 numbering regressions: out of admin01's scope (deferred to
  sales01/ar01/purch01/ap01/appr01/tax01 reports). Indirect signal: all 3 of admin01's own
  creates (product/customer/vendor) returned success with **zero HTTP ≥400 responses observed
  anywhere in the session** (`results.httpErrors = []`) — no 500/23505 seen from this agent's
  traffic.
- **New master data created this round:** product=P990777, customer=C990777, vendor=V990777 (plus
  incidental P435664/C435664/V435664, P718181/C718181/V718181 from debug reruns — all new codes,
  see Done section). No existing master data touched or modified.

## Findings
None new this round in WP5 scope. Both round3/4 LOW findings assigned to admin01
(api-keys #418 hydration error; users self-row SoD guard missing) are **CONFIRMED CLOSED** by
this round's live verification.

## Console/page errors observed (full session)
- `[console] https://teas.kazaki-rio.com/login :: Failed to load resource: 404` (repeat of
  round3/4, cosmetic, unrelated to WP5)
- **Zero `pageerror` events for the entire session** (in particular: zero React error #418 on
  /settings/api-keys, confirming the hydration-mismatch fix holds)
- **Zero HTTP ≥400 responses observed** anywhere in this agent's traffic
