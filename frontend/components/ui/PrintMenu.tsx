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
// cont.69 Phase 4 (D8) — ORIGINAL/COPY tracking is UNIVERSAL: every sales document
// (Q / SO / DO / Invoice / TI / RC / CN / DN) records each print to the BE audit and
// stamps ต้นฉบับ/สำเนา via ?copy. cont.70 (Ham): re-printing the ORIGINAL is allowed
// (2nd time onward) for Q / SO / DO / Invoice / RC — the UI just warns it was printed
// before. The strictly-fiscal documents (Tax Invoice / CN / DN) keep the ม.86/4 /
// ม.86/12 rule: only ONE original exists, so a reprint is downgraded to a สำเนา.
// The `fiscal` prop is kept for back-compat but `tracked` (default true) drives the menu.

// Tax Invoice / Credit Note / Debit Note: a reprint of the original is forced to สำเนา
// (only one physical original may circulate, ม.86/4 / ม.86/12).
const STRICT_ONE_ORIGINAL = new Set(['tax-invoices', 'credit-notes', 'debit-notes']);
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
    // The BE binds `bool? copy`, which only accepts true/false — never "1" (→ 400).
    return `${docType}/${id}/pdf${copy ? '?copy=true' : ''}`;
  }

  // Tracked: record the print (audit) first, then render.
  async function trackedDoc(requestedCopy: boolean, action: 'print' | 'download') {
    const strict = STRICT_ONE_ORIGINAL.has(docType);
    let copyMode = requestedCopy;
    try {
      const res = await mark.mutateAsync(requestedCopy);
      if (!requestedCopy && res.wasReprint) {
        if (strict) {
          // ม.86/4 / ม.86/12 — only one original TI/CN/DN; reprint = สำเนา.
          copyMode = true;
          toast.warning('ต้นฉบับเคยถูกพิมพ์แล้ว — พิมพ์เป็นสำเนาแทน');
        } else {
          // Q / SO / DO / Invoice / RC — original is re-printable; just warn.
          toast.warning('เอกสารนี้เคยถูกพิมพ์ไปแล้ว');
        }
      }
    } catch {
      // Recording failed (permission/cross-tenant). For a strictly-fiscal doc never emit
      // an unrecorded original → fall back to สำเนา; others may still print.
      toast.error('บันทึกการพิมพ์ไม่สำเร็จ');
      if (strict && !requestedCopy) copyMode = true;
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
