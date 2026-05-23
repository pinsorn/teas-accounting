'use client';

import { Printer, Download, ChevronDown, FileText, Copy } from 'lucide-react';
import { toast } from 'sonner';
import { useMarkPrinted } from '@/lib/queries';
import { printPdf, downloadFile } from '@/lib/api';

// Sprint 13j-PDF — Print + Download menu for document detail pages. BOTH actions
// now use the server-rendered QuestPDF (GET /{docType}/{id}/pdf) — identical to the
// on-screen PaperDocument but a real PDF (the old window.print() of the HTML page
// rendered differently + leaked the browser print chrome).
//
// cont.69 Phase 4 (D8) — ORIGINAL/COPY tracking is now UNIVERSAL: every sales
// document (Q / SO / DO / Invoice / TI / RC / CN / DN) records each print to the
// BE audit and stamps ต้นฉบับ/สำเนา via ?copy. A reprint of an already-printed
// original is auto-downgraded to a copy (สำเนา) and the UI warns. The `fiscal`
// prop is kept for back-compat but `tracked` (default true) now drives the menu.
export function PrintMenu({
  docType,
  id,
  fiscal,
  tracked = true,
}: {
  docType: string; // route segment, e.g. "tax-invoices", "quotations", "billing-notes"
  id: number;
  /** @deprecated cont.69 P4 — tracking is universal; use `tracked` to opt out. */
  fiscal?: boolean;
  /** When true (default) the menu offers ต้นฉบับ/สำเนา + reprint-downgrade. */
  tracked?: boolean;
}) {
  const mark = useMarkPrinted(docType, id);
  // Back-compat: an explicit fiscal=false from an old caller still disables tracking.
  const isTracked = fiscal ?? tracked;

  function pdfPath(copy: boolean) {
    return `${docType}/${id}/pdf${copy ? '?copy=1' : ''}`;
  }

  // Tracked: record first (audit), resolving the effective copy mode, then act.
  async function trackedDoc(requestedCopy: boolean, action: 'print' | 'download') {
    let copyMode = requestedCopy;
    try {
      const res = await mark.mutateAsync(requestedCopy);
      if (!requestedCopy && res.wasReprint) {
        copyMode = true;
        toast.warning('ต้นฉบับเคยถูกพิมพ์แล้ว — พิมพ์เป็นสำเนาแทน');
      }
    } catch {
      // Recording failed (permission/cross-tenant): never an unrecorded original.
      copyMode = true;
      toast.error('บันทึกการพิมพ์ไม่สำเร็จ — ออกเป็นสำเนา');
    }
    await run(action, pdfPath(copyMode));
  }

  async function plainDoc(action: 'print' | 'download') {
    await run(action, pdfPath(false));
  }

  async function run(action: 'print' | 'download', path: string) {
    try {
      if (action === 'print') await printPdf(path);
      else await downloadFile(path, `${docType}-${id}.pdf`);
    } catch {
      toast.error(action === 'print' ? 'พิมพ์ไม่สำเร็จ' : 'ดาวน์โหลด PDF ไม่สำเร็จ');
    }
  }

  return (
    <div className="dropdown dropdown-end">
      <label tabIndex={0} className="btn btn-secondary btn-sm gap-1">
        <Printer className="h-4 w-4" aria-hidden /> พิมพ์ / PDF
        <ChevronDown className="h-3.5 w-3.5" aria-hidden />
      </label>
      <ul tabIndex={0} className="menu dropdown-content z-[1] mt-1 w-56 rounded-card border border-ink-100 bg-base-100 p-2 shadow-warm-lg">
        {isTracked ? (
          <>
            <li>
              <button onClick={() => trackedDoc(false, 'print')} className="gap-2">
                <FileText className="h-4 w-4" aria-hidden /> พิมพ์ต้นฉบับ
              </button>
            </li>
            <li>
              <button onClick={() => trackedDoc(true, 'print')} className="gap-2">
                <Copy className="h-4 w-4" aria-hidden /> พิมพ์สำเนา
              </button>
            </li>
            <li>
              <button onClick={() => trackedDoc(true, 'download')} className="gap-2">
                <Download className="h-4 w-4" aria-hidden /> ดาวน์โหลด PDF (สำเนา)
              </button>
            </li>
          </>
        ) : (
          <>
            <li>
              <button onClick={() => plainDoc('print')} className="gap-2">
                <Printer className="h-4 w-4" aria-hidden /> พิมพ์
              </button>
            </li>
            <li>
              <button onClick={() => plainDoc('download')} className="gap-2">
                <Download className="h-4 w-4" aria-hidden /> ดาวน์โหลด PDF
              </button>
            </li>
          </>
        )}
      </ul>
    </div>
  );
}
