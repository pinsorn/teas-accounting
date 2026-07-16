'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { BillingNoteForm } from '@/components/forms/BillingNoteForm';
import { useBillingNote } from '@/lib/queries';

// S15 (F6-parity) — Draft-only edit (mirrors quotations/[id]/edit/page.tsx). An
// Invoice leaves the editable phase once it is no longer Draft — bounce back to
// the detail page rather than render an editable form on an Issued/Settled/
// Cancelled Invoice.
export default function BillingNoteEditPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const bnId = Number(id);
  const tc = useTranslations('common');
  const router = useRouter();
  const q = useBillingNote(bnId);
  const d = q.data;

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  if (d.status !== 'Draft') {
    router.replace(`/invoices/${bnId}`);
    return <div className="p-6 text-base-content/50">{tc('loading')}</div>;
  }

  return <BillingNoteForm edit={d} />;
}
