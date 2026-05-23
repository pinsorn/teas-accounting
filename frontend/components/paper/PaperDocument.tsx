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
  items,
  summary,
  amountWords,
  notes,
  signRoles,
  watermark,
  extraMetaBlock,
  signatureImg,
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
        issueDate={issueDate}
        validUntil={validUntil}
        validUntilLabel={validUntilLabel}
        extraMetaBlock={extraMetaBlock}
      />
      <PaperItems items={items} />
      <PaperFoot summary={effectiveSummary} notes={notes} amountWords={amountWords} />
      <PaperSign signRoles={signRoles} sellerName={seller.name} signatureImg={signatureImg} />
    </div>
  );
}
