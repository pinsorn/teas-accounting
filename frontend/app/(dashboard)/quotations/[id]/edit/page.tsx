'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { QuotationForm } from '@/components/forms/QuotationForm';
import { useQuotation } from '@/lib/queries';

// Sprint 13i C1 — Draft-only edit. Non-Draft quotations are immutable past Send;
// bounce back to the detail page rather than render an editable form.
export default function QuotationEditPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const qid = Number(id);
  const tc = useTranslations('common');
  const router = useRouter();
  const q = useQuotation(qid);
  const d = q.data;

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  if (d.status !== 'Draft') {
    router.replace(`/quotations/${qid}`);
    return <div className="p-6 text-base-content/50">{tc('loading')}</div>;
  }

  return <QuotationForm edit={d} />;
}
