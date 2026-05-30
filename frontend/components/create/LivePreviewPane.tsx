'use client';

import { useLayoutEffect, useRef, useState, type ReactNode } from 'react';

// Create-page redesign (cont.80) — the right-hand LIVE PREVIEW column. A small
// "LIVE PREVIEW" chip sits above the page's existing PaperDocument (passed as
// children, unchanged).
//
// cont.80 (Ham): the preview is a FIXED A4 page (210×297mm) that SCALES DOWN to
// the column width — so it always reads as a real A4 sheet, shrinking with the
// pane instead of reflowing. We render the paper at its natural A4 pixel size
// (794×1123 @96dpi) inside a fixed box and CSS-`scale()` it by columnWidth/794
// (measured via ResizeObserver); the outer height tracks the scaled paper so the
// A4 aspect ratio is preserved at any width. `overflow-hidden` clips any bleed.
const A4_W = 794; // 210mm @ 96dpi
const A4_H = 1123; // 297mm @ 96dpi

export function LivePreviewPane({ children }: { children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0.5);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const apply = () => setScale(Math.min(1, el.clientWidth / A4_W));
    apply();
    const ro = new ResizeObserver(apply);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  return (
    <div>
      <div className="mb-3 flex items-center gap-2">
        <span className="inline-flex items-center gap-1.5 rounded-chip bg-ink-100 px-2.5 py-1 text-xs font-semibold uppercase tracking-wide text-ink-600">
          <span className="h-1.5 w-1.5 rounded-full bg-peach-500" aria-hidden />
          LIVE PREVIEW
        </span>
        <span className="text-[11px] text-ink-400">A4</span>
      </div>
      {/* Outer = full column width; height = scaled A4 height (keeps the ratio). */}
      <div
        ref={ref}
        className="w-full overflow-hidden rounded-card border border-ink-100 bg-ink-50"
        style={{ height: A4_H * scale }}
      >
        {/* Fixed A4 sheet, scaled to fit. transform-origin top-left so it pins to the box. */}
        <div
          className="bg-white shadow-warm-sm"
          style={{
            width: A4_W,
            minHeight: A4_H,
            transform: `scale(${scale})`,
            transformOrigin: 'top left',
          }}
        >
          {children}
        </div>
      </div>
    </div>
  );
}
