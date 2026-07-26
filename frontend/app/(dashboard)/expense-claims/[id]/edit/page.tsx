'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { ExpenseClaimForm } from '@/components/forms/ExpenseClaimForm';
import { useExpenseClaim } from '@/lib/queries';

// O4 — Draft/Rejected edit route, following the sibling Invoice and Purchase
// Order routes: fetch the detail, enforce the editable statuses, reuse the form.
export default function ExpenseClaimEditPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const claimId = Number(id);
  const tc = useTranslations('common');
  const router = useRouter();
  const q = useExpenseClaim(claimId);
  const d = q.data;

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  if (d.status !== 'Draft' && d.status !== 'Rejected') {
    router.replace(`/expense-claims/${claimId}`);
    return <div className="p-6 text-base-content/50">{tc('loading')}</div>;
  }

  return <ExpenseClaimForm edit={d} />;
}
