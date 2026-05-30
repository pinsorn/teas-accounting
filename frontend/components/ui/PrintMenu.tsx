'use client';

import { Printer, Download, ChevronDown, FileText, Copy } from 'lucide-react';
import { toast } from 'sonner';
import { useState } from 'react';
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

// cont.80 (Ham) — a reprint of the ORIGINAL is now allowed for EVERY tracked document,
// INCLUDING the fiscal TI / CN / DN. Rationale: a fail-safe for when a print physically
// did not happen (paper jam, wrong printer) — forcing a สำเนา then was wrong. The reprint
// is gated by an explicit confirm dialog ("พิมพ์ต้นฉบับซ้ำจริงหรือไม่") and EVERY print is
// still recorded to the audit trail (PrintCount / activity), so the control is the
// confirm + the audit, not a hard block. (Was: STRICT_ONE_ORIGINAL → auto-downgrade.)
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
  // cont.80 — when a reprint of the original is detected, hold the action here and ask
  // the user to confirm before emitting a 2nd original (null = no pending reprint).
  const [confirmReprint, setConfirmReprint] = useState<null | 'print' | 'download'>(null);

  function pdfPath(copy: boolean) {
    // The BE binds `bool? copy`, which only accepts true/false — never "1" (→ 400).
    return `${docType}/${id}/pdf${copy ? '?copy=true' : ''}`;
  }

  // Tracked: record the print (audit) first, then render.
  async function trackedDoc(requestedCopy: boolean, action: 'print' | 'download') {
    // สำเนา — always allowed, record + render.
    if (requestedCopy) {
      try { await mark.mutateAsync(true); }
      catch { toast.error('บันทึกการพิมพ์ไม่สำเร็จ'); return; }
      await run(action, pdfPath(true));
      return;
    }
    // ต้นฉบับ — record + learn whether an original was already printed.
    let wasReprint = false;
    try { wasReprint = !!(await mark.mutateAsync(false)).wasReprint; }
    catch { toast.error('บันทึกการพิมพ์ไม่สำเร็จ'); return; }
    if (wasReprint) {
      // cont.80 — already printed once: confirm before emitting another ORIGINAL
      // (no longer auto-downgraded to สำเนา). The print runs from the modal's onConfirm.
      setConfirmReprint(action);
      return;
    }
    await run(action, pdfPath(false));
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

      {/* cont.80 — reprint-original confirm. Allowed for every doc as a fail-safe; the
          warning + this confirm are the control (every print is still audited). */}
      {confirmReprint && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-backdrop" onClick={() => setConfirmReprint(null)} />
          <div className="modal-box max-w-sm">
            <h3 className="text-lg font-bold text-ink-900">พิมพ์ต้นฉบับซ้ำ?</h3>
            <p className="mt-2 text-sm text-ink-600">
              เอกสารนี้เคยพิมพ์ “ต้นฉบับ” ไปแล้ว — พิมพ์ต้นฉบับซ้ำได้ (ระบบบันทึกประวัติการพิมพ์ทุกครั้ง).
              ยืนยันพิมพ์ต้นฉบับอีกครั้งหรือไม่?
            </p>
            <div className="mt-4 flex justify-end gap-2">
              <button className="btn btn-ghost btn-sm" onClick={() => setConfirmReprint(null)}>
                ยกเลิก
              </button>
              <button
                className="btn btn-primary btn-sm"
                onClick={() => {
                  const a = confirmReprint;
                  setConfirmReprint(null);
                  if (a) void run(a, pdfPath(false));
                }}
              >
                ยืนยันพิมพ์ต้นฉบับ
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
