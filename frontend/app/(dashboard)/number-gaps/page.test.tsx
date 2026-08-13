// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest';
import '@testing-library/jest-dom/vitest';
import { render, screen, cleanup } from '@testing-library/react';
import NumberGapAuditPage from './page';
import { useNumberGaps } from '@/lib/queries';
import type { NumberGapReport } from '@/lib/types';

// H1 (specs/fix-duplicate-tax-doc-numbers.md) WP-3b / T11 — the verdict's exact complaint one layer
// up: the API can correctly report a duplicate while the SCREEN still shows a green "compliant"
// shield, because pre-fix `clean = gaps.length === 0` never looked at duplicates at all. This test
// pins `clean` to require BOTH lists empty, and that the duplicates table actually renders.

vi.mock('next-intl', () => ({
  useTranslations: () => (key: string) => key,
}));
vi.mock('@/lib/queries', () => ({
  useNumberGaps: vi.fn(),
}));

function mockReport(overrides: Partial<NumberGapReport>): NumberGapReport {
  return {
    year: null, month: null, docType: null,
    gaps: [], hasGaps: false, duplicates: [], hasDuplicates: false,
    ...overrides,
  };
}

describe('NumberGapAuditPage — green shield requires BOTH gaps and duplicates empty', () => {
  afterEach(() => cleanup());

  it('hides the green shield and renders the duplicate row when only duplicates exist', () => {
    vi.mocked(useNumberGaps).mockReturnValue({
      data: mockReport({
        duplicates: [{ table: 'sales.receipts', docNo: '07-2026-RC-LAB-0001', copies: 2, branchIds: [0, 2] }],
        hasDuplicates: true,
      }),
      isLoading: false, isError: false,
    } as ReturnType<typeof useNumberGaps>);

    render(<NumberGapAuditPage />);

    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    expect(screen.getByText('07-2026-RC-LAB-0001')).toBeInTheDocument();
  });

  it('shows the green shield when both gaps and duplicates are empty', () => {
    vi.mocked(useNumberGaps).mockReturnValue({
      data: mockReport({}),
      isLoading: false, isError: false,
    } as ReturnType<typeof useNumberGaps>);

    render(<NumberGapAuditPage />);

    expect(screen.getByRole('status')).toBeInTheDocument();
  });
});
