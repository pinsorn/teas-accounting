'use client';

import Link from 'next/link';
import { useState, type ReactNode } from 'react';
import {
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  getPaginationRowModel,
  getFacetedRowModel,
  getFacetedUniqueValues,
  useReactTable,
  type ColumnDef,
  type SortingState,
  type ColumnFiltersState,
  type Column,
  type Row,
  type RowData,
} from '@tanstack/react-table';
import { useTranslations } from 'next-intl';
import { ArrowUpDown, ArrowUp, ArrowDown, Search, ChevronLeft, ChevronRight } from 'lucide-react';

// cont.81 (Ham) — ONE TanStack-powered table for every list page. Unified style
// (the existing `table table-zebra` in a rounded card), global search, per-column
// filters (text / faceted select), click-to-sort headers, client pagination.
// Column behaviour is data-driven via `meta`:
//   meta.align       'right' | 'center'         — cell + header alignment
//   meta.filter      'text' | 'select'          — render a filter control for the column
//   meta.filterLabel string                     — placeholder/label for that filter
// Pages supply `columns` (use the helpers below) + `data` (a flat array — cursor lists
// fetch-all first via fetchAllPages). Filtering/sorting run client-side over the WHOLE set.

declare module '@tanstack/react-table' {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  interface ColumnMeta<TData extends RowData, TValue> {
    align?: 'right' | 'center';
    filter?: 'text' | 'select';
    filterLabel?: string;
  }
}

const alignClass = (a?: 'right' | 'center') =>
  a === 'right' ? 'text-right' : a === 'center' ? 'text-center' : '';

/** cont.81 — the clickable primary cell (docNo / name) every list row carries. */
export function RowLink({ href, children, mono = false }: { href: string; children: ReactNode; mono?: boolean }) {
  return (
    <Link href={href} className={`font-medium text-peach-700 hover:underline ${mono ? 'font-mono' : ''}`}>
      {children}
    </Link>
  );
}

