'use client';

import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Download, FileCode, Send, Printer } from 'lucide-react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { useTaxInvoice, useSystemInfo } from '@/lib/queries';
import { apiPost, downloadFile } from '@/lib/api';
import { formatTHB, formatDate, formatTaxId } from '@/lib/utils';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';

export default function TaxInvoiceDetailPage() {
  const params = useParams<{ id: string }>();
  const id = Number(params.id);
  const t = useTranslations('ti.detail');
  const tc = useTranslations('common');
  const tb = useTranslations('businessUnit');
  const { data: d, isLoading, isError } = useTaxInvoice(id);
  const { data: sys } = useSystemInfo();

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

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={d.docNo ?? undefined}
        actions={
          <>
            <button
              className="btn btn-ghost btn-sm gap-1"
              onClick={() => downloadFile(`tax-invoices/${id}/pdf`, `tax-invoice-${id}.pdf`)}
            >
              <Download className="h-4 w-4" aria-hidden /> {t('downloadPdf')}
            </button>
            {/* Sprint 8.5 — e-Tax is VAT-registered only (ม.3 อัฏฐ). Hide for non-VAT. */}
            {sys?.vatMode && (
              <>
                <button
                  className="btn btn-ghost btn-sm gap-1"
                  onClick={() => downloadFile(`tax-invoices/${id}/xml`, `tax-invoice-${id}.xml`)}
                >
                  <FileCode className="h-4 w-4" aria-hidden /> {t('downloadXml')}
                </button>
                <button className="btn btn-ghost btn-sm gap-1" onClick={resend}>
                  <Send className="h-4 w-4" aria-hidden /> {t('resend')}
                </button>
              </>
            )}
            <button className="btn btn-ghost btn-sm gap-1" onClick={() => window.print()}>
              <Printer className="h-4 w-4" aria-hidden /> {t('print')}
            </button>
          </>
        }
      />

      <div className="mb-4 flex items-center gap-3">
        <DocumentNumberBadge value={d.docNo} />
        <StatusBadge status={d.status} />
        {d.businessUnitCode && (
          <span className="badge badge-outline">{tb('title')}: {d.businessUnitCode}</span>
        )}
        <span className="text-sm text-base-content/60">{formatDate(d.docDate)}</span>
      </div>

      <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
        <section className="card bg-base-100 shadow-sm">
          <div className="card-body">
            <h2 className="card-title text-base">{t('seller')}</h2>
            <p>{d.supplierName}</p>
            <p className="text-sm text-base-content/60">{d.supplierAddress}</p>
            <p className="text-sm">{t('taxId')}: {formatTaxId(d.supplierTaxId)} · {t('branch')}: {d.supplierBranchCode}</p>
          </div>
        </section>
        <section className="card bg-base-100 shadow-sm">
          <div className="card-body">
            <h2 className="card-title text-base">{t('buyer')}</h2>
            <p>{d.customerName}</p>
            <p className="text-sm text-base-content/60">{d.customerAddress}</p>
            {d.customerTaxId && (
              <p className="text-sm">{t('taxId')}: {formatTaxId(d.customerTaxId)} · {t('branch')}: {d.customerBranchCode ?? '-'}</p>
            )}
          </div>
        </section>
      </div>

      <section className="mt-6 overflow-x-auto rounded-lg border border-base-300">
        <table className="table">
          <thead>
            <tr>
              <th>#</th><th>{t('lines')}</th>
              <th className="text-right">Qty</th>
              <th className="text-right">Price</th>
              <th className="text-right">Amount</th>
            </tr>
          </thead>
          <tbody>
            {d.lines.map((l) => (
              <tr key={l.lineNo}>
                <td>{l.lineNo}</td>
                <td>{l.descriptionTh}</td>
                <td className="text-right tabular-nums">{l.quantity}</td>
                <td className="text-right tabular-nums">{formatTHB(l.unitPrice)}</td>
                <td className="text-right tabular-nums">{formatTHB(l.lineAmount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <div className="mt-4 flex justify-end">
        <dl className="w-64 space-y-1 text-sm">
          <div className="flex justify-between">
            <dt className="text-base-content/60">{t('subtotal')}</dt>
            <dd className="tabular-nums">{formatTHB(d.subtotalAmount)}</dd>
          </div>
          <div className="flex justify-between">
            <dt className="text-base-content/60">{t('vat')}</dt>
            <dd className="tabular-nums">{formatTHB(d.taxAmount)}</dd>
          </div>
          <div className="flex justify-between text-base font-bold">
            <dt>{t('total')}</dt>
            <dd className="tabular-nums">{formatTHB(d.totalAmount)}</dd>
          </div>
        </dl>
      </div>
      <AttachmentsSection parentType="TAX_INVOICE" parentId={id} />
    </>
  );
}
