'use client';

import { useTranslations } from 'next-intl';
import { useEmployeeLookup } from '@/lib/queries';

// Cycle C (specs/expense-claims.md §5) — employee (payee) picker for Expense Claim create.
// Clones BusinessUnitSelector.tsx's shape; active employees only, label `code — nameTh`.
// U6 (specs/fix-r2-u6-employee-lookup.md, L4-1) — switched from useEmployees() (gated by
// master.employee.manage, payroll data) to useEmployeeLookup() (master.employee.lookup,
// name-only) so ACCOUNTANT and other expense.claim.create-holding roles don't 403 here.
export function EmployeeSelector({
  value,
  onChange,
  error = false,
}: {
  value: number | null;
  onChange: (id: number | null) => void;
  error?: boolean;
}) {
  const t = useTranslations('expenseClaims');
  const tt = useTranslations('toast');
  const { data: employees = [], isLoading } = useEmployeeLookup();

  return (
    <label className="form-control">
      <span className="label-text">{t('employee')} *</span>
      <select
        className={`select select-bordered${error ? ' select-error' : ''}`}
        value={value ?? ''}
        disabled={isLoading}
        aria-label={t('employee')}
        aria-invalid={error || undefined}
        onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}
      >
        <option value="">{isLoading ? t('loading') : `— ${t('selectEmployee')} —`}</option>
        {employees.map((e) => (
          <option key={e.employeeId} value={e.employeeId}>
            {e.employeeCode} — {e.fullNameTh}
          </option>
        ))}
      </select>
      {error && <span className="text-error text-sm">{tt('requiredField')}</span>}
    </label>
  );
}
