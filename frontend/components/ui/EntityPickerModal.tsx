'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Search, X, Plus } from 'lucide-react';
import { apiGet, qs } from '@/lib/api';

// ITEM 2+3 — shared "search & pick" MODAL behind VendorSelector / CustomerSelector.
// Mirrors ProductSearchModal's daisyUI modal shell (modal-open / modal-box /
// modal-backdrop). Debounced search against an arbitrary list endpoint; the caller
// maps the raw rows into {id,label,sub} and receives the picked row's id+label.
// Selectors stay thin trigger buttons; this owns all the modal markup so the two
// pickers can't drift apart.

export interface PickerRow {
  id: number;
  label: string; // primary display (e.g. nameTh)
  sub?: string | null; // secondary muted text (e.g. taxId / code)
}

export function EntityPickerModal({
  open,
  onClose,
  onSelect,
  title,
  searchPlaceholder,
  emptyText,
  endpoint,
  mapRow,
  quickCreateLabel,
  renderQuickCreate,
  createHref,
  createLabel,
}: {
  open: boolean;
  onClose: () => void;
  onSelect: (id: number, label: string) => void;
  title: string;
  searchPlaceholder: string;
  emptyText: string;
  /** Path prefix, e.g. "vendors" / "customers". The query string is appended. */
  endpoint: string;
  /** Map one raw list row → PickerRow, or null to drop it. */
  mapRow: (raw: Record<string, unknown>) => PickerRow | null;
  /** WP3 3.8 — label for the "+ create new" toggle. Omit to hide quick-create entirely. */
  quickCreateLabel?: string;
  /** Renders the quick-create mini-form; call onDone(id,label) on success. */
  renderQuickCreate?: (onDone: (id: number, label: string) => void) => React.ReactNode;
  /** S8 fix (2026-07-16) — minimal alternative to `renderQuickCreate` for pickers
   *  that don't have an inline quick-create form yet (currently the customer
   *  picker): a plain link that opens `createHref` in a NEW TAB, so the user can
   *  create the record without losing the in-progress document form. Combined
   *  with the focus-refetch effect below, the newly created record shows up in
   *  the list as soon as the user tabs back. Ignored when `renderQuickCreate` is set. */
  createHref?: string;
  createLabel?: string;
}) {
  const tc = useTranslations('common');
  const [q, setQ] = useState('');
  const [items, setItems] = useState<PickerRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [creating, setCreating] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  // S8 fix — bumped whenever the window regains focus while the modal is open, so
  // a customer created via the `createHref` new-tab link reappears without the
  // user needing to retype/clear the search box.
  const [refreshTick, setRefreshTick] = useState(0);

  // Reset the search box (and the quick-create panel) each time the modal opens.
  useEffect(() => {
    if (!open) return;
    setQ('');
    setItems([]);
    setCreating(false);
    searchRef.current?.focus();
  }, [open]);

  // Close on Escape.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  // S8 fix — refetch on window focus while open (see refreshTick above).
  useEffect(() => {
    if (!open) return;
    const onFocus = () => setRefreshTick((n) => n + 1);
    window.addEventListener('focus', onFocus);
    return () => window.removeEventListener('focus', onFocus);
  }, [open]);

  // Debounced search (300ms). Empty query lists the first page so the modal is
  // never blank on open. Defensive shape parsing — list endpoints aren't pinned.
  useEffect(() => {
    if (!open) return;
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(async () => {
      setLoading(true);
      try {
        const raw = await apiGet<unknown>(
          `${endpoint}${qs({ search: q.trim() || undefined, pageSize: 20 })}`,
        );
        const arr =
          Array.isArray(raw) ? raw
          : raw && typeof raw === 'object' && Array.isArray((raw as { items?: unknown }).items)
            ? (raw as { items: unknown[] }).items
            : [];
        setItems(
          arr
            .map((x) => mapRow(x as Record<string, unknown>))
            .filter((r): r is PickerRow => r !== null),
        );
      } catch {
        setItems([]);
      } finally {
        setLoading(false);
      }
    }, 300);
    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
    // mapRow is a stable inline arrow per render; endpoint/q/open/refreshTick drive the fetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q, open, endpoint, refreshTick]);

  if (!open) return null;

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-backdrop" onClick={onClose} />
      <div className="modal-box flex max-h-[80vh] max-w-2xl flex-col gap-3 p-0">
        <div className="flex items-center justify-between border-b border-ink-100 px-5 py-3">
          <h3 className="text-lg font-bold text-ink-900">{title}</h3>
          <button
            type="button"
            className="btn btn-ghost btn-sm btn-circle"
            onClick={onClose}
            aria-label={tc('cancel')}
          >
            <X className="h-4 w-4" aria-hidden />
          </button>
        </div>

        <div className="px-5">
          <label className="input input-bordered flex items-center gap-2">
            <Search className="h-4 w-4 opacity-50" aria-hidden />
            <input
              ref={searchRef}
              className="grow"
              value={q}
              placeholder={searchPlaceholder}
              onChange={(e) => setQ(e.target.value)}
              aria-label={searchPlaceholder}
            />
          </label>
        </div>

        {!creating && (
          <ul className="min-h-[10rem] flex-1 overflow-y-auto px-2 pb-2">
            {loading && <li className="px-3 py-6 text-center text-sm text-ink-400">…</li>}
            {!loading && items.length === 0 && (
              <li className="px-3 py-6 text-center text-sm text-ink-400">{emptyText}</li>
            )}
            {!loading &&
              items.map((r) => (
                <li key={r.id}>
                  <button
                    type="button"
                    className="flex w-full items-center gap-3 rounded-card px-3 py-2.5 text-left hover:bg-peach-50"
                    onClick={() => {
                      onSelect(r.id, r.label);
                      onClose();
                    }}
                  >
                    <span className="flex-1 truncate text-sm text-ink-900">{r.label}</span>
                    {r.sub && (
                      <span className="shrink-0 font-mono text-xs text-ink-400">{r.sub}</span>
                    )}
                  </button>
                </li>
              ))}
          </ul>
        )}

        {/* WP3 3.8 — quick-create toggle/panel, shared by every EntityPickerModal caller
            that opts in (currently the vendor picker behind PartySelectBox — PO/VI/PV forms). */}
        {renderQuickCreate && creating && renderQuickCreate((id, label) => {
          onSelect(id, label);
          onClose();
        })}

        <div className="flex items-center justify-between gap-2 border-t border-ink-100 px-5 py-3">
          {renderQuickCreate && !creating ? (
            <button type="button" className="btn btn-ghost btn-sm" onClick={() => setCreating(true)}>
              {quickCreateLabel}
            </button>
          ) : createHref && !creating ? (
            // S8 fix — new tab so the in-progress document form (customer picked
            // from a QT/SO/Invoice/Receipt) is never lost; refetch-on-focus above
            // brings the new customer back into this list on return.
            <a href={createHref} target="_blank" rel="noopener noreferrer"
              className="btn btn-ghost btn-sm gap-1">
              <Plus className="h-4 w-4" aria-hidden /> {createLabel}
            </a>
          ) : <span />}
          <button type="button" className="btn btn-ghost btn-sm" onClick={creating ? () => setCreating(false) : onClose}>
            {tc('cancel')}
          </button>
        </div>
      </div>
    </div>
  );
}
