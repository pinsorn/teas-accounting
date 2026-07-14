'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { PurchaseOrderForm } from '@/components/forms/PurchaseOrderForm';
import { usePurchaseOrder } from '@/lib/queries';

// WP3 3.3 (D2) — Draft-only edit (mirrors quotations/[id]/edit/page.tsx). A PO
// leaves the editable phase once it is no longer Draft — bounce back to the
// detail page rather than render an editable form on an Approved/Sent/Closed/
// Cancelled PO.
export default function PurchaseOrderEditPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const poId = Number(id);
  const tc = useTranslations('common');
  const router = useRouter();
  const q = usePurchaseOrder(poId);
  const d = q.data;

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  if (d.status !== 'Draft') {
    router.replace(`/purchase-orders/${poId}`);
    return <div className="p-6 text-base-content/50">{tc('loading')}</div>;
  }

  return <PurchaseOrderForm edit={d} />;
}
