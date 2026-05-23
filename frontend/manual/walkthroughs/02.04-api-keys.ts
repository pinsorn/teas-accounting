/**
 * 02.04 — API Keys (admin-only, external integration)
 *
 * Chapter: 2. ตั้งค่าระบบ
 * Story: Admin สร้าง API key สำหรับ external system (microservice, mobile app,
 *        partner integration) พร้อม scopes + BU binding + expiry
 *
 * Verified live via Chrome MCP at http://localhost:3000/settings/api-keys
 * (2026-05-19, post-Sprint 13d).
 *
 * Verified:
 *   - Admin GET /api/proxy/api-keys → 200 with empty list (manual-demo
 *     doesn't seed API keys)
 *   - Sprint 13d-P2 QueryState: accountant → 403 → NoAccessState
 *     ("ต้องมีสิทธิ์ผู้ดูแลระบบ" + shield icon, ไม่ใช่ silent empty)
 *   - Sprint 13d-P3 PermissionGate: "+ สร้าง API key" button hidden for
 *     non-admin (sys.api_key.manage scope required)
 *   - Modal fields: ชื่อ*, สิทธิ์ (scopes — multi-checkbox), Business Unit
 *     เริ่มต้น (dropdown ECOM/LAB/REPT/ไม่กำหนด), หมดอายุ (date)
 *
 * ⚠️ Role requirement: ADMIN only (sys.api_key.manage scope).
 *    Accountant on this page sees NoAccessState — cannot view list nor create.
 *
 * ⚠️ Critical security note: full API key shown ONLY ONCE after creation.
 *    System stores only hash + prefix. If lost → must create new key.
 *
 * Pre-condition:
 *   - Logged in as ADMIN (demo-admin)
 *   - BU configured (walkthrough 02.01) — needed for BU binding dropdown
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '02.04',
  title: 'สร้าง API Keys (Admin only)',
  chapter: '2. ตั้งค่าระบบ',
  intro: `
API Key ใช้ให้ระบบภายนอก (microservice, mobile app, partner integration)
เรียก TEAS โดยไม่ต้อง login. แต่ละ key มี:

- **Scopes** — granular permissions เฉพาะที่จำเป็น (least privilege)
  เช่น \`sales.tax_invoice.create\`, \`sales.receipt.post\`
- **Business Unit เริ่มต้น** (Sprint 14) — ผูก key กับ 1 BU
  เอกสารที่สร้างจาก key นี้จะถูก tag กับ BU นั้นอัตโนมัติ —
  ป้องกัน microservice ของ ECOM ไปสร้างเอกสารใน LAB โดยไม่ตั้งใจ
- **หมดอายุ** (optional) — แนะนำตั้งสำหรับ contractor / temp staff

**🔐 สำคัญ — key เห็นได้ครั้งเดียวเท่านั้น**: หลังกด "สร้าง" ระบบจะแสดง
full key 1 ครั้ง. ระบบเก็บแค่ hash + prefix. ถ้าไม่ copy เก็บใน secret
manager ของ external app → ลืมแล้วต้องสร้างใหม่ (revoke ของเก่า).

**Request header**: external app ส่ง key มาทาง \`X-Api-Key: <full_key>\`
ในทุก request ไป \`/api/v1/*\` endpoints.

**Role**: ADMIN only — sys.api_key.manage scope. Accountant เห็น
NoAccessState ("ต้องมีสิทธิ์ผู้ดูแลระบบ").
  `.trim(),
  prerequisites: [
    'login ในฐานะ ADMIN (demo-admin)',
    'BU ตั้งครบแล้ว (walkthrough 02.01)',
    'ทราบว่า external app จะใช้ scopes อะไรบ้าง (least privilege)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: empty list (admin) ──────────────────────────────────────
  await page.goto('/settings/api-keys');
  await capture('step-01-empty-list', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: หน้า "API Keys" — เริ่มต้นไม่มี key (empty state พร้อม icon).' +
      ' คอลัมน์: ชื่อ, Key prefix, สิทธิ์ (scopes), Business Unit เริ่มต้น,' +
      ' ใช้ล่าสุด, หมดอายุ, สถานะ',
  });

  // ─── Step 2: contrast — accountant gets NoAccessState (illustrative) ─
  await capture('step-02-rbac-note', {
    highlight: 'main',
    caption:
      'ขั้นที่ 2: หมายเหตุ — accountant ที่เข้าหน้านี้จะเห็น "ต้องมีสิทธิ์' +
      ' ผู้ดูแลระบบ" (Sprint 13d-P2 NoAccessState) + ไม่มีปุ่ม "+ สร้าง"' +
      ' (Sprint 13d-P3 PermissionGate). ต้อง admin เท่านั้น',
  });

  // ─── Step 3: Open Create modal ───────────────────────────────────────
  // Note: page-level open trigger + modal submit button share text "สร้าง API key"
  // → strict-mode collision once modal opens. Use test-id to disambiguate.
  await page.getByTestId('api-key-new').click();
  await capture('step-03-create-modal', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 3: คลิก "+ สร้าง API key" → modal เปิด. 4 sections: ชื่อ*,' +
      ' สิทธิ์ (scopes)*, Business Unit เริ่มต้น, หมดอายุ',
  });

  // Random suffix per run — name uniqueness not strictly required (api-keys
  // allow duplicate names) but helps Sana inspect different runs in the list.
  const keyName = `ECOM Storefront ${Date.now().toString(36).slice(-5)}`;

  await page.getByLabel('ชื่อ').fill(keyName);
  await capture('step-04-name', {
    highlight: 'input[type="text"]',
    arrow: 'right',
    caption:
      'ขั้นที่ 4: กรอก "ชื่อ" — ใช้บอกว่า key นี้สำหรับอะไร' +
      ' (เช่น "ECOM Storefront — production"). ไม่กระทบความปลอดภัย แต่ช่วย' +
      ' ตอน revoke ทีหลัง',
  });

  // ─── Step 5: scopes checklist (multi) ────────────────────────────────
  // Tick scopes so submit button enables. Scope text appears in 2 places —
  // <span class="badge"> preview + <span class="label-text"> in <label>.
  // getByRole('checkbox', { name }) resolves to the label-associated input
  // via ARIA accessible name (unique, skips the badge preview).
  for (const scope of [
    'sales.tax_invoice.create',
    'sales.tax_invoice.post',
    'sales.receipt.create',
    'sales.receipt.post',
  ]) {
    await page.getByRole('checkbox', { name: scope }).check();
  }
  await capture('step-05-scopes', {
    highlight: 'input[type="checkbox"]:checked',
    arrow: 'right',
    caption:
      'ขั้นที่ 5: เลือก scopes — สำหรับ storefront ทั่วไป: sales.tax_invoice' +
      '.create, sales.tax_invoice.post, sales.receipt.create, sales.receipt.post.' +
      ' หลีกเลี่ยง .delete หรือ admin scopes ที่ไม่ใช้',
  });

  // ─── Step 6: BU binding (Sprint 14) ──────────────────────────────────
  await page.getByLabel('Business Unit เริ่มต้น').selectOption('1'); // ECOM
  await capture('step-06-bu-binding', {
    highlight: 'select',
    arrow: 'right',
    caption:
      'ขั้นที่ 6: เลือก "Business Unit เริ่มต้น" — เช่น ECOM. ทุกเอกสารที่' +
      ' key นี้สร้างจะ tag เป็น BU ECOM อัตโนมัติ. ถ้าเลือก "ไม่กำหนด" →' +
      ' ผู้เรียกต้องส่ง BU ในทุก request',
  });

  // ─── Step 7: Expiry ──────────────────────────────────────────────────
  await capture('step-07-expiry', {
    highlight: 'input[type="date"]',
    arrow: 'right',
    caption:
      'ขั้นที่ 7: "หมดอายุ" (optional). แนะนำตั้งสำหรับ contractor / temp.' +
      ' Production keys อาจปล่อยว่าง แต่ควร rotate ทุก 90 วัน (best practice)',
  });

  // ─── Step 8: Create + show-once ──────────────────────────────────────
  // Use modal submit test-id (not the page-level "+ สร้าง" trigger)
  // Wait for submit to be enabled (form valid after name + scopes filled)
  const submitBtn = page.getByTestId('api-key-submit');
  await submitBtn.waitFor({ state: 'visible' });
  await page.waitForFunction(
    () => {
      const btn = document.querySelector('[data-testid="api-key-submit"]') as HTMLButtonElement | null;
      return !!btn && !btn.disabled;
    },
    { timeout: 5000 },
  );
  await submitBtn.click();
  await capture('step-08-key-revealed', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 8: กด "สร้าง" → modal แสดง **full key 1 ครั้งเท่านั้น** พร้อม' +
      ' ปุ่ม "Copy". 🔐 Copy ใส่ secret manager ของ external app ทันที' +
      ' (Azure Key Vault / AWS Secrets Manager / Vault). ถ้าปิด modal ก่อน' +
      ' copy → ต้อง revoke + สร้างใหม่ (ระบบเก็บแค่ hash)',
  });

});
