import { describe, it, expect } from 'vitest';
import { ApiError } from '../api';
import { errorToToast } from './errors';

// WP2.4 (F19) — domain-error toasts resolve Thai by the stable error CODE first; an unknown
// code falls back unchanged to the backend's own detail string.
describe('errorToToast — WP2.4 domain-error Thai resolution', () => {
  it('resolves a known purchase-side domain-error code to Thai', () => {
    const err = new ApiError(
      422,
      'vi.expense_account_missing',
      'Line 1: no expense account (category "COGS" has no default).',
    );
    expect(errorToToast(err)).toBe(
      'หมวดหมู่ค่าใช้จ่ายนี้ยังไม่ได้ผูกบัญชี GL กรุณาตั้งค่าบัญชีเริ่มต้นก่อนบันทึก',
    );
  });

  it('resolves the D1.4 vendor taxId code to Thai', () => {
    const err = new ApiError(422, 'vendor.vat_registered_requires_taxid', 'TaxId is required.');
    expect(errorToToast(err)).toBe('ผู้ขายที่จดทะเบียน VAT ต้องมีเลขผู้เสียภาษี 13 หลัก');
  });

  it('R3: resolves po.reopen_blocked to Thai', () => {
    const err = new ApiError(
      422,
      'po.reopen_blocked',
      'Cannot reopen: a posted Vendor Invoice is already linked to this Purchase Order.',
    );
    expect(errorToToast(err)).toBe(
      'เปิดใบสั่งซื้อใหม่ไม่ได้ — มีใบกำกับภาษีซื้อที่บันทึก (Post) แล้วเชื่อมกับใบสั่งซื้อนี้',
    );
  });

  it('R3: resolves po.not_approved to Thai', () => {
    const err = new ApiError(422, 'po.not_approved', 'PO must be Approved to link a Vendor Invoice.');
    expect(errorToToast(err)).toBe('เชื่อมใบสั่งซื้อไม่ได้ — ใบสั่งซื้อต้องอยู่ในสถานะอนุมัติแล้ว');
  });

  it('falls back to the backend detail for an unrecognized code', () => {
    const err = new ApiError(500, 'some.unmapped_code', 'Something specific went wrong.');
    expect(errorToToast(err)).toBe('Something specific went wrong.');
  });

  it('falls back to detail for a non-ApiError value', () => {
    expect(errorToToast(new Error('plain error'))).toBe('Unexpected error');
  });
});
