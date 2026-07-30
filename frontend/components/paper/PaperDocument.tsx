'use client';

import { PaperHead } from './PaperHead';
import { PaperMeta } from './PaperMeta';
import { PaperItems } from './PaperItems';
import { PaperFoot } from './PaperFoot';
import { PaperSign } from './PaperSign';
import type { PaperDocumentProps } from './types';
import { useSystemInfo } from '@/lib/queries';

export type {
  PaperDocumentProps,
  SellerInfo,
  CustomerInfo,
  PaperLineItem,
  PaperSummary,
  WatermarkVariant,
} from './types';

// Sprint 13j-FE ★ — A4 paper document. Visual contract shared by the FE
// detail/create preview and (downstream) the QuestPDF mirror. Props are
// LOCKED (§C4). Wrap in <div className="paper-wrap"> on detail pages, or in
// the create page's `.preview-side` for the sticky live preview.
export function PaperDocument({
  docType,
  docTypeEn,
  docNo,
  issueDate,
  validUntil,
  validUntilLabel,
  seller,
  customer,
  partyLabel,
  items,
  summary,
  amountWords,
  notes,
  signRoles,
  watermark,
  extraMetaBlock,
  signatures,
}: PaperDocumentProps) {
  // Non-VAT companies (ม.86): drive the foot's VAT visibility from /system/info.
  // An explicit summary.showVat (e.g. a fixture) still wins.
  const { data: sys } = useSystemInfo();
  const effectiveSummary =
    summary.showVat === undefined ? { ...summary, showVat: sys?.vatMode ?? true } : summary;
  return (
    <div className="paper font-doc">
      {watermark && <div className={`paper-wm ${watermark.variant}`}>{watermark.text}</div>}
      <PaperHead seller={seller} docType={docType} docTypeEn={docTypeEn} docNo={docNo} />
      <PaperMeta
        customer={customer}
        partyLabel={partyLabel}
        issueDate={issueDate}
        validUntil={validUntil}
        validUntilLabel={validUntilLabel}
        extraMetaBlock={extraMetaBlock}
      />
      <PaperItems items={items} />
      {/* doc-signature-and-foot-layout §F2.5/§C1 — หมายเหตุ + price summary + signature strip
          are one bottom-anchored group on screen too (mirrors the PDF's bottom-group atomicity;
          screen never paginates, so this is purely a wrapper, no layout behaviour change beyond
          the CSS in paper.css). */}
      <div className="paper-bottom">
        <PaperFoot summary={effectiveSummary} notes={notes} amountWords={amountWords} />
        <PaperSign signRoles={signRoles} sellerName={seller.name} counterpartyName={customer.name} signatures={signatures} />
      </div>
    </div>
  );
}
