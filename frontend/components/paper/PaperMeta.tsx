import type { ReactNode } from 'react';
import { fmtPaperDate, type CustomerInfo } from './types';

// ม.86/4 #3 — buyer name/address/taxId/branch + dates.
export function PaperMeta({
  customer,
  issueDate,
  validUntil,
  validUntilLabel,
  extraMetaBlock,
}: {
  customer: CustomerInfo;
  issueDate: string;
  validUntil?: string;
  validUntilLabel?: string;
  extraMetaBlock?: ReactNode;
}) {
  return (
    <div className="paper-meta">
      <div className="block">
        <div className="lbl">ลูกค้า / Customer</div>
        <div className="val" style={{ fontWeight: 700, marginBottom: 4 }}>
          {customer.name || '—'}
        </div>
        <div className="val" style={{ fontSize: 14 }}>
          {customer.address && (
            <>
              {customer.address}
              <br />
            </>
          )}
          {customer.taxId && (
            <>
              เลขประจำตัวผู้เสียภาษี: {customer.taxId}
              <br />
            </>
          )}
          {customer.branchCode && (
            <>
              สาขา: {customer.branchCode}
              <br />
            </>
          )}
          {customer.phone && <>โทร {customer.phone}</>}
        </div>
      </div>
      <div className="block">
        <dl className="kv">
          <dt>วันที่ / Date</dt>
          <dd>{fmtPaperDate(issueDate)}</dd>
          {validUntil && (
            <>
              <dt>{validUntilLabel || 'ยืนราคาถึง'}</dt>
              <dd>{fmtPaperDate(validUntil)}</dd>
            </>
          )}
          {customer.contact && (
            <>
              <dt>ผู้ติดต่อ</dt>
              <dd>{customer.contact}</dd>
            </>
          )}
          {extraMetaBlock}
        </dl>
      </div>
    </div>
  );
}
