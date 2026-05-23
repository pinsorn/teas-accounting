'use client';

import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';

// Route-level guard for VAT-only features (Tax Invoice, Credit/Debit Note, ภ.พ.30).
// A non-VAT company can't issue these (ม.86/4) — the nav hides them, and this catches
// direct URL access with an empty state instead of a broken form. Render it AFTER all
// hooks in the page (early-returning before later hooks breaks React's hook order).
export function NonVatGuard({ title }: { title: string }) {
  const tc = useTranslations('common');
  return (
    <>
      <PageHeader title={title} />
      <div className="rounded-card border border-ink-100 bg-base-100 p-8 text-center text-ink-600">
        {tc('nonVatUnavailable')}
      </div>
    </>
  );
}
