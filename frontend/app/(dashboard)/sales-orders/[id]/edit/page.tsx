'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { SalesOrderForm } from '@/components/forms/SalesOrderForm';
import { useSalesOrder } from '@/lib/queries';

// S15 (F6-parity) — Draft-only edit (mirrors quotations/[id]/edit/page.tsx). A
// Sales Order leaves the editable phase once it is no longer Draft — bounce back
// to the detail page rather than render an editable form on a Posted SO.
export default function SalesOrderEditPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const soId = Number(id);
  const tc = useTranslations('common');
  const router = useRouter();
  const q = useSalesOrder(soId);
  const d = q.data;

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  if (d.status !== 'Draft') {
    router.replace(`/sales-orders/${soId}`);
    return <div className="p-6 text-base-content/50">{tc('loading')}</div>;
  }

  return <SalesOrderForm edit={d} />;
}
