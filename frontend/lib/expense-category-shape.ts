import type { ExpenseCategoryLite } from './types';

// Defensive shape parse for GET /expense-categories — list endpoint contract not pinned.
// Extracted from components/ui/ExpenseCategorySelector.tsx (dependency-free but for the
// type-only import above, which is erased at build time) so WP1.5c's null-account detection
// is unit-testable under this repo's vitest setup, which has no `@/` alias resolution.
export function pick(raw: unknown): ExpenseCategoryLite[] {
  const arr =
    Array.isArray(raw) ? raw
    : raw && typeof raw === 'object' && Array.isArray((raw as { items?: unknown }).items)
      ? (raw as { items: unknown[] }).items
      : [];
  return arr
    .map((x) => x as Record<string, unknown>)
    .filter((x) => typeof x.categoryId === 'number')
    .map((x) => ({
      categoryId: x.categoryId as number,
      categoryCode: String(x.categoryCode ?? ''),
      nameTh: String(x.nameTh ?? x.categoryCode ?? ''),
      // BP-02 — the BE field is `defaultIsRecoverableVat`; tolerate the legacy
      // `isRecoverableVat` shape too. Reading the wrong key meant the ม.82/5
      // ⚠ warning never fired for non-recoverable categories (ENT/VEHI).
      defaultIsRecoverableVat:
        (x.defaultIsRecoverableVat ?? x.isRecoverableVat) !== false,
      isCapex: x.isCapex === true,
      // WP1.5c (F20) — null = no default GL account (unusable at save).
      defaultExpenseAccountId:
        typeof x.defaultExpenseAccountId === 'number' ? x.defaultExpenseAccountId : null,
    }));
}
