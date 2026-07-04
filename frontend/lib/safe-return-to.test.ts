import { describe, it, expect } from 'vitest';
import { isSafeReturnTo } from './safe-return-to';

// M6 (auth-oauth review 2026-07-04) — open-redirect guard. The backslash bypass:
// browsers normalize '\' -> '/' during URL resolution, so a rt that starts with '/\' resolves
// the same as '//' (protocol-relative -> off-origin navigation).
describe('isSafeReturnTo', () => {
  it('accepts a legitimate same-origin path with a query string', () => {
    expect(isSafeReturnTo('/tax-invoices/1?action=approve')).toBe(true);
  });

  it('accepts an OAuth consent deep link', () => {
    expect(isSafeReturnTo('/oauth/consent?client_id=mcp')).toBe(true);
  });

  it('rejects protocol-relative //evil.com', () => {
    expect(isSafeReturnTo('//evil.com')).toBe(false);
  });

  it('rejects the backslash bypass /\\evil.com', () => {
    expect(isSafeReturnTo('/\\evil.com')).toBe(false);
  });

  it('rejects /\\/evil.com', () => {
    expect(isSafeReturnTo('/\\/evil.com')).toBe(false);
  });

  it('rejects leading-backslash \\/evil.com (does not start with a single slash)', () => {
    expect(isSafeReturnTo('\\/evil.com')).toBe(false);
  });

  it('rejects a double-backslash \\\\evil.com', () => {
    expect(isSafeReturnTo('\\\\evil.com')).toBe(false);
  });

  it('rejects an absolute URL', () => {
    expect(isSafeReturnTo('http://evil.com')).toBe(false);
  });

  it('rejects null/empty', () => {
    expect(isSafeReturnTo(null)).toBe(false);
    expect(isSafeReturnTo(undefined)).toBe(false);
    expect(isSafeReturnTo('')).toBe(false);
  });
});
