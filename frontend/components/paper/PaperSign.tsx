export function PaperSign({
  signRoles,
  sellerName,
  signatureImg,
}: {
  signRoles: { left: string; right: string };
  sellerName: string;
  signatureImg?: string;
}) {
  return (
    <div className="paper-sign">
      <div className="box">
        <div style={{ height: 50 }} />
        <div className="role">{signRoles.left}</div>
        <div className="sub">วันที่ ___ / ___ / ______</div>
      </div>
      <div className="box">
        <div style={{ height: 50, display: 'grid', placeItems: 'center' }}>
          {signatureImg && <span className="sig">{signatureImg}</span>}
        </div>
        <div className="role">{signRoles.right}</div>
        <div className="sub">{sellerName}</div>
      </div>
    </div>
  );
}
