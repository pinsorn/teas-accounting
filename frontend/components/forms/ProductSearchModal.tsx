'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Plus, Search, X } from 'lucide-react';
import { apiGet, qs } from '@/lib/api';
import type { ProductListItem, ProductTypeStr, ProductPurpose } from '@/lib/types';
import type { ProductPick } from '@/components/forms/ProductPicker';
import { formatTHB } from '@/lib/utils';

// cont.70 — product/service picker as a proper MODAL (replaces the cramped dropdown).
// Self-contained search: debounced query against the product master, a roomy result
// list, and an inline "create new" hand-off. Used by every line table via ProductPicker.

const TYPE_BADGE: Record<ProductTypeStr, { label: string; cls: string }> = {
  GOOD: { label: 'สินค้า', cls: 'bg-sky-100 text-sky-700' },
  SERVICE: { label: 'บริการ', cls: 'bg-violet-100 text-violet-700' },
  EXEMPT_GOOD: { label: 'สินค้า (ยกเว้น VAT)', cls: 'bg-amber-100 text-amber-700' },
  EXEMPT_SERVICE: { label: 'บริการ (ยกเว้น VAT)', cls: 'bg-amber-100 text-amber-700' },
};

function mapItems(raw: unknown): ProductPick[] {
  const arr = Array.isArray(raw)
    ? raw
    : raw && typeof raw === 'object' && Array.isArray((raw as { items?: unknown }).items)
      ? (raw as { items: unknown[] }).items
      : [];
  return arr
    .map((x) => x as Record<string, unknown>)
    .filter((x) => typeof x.productId === 'number' && typeof x.nameTh === 'string')
    .map((x) => ({
      productId: x.productId as number,
      productCode: (x.productCode as string | undefined) ?? '',
      nameTh: x.nameTh as string,
      productType: (x.productType as ProductTypeStr | undefined) ?? 'GOOD',
      defaultUnitPrice:
        typeof x.defaultUnitPrice === 'number' ? (x.defaultUnitPrice as number) : null,
      defaultUomText:
        typeof x.defaultUomText === 'string' ? (x.defaultUomText as string) : null,
    }));
}

export function ProductSearchModal({
  open,
  initialQuery,
  onClose,
  onSelect,
  onCreateNew,
  purpose,
  businessUnitId,
}: {
  open: boolean;
  initialQuery: string;
  onClose: () => void;
  onSelect: (p: ProductPick) => void;
  /** Open the quick-create modal, seeded with the current search text. */
  onCreateNew: (seedText: string) => void;
  // cont.81 — filter the list to this side of the ledger + the doc's selected BU
  // (BU filter also includes shared null-BU products, handled server-side).
  purpose?: ProductPurpose;
  businessUnitId?: number | null;
}) {
  const t = useTranslations('quotation');
  const tc = useTranslations('common');
  const [q, setQ] = useState(initialQuery);
  const [items, setItems] = useState<ProductPick[]>([]);
  const [loading, setLoading] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const searchRef = useRef<HTMLInputElement>(null);

  // Re-seed the search box from the line's current text each time the modal opens.
  const [seededFor, setSeededFor] = useState<string | null>(null);
  if (open && seededFor !== initialQuery) {
    setSeededFor(initialQuery);
    setQ(initialQuery);
  }
  if (!open && seededFor !== null) setSeededFor(null);

  useEffect(() => {
    if (!open) return;
    searchRef.current?.focus();
  }, [open]);

  // Close on Escape.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  useEffect(() => {
    if (!open) return;
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(async () => {
      setLoading(true);
      try {
        const raw = await apiGet<ProductListItem[]>(`products${qs({
          search: q.trim() || undefined,
          purpose,
          businessUnitId: businessUnitId ?? undefined,
        })}`);
        setItems(mapItems(raw));
      } catch {
        setItems([]);
      } finally {
        setLoading(false);
      }
    }, 250);
    return () => { if (timer.current) clearTimeout(timer.current); };
  }, [q, open, purpose, businessUnitId]);

  if (!open) return null;

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      {/* backdrop click closes */}
      <div className="modal-backdrop" onClick={onClose} />
      <div className="modal-box flex max-h-[80vh] max-w-2xl flex-col gap-3 p-0">
        <div className="flex items-center justify-between border-b border-ink-100 px-5 py-3">
          <h3 className="text-lg font-bold text-ink-900">{t('pickerTitle')}</h3>
          <button type="button" className="btn btn-ghost btn-sm btn-circle" onClick={onClose} aria-label={t('pickerTitle')}>
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
              placeholder={t('pickerSearch')}
              onChange={(e) => setQ(e.target.value)}
              aria-label={t('pickerSearch')}
            />
          </label>
        </div>

        <ul className="min-h-[10rem] flex-1 overflow-y-auto px-2 pb-2">
          {loading && (
            <li className="px-3 py-6 text-center text-sm text-ink-400">…</li>
          )}
          {!loading && items.length === 0 && (
            <li className="px-3 py-6 text-center text-sm text-ink-400">{t('noProduct')}</li>
          )}
          {!loading && items.map((p) => {
            const badge = TYPE_BADGE[p.productType];
            return (
              <li key={p.productId}>
                <button
                  type="button"
                  className="flex w-full items-center gap-3 rounded-card px-3 py-2.5 text-left hover:bg-peach-50"
                  onClick={() => { onSelect(p); onClose(); }}
                >
                  <span className="w-28 shrink-0 font-mono text-xs text-ink-400">{p.productCode}</span>
                  <span className="flex-1 truncate text-sm text-ink-900">{p.nameTh}</span>
                  <span className={`shrink-0 rounded-field px-2 py-0.5 text-xs font-medium ${badge.cls}`}>
                    {badge.label}
                  </span>
                  <span className="w-24 shrink-0 text-right text-sm tabular-nums text-ink-600">
                    {p.defaultUnitPrice != null ? formatTHB(p.defaultUnitPrice) : '—'}
                  </span>
                </button>
              </li>
            );
          })}
        </ul>

        <div className="flex items-center justify-between gap-2 border-t border-ink-100 px-5 py-3">
          <button
            type="button"
            className="btn btn-outline btn-sm gap-1 border-peach-300 text-peach-700 hover:border-peach-400 hover:bg-peach-50"
            onClick={() => onCreateNew(q)}
          >
            <Plus className="h-4 w-4" aria-hidden /> {t('createProduct')}
          </button>
          <button type="button" className="btn btn-ghost btn-sm" onClick={onClose}>
            {tc('cancel')}
          </button>
        </div>
      </div>
    </div>
  );
}
