'use client';

import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { FileCode, Send, ReceiptText } from 'lucide-react';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import { useTaxInvoice, useSystemInfo } from '@/lib/queries';
import { apiPost, downloadFile } from '@/lib/api';
import { formatTaxId } from '@/lib/utils';
import { PAPER_DOC, paperWatermark } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { NonVatGuard } from '@/components/ui/NonVatGuard';

export default function TaxInvoiceDetailPage() {
  const params = useParams<{ id: string }>();
  const id = Number(params.id);
  const t = useTranslations('ti.detail');
  const tc = useTranslations('common');
  const { data: d, isLoading, isError } = useTaxInvoice(id);
  const { data: sys } = useSystemInfo();

  if (!sys?.vatMode && sys !== undefined) return <NonVatGuard title={t('title')} />;
  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  async function resend() {
    try {
      const r = await apiPost<{ sent: boolean; message: string }>(`tax-invoices/${id}/resend`);
      toast[r.sent ? 'success' : 'info'](r.message);
    } catch {
      toast.error(tc('error'));
    }
  }

  const cfg = PAPER_DOC['tax-invoice'];

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={d.docNo ?? undefined}
        actions={
          <>
            {/* Sprint 13j-tail — issue a Receipt against this posted TI (prefilled). */}
            {d.status === 'Posted' && d.paymentStatus !== 'PAID' && (
              <Link
                href={`/receipts/new?ti=${id}&customer=${d.customerId}&amount=${d.totalAmount}`}
                className="btn btn-primary btn-sm gap-1"
              >
                <ReceiptText className="h-4 w-4" aria-hidden /> {t('createReceipt')}
              </Link>
            )}
            <PrintMenu docType="tax-invoices" id={id} fiscal />
            {/* Sprint 8.5 — e-Tax is VAT-registered only (ม.3 อัฏฐ). Hide for non-VAT. */}
            {sys?.vatMode && (
              <>
                <button className="btn btn-ghost btn-sm gap-1" onClick={() => downloadFile(`tax-invoices/${id}/xml`, `tax-invoice-${id}.xml`)}>
                  <FileCode className="h-4 w-4" aria-hidden /> {t('downloadXml')}
                </button>
                <button className="btn btn-ghost btn-sm gap-1" onClick={resend}>
                  <Send className="h-4 w-4" aria-hidden /> {t('resend')}
                </button>
              </>
            )}
          </>
        }
      />

      <DocActionBar status={d.status} docNo={d.docNo ?? `#${d.taxInvoiceId}`} />

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            docType={cfg.docType}
            docTypeEn={cfg.docTypeEn}
            docNo={d.docNo ?? `#${d.taxInvoiceId}`}
            issueDate={d.docDate}
            validUntil={d.dueDate ?? undefined}
            validUntilLabel={cfg.validUntilLabel}
            seller={{
              name: d.supplierName,
              taxId: formatTaxId(d.supplierTaxId),
              branchCode: d.supplierBranchCode,
              address: d.supplierAddress,
            }}
            customer={{
              name: d.customerName,
              taxId: d.customerTaxId ? formatTaxId(d.customerTaxId) : null,
              branchCode: d.customerBranchCode,
              address: d.customerAddress,
            }}
            items={d.lines.map((l) => ({
              description: l.descriptionTh,
              quantity: l.quantity,
              unit: l.uomText,
              unitPrice: l.unitPrice,
              discountPercent: undefined,
              amount: l.lineAmount,
            }))}
            summary={{
              subtotal: d.subtotalAmount,
              discount: d.discountAmount || undefined,
              beforeVat: d.taxableAmount,
              vat: d.taxAmount,
              total: d.totalAmount,
            }}
            notes={d.notes}
            signRoles={cfg.signRoles}
            watermark={paperWatermark('tax-invoice', d.status)}
          />
        </div>
        <div className="detail-side">
          <DocumentChain type="tax-invoice" id={id} />
          <ActivityLog docType="tax-invoices" id={id} />
        </div>
      </div>

      <AttachmentsSection parentType="TAX_INVOICE" parentId={id} />
    </>
  );
}
