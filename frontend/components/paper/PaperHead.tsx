import Image from 'next/image';
import { resolveLogoUrl, FALLBACK_LOGO } from '@/lib/company-logo';
import type { SellerInfo } from './types';

// ม.86/4 #1+#2 — title + seller name/address/taxId/branch (compliance).
export function PaperHead({
  seller,
  docType,
  docTypeEn,
  docNo,
}: {
  seller: SellerInfo;
  docType: string;
  docTypeEn: string;
  docNo: string;
}) {
  const logoSrc = resolveLogoUrl(seller.logoUrl);
  // A real uploaded logo renders clean (transparent bg, contained, wider box allowed); only
  // the bundled fallback mascot keeps the branded peach square (Ham 2026-07-01).
  const hasLogo = logoSrc !== FALLBACK_LOGO;
  return (
    <div className="paper-head">
      <div className="paper-company">
        <div className={`mark ${hasLogo ? 'is-logo' : 'is-fallback'}`}>
          <Image
            src={logoSrc}
            alt=""
            width={hasLogo ? 180 : 56}
            height={56}
            aria-hidden
            unoptimized={logoSrc.startsWith('/api/proxy/')}
          />
        </div>
        <div className="info">
          <div className="name">{seller.name}</div>
          <div className="addr">
            {seller.address}
            <br />
            เลขประจำตัวผู้เสียภาษี: {seller.taxId} · สาขา {seller.branchCode}
            {(seller.phone || seller.email) && (
              <>
                <br />
                {seller.phone && <>โทร {seller.phone}</>}
                {seller.phone && seller.email && <> · </>}
                {seller.email}
              </>
            )}
          </div>
        </div>
      </div>
      <div className="paper-title">
        <div className="label-en">{docTypeEn}</div>
        <div className="label-th">{docType}</div>
        <div className="docno">{docNo || '—'}</div>
      </div>
    </div>
  );
}
