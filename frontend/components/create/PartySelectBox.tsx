'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { Plus } from 'lucide-react';
import { EntityPickerModal, type PickerRow } from '@/components/ui/EntityPickerModal';
import { useCustomer, useVendor } from '@/lib/queries';
import { formatTaxId } from '@/lib/utils';

// Create-page redesign (cont.80) — the selected party (customer / vendor)
// highlight box. Reuses EntityPickerModal under the hood (same search/pick UX
// as CustomerSelector/VendorSelector). When a party is chosen it shows a cream
// card: peach avatar with initials, bold name, address, taxId·contact, and a
// small "เปลี่ยน" button (top-right) that reopens the picker. Empty state = a
// dashed "เลือก…" button that opens the picker. Display data is fetched from the
// master record via useCustomer/useVendor so a programmatically-prefilled value
// (e.g. TI from a Quotation) renders fully, not just an id.

function initials(name: string): string {
  const cleaned = name.trim();
  if (!cleaned) return '?';
  // Thai company names have no spaces in the way Latin names do; take the first
  // 2 visible characters, which gives a sensible monogram for both scripts.
  const parts = cleaned.split(/\s+/).filter(Boolean);
  if (parts.length >= 2) return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).slice(0, 2) || cleaned.slice(0, 2);
  return cleaned.slice(0, 2);
}

interface NormalizedParty {
  name: string;
  taxId: string | null;
  address: string | null;
  contact: string | null;
  phone: string | null;
}

function mapRow(idKey: 'customerId' | 'vendorId') {
  return (x: Record<string, unknown>): PickerRow | null => {
    const id = x[idKey];
    if (typeof id !== 'number' || typeof x.nameTh !== 'string') return null;
    return { id, label: x.nameTh, sub: (x.taxId as string | null | undefined) ?? null };
  };
}

export function PartySelectBox({
  party,
  onChange,
  kind,
}: {
  /** The selected party id, or null when none picked yet. */
  party: number | null;
  /** Receives the picked id + display name (name kept for callers that cache it). */
  onChange: (id: number, name: string) => void;
  kind: 'customer' | 'vendor';
}) {
  const tc = useTranslations('common');
  const tp = useTranslations('party');
  const [open, setOpen] = useState(false);

  // Fetch the master detail for display. Both hooks are always called (rules of
  // hooks); the irrelevant one is disabled by passing 0 / is simply ignored.
  const customer = useCustomer(kind === 'customer' ? party : null);
  const vendor = useVendor(kind === 'vendor' && party ? party : 0);

  let normalized: NormalizedParty | null = null;
  if (party) {
    if (kind === 'customer' && customer.data) {
      const c = customer.data;
      normalized = {
        name: c.nameTh,
        taxId: c.taxId ? formatTaxId(c.taxId) : null,
        address: c.billingAddress,
        contact: c.contactPerson,
        phone: c.phone,
      };
    } else if (kind === 'vendor' && vendor.data) {
      const v = vendor.data;
      normalized = {
        name: v.nameTh,
        taxId: v.taxId ? formatTaxId(v.taxId) : null,
        address: v.address,
        contact: v.contactPerson,
        phone: v.phone,
      };
    }
  }

  const picker = (
    <EntityPickerModal
      open={open}
      onClose={() => setOpen(false)}
      onSelect={(id, name) => onChange(id, name)}
      title={kind === 'customer' ? tp('selectCustomer') : tp('selectVendor')}
      searchPlaceholder={tp('searchPlaceholder')}
      emptyText={kind === 'customer' ? tp('emptyCustomer') : tp('emptyVendor')}
      endpoint={kind === 'customer' ? 'customers' : 'vendors'}
      mapRow={mapRow(kind === 'customer' ? 'customerId' : 'vendorId')}
    />
  );

  // Empty state — a dashed button that opens the picker.
  if (!party) {
    return (
      <>
        <button
          type="button"
          className="flex w-full items-center justify-center gap-2 rounded-field border border-dashed border-ink-200 bg-base-100 py-5 text-sm font-medium text-peach-700 hover:border-peach-300 hover:bg-peach-50"
          onClick={() => setOpen(true)}
        >
          <Plus className="h-4 w-4" aria-hidden />
          {kind === 'customer' ? tp('selectCustomer') : tp('selectVendor')}
        </button>
        {picker}
      </>
    );
  }

  // Selected — the highlight box. `normalized` may still be loading; fall back to
  // a name-less skeleton-ish state without crashing.
  const name = normalized?.name ?? `#${party}`;
  const metaParts = [
    normalized?.taxId ? `${tp('taxId')} ${normalized.taxId}` : null,
    normalized?.contact ?? null,
    normalized?.phone ?? null,
  ].filter(Boolean);

  return (
    <>
      <div className="relative rounded-field border border-ink-100 bg-ink-50 p-4">
        <button
          type="button"
          className="absolute right-3 top-3 rounded-chip border border-ink-200 bg-base-100 px-2.5 py-1 text-xs font-medium text-ink-700 hover:bg-ink-75"
          onClick={() => setOpen(true)}
        >
          {tp('change')}
        </button>
        <div className="flex items-start gap-3 pr-16">
          <span
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-field bg-peach-200 text-sm font-bold text-peach-700"
            aria-hidden
          >
            {initials(name)}
          </span>
          <div className="min-w-0">
            <div className="truncate font-bold text-ink-900">{name}</div>
            {normalized?.address && (
              <div className="mt-0.5 text-sm text-ink-600">{normalized.address}</div>
            )}
            {metaParts.length > 0 && (
              <div className="mt-0.5 text-sm text-ink-500">{metaParts.join(' · ')}</div>
            )}
            {!normalized && (
              <div className="mt-0.5 text-sm text-ink-400">{tc('loading')}</div>
            )}
          </div>
        </div>
      </div>
      {picker}
    </>
  );
}
