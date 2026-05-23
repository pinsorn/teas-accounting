'use client';

import Link from 'next/link';
import { useParams, useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { QueryStateRow } from '@/components/states/QueryState';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { ListFilters } from '@/components/ui/ListFilters';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import { applyListFilters } from '@/lib/list-filter';
import { useAdjustmentNotes, useAdjustmentNote, useCompanyProfile, useSystemInfo } from '@/lib/queries';
import { formatTHB, formatDate, formatTaxId } from '@/lib/utils';
import { PAPER_DOC, paperWatermark, companyToSeller } from '@/lib/paper-doc-config';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';
import { NonVatGuard } from '@/components/ui/NonVatGuard';

type Kind = 'Credit' | 'Debit';
const cfg = (k: Kind) =>
  k === 'Credit'
    ? { api: 'CREDIT' as const, base: '/credit-notes', titleKey: 'cnTitle' }
    : { api: 'DEBIT' as const, base: '/debit-notes', titleKey: 'dnTitle' };

export function AdjustmentNoteList({ kind }: { kind: Kind }) {
  const c = cfg(kind);
  const t = useTranslations('note');
  const tc = useTranslations('common');
  const params = useSearchParams();
  const buId = params.get('bu') ? Number(params.get('bu')) : undefined;
  // Sprint 13i C3 — BU stays server-side (paginated); status + customer + date
  // filter the loaded rows client-side. All URL-persisted.
  const q = useAdjustmentNotes(c.api, buId);
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const rows = applyListFilters(q.data?.pages.flatMap((p) => p.items) ?? [], params, {
    status: (r) => r.status,
    customerId: (r) => r.customerId,
    docDate: (r) => r.docDate,
  });

  // CN/DN adjust a Tax Invoice's VAT (ม.86/10) — non-VAT issues none → hidden.
  if (!vatMode) return <NonVatGuard title={t(c.titleKey)} />;

  return (
    <>
      <PageHeader
        title={t(c.titleKey)}
        actions={
          <Link href={`${c.base}/new`} className="btn btn-primary btn-sm gap-1">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </Link>
        }
      />
      <ListFilters statusOptions={['Draft', 'Posted', 'Voided']} statusTestId="note-filter-status" />
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr><th>{t('colNo')}</th><th>{t('colDate')}</th><th>{t('colCustomer')}</th><th className="text-right">{t('colTotal')}</th><th>{t('colOrigTi')}</th><th>{t('colStatus')}</th><th className="text-right" /></tr></thead>
          <tbody>
            <QueryStateRow query={q} colSpan={7} isEmpty={rows.length === 0} />
            {rows.map((r) => (
              <tr key={r.noteId} className="hover">
                <td><Link href={`${c.base}/${r.noteId}`}><DocumentNumberBadge value={r.docNo} /></Link></td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td>{r.customerName}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalAmount)}</td>
                <td className="font-mono">#{r.originalTaxInvoiceId}</td>
                <td><StatusBadge status={r.status} /></td>
                <td className="text-right">
                  <Link href={`${c.base}/${r.noteId}`} className="link link-primary text-sm">
                    {tc('view')}
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {q.hasNextPage && (
        <div className="mt-4 text-center">
          <button className="btn btn-ghost btn-sm" onClick={() => q.fetchNextPage()} disabled={q.isFetchingNextPage}>
            {tc('loadMore')}
          </button>
        </div>
      )}
    </>
  );
}

export function AdjustmentNoteDetailView({ kind }: { kind: Kind }) {
  const c = cfg(kind);
  const id = Number(useParams<{ id: string }>().id);
  const t = useTranslations('note');
  const tc = useTranslations('common');
  const { data: d, isLoading, isError } = useAdjustmentNote(id);
  const company = useCompanyProfile();
  const vatMode = useSystemInfo().data?.vatMode ?? true;

  if (!vatMode) return <NonVatGuard title={t(c.titleKey)} />;
  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  const paperKind = kind === 'Credit' ? 'credit-note' : 'debit-note';
  const activityType = kind === 'Credit' ? 'credit-notes' : 'debit-notes';
  const pcfg = PAPER_DOC[paperKind];

  // Adjustment notes carry no line array — synthesize one line from the
  // reason + adjusted value (ม.86/10 value-difference disclosure).
  const items = [{ description: `เหตุผล (${d.reasonCode}): ${d.reason}`, amount: d.subtotalAmount }];

  const extraMeta = (
    <>
      <dt>อ้างอิงใบกำกับภาษี</dt>
      <dd>{d.originalTiDocNo ?? `#${d.originalTaxInvoiceId}`}</dd>
    </>
  );

  return (
    <>
      <PageHeader
        title={t(c.titleKey)}
        subtitle={d.docNo ?? undefined}
        actions={<PrintMenu docType={activityType} id={id} fiscal />}
      />

      <DocActionBar status={d.status} docNo={d.docNo ?? `#${d.noteId}`} />

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            docType={pcfg.docType}
            docTypeEn={pcfg.docTypeEn}
            docNo={d.docNo ?? `#${d.noteId}`}
            issueDate={d.docDate}
            seller={companyToSeller(company.data)}
            customer={{
              name: d.customerName,
              taxId: d.customerTaxId ? formatTaxId(d.customerTaxId) : null,
              address: d.customerAddress,
            }}
            items={items}
            summary={{ subtotal: d.subtotalAmount, vat: d.taxAmount, total: d.totalAmount, vatRate: d.taxRate * 100 }}
            notes={d.notes}
            signRoles={pcfg.signRoles}
            watermark={paperWatermark(paperKind, d.status)}
            extraMetaBlock={extraMeta}
          />
        </div>
        <div className="detail-side">
          <DocumentChain type="adjustment-note" id={id} />
          <ActivityLog docType={activityType} id={id} />
        </div>
      </div>

      <AttachmentsSection parentType="TAX_ADJUSTMENT_NOTE" parentId={id} />
    </>
  );
}
