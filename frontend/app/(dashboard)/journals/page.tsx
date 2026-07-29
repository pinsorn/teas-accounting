'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { DataTable, RowLink } from '@/components/ui/DataTable';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { useJournals } from '@/lib/queries';
import type { JournalListItem } from '@/lib/types';
import { formatTHB, bangkokMonthStart, bangkokMonthEnd } from '@/lib/utils';

// specs/manual-jv-and-coa-management.md §B8.1 — manual JV list. Date-range filter
// (defaults to the current month, like reports/general-ledger) feeds the server-side
// GET /journals?from&to&search; DataTable itself just renders/sorts the returned page
// (its own global-search box is off — the search box below already narrows server-side).
export default function JournalsListPage() {
  const t = useTranslations('jv');
  const tc = useTranslations('common');
  const [from, setFrom] = useState(bangkokMonthStart());
  const [to, setTo] = useState(bangkokMonthEnd());
  const [search, setSearch] = useState('');
  const q = useJournals({ from, to, search: search || undefined });

  const columns = useMemo<ColumnDef<JournalListItem>[]>(() => [
    {
      accessorKey: 'docNo', header: t('docNo'),
      cell: ({ row }) => (
        <RowLink href={`/journals/${row.original.journalId}`} mono>
          {row.original.docNo ?? '—'}
        </RowLink>
      ),
    },
    { accessorKey: 'docDate', header: t('docDate') },
    { accessorKey: 'description', header: t('description') },
    {
      accessorKey: 'reference', header: t('reference'),
      cell: ({ getValue }) => <span>{getValue<string | null>() ?? '—'}</span>,
    },
    {
      accessorKey: 'totalDebit', header: t('totalDebit'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      accessorKey: 'totalCredit', header: t('totalCredit'), meta: { align: 'right' },
      cell: ({ getValue }) => <span className="tabular-nums">{formatTHB(getValue<number>())}</span>,
    },
    {
      accessorKey: 'status', header: tc('status'), meta: { filter: 'select' },
      cell: ({ getValue }) => <StatusBadge status={getValue<string>()} />,
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [t, tc]);

  return (
    <>
      <PageHeader
        title={t('listTitle')}
        actions={
          <PermissionGate scope="gl.journal.post">
            <Link href="/journals/new" data-testid="jv-create" className="btn btn-primary btn-sm gap-1">
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </Link>
          </PermissionGate>
        }
      />

      <div className="mb-4 flex flex-wrap items-end gap-3 rounded-card border border-ink-100 bg-base-100 p-3 shadow-warm-sm">
        <label className="form-control">
          <span className="label-text text-ink-600">{tc('from')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="form-control">
          <span className="label-text text-ink-600">{tc('to')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <label className="form-control min-w-[14rem] flex-1">
          <span className="label-text text-ink-600">{tc('search')}</span>
          <input className="input input-bordered input-sm" value={search}
            placeholder={t('searchPlaceholder')} aria-label={tc('search')}
            onChange={(e) => setSearch(e.target.value)} />
        </label>
      </div>

      <DataTable
        data={q.data ?? []}
        columns={columns}
        isLoading={q.isLoading}
        getRowId={(r) => String(r.journalId)}
        globalSearch={false}
      />
    </>
  );
}
