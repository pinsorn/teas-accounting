export function PaperSign({
  signRoles,
  sellerName,
  counterpartyName,
  signatureImg,
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
  signatureImg?: string;
}) {
  // Design review 2026-07-02 — every box carries the SAME three lines (role,
  // parenthesised name, date) in the standard Thai sign-off shape, so the
  // columns stay symmetric; mirrors PaperDocumentPdf.SignBox. A box with no
  // pre-known name (PV's middle ผู้อนุมัติ box) gets a 30-dot blank instead.
  const DOTS = '.'.repeat(30);
  const nameLine = (name?: string | null) => `( ${name || DOTS} )`;
  const DATE_LINE = 'วันที่ ____ / ____ / ______';

  return (
    <div className="paper-sign">
      {/* Left = the issuer/seller (signRoles.left: ผู้ขาย / ผู้ส่งของ / ผู้ออก…) — that is
          us, so our name + signature belong here. Right = the counterparty's sign line. */}
      <div className="box">
        <div style={{ height: 50, display: 'grid', placeItems: 'center' }}>
          {signatureImg && <span className="sig">{signatureImg}</span>}
        </div>
        <div className="role">ลงชื่อ {signRoles.left}</div>
        <div className="sub">{nameLine(sellerName)}</div>
        <div className="sub">{DATE_LINE}</div>
      </div>
      {/* Optional middle box (ผู้อนุมัติ) — only rendered for documents that supply a
          middle role (Payment Voucher). Keeps the two-box layout otherwise. */}
      {signRoles.middle != null && (
        <div className="box">
          <div style={{ height: 50 }} />
          <div className="role">ลงชื่อ {signRoles.middle}</div>
          <div className="sub">{nameLine(null)}</div>
          <div className="sub">{DATE_LINE}</div>
        </div>
      )}
      <div className="box">
        <div style={{ height: 50 }} />
        <div className="role">ลงชื่อ {signRoles.right}</div>
        <div className="sub">{nameLine(counterpartyName)}</div>
        <div className="sub">{DATE_LINE}</div>
      </div>
    </div>
  );
}
