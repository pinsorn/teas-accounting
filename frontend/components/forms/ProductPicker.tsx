'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { Search } from 'lucide-react';
import type { ProductTypeStr, ProductPurpose } from '@/lib/types';
import { ProductSearchModal } from '@/components/forms/ProductSearchModal';
import { ProductQuickCreateModal } from '@/components/forms/ProductQuickCreateModal';

// Sprint 13e P2 — line-item description cell that doubles as a product chooser.
// Typing in the field is free text (productId stays null = ad-hoc line). The 🔍 button
// opens a proper MODAL (cont.70, replaces the old cramped dropdown) to search/select a
// product, or create a new one. Picking fills description / price / tax rate / code.
// Shared by Quotation / SO / DO / Invoice / TI / Receipt line tables via LineItemsTable.

export interface ProductPick {
  productId: number;
  productCode: string;
  nameTh: string;
  productType: ProductTypeStr;
  defaultUnitPrice: number | null;
  // cont.81 follow-up — default UoM, filled onto the line on select.
  defaultUomText: string | null;
}

/** EXEMPT_* products carry no output VAT (ม.81); everything else is 7%. */
export function taxRateForProductType(t: ProductTypeStr): number {
  return t === 'EXEMPT_GOOD' || t === 'EXEMPT_SERVICE' ? 0 : 0.07;
}

export function ProductPicker({
  description,
  onDescriptionChange,
  onSelectProduct,
  ariaLabel,
  purpose,
  businessUnitId,
}: {
  description: string;
  onDescriptionChange: (text: string) => void;
  onSelectProduct: (p: ProductPick) => void;
  ariaLabel?: string;
  // cont.81 — forwarded to the search + quick-create modals to filter/seed by
  // sale-vs-purchase and the document's selected Business Unit.
  purpose?: ProductPurpose;
  businessUnitId?: number | null;
}) {
  const t = useTranslations('quotation');
  const [pickOpen, setPickOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [createSeed, setCreateSeed] = useState('');

  return (
    <div className="flex items-center gap-1">
      <input
        className="input input-sm input-bordered w-full"
        value={description}
        placeholder={t('searchProduct')}
        onChange={(e) => onDescriptionChange(e.target.value)}
        aria-label={ariaLabel}
      />
      <button
        type="button"
        className="btn btn-square btn-sm btn-ghost shrink-0 text-peach-700"
        onClick={() => setPickOpen(true)}
        aria-label={t('pickerOpen')}
        title={t('pickerOpen')}
      >
        <Search className="h-4 w-4" aria-hidden />
      </button>

      <ProductSearchModal
        open={pickOpen}
        initialQuery={description}
        onClose={() => setPickOpen(false)}
        onSelect={(p) => onSelectProduct(p)}
        onCreateNew={(seed) => {
          setPickOpen(false);
          setCreateSeed(seed);
          setCreateOpen(true);
        }}
        purpose={purpose}
        businessUnitId={businessUnitId}
      />
      <ProductQuickCreateModal
        open={createOpen}
        initialNameTh={createSeed}
        onClose={() => setCreateOpen(false)}
        onCreated={(p) => {
          setCreateOpen(false);
          onSelectProduct(p);
        }}
        purpose={purpose}
        businessUnitId={businessUnitId}
      />
    </div>
  );
}
