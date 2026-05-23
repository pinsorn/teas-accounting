'use client';

import { useEffect, useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { apiGet, qs } from '@/lib/api';
import type { ProductListItem, ProductTypeStr } from '@/lib/types';
import { FloatingListbox } from '@/components/ui/FloatingListbox';
import { ProductQuickCreateModal } from '@/components/forms/ProductQuickCreateModal';

// Sprint 13e P2 — line-item description cell that doubles as a product
// autocomplete. Typing is free text (productId stays null = ad-hoc line);
// picking a product fills description / price / tax rate / code. Shared by
// Quotation, SO and (future) TI line tables via LineItemsTable enableProduct.

export interface ProductPick {
  productId: number;
  productCode: string;
  nameTh: string;
  productType: ProductTypeStr;
  defaultUnitPrice: number | null;
}

/** EXEMPT_* products carry no output VAT (ม.81); everything else is 7%. */
export function taxRateForProductType(t: ProductTypeStr): number {
  return t === 'EXEMPT_GOOD' || t === 'EXEMPT_SERVICE' ? 0 : 0.07;
}

function pickItems(raw: unknown): ProductPick[] {
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
    }));
}

export function ProductPicker({
  description,
  onDescriptionChange,
  onSelectProduct,
  ariaLabel,
}: {
  description: string;
  onDescriptionChange: (text: string) => void;
  onSelectProduct: (p: ProductPick) => void;
  ariaLabel?: string;
}) {
  const t = useTranslations('quotation');
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<ProductPick[]>([]);
  const [loading, setLoading] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!open) return;
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(async () => {
      setLoading(true);
      try {
        const raw = await apiGet<ProductListItem[]>(
          `products${qs({ search: description.trim() || undefined })}`,
        );
        setItems(pickItems(raw));
      } catch {
        setItems([]);
      } finally {
        setLoading(false);
      }
    }, 300);
    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
  }, [description, open]);

  return (
    <div>
      <input
        ref={inputRef}
        className="input input-sm input-bordered w-full"
        value={description}
        placeholder={t('searchProduct')}
        onFocus={() => setOpen(true)}
        onChange={(e) => {
          onDescriptionChange(e.target.value);
          setOpen(true);
        }}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        aria-label={ariaLabel}
        role="combobox"
        aria-expanded={open}
      />
      <FloatingListbox anchorRef={inputRef} open={open}>
        {loading && <li className="px-3 py-2 text-sm text-base-content/50">…</li>}
        {!loading && items.length === 0 && (
          <li className="px-3 py-2 text-sm text-base-content/50">{t('noProduct')}</li>
        )}
        {/* No match → create a new product/service inline (sprint plan #3). */}
        <li>
          <button
            type="button"
            className="text-peach-700"
            onMouseDown={(e) => {
              e.preventDefault();
              setOpen(false);
              setCreateOpen(true);
            }}
          >
            <Plus className="h-4 w-4" aria-hidden />
            <span>{t('createProduct')}</span>
          </button>
        </li>
        {items.map((p) => (
          <li key={p.productId}>
            <button
              type="button"
              onMouseDown={(e) => {
                e.preventDefault();
                setOpen(false);
                onSelectProduct(p);
              }}
            >
              <span className="font-mono text-xs opacity-60">{p.productCode}</span>
              <span>{p.nameTh}</span>
              <span className="ml-auto text-xs opacity-60">{p.productType}</span>
            </button>
          </li>
        ))}
      </FloatingListbox>
      <ProductQuickCreateModal
        open={createOpen}
        initialNameTh={description}
        onClose={() => setCreateOpen(false)}
        onCreated={(p) => {
          setCreateOpen(false);
          onSelectProduct(p);
        }}
      />
    </div>
  );
}
