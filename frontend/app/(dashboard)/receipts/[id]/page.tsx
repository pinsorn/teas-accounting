'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import { ReceiptWhtCertSection } from '@/components/doc/ReceiptWhtCertSection';
import { useReceipt, useCompanyProfile, usePostReceipt } from '@/lib/queries';
import { formatTHB, formatTaxId } from '@/lib/utils';
import { PAPER_DOC, paperWatermark, companyToSeller } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { useHasScope } from '@/components/PermissionGate';

export default function ReceiptDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const tc = useTranslations('common');
  const tw = useTranslations('rc.wht');
  const ta = useTranslations('approve');
  const { data: d, isLoading, isError } = useReceipt(id);
  const company = useCompanyProfile();
  const post = usePostReceipt();
  const hasScope = useHasScope();
  const [isApproveAction, setIsApproveAction] = useState(false);

  useEffect(() => {
    const action = new URLSearchParams(window.location.search).get('action');
    if (action === 'approve') setIsApproveAction(true);
  }, []);

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  const cfg = PAPER_DOC.receipt;

  const extraMeta = (
    <>
      <dt>วิธีชำระ</dt>
      <dd>{d.paymentMethod}{d.chequeNo ? ` (${d.chequeNo})` : ''}</dd>
      {d.whtAmount > 0 && (
        <>
          <dt>{tw('amount')}</dt>
          <dd>({formatTHB(d.whtAmount)})</dd>
          <dt>{tw('cashReceived')}</dt>
          <dd>{formatTHB(d.cashReceived)}</dd>
        </>
      )}
    </>
  );

  return (
    <>
      <PageHeader
        title="ใบเสร็จรับเงิน"
        subtitle={d.docNo ?? undefined}
        actions={<PrintMenu docType="receipts" id={id} fiscal />}
      />

      {/* ?action=approve — prominent approval banner for agent-created drafts */}
      {isApproveAction && d.status === 'Draft' && (
        <div className="mb-4 rounded-lg border border-warning bg-warning/10 p-4">
          <p className="font-semibold text-warning-content">{ta('bannerTitle')}</p>
          <p className="mt-1 text-sm text-base-content/80">{ta('bannerDesc')}</p>
          <div className="mt-3">
            {hasScope('sales.receipt.post') ? (
              <button
                data-testid="rc-approve-cta"
                className="btn btn-warning btn-sm"
                disabled={post.isPending}
                onClick={async () => {
                  try {
                    await post.mutateAsync(id);
                    toast.success(tc('posted'));
                  } catch (e) {
                    toast.error((e as { detail?: string })?.detail ?? tc('error'));
                  }
                }}
              >
                {ta('cta')}
              </button>
            ) : (
              <p className="text-sm font-medium text-error">{ta('noPermission')}</p>
            )}
          </div>
        </div>
      )}
      {isApproveAction && d.status !== 'Draft' && (
        <div className="mb-4 rounded-lg border border-base-300 bg-base-200 p-3 text-sm text-base-content/60">
          {ta('alreadyPosted')}
        </div>
      )}

      <DocActionBar status={d.status} docNo={d.docNo ?? `#${d.receiptId}`} />

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            docType={cfg.docType}
            docTypeEn={cfg.docTypeEn}
            docNo={d.docNo ?? `#${d.receiptId}`}
            issueDate={d.docDate}
            seller={companyToSeller(company.data)}
            customer={{
              name: d.customerName,
              taxId: d.customerTaxId ? formatTaxId(d.customerTaxId) : null,
              branchCode: d.customerBranchCode,
              address: d.customerAddress,
            }}
            items={(d.lines && d.lines.length > 0
              ? d.lines.map((l) => ({
                  description: l.descriptionTh,
                  descriptionSub: l.tiDocNo ?? undefined,
                  quantity: l.quantity,
                  unit: l.uomText,
                  unitPrice: l.unitPrice,
                  amount: l.lineAmount,
                }))
              : d.appliedTo.map((a) => ({
                  description: `ใบกำกับภาษี ${a.tiDocNo ?? `#${a.taxInvoiceId}`}`,
                  descriptionSub: a.businessUnitCode ?? undefined,
                  amount: a.appliedAmount,
                })))}
            summary={{ subtotal: d.amount, vat: 0, total: d.amount }}
            notes={d.notes}
            signRoles={cfg.signRoles}
            watermark={paperWatermark('receipt', d.status)}
            extraMetaBlock={extraMeta}
          />
        </div>
        <div className="detail-side">
          {d.whtLines && d.whtLines.length > 0 && (
            <div className="rounded-lg border border-base-300 p-3">
              <h3 className="mb-2 text-sm font-semibold">{tw('title')}</h3>
              <table className="table table-sm">
                <thead><tr>
                  <th>{tw('type')}</th>
                  <th className="text-right">{tw('rate')}</th>
                  <th className="text-right">{tw('base')}</th>
                  <th className="text-right">{tw('amount')}</th>
                </tr></thead>
                <tbody>
                  {d.whtLines.map((w) => (
                    <tr key={w.whtTypeId}>
                      <td>{w.whtTypeCode}</td>
                      <td className="text-right tabular-nums">{(w.whtRate * 100).toFixed(2)}%</td>
                      <td className="text-right tabular-nums">{formatTHB(w.baseAmount)}</td>
                      <td className="text-right tabular-nums">{formatTHB(w.whtAmount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <DocumentChain type="receipt" id={id} />
          <ActivityLog docType="receipts" id={id} />
        </div>
      </div>

      <ReceiptWhtCertSection
        receiptId={id}
        whtAmount={d.whtAmount}
        certNo={d.customerWhtCertNo}
        certDate={d.customerWhtCertDate}
      />

      <AttachmentsSection parentType="RECEIPT" parentId={id} />
    </>
  );
}
