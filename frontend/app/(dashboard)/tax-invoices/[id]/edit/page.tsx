'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { TaxInvoiceForm } from '@/components/forms/TaxInvoiceForm';
import { useTaxInvoice, useTaxInvoiceDraftInput } from '@/lib/queries';

// draft-edit-receipt-taxinvoice — Draft-only edit (mirrors invoices/[id]/edit/page.tsx). Prefilled
// from GET /tax-invoices/{id}/draft-input so quotationId / terms / discount / tax-code ids survive.
export default function TaxInvoiceEditPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const tiId = Number(id);
  const tc = useTranslations('common');
  const router = useRouter();
  const d = useTaxInvoice(tiId).data;
  const input = useTaxInvoiceDraftInput(tiId, d?.status === 'Draft').data;

  const loading = <div className="p-6 text-base-content/50">{tc('loading')}</div>;
  if (!d) return loading;
  if (d.status !== 'Draft') {
    router.replace(`/tax-invoices/${tiId}`);
    return loading;
  }
  if (!input) return loading;

  return <TaxInvoiceForm edit={{ id: tiId, input, customerName: d.customerName }} />;
}
