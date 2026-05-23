'use client';

import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { useCrossReferences } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';
import type { CrossRefDocType, DocumentRef, ReceiptRef } from '@/lib/types';

// Sprint 13h P8 — render the document cross-reference graph as clickable chips.
// Mirrors the chip pattern from the BN detail page (ckpt3) so TI/RC/CN/DN
// detail surfaces share one component.

function Chip({ href, testId, label }: { href: string; testId?: string; label: string }) {
  return (
    <Link
      data-testid={testId}
      href={href}
      className="badge badge-outline badge-sm link link-primary"
    >
      {label}
    </Link>
  );
}

export function CrossRefChipRow({ docType, id }: { docType: CrossRefDocType; id: number }) {
  const q = useCrossReferences(docType, id);
  const t = useTranslations('crossRef');
  if (!q.data) return null;
  const d = q.data;

  const hasAny =
    d.quotation || d.salesOrder || d.deliveryOrder ||
    d.taxInvoices.length || d.receipts.length ||
    d.creditNotes.length || d.debitNotes.length || d.billingNotes.length;
  if (!hasAny) return null;

  return (
    <div className="mb-4 flex flex-wrap items-center gap-2" data-testid="cross-ref-row">
      <span className="text-sm text-base-content/60">{t('title')}:</span>
      {d.quotation && (
        <Chip testId="xref-quotation"
          href={`/quotations/${d.quotation.id}`}
          label={`${t('quotation')} ${d.quotation.docNo ?? `#${d.quotation.id}`}`} />
      )}
      {d.salesOrder && (
        <Chip testId="xref-sales-order"
          href={`/sales-orders/${d.salesOrder.id}`}
          label={`${t('salesOrder')} ${d.salesOrder.docNo ?? `#${d.salesOrder.id}`}`} />
      )}
      {d.deliveryOrder && (
        <Chip testId="xref-delivery-order"
          href={`/delivery-orders/${d.deliveryOrder.id}`}
          label={`${t('deliveryOrder')} ${d.deliveryOrder.docNo ?? `#${d.deliveryOrder.id}`}`} />
      )}
      {d.taxInvoices.map((r: DocumentRef) => (
        <Chip key={`ti-${r.id}`} testId={`xref-tax-invoice-${r.id}`}
          href={`/tax-invoices/${r.id}`}
          label={`${t('taxInvoice')} ${r.docNo ?? `#${r.id}`}`} />
      ))}
      {d.receipts.map((r: ReceiptRef) => (
        <Chip key={`rc-${r.id}`} testId={`xref-receipt-${r.id}`}
          href={`/receipts/${r.id}`}
          label={`${t('receipt')} ${r.docNo ?? `#${r.id}`} (${formatTHB(r.appliedAmount)})`} />
      ))}
      {d.creditNotes.map((r: DocumentRef) => (
        <Chip key={`cn-${r.id}`} testId={`xref-credit-note-${r.id}`}
          href={`/credit-notes/${r.id}`}
          label={`${t('creditNote')} ${r.docNo ?? `#${r.id}`}`} />
      ))}
      {d.debitNotes.map((r: DocumentRef) => (
        <Chip key={`dn-${r.id}`} testId={`xref-debit-note-${r.id}`}
          href={`/debit-notes/${r.id}`}
          label={`${t('debitNote')} ${r.docNo ?? `#${r.id}`}`} />
      ))}
      {d.billingNotes.map((r: DocumentRef) => (
        <Chip key={`bn-${r.id}`} testId={`xref-billing-note-${r.id}`}
          href={`/invoices/${r.id}`}
          label={`${t('billingNote')} ${r.docNo ?? `#${r.id}`}`} />
      ))}
    </div>
  );
}
