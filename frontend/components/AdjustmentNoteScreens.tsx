'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Plus, Download } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { DocumentNumberBadge } from '@/components/ui/DocumentNumberBadge';
import { useAdjustmentNotes, useAdjustmentNote, useBusinessUnits } from '@/lib/queries';
import { downloadFile } from '@/lib/api';
import { formatTHB, formatDate } from '@/lib/utils';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';

type Kind = 'Credit' | 'Debit';
const cfg = (k: Kind) =>
  k === 'Credit'
    ? { api: 'CREDIT' as const, base: '/credit-notes', titleKey: 'cnTitle' }
    : { api: 'DEBIT' as const, base: '/debit-notes', titleKey: 'dnTitle' };

export function AdjustmentNoteList({ kind }: { kind: Kind }) {
  const c = cfg(kind);
  const t = useTranslations('note');
  const tc = useTranslations('common');
  const tb = useTranslations('businessUnit');
  const [buId, setBuId] = useState<number | undefined>();
  const [includeUnspec, setIncludeUnspec] = useState(false);
  const { data: bus = [] } = useBusinessUnits();
  const q = useAdjustmentNotes(c.api, buId, includeUnspec || undefined);
  const rows = q.data?.pages.flatMap((p) => p.items) ?? [];

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
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{tb('filter')}</span>
          <select
            className="select select-bordered select-sm"
            aria-label={tb('filter')}
            value={buId ?? ''}
            onChange={(e) => setBuId(e.target.value ? Number(e.target.value) : undefined)}
          >
            <option value="">{tc('all')}</option>
            {bus.map((u) => (
              <option key={u.businessUnitId} value={u.businessUnitId}>{u.code}</option>
            ))}
          </select>
        </label>
        <label className="label cursor-pointer gap-2 self-end">
          <input
            type="checkbox"
            className="checkbox checkbox-sm"
            checked={includeUnspec}
            onChange={(e) => setIncludeUnspec(e.target.checked)}
          />
          <span className="label-text text-xs">{tb('includeUnspecified')}</span>
        </label>
      </div>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead><tr><th>No.</th><th>Date</th><th>Customer</th><th className="text-right">Total</th><th>Orig. TI</th><th>Status</th></tr></thead>
          <tbody>
            {q.isLoading && <tr><td colSpan={6} className="py-8 text-center text-base-content/50">{tc('loading')}</td></tr>}
            {!q.isLoading && rows.length === 0 && <tr><td colSpan={6} className="py-8 text-center text-base-content/50">{tc('empty')}</td></tr>}
            {rows.map((r) => (
              <tr key={r.noteId} className="hover">
                <td><Link href={`${c.base}/${r.noteId}`}><DocumentNumberBadge value={r.docNo} /></Link></td>
                <td className="tabular-nums">{formatDate(r.docDate)}</td>
                <td>{r.customerName}</td>
                <td className="text-right tabular-nums">{formatTHB(r.totalAmount)}</td>
                <td className="font-mono">#{r.originalTaxInvoiceId}</td>
                <td><StatusBadge status={r.status} /></td>
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
  const tb = useTranslations('businessUnit');
  const { data: d, isLoading, isError } = useAdjustmentNote(id);

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  return (
    <>
      <PageHeader
        title={t(c.titleKey)}
        subtitle={d.docNo ?? undefined}
        actions={
          <button className="btn btn-ghost btn-sm gap-1"
            onClick={() => downloadFile(`tax-adjustment-notes/${id}/pdf`, `note-${id}.pdf`)}>
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
      <div className="card bg-base-100 shadow-sm">
        <div className="card-body space-y-1">
          <p><b>อ้างอิงใบกำกับภาษี:</b> {d.originalTiDocNo ?? `#${d.originalTaxInvoiceId}`}</p>
          <p><b>ลูกค้า:</b> {d.customerName}</p>
          <p><b>เหตุผล ({d.reasonCode}):</b> {d.reason}</p>
          <p className="tabular-nums">มูลค่า {formatTHB(d.subtotalAmount)} · VAT {formatTHB(d.taxAmount)}</p>
          <p className="text-lg font-bold tabular-nums">รวม {formatTHB(d.totalAmount)} {d.currencyCode}</p>
        </div>
      </div>
      <AttachmentsSection parentType="TAX_ADJUSTMENT_NOTE" parentId={id} />
    </>
  );
}
