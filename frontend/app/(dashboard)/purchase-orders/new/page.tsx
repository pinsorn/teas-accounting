'use client';

import { useTranslations } from 'next-intl';
import { ShieldAlert } from 'lucide-react';
import { useMePermissions } from '@/lib/queries';
import { PurchaseOrderForm } from '@/components/forms/PurchaseOrderForm';

const SCOPE = 'purchase.purchase_order.create';

// WP3 3.3 (D2) — the full form now lives in PurchaseOrderForm (shared with
// /purchase-orders/[id]/edit). This page stays as a thin create-mode wrapper.
export default function NewPurchaseOrderPage() {
  const tc = useTranslations('common');
  const perms = useMePermissions();
  const canCreate = perms.data?.isSuperAdmin || (perms.data?.permissions.includes(SCOPE) ?? false);
  if (perms.data && !canCreate) {
    return (
      <div className="flex flex-col items-center gap-2 py-12 text-center" data-testid="state-no-access">
        <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
        <div className="font-semibold">{tc('noAccessTitle')}</div>
        <div className="max-w-md text-sm text-base-content/60">{tc('noAccessBody', { perm: SCOPE })}</div>
      </div>
    );
  }
  return <PurchaseOrderForm />;
}
