'use client';

import { use } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { ReceiptForm } from '@/components/forms/ReceiptForm';
import { useReceipt, useReceiptDraftInput } from '@/lib/queries';

// draft-edit-receipt-taxinvoice — Draft-only edit (mirrors invoices/[id]/edit/page.tsx). The form is
// prefilled from GET /receipts/{id}/draft-input, NOT from the detail DTO (which drops payment
// method / bank account / settlement fields).
export default function ReceiptEditPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const rcId = Number(id);
  const t = useTranslations('rc');
  const tc = useTranslations('common');
  const tcr = useTranslations('create');
  const router = useRouter();
  const d = useReceipt(rcId).data;
  const input = useReceiptDraftInput(rcId, d?.status === 'Draft').data;

  const loading = <div className="p-6 text-base-content/50">{tc('loading')}</div>;
  if (!d) return loading;
  if (d.status !== 'Draft') {
    router.replace(`/receipts/${rcId}`);
    return loading;
  }
  if (!input) return loading;

  // FE-only guard (not a server rule): the form has no delivery-order mode and no "applications +
  // own lines" mode, and the forced whtAmount:0 would drop a legacy scalar-only WHT. The draft
  // stays postable and API/MCP-editable, so nothing is trapped.
  const unsupported =
    input.applications.some((a) => a.deliveryOrderId != null)
    || (input.applications.length > 0 && (input.lines?.length ?? 0) > 0)
    || ((input.whtAmount ?? 0) > 0 && (input.whtLines?.length ?? 0) === 0);
  if (unsupported) {
    return (
      <div className="mx-auto max-w-xl space-y-4 py-12 text-center" data-testid="rc-edit-unsupported">
        <p className="text-sm text-base-content/70">{t('editUnsupported')}</p>
        <Link href={`/receipts/${rcId}`} className="btn btn-secondary btn-sm">
          {tcr('cancel')}
        </Link>
      </div>
    );
  }

  return (
    <ReceiptForm
      edit={{ id: rcId, input, customerName: d.customerName, whtViews: d.whtLines ?? [] }}
    />
  );
}
