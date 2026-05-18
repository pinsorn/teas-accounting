'use client';

import { useState } from 'react';
import { Copy, Check } from 'lucide-react';

// component-patterns.md §1 — monospace doc number, click to copy.
export function DocumentNumberBadge({ value }: { value: string | null }) {
  const [copied, setCopied] = useState(false);
  if (!value) return <span className="text-base-content/50">—</span>;

  async function copy() {
    try {
      await navigator.clipboard.writeText(value!);
      setCopied(true);
      setTimeout(() => setCopied(false), 1200);
    } catch {
      /* clipboard blocked — non-fatal */
    }
  }

  return (
    <button
      type="button"
      onClick={copy}
      title="คัดลอกเลขที่เอกสาร / Copy"
      className="group inline-flex items-center gap-1 font-mono text-sm tabular-nums hover:text-primary"
    >
      {value}
      {copied ? (
        <Check className="h-3 w-3 text-success" aria-hidden />
      ) : (
        <Copy className="h-3 w-3 opacity-0 transition group-hover:opacity-60" aria-hidden />
      )}
    </button>
  );
}
