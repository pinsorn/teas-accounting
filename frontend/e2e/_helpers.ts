import { expect, type Page } from '@playwright/test';
import { TestIds } from './helpers/test-ids';

export async function login(page: Page, username = 'admin') {
  await page.goto('/login');
  await page.getByRole('textbox', { name: /ชื่อผู้ใช้|username/i }).fill(username);
  await page.locator('input[type="password"]').fill('Admin@1234');
  await page.getByRole('button', { name: /เข้าสู่ระบบ|sign in/i }).click();
  // Robust redirect wait: `waitForURL('**/')` raced the dashboard redirect under
  // load / repeated logout→login cycles (false "login timeout"). Wait until we've
  // actually LEFT /login, then block on the dashboard's nav-gates sentinel so the
  // shell has fully settled before the test proceeds.
  await page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 30_000 });
  await waitForNavGates(page);
}

export async function logout(page: Page) {
  await page.getByRole('button', { name: /ออกจากระบบ|sign out/i }).click();
  await page.waitForURL('**/login', { timeout: 15_000 });
}

/** Live grants of the currently logged-in user (via the BFF proxy, cookie auth). */
export async function mePermissions(
  page: Page,
): Promise<{ isSuperAdmin: boolean; permissions: string[]; roles: string[] }> {
  const r = await page.request.get('/api/proxy/me/permissions');
  if (!r.ok()) throw new Error(`/me/permissions ${r.status()}`);
  return r.json();
}

/** Current company's VAT mode (true = VAT-registered) via the BFF proxy. */
export async function vatMode(page: Page): Promise<boolean> {
  const r = await page.request.get('/api/proxy/system/info');
  return r.ok() ? Boolean((await r.json()).vatMode) : true;
}

/**
 * Block until SidebarNav has applied its final permission/VAT filter. The nav
 * renders a hidden `nav-gates-ready` sentinel only once BOTH useMePermissions
 * and useSystemInfo have settled — so waiting on it (attached, it is hidden)
 * removes the client-gate race without guessing a timeout, and closes the
 * vatMode-default-true flash on non-VAT companies. Call after every navigation
 * that lands inside the dashboard shell.
 */
export async function waitForNavGates(page: Page) {
  await page.getByTestId('nav-gates-ready').waitFor({ state: 'attached', timeout: 15_000 });
}

/**
 * Pick a customer via EntityPickerModal. Two trigger generations exist:
 * the create-form redesign's PartySelectBox renders an empty-state dashed
 * "เลือกลูกค้า" button, while older pages still use CustomerSelector whose
 * trigger's accessible name is its placeholder "ค้นหาชื่อ หรือเลขผู้เสียภาษี".
 * Both open the same role=dialog with a search input + result <button>s.
 */
export async function pickCustomer(page: Page, search = 'ลูกค้า', name: RegExp = /ลูกค้าทดสอบ/) {
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox').fill(search);
  await dialog.getByRole('button', { name }).click();
}

/** Create + post a Tax Invoice via the UI; returns its numeric id from the detail URL. */
export async function createAndPostTaxInvoice(page: Page): Promise<number> {
  await page.goto('/tax-invoices/new');
  // Customer is the CustomerSelector MODAL — open it via the trigger button, then
  // search + pick inside the dialog (replaced the old inline combobox/listbox).
  await pickCustomer(page);
  await page.getByLabel('รายละเอียด 1').fill('e2e item');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('1000');
  await page.getByRole('button', { name: /^Post|บันทึกเอกสาร/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/tax-invoices\/\d+$/, { timeout: 15_000 });
  const m = page.url().match(/\/tax-invoices\/(\d+)$/);
  return Number(m![1]);
}

/** Create a vendor via the UI; returns its unique code. */
export async function createVendor(page: Page): Promise<string> {
  // §14 (resolved Sprint 14.5): random suffix via shared TestIds, was Date.now().
  const code = TestIds.vendorCode('E2EV');
  await page.goto('/vendors/new');
  await page.getByText(/รหัสผู้ขาย|Vendor code/).locator('xpath=following::input[1]').fill(code);
  await page.getByText(/^ชื่อ \(ไทย\)|Name \(Thai\)/).locator('xpath=following::input[1]')
    .fill('ผู้ขาย e2e จำกัด');
  await page.getByRole('button', { name: /บันทึกผู้ขาย|Save vendor/ }).click();
  await page.waitForURL(/\/vendors$/, { timeout: 15_000 });
  return code;
}

/**
 * Pick a vendor in the VendorSelector MODAL (EntityPickerModal) by its code/label
 * fragment. The selector is a trigger button whose accessible name (when empty) is
 * "ค้นหาชื่อ หรือรหัสผู้ขาย"; clicking it opens a role=dialog with a search input +
 * result <button>s. Search by the supplied fragment, then click the first result.
 */
export async function pickVendor(page: Page, query: string) {
  // Same two-generation trigger as pickCustomer: PartySelectBox "เลือกผู้ขาย"
  // (create-form redesign) OR the legacy VendorSelector placeholder name.
  await page.getByRole('button', { name: /^เลือกผู้ขาย$|ค้นหาชื่อ หรือรหัสผู้ขาย/ }).first().click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox').fill(query);
  // Design-swap: result buttons are named by the vendor NAME only (the code no
  // longer appears in the accessible name), so after searching by the unique
  // code wait for the single filtered result and click it.
  const results = dialog.getByRole('listitem').getByRole('button');
  await expect(results).toHaveCount(1, { timeout: 10_000 });
  await results.first().click();
}

/**
 * Scrape the allocated doc number (e.g. 06-2026-TI-0007 or 06-2026-TI-LAB-0001)
 * from the detail page currently shown. Needed because the redesigned
 * TaxInvoicePicker searches by doc_no/customer name — the numeric id no longer
 * works as a search term.
 */
export async function detailDocNo(page: Page, type = 'TI'): Promise<string> {
  const re = new RegExp(`\\d{2}-\\d{4}-${type}(?:-[A-Z0-9]+)?-\\d{4}`);
  // Detail data loads async after waitForURL — poll until the number renders.
  await expect(page.locator('main')).toContainText(re, { timeout: 15_000 });
  const text = await page.locator('main').innerText();
  const m = text.match(re);
  if (!m) throw new Error(`doc number (${type}) not found on ${page.url()}`);
  return m[0];
}

/**
 * Pick a Tax Invoice in the redesigned TaxInvoicePicker typeahead: the field is
 * an <input role=combobox aria-label="taxInvoiceId N">; typing searches by
 * doc_no / customer name and options render in #taxinvoice-listbox as
 * <li><button> whose name starts with the doc no.
 */
export async function pickTaxInvoice(page: Page, index: number, docNo: string) {
  const box = page.getByRole('combobox', { name: `taxInvoiceId ${index}` });
  await box.click();
  await box.fill(docNo);
  // The portal-positioned FloatingListbox can land outside the viewport
  // (Playwright refuses to click), and the option commits on onMouseDown —
  // dispatch the event directly once the option is attached.
  const option = page.locator('#taxinvoice-listbox')
    .getByRole('button', { name: new RegExp(docNo) }).first();
  await option.waitFor({ state: 'attached', timeout: 10_000 });
  await option.dispatchEvent('mousedown');
}
