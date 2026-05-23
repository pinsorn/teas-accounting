// === Generic document detail page (paper-like) ===

function DocDetail({ doc, docTypeLabel, docTypeEn, basePath, primaryAction, statusWatermark, validUntilLabel, signRoles, extraMetaBlock, related, activity, allowConvert }) {
  const { nav } = useRoute();
  const toast = useToast();
  const cust = getCustomer(doc.customerId);
  const summary = computeSummary(doc.items);

  const watermark = statusWatermark || (
    doc.status === "cancelled" ? "ยกเลิก" :
    doc.status === "paid" ? "ชำระแล้ว" :
    doc.status === "draft" ? "ฉบับร่าง" :
    doc.status === "accepted" ? "ตอบรับแล้ว" :
    doc.status === "posted" ? "บันทึกแล้ว" :
    doc.status === "delivered" ? "ส่งของแล้ว" :
    null
  );
  const watermarkClass = (
    doc.status === "cancelled" ? "danger" :
    doc.status === "paid" || doc.status === "accepted" || doc.status === "posted" || doc.status === "delivered" ? "success" :
    "warning"
  );

  return (
    <div>
      <PageHeader
        title={`${docTypeLabel} ${doc.no}`}
        subtitle={`${docTypeEn} · ${cust.name}`}
        actions={<>
          <button className="btn btn-secondary" onClick={() => toast("ส่งอีเมลแล้ว")}><Icon.email /> ส่งอีเมล</button>
          <button className="btn btn-secondary" onClick={() => toast("กำลังพิมพ์...")}><Icon.print /> พิมพ์</button>
          <button className="btn btn-secondary" onClick={() => toast("ดาวน์โหลด PDF")}><Icon.download /> PDF</button>
          <button className="btn btn-icon"><Icon.more /></button>
        </>}
      />

      {/* Action bar */}
      <div className="doc-actionbar">
        <div className="status-block">
          <Badge status={doc.status} withEn />
        </div>
        <div className="docno-block">
          <span className="lbl">เลขอ้างอิงภายใน</span>
          <span className="val">{doc.no}</span>
        </div>
        <div className="right">
          {doc.status === "draft" && <button className="btn btn-secondary"><Icon.edit /> แก้ไข</button>}
          {doc.status === "draft" && <button className="btn btn-primary" onClick={() => toast("ส่งให้ลูกค้าแล้ว")}><Icon.email /> ส่งให้ลูกค้า</button>}
          {primaryAction && doc.status !== "cancelled" && doc.status !== "draft" && (
            <button className="btn btn-primary" onClick={() => { primaryAction.onClick?.(nav, toast); }}>
              <Icon.arrowRight /> {primaryAction.label}
            </button>
          )}
          {doc.status !== "cancelled" && doc.status !== "draft" && (
            <button className="btn btn-danger" onClick={() => toast("ยกเลิกเอกสารแล้ว")}><Icon.x /> ยกเลิก</button>
          )}
        </div>
      </div>

      <div className="detail-grid">
        <div className="paper-wrap">
          <PaperDocument
            docType={docTypeLabel}
            docTypeEn={docTypeEn}
            docNo={doc.no}
            date={doc.date}
            validUntil={doc.validUntil || doc.dueDate || doc.deliveryDate}
            validUntilLabel={validUntilLabel}
            customer={cust}
            terms={doc.terms}
            items={doc.items}
            summary={summary}
            notes={doc.notes || doc.reason}
            signRoles={signRoles}
            watermark={watermark}
            watermarkClass={watermarkClass}
            extraMetaBlock={extraMetaBlock?.(doc)}
            signatureImg={doc.status !== "draft" ? "K. Kittipong" : null}
          />
        </div>

        <div className="detail-side">
          {/* Summary highlight */}
          <div className="card" style={{
            background: "linear-gradient(135deg, var(--ink-900) 0%, var(--ink-800) 100%)",
            color: "white",
            border: "none",
          }}>
            <div style={{ padding: 20 }}>
              <div style={{ fontSize: 12, color: "var(--peach-200)", textTransform: "uppercase", letterSpacing: 1, fontWeight: 600 }}>ยอดรวมสุทธิ</div>
              <div style={{ fontSize: 30, fontWeight: 800, marginTop: 4, fontVariantNumeric: "tabular-nums" }}>
                ฿ {fmt(summary.total)}
              </div>
              <div style={{ fontSize: 12, color: "var(--ink-300)", marginTop: 6 }}>
                รวม VAT 7% : ฿ {fmt(summary.vat)}
              </div>
              <div style={{ marginTop: 14, padding: "10px 12px", background: "rgba(232,168,124,0.15)", borderRadius: 8, border: "1px solid rgba(232,168,124,0.3)" }}>
                <div style={{ fontSize: 11, color: "var(--peach-200)", letterSpacing: 0.5, fontWeight: 600, textTransform: "uppercase" }}>Internal Reference</div>
                <div style={{ fontSize: 13, fontWeight: 600, marginTop: 2 }}>#{doc.no.split("-").slice(-2).join("-")}</div>
              </div>
            </div>
          </div>

          {related && <RelatedDocs items={related} />}

          {activity && <ActivityLog entries={activity} />}
        </div>
      </div>
    </div>
  );
}

// Build related items from doc links
function buildRelated(doc) {
  const r = [];
  if (doc.linkedQT) r.push({ type: "ใบเสนอราคา", no: doc.linkedQT, icon: "quote", path: `/quotations/${doc.linkedQT}` });
  if (doc.linkedSO) r.push({ type: "ใบสั่งขาย", no: doc.linkedSO, icon: "cart", path: `/sales-orders/${doc.linkedSO}` });
  if (doc.linkedDO) r.push({ type: "ใบส่งของ", no: doc.linkedDO, icon: "truck", path: `/delivery-orders/${doc.linkedDO}` });
  if (doc.linkedTAX) r.push({ type: "ใบกำกับภาษี", no: doc.linkedTAX, icon: "tax", path: `/tax-invoices/${doc.linkedTAX}` });
  if (doc.linkedBL) r.push({ type: "ใบแจ้งหนี้", no: doc.linkedBL, icon: "bill", path: `/billing-notes/${doc.linkedBL}` });
  if (doc.linkedRC) r.push({ type: "ใบเสร็จรับเงิน", no: doc.linkedRC, icon: "receipt", path: `/receipts/${doc.linkedRC}` });
  if (doc.linkedCN) r.push({ type: "ใบลดหนี้", no: doc.linkedCN, icon: "cn", path: `/credit-notes/${doc.linkedCN}` });
  if (doc.linkedDN) r.push({ type: "ใบเพิ่มหนี้", no: doc.linkedDN, icon: "dn", path: `/debit-notes/${doc.linkedDN}` });
  return r;
}

function buildActivity(doc) {
  const entries = [];
  // Most recent first
  if (doc.status === "paid" || doc.status === "posted" || doc.status === "delivered" || doc.status === "accepted") {
    entries.push({ icon: "check", title: `${STATUS[doc.status]?.label || "เสร็จสิ้น"} โดยลูกค้า`, when: "วันนี้ 14:20 น.", by: "ระบบ" });
  }
  entries.push({ icon: "email", title: "ส่งอีเมลให้ลูกค้าสำเร็จ", when: "เมื่อวาน 10:45 น.", by: "ระบบอัตโนมัติ" });
  entries.push({ icon: "edit", title: "สร้างเอกสารต้นร่าง", when: "20 พ.ค. 2569, 09:12 น.", by: "สมชาย" });
  return entries;
}

Object.assign(window, { DocDetail, buildRelated, buildActivity });
