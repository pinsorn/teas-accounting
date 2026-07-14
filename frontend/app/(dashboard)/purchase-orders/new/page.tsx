'use client';

import { PurchaseOrderForm } from '@/components/forms/PurchaseOrderForm';

// WP3 3.3 (D2) — the full form now lives in PurchaseOrderForm (shared with
// /purchase-orders/[id]/edit). This page stays as a thin create-mode wrapper.
export default function NewPurchaseOrderPage() {
  return <PurchaseOrderForm />;
}