export function DataTable<T>({
  data,
  columns,
  getRowId,
  isLoading = false,
  globalSearch = true,
  searchPlaceholder,
  initialSorting = [],
  pageSize = 25,
  emptyContent,
}: {
  data: T[];
  columns: ColumnDef<T, unknown>[];
  getRowId?: (row: T) => string;
  isLoading?: boolean;
  globalSearch?: boolean;
  searchPlaceholder?: string;
  initialSorting?: SortingState;
  pageSize?: number;
  emptyContent?: ReactNode;
}) {
  const tc = useTranslations('common');
  const [sorting, setSorting] = useState<SortingState>(initialSorting);
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
  const [globalFilter, setGlobalFilter] = useState('');

  const table = useReactTable({
    data,
    columns,
    state: { sorting, columnFilters, globalFilter },
    getRowId: getRowId ? (r) => getRowId(r) : undefined,
    onSortingChange: setSorting,
    onColumnFiltersChange: setColumnFilters,
    onGlobalFilterChange: setGlobalFilter,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    getFacetedRowModel: getFacetedRowModel(),
    getFacetedUniqueValues: getFacetedUniqueValues(),
    initialState: { pagination: { pageSize } },
  });

  const filterCols = table.getAllLeafColumns().filter((c) => c.columnDef.meta?.filter);
  const rows = table.getRowModel().rows;
  const totalFiltered = table.getFilteredRowModel().rows.length;

  return (
    <div className="flex flex-col gap-3">
      {/* Toolbar: global search + per-column filters */}
      {(globalSearch || filterCols.length > 0) && (
        <div className="flex flex-wrap items-end gap-3 rounded-card border border-ink-100 bg-base-100 p-3 shadow-warm-sm">
          {globalSearch && (
            <label className="form-control min-w-[14rem] flex-1">
              <span className="label-text text-ink-600">{tc('search')}</span>
              <span className="relative">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" aria-hidden />
                <input
                  className="input input-bordered w-full pl-9"
                  value={globalFilter}
                  onChange={(e) => setGlobalFilter(e.target.value)}
                  placeholder={searchPlaceholder ?? tc('search')}
                  aria-label={tc('search')}
                  data-testid="dt-search"
                />
              </span>
            </label>
          )}
          {filterCols.map((col) => (
            <ColumnFilter key={col.id} column={col} allLabel={tc('all')} />
          ))}
        </div>
      )}

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            {table.getHeaderGroups().map((hg) => (
              <tr key={hg.id}>
                {hg.headers.map((h) => {
                  const canSort = h.column.getCanSort();
                  const sorted = h.column.getIsSorted();
                  return (
                    <th key={h.id} className={alignClass(h.column.columnDef.meta?.align)}>
                      {h.isPlaceholder ? null : canSort ? (
                        <button
                          type="button"
                          className="inline-flex items-center gap-1 hover:text-ink-900"
                          onClick={h.column.getToggleSortingHandler()}
                        >
                          {flexRender(h.column.columnDef.header, h.getContext())}
                          {sorted === 'asc' ? <ArrowUp className="h-3.5 w-3.5" />
                            : sorted === 'desc' ? <ArrowDown className="h-3.5 w-3.5" />
                            : <ArrowUpDown className="h-3 w-3 opacity-40" />}
                        </button>
                      ) : (
                        flexRender(h.column.columnDef.header, h.getContext())
                      )}
                    </th>
                  );
                })}
              </tr>
            ))}
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={columns.length} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {!isLoading && rows.length === 0 && (
              <tr><td colSpan={columns.length} className="py-8 text-center text-base-content/50">
                {emptyContent ?? tc('empty')}
              </td></tr>
            )}
            {rows.map((r: Row<T>) => (
              <tr key={r.id}>
                {r.getVisibleCells().map((cell) => (
                  <td key={cell.id} className={alignClass(cell.column.columnDef.meta?.align)}>
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination footer */}
      {totalFiltered > pageSize && (
        <div className="flex items-center justify-between text-sm text-ink-600">
          <span>{tc('rowsTotal', { count: totalFiltered })}</span>
          <div className="flex items-center gap-2">
            <button className="btn btn-ghost btn-sm" onClick={() => table.previousPage()} disabled={!table.getCanPreviousPage()} aria-label={tc('prev')}>
              <ChevronLeft className="h-4 w-4" />
            </button>
            <span>{table.getState().pagination.pageIndex + 1} / {table.getPageCount()}</span>
            <button className="btn btn-ghost btn-sm" onClick={() => table.nextPage()} disabled={!table.getCanNextPage()} aria-label={tc('next')}>
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function ColumnFilter<T>({ column, allLabel }: { column: Column<T, unknown>; allLabel: string }) {
  const variant = column.columnDef.meta?.filter;
  const label = column.columnDef.meta?.filterLabel
    ?? (typeof column.columnDef.header === 'string' ? column.columnDef.header : column.id);
  const value = (column.getFilterValue() ?? '') as string;

  if (variant === 'select') {
    const options = Array.from(column.getFacetedUniqueValues().keys())
      .filter((v) => v != null && v !== '')
      .sort();
    return (
      <label className="form-control">
        <span className="label-text text-ink-600">{label}</span>
        <select
          className="select select-bordered"
          value={value}
          onChange={(e) => column.setFilterValue(e.target.value || undefined)}
          aria-label={label}
        >
          <option value="">{allLabel}</option>
          {options.map((o) => <option key={String(o)} value={String(o)}>{String(o)}</option>)}
        </select>
      </label>
    );
  }
  return (
    <label className="form-control">
      <span className="label-text text-ink-600">{label}</span>
      <input
        className="input input-bordered"
        value={value}
        onChange={(e) => column.setFilterValue(e.target.value || undefined)}
        placeholder={label}
        aria-label={label}
      />
    </label>
  );
}
