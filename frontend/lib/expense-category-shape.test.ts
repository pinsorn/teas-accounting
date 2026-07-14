import { describe, it, expect } from 'vitest';
import { pick } from './expense-category-shape';

// WP1.5c (F20) — a category with a NULL default_expense_account_id must be flagged so the
// <select> can disable it (no 422 vi/pv.expense_account_missing mid-form). No DOM test env in
// this repo's vitest setup, so this covers the data-shaping layer the component renders from.
describe('pick() (expense category list shape)', () => {
  it('surfaces defaultExpenseAccountId as null when the API omits/nulls it', () => {
    const cats = pick([
      { categoryId: 1, categoryCode: 'COGS', nameTh: 'ต้นทุนขาย', defaultExpenseAccountId: null },
      { categoryId: 2, categoryCode: 'RENT', nameTh: 'ค่าเช่า' }, // field entirely absent
    ]);
    expect(cats[0]?.defaultExpenseAccountId).toBeNull();
    expect(cats[1]?.defaultExpenseAccountId).toBeNull();
  });

  it('passes through a real account id unchanged', () => {
    const cats = pick([
      { categoryId: 3, categoryCode: 'SVC', nameTh: 'บริการ', defaultExpenseAccountId: 42 },
    ]);
    expect(cats[0]?.defaultExpenseAccountId).toBe(42);
  });

  it('tolerates the {items:[...]} wrapper shape', () => {
    const cats = pick({ items: [{ categoryId: 5, categoryCode: 'X', defaultExpenseAccountId: 7 }] });
    expect(cats).toHaveLength(1);
    expect(cats[0]?.defaultExpenseAccountId).toBe(7);
  });
});
