'use client';

import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Download } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { useReceipt } from '@/lib/queries';
import { downloadFile } from '@/lib/api';
import { formatTHB, formatDate } from '@/lib/utils';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';

export default function ReceiptDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const tc = useTranslations('common');
  const tb = useTranslations('businessUnit');
  const tw = useTranslations('rc.wht');
  const { data: d, isLoading, isError } = useReceipt(id);

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  const appliedBuCodes = [...new Set(
    d.appliedTo.map((a) => a.businessUnitCode).filter((c): c is string => !!c))];
  const crossesBu = appliedBuCodes.length > 1;

  return (
    <>
      <PageHeader
        title="ใบเสร็จรับเงิน"
        subtitle={d.docNo ?? undefined}
        actions={
          <button className="btn btn-ghost btn-sm gap-1"
            onClick={() => downloadFile(`receipts/${id}/pdf`, `receipt-${id}.pdf`)}>
            <Download className="h-4 w-4" aria-hidden /> PDF
          </button>
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
      {crossesBu && (
        <div role="alert" className="alert alert-warning mb-4 text-sm">
          {tb('crossBuWarning', { n: appliedBuCodes.length, codes: appliedBuCodes.join(', ') })}
        </div>
      )}
      <div className="card bg-base-100 shadow-sm">
        <div className="card-body">
          <p><b>ลูกค้า:</b> {d.customerName}</p>
          <p><b>วิธีชำระ:</b> {d.paymentMethod}{d.chequeNo ? ` (${d.chequeNo})` : ''}</p>
          <p className="text-lg font-bold tabular-nums">{formatTHB(d.amount)} {d.currencyCode}</p>
          <h3 className="mt-2 font-semibold">ชำระสำหรับ</h3>
          <table className="table">
            <thead><tr><th>ใบกำกับภาษี</th><th>{tb('title')}</th><th className="text-right">จำนวน</th></tr></thead>
            <tbody>
              {d.appliedTo.map((a) => (
                <tr key={a.taxInvoiceId}>
                  <td className="font-mono">{a.tiDocNo ?? a.taxInvoiceId}</td>
                  <td>{a.businessUnitCode ?? <span className="text-base-content/40">{tb('none')}</span>}</td>
                  <td className="text-right tabular-nums">{formatTHB(a.appliedAmount)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {d.whtAmount > 0 && (
            <div className="mt-4 rounded-lg border border-base-300 p-3 text-sm">
              <h3 className="mb-2 font-semibold">{tw('title')}</h3>
              <div className="grid grid-cols-2 gap-x-6 gap-y-1 md:grid-cols-3">
                <p><b>{tw('type')}:</b> {d.whtTypeCode ?? '—'}</p>
                <p><b>{tw('rate')}:</b> {(d.whtRate * 100).toFixed(2)}%</p>
                <p><b>{tw('base')}:</b> <span className="tabular-nums">{formatTHB(d.whtBase)}</span></p>
                <p><b>{tw('amount')}:</b> <span className="tabular-nums">({formatTHB(d.whtAmount)})</span></p>
                <p><b>{tw('cashReceived')}:</b> <span className="tabular-nums font-bold">{formatTHB(d.cashReceived)}</span></p>
                <p><b>{tw('certNo')}:</b> {d.customerWhtCertNo ?? '—'}
                  {d.customerWhtCertDate ? ` (${formatDate(d.customerWhtCertDate)})` : ''}</p>
              </div>
            </div>
          )}
        </div>
      </div>
      <AttachmentsSection parentType="RECEIPT" parentId={id} />
    </>
  );
}
