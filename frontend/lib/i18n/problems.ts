// WP2.4 (F19) — domain-error toasts to Thai. Backend business-rule errors (DomainException,
// see DomainExceptionMiddleware) carry a STABLE CODE in ProblemDetails `title` (e.g.
// "vi.expense_account_missing"); this dict resolves that code to a Thai message so the toast
// leads with something a Thai-speaking user can act on, instead of the raw EN detail string.
//
// TH-only by design (mirrors lib/i18n/validation.ts's per-locale DICT shape, but skips a
// parallel EN dict): the backend's ProblemDetails `detail` is already a reasonable English
// sentence, so an `en` locale falls straight through to it — duplicating ~60 EN restatements
// here would be pure upkeep with no user-facing benefit. Seeded with the purchase-side codes
// named in the design plus every `throw new DomainException("...")` code found under
// Accounting.Infrastructure/Purchase/* and /Master/* (2026-07-14).
import { currentLocale } from './validation';

const TH: Record<string, string> = {
  // period.*
  'period.year_closed': 'ปีบัญชีนี้ปิดแล้ว กรุณาเปิดปีบัญชีก่อน แล้วจึงเปิดงวดนี้ใหม่',
  'period.not_closed': 'งวดบัญชีนี้ไม่ได้อยู่ในสถานะปิด',
  // WP5 (specs/fix-swarm-findings-all.md, appr01 LOW) — DomainExceptionMiddleware's generic
  // catch-all (unexpected exceptions, e.g. the PO-approve 500 appr01 hit) always emits CODE
  // "internal_error" with an English `detail` ("An unexpected error occurred."); without an
  // entry here that English sentence surfaced verbatim on an otherwise all-Thai UI.
  'internal_error': 'เกิดข้อผิดพลาดที่ไม่คาดคิด กรุณาลองใหม่อีกครั้ง หากยังพบปัญหากรุณาติดต่อผู้ดูแลระบบ',

  // auth.*
  'auth.required': 'ต้องเข้าสู่ระบบก่อนทำรายการนี้',
  'auth.unauthenticated': 'เซสชันหมดอายุ กรุณาเข้าสู่ระบบใหม่',
  'auth.forbidden': 'คุณไม่มีสิทธิ์ทำรายการนี้',
  'auth.invalid_credentials': 'ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง',
  'auth.account_locked': 'บัญชีถูกล็อกชั่วคราว กรุณาลองใหม่ภายหลัง',
  'auth.account_disabled': 'บัญชีถูกปิดใช้งาน',
  'auth.invalid_mfa': 'รหัส MFA ไม่ถูกต้อง',
  'auth.no_company_assignment': 'ผู้ใช้นี้ไม่มีบริษัทที่กำหนดสิทธิ์',
  'auth.session_absolute_cap_exceeded': 'เซสชันหมดอายุการใช้งานสูงสุดแล้ว กรุณาเข้าสู่ระบบใหม่',

  // vi.* (Vendor Invoice / ใบกำกับภาษีซื้อ)
  'vi.not_found': 'ไม่พบใบกำกับภาษีซื้อนี้',
  'vi.not_posted': 'ใบกำกับภาษีซื้อนี้ยังไม่ได้บันทึก',
  'vi.already_settled': 'ใบกำกับภาษีซื้อนี้ชำระครบแล้ว',
  'vi.pv_exists': 'มีใบสำคัญจ่ายเชื่อมกับใบกำกับภาษีซื้อนี้อยู่แล้ว',
  'vi.vendor_missing': 'ไม่พบผู้ขายที่เลือก',
  'vi.expense_category_missing': 'ไม่พบหมวดหมู่ค่าใช้จ่ายที่เลือก',
  'vi.expense_account_missing':
    'หมวดหมู่ค่าใช้จ่ายนี้ยังไม่ได้ผูกบัญชี GL กรุณาตั้งค่าบัญชีเริ่มต้นก่อนบันทึก',
  'vi.product_type_invalid': 'ประเภทสินค้า/บริการไม่ถูกต้อง',
  'vi.not_draft': 'แก้ไขได้เฉพาะใบที่ยังเป็นฉบับร่าง',
  'vi.po_not_found': 'ไม่พบใบสั่งซื้อที่เชื่อม',
  'vi.po_link_invalid': 'เชื่อมกับใบสั่งซื้อนี้ไม่ได้',
  'vi.claim_period_closed': 'งวดภาษีนี้ปิดแล้ว ไม่สามารถเครมภาษีซื้อได้',
  'vi.claim_period_out_of_range': 'วันที่เครมภาษีซื้ออยู่นอกช่วงที่กำหนด',
  'vi.vat_rate_out_of_range': 'อัตราภาษีมูลค่าเพิ่มไม่ถูกต้อง ต้องอยู่ระหว่าง 0 ถึง 1',
  'vi.no_docno': 'ไม่สามารถออกเลขที่เอกสารใบกำกับภาษีซื้อได้',
  'vi.invalid_amount': 'จำนวนเงินรวมต้องมากกว่า 0',

  // pv.* (Payment Voucher / ใบสำคัญจ่าย)
  'pv.not_found': 'ไม่พบใบสำคัญจ่ายนี้',
  'pv.vi_exists': 'มีใบกำกับภาษีซื้อเชื่อมกับใบสำคัญจ่ายนี้อยู่แล้ว',
  'pv.vendor_missing': 'ไม่พบผู้ขายที่เลือก',
  'pv.expense_category_missing': 'ไม่พบหมวดหมู่ค่าใช้จ่ายที่เลือก',
  'pv.expense_account_missing':
    'หมวดหมู่ค่าใช้จ่ายนี้ยังไม่ได้ผูกบัญชี GL กรุณาตั้งค่าบัญชีเริ่มต้นก่อนบันทึก',
  'pv.product_type_invalid': 'ประเภทสินค้า/บริการไม่ถูกต้อง',
  'pv.vendor_not_vat_registered': 'ผู้ขายรายนี้ไม่ได้จดทะเบียน VAT',
  'pv.exempt_product_vat': 'สินค้า/บริการนี้ได้รับยกเว้น VAT',
  'pv.vat_rate_invalid': 'อัตรา VAT ไม่ถูกต้อง',
  'pv.wht_type_missing': 'ไม่พบประเภทภาษีหัก ณ ที่จ่ายที่เลือก',
  'pv.vi_not_found': 'ไม่พบใบกำกับภาษีซื้อที่เชื่อม',
  'pv.vi_cross_tenant': 'ใบกำกับภาษีซื้อนี้ไม่ได้อยู่ในบริษัทเดียวกัน',
  'pv.vi_not_posted': 'ใบกำกับภาษีซื้อนี้ยังไม่ได้บันทึก',
  'pv.vi_over_settle': 'ยอดชำระเกินยอดคงค้างของใบกำกับภาษีซื้อ',
  'pv.wht_rate_out_of_range': 'อัตราภาษีหัก ณ ที่จ่ายไม่ถูกต้อง ต้องอยู่ระหว่าง 0 ถึง 1',
  'pv.not_approved': 'บันทึก (Post) ใบสำคัญจ่ายได้เฉพาะใบที่อนุมัติแล้วเท่านั้น',
  'pv.no_docno': 'ไม่สามารถออกเลขที่เอกสารใบสำคัญจ่ายได้',
  'pv.invalid_amount': 'จำนวนเงินที่ชำระต้องมากกว่า 0',

  // po.* (Purchase Order / ใบสั่งซื้อ)
  'po.not_found': 'ไม่พบใบสั่งซื้อนี้',
  'po.not_draft': 'แก้ไขได้เฉพาะใบสั่งซื้อที่ยังเป็นฉบับร่าง',
  // R3 (confirm-round 2026-07-15) — Ham-specified wording; this code is also thrown by the
  // "close"/"mark sent" guards (same CODE, different call-sites) where this text reads a
  // little off-context ("เชื่อม..." = "link"), but a single code → single message dict can't
  // branch by call-site without a backend change (out of scope for an FE-only fix).
  'po.not_approved': 'เชื่อมใบสั่งซื้อไม่ได้ — ใบสั่งซื้อต้องอยู่ในสถานะอนุมัติแล้ว',
  'po.vi_exists': 'มีใบกำกับภาษีซื้อเชื่อมกับใบสั่งซื้อนี้อยู่แล้ว',
  // R3 — WP3.4+ (close/reopen) codes, missing from the WP2.4 pass (2026-07-14, predates WP3.4).
  'po.reopen_blocked': 'เปิดใบสั่งซื้อใหม่ไม่ได้ — มีใบกำกับภาษีซื้อที่บันทึก (Post) แล้วเชื่อมกับใบสั่งซื้อนี้',
  'po.terminal': 'ไม่สามารถยกเลิกใบสั่งซื้อนี้ได้ เนื่องจากอยู่ในสถานะสุดท้ายแล้ว',
  'po.not_closed': 'เปิดใบสั่งซื้อใหม่ได้เฉพาะใบที่อยู่ในสถานะปิดแล้วเท่านั้น',

  // wht.*
  'wht.not_found': 'ไม่พบหนังสือรับรองหัก ณ ที่จ่ายนี้',

  // bu.* (Business Unit / หน่วยธุรกิจ)
  'bu.required': 'ต้องระบุหน่วยธุรกิจ',
  'bu.invalid': 'หน่วยธุรกิจไม่ถูกต้องหรือถูกปิดใช้งาน',
  'bu.duplicate': 'รหัสหน่วยธุรกิจนี้มีอยู่แล้ว',
  'bu.not_found': 'ไม่พบหน่วยธุรกิจนี้',

  // vendor.*
  'vendor.not_found': 'ไม่พบผู้ขายนี้',
  'vendor.duplicate': 'รหัสผู้ขายนี้มีอยู่แล้ว',
  'vendor.vat_registered_requires_taxid': 'ผู้ขายที่จดทะเบียน VAT ต้องมีเลขผู้เสียภาษี 13 หลัก',

  // company.* / company_profile.* / company_info.*
  'company.not_found': 'ไม่พบบริษัทนี้',
  'company.duplicate': 'เลขผู้เสียภาษีนี้ถูกใช้โดยบริษัทอื่นแล้ว',
  'company_profile.not_found': 'ไม่พบข้อมูลโปรไฟล์บริษัท',
  'company_profile.address_incomplete': 'กรุณากรอกจังหวัดและรหัสไปรษณีย์ให้ครบถ้วน',
  'company_profile.logo_bad_mime': 'ไฟล์โลโก้ต้องเป็นรูปภาพเท่านั้น',
  'company_profile.logo_too_large': 'ไฟล์โลโก้มีขนาดใหญ่เกินกำหนด',
  'company_info.name_required': 'กรุณากรอกชื่อนิติบุคคล',
  'company_info.tax_id_invalid': 'เลขผู้เสียภาษีต้องเป็นตัวเลข 13 หลักและถูกต้องตาม checksum',
  'company_info.branch_invalid': 'รหัสสาขาต้องเป็นตัวเลข 5 หลัก (00000 = สำนักงานใหญ่)',
  'company_info.address_incomplete': 'กรุณากรอกจังหวัดและรหัสไปรษณีย์ให้ครบถ้วน',
  'company_info.vat_rate_invalid': 'อัตรา VAT ต้องอยู่ระหว่าง 0 ถึง 1',
  'company_info.pnd30_invalid': "รูปแบบการยื่น ภ.พ.30 ต้องเป็น 'manual' หรือ 'auto'",

  // pp30_batch.* (ภ.พ.30 RD Prep "Format กลาง" batch-file export guard — Pp30BatchExportService)
  'pp30_batch.no_data': 'ไม่มียอดขายในงวดนี้ ไม่สามารถสร้างไฟล์ ภ.พ.30 ได้ (ต้องมียอดขายมากกว่า 0)',
  'pp30_batch.missing_address':
    'ข้อมูลที่อยู่จดทะเบียนของบริษัทไม่ครบถ้วน (ต้องมีเลขที่และรหัสไปรษณีย์) กรุณากรอกข้อมูลโปรไฟล์บริษัทให้ครบก่อนสร้างไฟล์ ภ.พ.30',

  // payroll.*
  'payroll.duplicate_period': 'มีรอบจ่ายของงวดนี้อยู่แล้ว',
  'payroll.bank_required': 'กรุณาเลือกบัญชีธนาคารสำหรับจ่ายเงินเดือน',
  'payroll.bank_not_found': 'ไม่พบบัญชีธนาคารที่เลือกหรือบัญชีถูกปิดใช้งาน',

  // customer.* / employee.* / branch.* / coa.* / prefix.* / expense_category.* / product.*
  'customer.not_found': 'ไม่พบลูกค้านี้',
  'customer.duplicate_code': 'รหัสลูกค้านี้มีอยู่แล้ว',
  'employee.not_found': 'ไม่พบพนักงานนี้',
  'employee.duplicate': 'รหัสพนักงานนี้มีอยู่แล้ว',
  'branch.not_found': 'ไม่พบสาขานี้',
  'branch.duplicate': 'รหัสสาขานี้มีอยู่แล้ว',
  'coa.not_found': 'ไม่พบผังบัญชีนี้',
  'coa.duplicate': 'รหัสบัญชีนี้มีอยู่แล้ว',
  'prefix.duplicate': 'รหัสเลขที่เอกสารนี้มีอยู่แล้ว',
  'expense_category.duplicate': 'รหัสหมวดหมู่ค่าใช้จ่ายนี้มีอยู่แล้ว',
  'product.bad_type': 'ประเภทสินค้า/บริการไม่ถูกต้อง',
  'product.duplicate': 'รหัสสินค้านี้มีอยู่แล้ว',
  'product.not_found': 'ไม่พบสินค้านี้',
  'product.in_use': 'ไม่สามารถลบสินค้านี้ได้ เนื่องจากถูกใช้งานอยู่',
  'product.bu_invalid': 'หน่วยธุรกิจของสินค้านี้ไม่ถูกต้อง',
};

/** Resolve a domain-error CODE to Thai. Returns null when the locale isn't `th` or the code
 * is unrecognized — callers fall back to the backend's own (English) detail string. */
export function resolveProblemKey(code: string, locale = currentLocale()): string | null {
  if (locale !== 'th' || !code) return null;
  return TH[code] ?? null;
}
