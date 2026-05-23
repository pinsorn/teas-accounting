'use client';

import { useEffect, useState, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';

// Sprint 13h P2 — shared portal-anchored listbox for the async comboboxes
// (CustomerSelector, ProductPicker, TaxInvoicePicker). Parent overflow:hidden
// on the table cells used to clip the dropdown; rendering on document.body
// with `position: fixed` + window-scroll re-anchoring eliminates it.

export function FloatingListbox({
  anchorRef,
  open,
  listboxId,
  className,
  children,
}: {
  anchorRef: RefObject<HTMLElement | null>;
  open: boolean;
  listboxId?: string;
  className?: string;
  children: ReactNode;
}) {
  const [pos, setPos] = useState<{ top: number; left: number; width: number } | null>(null);

  useEffect(() => {
    if (!open) {
      setPos(null);
      return;
    }
    const update = () => {
      const r = anchorRef.current?.getBoundingClientRect();
      if (r) setPos({ top: r.bottom + 4, left: r.left, width: r.width });
    };
    update();
    window.addEventListener('scroll', update, true);
    window.addEventListener('resize', update);
    return () => {
      window.removeEventListener('scroll', update, true);
      window.removeEventListener('resize', update);
    };
  }, [open, anchorRef]);

  if (!open || !pos || typeof document === 'undefined') return null;

  return createPortal(
    <ul
      id={listboxId}
      role="listbox"
      className={
        // Vertical scroll only (no horizontal scrollbar / column wrap); every item
        // is a single nowrap row with the label truncating so long Thai names + a
        // trailing rate/code never wrap. Applies to all consumers (WhtTypeSelect,
        // CustomerSelector, ProductPicker, …) via descendant selectors.
        'menu menu-vertical flex-nowrap max-h-72 overflow-y-auto overflow-x-hidden rounded-box bg-base-100 shadow ' +
        '[&_li>button]:flex [&_li>button]:w-full [&_li>button]:flex-nowrap [&_li>button]:items-center ' +
        '[&_li>button]:gap-2 [&_li>button]:min-w-0 [&_li>button>span:first-child]:truncate ' +
        (className ?? '')
      }
      style={{
        position: 'fixed',
        top: pos.top,
        left: pos.left,
        width: pos.width,
        minWidth: Math.max(pos.width, 280),
        zIndex: 50,
      }}
    >
      {children}
    </ul>,
    document.body,
  );
}
