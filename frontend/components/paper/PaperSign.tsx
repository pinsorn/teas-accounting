import { resolveAttachmentUrl } from '@/lib/company-logo';
import type { PaperSignaturesDto } from './types';

export function PaperSign({
  signRoles,
  sellerName,
  counterpartyName,
  signatures,
}: {
  // Sprint 13j-PURCH D-supplement — `middle` is optional. When present, render a
  // 3-box strip (issuer / approver / payee) for the Payment Voucher; mirrors C#
  // PaperSignRoles.Middle. When absent, the strip is the original two boxes —
  // byte-identical to every Sales caller.
  signRoles: { left: string; middle?: string; right: string };
  sellerName: string;
  // cont.80 (Ham) — name the counterparty under the right signature box (the
  // customer/vendor), so the printed signature line says who signs.
  counterpartyName?: string;
  // doc-signature-and-foot-layout §F2.1/§F2.4 — replaces the dead `signatureImg` text hack.
  // null/undefined = the document is not signed yet (Draft) → every box renders exactly as
  // today (§I2).
  signatures?: PaperSignaturesDto | null;
}) {
  // Design review 2026-07-02 — every box carries the SAME three lines (role,
  // parenthesised name, date) in the standard Thai sign-off shape, so the
  // columns stay symmetric; mirrors PaperDocumentPdf.SignBox. A box with no
  // pre-known name (PV's middle ผู้อนุมัติ box) gets a 30-dot blank instead.
  const DOTS = '.'.repeat(30);
  const nameLine = (name?: string | null) => `( ${name || DOTS} )`;
  const DATE_LINE = 'วันที่ ____ / ____ / ______';

  const s = signatures;
  // doc-signature-and-foot-layout §D1/§D2 — stamp beside the signature, never over it; the stamp
  // renders on whichever box `stampOnMiddle` designates (false→left, true→middle — PV only),
  // mirroring the C# Sign()/SignBox() split exactly.
  const leftSig = resolveAttachmentUrl(s?.leftUrl);
  const middleSig = resolveAttachmentUrl(s?.middleUrl);
  const stampUrl = resolveAttachmentUrl(s?.stampUrl);
  const stampLeft = s && !s.stampOnMiddle ? stampUrl : null;
  const stampMiddle = s && s.stampOnMiddle ? stampUrl : null;

  // One image slot per box: stamp first (if any), then the signature, in a flex row
  // (`.sig-slot`, paper.css). No image at all → the ORIGINAL blank slot, byte-identical to today.
  function ImageSlot({ stamp, signature }: { stamp: string | null; signature: string | null }) {
    if (!stamp && !signature) return <div style={{ height: 50 }} />;
    return (
      <div className="sig-slot" style={{ height: 50 }}>
        {stamp && <img className="sig-img" src={stamp} alt="" />}
        {signature && <img className="sig-img" src={signature} alt="" />}
      </div>
    );
  }

  return (
    <div className="paper-sign">
      {/* Left = the issuer/seller (signRoles.left: ผู้ขาย / ผู้ส่งของ / ผู้ออก…) — that is
          us, so our name + signature belong here. Right = the counterparty's sign line. */}
      <div className="box">
        <ImageSlot stamp={stampLeft} signature={leftSig} />
        <div className="role">ลงชื่อ {signRoles.left}</div>
        {/* §A4 (Ham 2026-07-29) — the SIGNER'S PERSON NAME replaces the company name once a
            signer exists; null (Draft, or an actor with no user record) falls back to sellerName
            exactly as today. MUST mirror PaperDocumentPdf.cs's Sign() coalesce exactly — the same
            `Signatures?.LeftName ?? m.Seller.Name` expression — or screen and print diverge (I3). */}
        <div className="sub">{nameLine(s?.leftName ?? sellerName)}</div>
        {/* NEW — ตำแหน่ง, only when known; reuses the `.sub` class (I1: adopts the nearest
            existing sibling's style, no new CSS rule for this line). */}
        {s?.leftPosition && <div className="sub">{s.leftPosition}</div>}
        <div className="sub">{DATE_LINE}</div>
      </div>
      {/* Optional middle box (ผู้อนุมัติ) — only rendered for documents that supply a
          middle role (Payment Voucher). Keeps the two-box layout otherwise. */}
      {signRoles.middle != null && (
        <div className="box">
          <ImageSlot stamp={stampMiddle} signature={middleSig} />
          <div className="role">ลงชื่อ {signRoles.middle}</div>
          {/* §A4 — null middleName falls through nameLine's `|| DOTS` to today's dotted blank,
              byte-identical to before this spec. */}
          <div className="sub">{nameLine(s?.middleName)}</div>
          {s?.middlePosition && <div className="sub">{s.middlePosition}</div>}
          <div className="sub">{DATE_LINE}</div>
        </div>
      )}
      <div className="box">
        <div style={{ height: 50 }} />
        <div className="role">ลงชื่อ {signRoles.right}</div>
        {/* Right = the counterparty. NEVER signed, NEVER stamped, NEVER given a position — §A4/I5
            explicitly excludes this box; the name line stays counterpartyName, UNCHANGED. */}
        <div className="sub">{nameLine(counterpartyName)}</div>
        <div className="sub">{DATE_LINE}</div>
      </div>
    </div>
  );
}
