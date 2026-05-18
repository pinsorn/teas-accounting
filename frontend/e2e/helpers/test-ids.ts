import { randomBytes } from 'node:crypto';

/**
 * Sprint 14.5 — §14 anti-collision helper (Playwright-side mirror of
 * backend Accounting.TestKit.TestIds). Every identifier carries a short
 * random suffix so test fixtures stay idempotent against the long-lived
 * shared dev DB (no accumulation → no false-positive sprint failures).
 *
 * Keep this surface byte-for-byte aligned with
 * backend/tests/Accounting.TestKit/TestIds.cs.
 */
export const TestIds = {
  suffix: () => randomBytes(4).toString('hex'), // 8 lowercase hex chars

  customerCode: (prefix = 'CUST') => `${prefix}-${TestIds.suffix()}`,
  vendorCode: (prefix = 'VEND') => `${prefix}-${TestIds.suffix()}`,
  productCode: (prefix = 'PROD') => `${prefix}-${TestIds.suffix()}`,
  branchCode: (prefix = 'BR') => `${prefix}-${TestIds.suffix()}`,

  businessUnitCode: (prefix = 'BU') =>
    `${prefix}${TestIds.suffix().slice(0, 3).toUpperCase()}`,

  expenseCategoryCode: (prefix = 'EXP') =>
    `${prefix}-${TestIds.suffix().slice(0, 4).toUpperCase()}`,

  whtTypeCode: (prefix = 'WHT') =>
    `${prefix}-${TestIds.suffix().slice(0, 4).toUpperCase()}`,

  email: (prefix = 'test') => `${prefix}+${TestIds.suffix()}@example.com`,

  taxId: () => {
    const n =
      Math.floor(Math.random() * (999_999_999 - 100_000_000 + 1)) + 100_000_000;
    return `0000${n.toString().padStart(9, '0')}`;
  },

  futurePeriod: () => {
    const now = new Date();
    const months = 12 + Math.floor(Math.random() * 99) + 1;
    const d = new Date(now.getFullYear(), now.getMonth() + months, 1);
    return d.getFullYear() * 100 + (d.getMonth() + 1);
  },

  name: (prefix = 'Test') => `${prefix} ${TestIds.suffix()}`,
};
