'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { useParams, useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DataTable, RowLink, dateRangeFilter } from '@/components/ui/DataTable';
import { DocActionBar } from '@/components/ui/DocActionBar';
import { PrintMenu } from '@/components/ui/PrintMenu';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { ActivityLog } from '@/components/doc/ActivityLog';
import { DocumentChain } from '@/components/doc/DocumentChain';
import { useAdjustmentNotes, useAdjustmentNote, useCompanyProfile, useSystemInfo, useBusinessUnitName } from '@/lib/queries';
import type { AdjustmentNoteListItem } from '@/lib/types';
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
  // cont.82 — CN/DN list rebuilt on the shared <DataTable>: fetch-all + client
  // global search, per-column filters (status / customer), sortable headers,
  // clickable docNo → detail. BU scope stays server-side via the `bu` URL param.
  const q = useAdjustmentNotes(c.api, buId);
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  const buName = useBusinessUnitName();

  const columns = useMemo<ColumnDef<AdjustmentNoteListItem>[]>(() => [
    {
      accessorKey: 'docNo',
      header: t('colNo'),
      cell: ({ row }) => (
        <RowLink href={`${c.base}/${row.original.noteId}`} mono>
          {row.original.docNo ?? `#${row.original.noteId}`}
        </RowLink>
      ),
    },
    {
      accessorKey: 'docDate',
      header: t('colDate'),
      meta: { filter: 'dateRange' },
      filterFn: dateRangeFilter,
      cell: ({ getValue }) => <span className="tabular-nums">{formatDate(getValue<string>())}</span>,
    },
    { accessorKey: 'customerName', header: t('colCustomer'), meta: { filter: 'text', filterLabel: t('colCustomer') } },
    {
      id: 'businessUnit',
      accessorFn: (r) => buName(r.businessUnitId),
      header: tc('businessUnit'),
      meta: { filter: 'select' },
      cell: ({ getValue }) => <span className="text-sm text-base-content/70">{getValue<string>()}</span>,
    },
    {
      accessorKey: 'totalAmount', header: t('colTotal'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      accessorKey: 'originalTaxInvoiceId', header: t('colOrigTi'),
      cell: ({ getValue }) => <span className="font-mono">#{getValue<number>()}</span>,
    },
    {
      accessorKey: 'status', header: t('colStatus'), meta: { filter: 'select', filterLabel: t('colStatus') },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
    {
      id: 'actions', header: '', enableSorting: false, meta: { align: 'right' },
      cell: ({ row }) => (
        <Link href={`${c.base}/${row.original.noteId}`} className="link link-primary text-sm">
          {tc('view')}
        </Link>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc, c.base]);

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
      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.noteId)}
        searchPlaceholder={t('colNo')}
        initialSorting={[{ id: 'docDate', desc: true }]}
      />
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
