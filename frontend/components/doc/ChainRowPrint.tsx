'use client';

import { Printer, FileText, Copy } from 'lucide-react';
import { toast } from 'sonner';
import { useMarkPrinted } from '@/lib/queries';
import { printPdf } from '@/lib/api';

// cont.69 Phase 4 (D8) — compact per-row original/copy print control for the
// DocumentChain. Same tracking semantics as PrintMenu: a reprint of an already
// printed original auto-downgrades to สำเนา and warns. `docType` is the API route
// segment for the row's document (e.g. "quotations", "billing-notes").
export function ChainRowPrint({ docType, id }: { docType: string; id: number }) {
  const mark = useMarkPrinted(docType, id);

  function pdfPath(copy: boolean) {
    // BE binds `bool? copy` — only true/false parse; "1" → 400 (Sprint 13j-PURCH D1 bugfix).
    return `${docType}/${id}/pdf${copy ? '?copy=true' : ''}`;
  }

  async function trackedPrint(requestedCopy: boolean) {
    let copyMode = requestedCopy;
    try {
      const res = await mark.mutateAsync(requestedCopy);
      if (!requestedCopy && res.wasReprint) {
        copyMode = true;
        toast.warning('ต้นฉบับเคยถูกพิมพ์แล้ว — พิมพ์เป็นสำเนาแทน');
      }
    } catch {
      copyMode = true;
      toast.error('บันทึกการพิมพ์ไม่สำเร็จ — ออกเป็นสำเนา');
    }
    try {
      await printPdf(pdfPath(copyMode));
    } catch {
      toast.error('พิมพ์ไม่สำเร็จ');
    }
  }

  return (
    <div className="dropdown dropdown-end">
      <label
        tabIndex={0}
        className="btn btn-ghost btn-xs gap-1 text-ink-600"
        aria-label="พิมพ์เอกสาร"
        title="พิมพ์ ต้นฉบับ/สำเนา"
        data-testid="chain-row-print"
      >
        <Printer className="h-3.5 w-3.5" aria-hidden />
      </label>
      <ul tabIndex={0} className="menu dropdown-content z-[2] mt-1 w-44 rounded-card border border-ink-100 bg-base-100 p-2 shadow-warm-lg">
        <li>
          <button onClick={() => trackedPrint(false)} className="gap-2 text-[13px]">
            <FileText className="h-3.5 w-3.5" aria-hidden /> พิมพ์ต้นฉบับ
          </button>
        </li>
        <li>
          <button onClick={() => trackedPrint(true)} className="gap-2 text-[13px]">
            <Copy className="h-3.5 w-3.5" aria-hidden /> พิมพ์สำเนา
          </button>
        </li>
      </ul>
    </div>
  );
}
