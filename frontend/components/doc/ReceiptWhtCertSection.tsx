'use client';

import { useState } from 'react';
import { AlertTriangle, Check } from 'lucide-react';
import { toast } from 'sonner';
import { useSetReceiptWhtCert } from '@/lib/queries';
import { formatDate } from '@/lib/utils';

// Sprint 13j-FE — 50ทวิ status + late entry on the receipt detail page.
// A receipt with WHT but no cert number shows "ขาดใบทวิ 50" + a form to add
// the number/date later. The scanned cert is attached via AttachmentsSection.
export function ReceiptWhtCertSection({
  receiptId,
  whtAmount,
  certNo,
  certDate,
}: {
  receiptId: number;
  whtAmount: number;
  certNo: string | null;
  certDate: string | null;
}) {
  const save = useSetReceiptWhtCert(receiptId);
  const [no, setNo] = useState('');
  const [date, setDate] = useState('');

  if (whtAmount <= 0) return null;

  async function submit() {
    if (!no.trim()) {
      toast.error('ระบุเลขที่ใบ 50ทวิ');
      return;
    }
    try {
      await save.mutateAsync({ certNo: no.trim(), certDate: date || null });
      toast.success('บันทึกใบ 50ทวิ แล้ว');
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? 'เกิดข้อผิดพลาด');
    }
  }

  return (
    <section className="mt-5 rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
      <h3 className="mb-3 text-[15px] font-bold text-ink-900">ใบหัก ณ ที่จ่าย (50 ทวิ)</h3>
      {certNo ? (
        <p className="inline-flex items-center gap-2 rounded-full bg-status-success-bg px-3 py-1 text-sm font-medium text-status-success">
          <Check className="h-4 w-4" aria-hidden />
          เลขที่ {certNo}
          {certDate && <> · ลงวันที่ {formatDate(certDate)}</>}
        </p>
      ) : (
        <div className="space-y-3">
          <p className="inline-flex items-center gap-2 rounded-full bg-status-warning-bg px-3 py-1 text-sm font-semibold text-status-warning">
            <AlertTriangle className="h-4 w-4" aria-hidden />
            ขาดใบทวิ 50 — รอลูกค้าส่งเลขที่ใบหัก ณ ที่จ่าย
          </p>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <label className="form-control">
              <span className="label-text text-ink-600">เลขที่ใบ 50ทวิ</span>
              <input className="input input-bordered" value={no} onChange={(e) => setNo(e.target.value)} aria-label="เลขที่ใบ 50ทวิ" />
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">วันที่ใบ 50ทวิ</span>
              <input type="date" className="input input-bordered" value={date} onChange={(e) => setDate(e.target.value)} aria-label="วันที่ใบ 50ทวิ" />
            </label>
            <div className="flex items-end">
              <button className="btn btn-primary" disabled={save.isPending} onClick={submit}>
                บันทึกใบ 50ทวิ
              </button>
            </div>
          </div>
          <p className="text-xs text-ink-500">แนบไฟล์สแกนใบ 50ทวิ ได้ที่ส่วน &ldquo;เอกสารแนบ&rdquo; ด้านล่าง</p>
        </div>
      )}
    </section>
  );
}
