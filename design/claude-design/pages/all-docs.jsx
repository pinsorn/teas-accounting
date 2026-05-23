// === Concrete document pages: List, Detail, Create wrappers ===

// ─── Quotation ─────────────────────────────────
function QuotationList() {
  return <DocList
    docTypeLabel="ใบเสนอราคา"
    docTypeEn="QUOTATIONS"
    items={QUOTATIONS}
    statusFilters={["draft", "sent", "accepted", "rejected", "expired", "cancelled"]}
    basePath="/quotations"
    createLabel="สร้างใบเสนอราคา"
    columns={[
      docNoCol("/quotations"),
      statusCol,
      customerCol,
      dateCol("date", "วันที่"),
      validUntilCol,
      totalCol,
    ]}
  />;
}
function QuotationDetail({ no }) {
  const doc = QUOTATIONS.find(q => q.no === no) || QUOTATIONS[0];
  const { nav } = useRoute();
  return <DocDetail
    doc={doc}
    docTypeLabel="ใบเสนอราคา"
    docTypeEn="QUOTATION"
    basePath="/quotations"
    validUntilLabel="ยืนราคาถึง"
    signRoles={["ผู้รับเสนอราคา / Customer", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
    primaryAction={doc.status === "accepted" ? { label: "แปลงเป็นใบสั่งขาย", onClick: (n, t) => { t("สร้างใบสั่งขายจากใบเสนอราคาแล้ว"); setTimeout(() => n("/sales-orders/05-2026-SO-ECOM-0001"), 800); } } : null}
    related={buildRelated(doc)}
    activity={buildActivity(doc)}
  />;
}
function QuotationCreate() {
  return <DocCreate
    docTypeLabel="ใบเสนอราคา"
    docTypeEn="QUOTATION"
    basePath="/quotations"
    nextDocNo="05-2026-QT-ECOM-0006"
    statusOnSave="sent"
    validUntilLabel="ยืนราคาถึง"
    validUntilOffsetDays={30}
    signRoles={["ผู้รับเสนอราคา / Customer", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
  />;
}

// ─── Sales Order ─────────────────────────────────
function SalesOrderList() {
  return <DocList
    docTypeLabel="ใบสั่งขาย"
    docTypeEn="SALES ORDERS"
    items={SALES_ORDERS}
    statusFilters={["draft", "confirmed", "delivered", "cancelled"]}
    basePath="/sales-orders"
    createLabel="สร้างใบสั่งขาย"
    columns={[
      docNoCol("/sales-orders"),
      statusCol,
      customerCol,
      dateCol("date", "วันที่"),
      dateCol("deliveryDate", "วันส่งของ"),
      totalCol,
    ]}
  />;
}
function SalesOrderDetail({ no }) {
  const doc = SALES_ORDERS.find(d => d.no === no) || SALES_ORDERS[0];
  return <DocDetail
    doc={doc}
    docTypeLabel="ใบสั่งขาย"
    docTypeEn="SALES ORDER"
    basePath="/sales-orders"
    validUntilLabel="วันที่ส่งของ"
    signRoles={["ผู้สั่งซื้อ / Customer", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
    primaryAction={{ label: "แปลงเป็นใบส่งของ", onClick: (n, t) => { t("สร้างใบส่งของจากใบสั่งขายแล้ว"); setTimeout(() => n("/delivery-orders/05-2026-DO-ECOM-0001"), 800); } }}
    related={buildRelated(doc)}
    activity={buildActivity(doc)}
  />;
}
function SalesOrderCreate() {
  return <DocCreate
    docTypeLabel="ใบสั่งขาย"
    docTypeEn="SALES ORDER"
    basePath="/sales-orders"
    nextDocNo="05-2026-SO-ECOM-0002"
    validUntilLabel="วันที่ส่งของ"
    validUntilOffsetDays={7}
    signRoles={["ผู้สั่งซื้อ / Customer", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
  />;
}

// ─── Delivery Order ─────────────────────────────────
function DeliveryOrderList() {
  return <DocList
    docTypeLabel="ใบส่งของ"
    docTypeEn="DELIVERY ORDERS"
    items={DELIVERY_ORDERS}
    statusFilters={["draft", "inTransit", "delivered", "cancelled"]}
    basePath="/delivery-orders"
    createLabel="สร้างใบส่งของ"
    columns={[
      docNoCol("/delivery-orders"),
      statusCol,
      customerCol,
      dateCol("date", "วันที่ส่ง"),
      { key: "tracking", label: "Tracking", w: 160, render: (d) => <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>{d.tracking || "—"}</span> },
      totalCol,
    ]}
  />;
}
function DeliveryOrderDetail({ no }) {
  const doc = DELIVERY_ORDERS.find(d => d.no === no) || DELIVERY_ORDERS[0];
  return <DocDetail
    doc={doc}
    docTypeLabel="ใบส่งของ"
    docTypeEn="DELIVERY ORDER"
    basePath="/delivery-orders"
    validUntilLabel="—"
    signRoles={["ผู้รับสินค้า / Receiver", "ผู้ส่งของ / Authorized Signature"]}
    primaryAction={{ label: "ออกใบกำกับภาษี", onClick: (n, t) => { t("ออกใบกำกับภาษีแล้ว"); setTimeout(() => n("/tax-invoices/05-2026-TI-ECOM-0001"), 800); } }}
    related={buildRelated(doc)}
    activity={buildActivity(doc)}
    extraMetaBlock={(d) => <>
      <dt>ผู้ขนส่ง</dt><dd>{d.courier}</dd>
      <dt>Tracking</dt><dd style={{ fontFamily: "var(--font-mono)", fontSize: 13 }}>{d.tracking}</dd>
    </>}
  />;
}
function DeliveryOrderCreate() {
  return <DocCreate
    docTypeLabel="ใบส่งของ"
    docTypeEn="DELIVERY ORDER"
    basePath="/delivery-orders"
    nextDocNo="05-2026-DO-ECOM-0002"
    validUntilLabel="—"
    validUntilOffsetDays={0}
    signRoles={["ผู้รับสินค้า / Receiver", "ผู้ส่งของ / Authorized Signature"]}
    extraFields={(ex, setEx) => (
      <div className="form-row">
        <div className="field">
          <label>ผู้ขนส่ง</label>
          <select className="select" value={ex.courier || ""} onChange={(e) => setEx({ ...ex, courier: e.target.value })}>
            <option value="">— เลือก —</option>
            <option>TEAS Express</option>
            <option>Kerry Express</option>
            <option>Flash Express</option>
            <option>ส่งเอง</option>
          </select>
        </div>
        <div className="field">
          <label>หมายเลข Tracking</label>
          <input className="input" placeholder="TX-..." value={ex.tracking || ""} onChange={(e) => setEx({ ...ex, tracking: e.target.value })} />
        </div>
      </div>
    )}
  />;
}

// ─── Tax Invoice ─────────────────────────────────
function TaxInvoiceList() {
  return <DocList
    docTypeLabel="ใบกำกับภาษี"
    docTypeEn="TAX INVOICES"
    items={TAX_INVOICES}
    statusFilters={["draft", "posted", "cancelled"]}
    basePath="/tax-invoices"
    createLabel="สร้างใบกำกับภาษี"
    columns={[docNoCol("/tax-invoices"), statusCol, customerCol, dateCol("date", "วันที่"), totalCol]}
  />;
}
function TaxInvoiceDetail({ no }) {
  const doc = TAX_INVOICES.find(d => d.no === no) || TAX_INVOICES[0];
  return <DocDetail
    doc={doc}
    docTypeLabel="ใบกำกับภาษี / ใบส่งสินค้า"
    docTypeEn="TAX INVOICE"
    basePath="/tax-invoices"
    signRoles={["ผู้รับสินค้า / Receiver", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
    primaryAction={{ label: "ออกใบแจ้งหนี้", onClick: (n, t) => { t("ออกใบแจ้งหนี้แล้ว"); setTimeout(() => n("/billing-notes/05-2026-BL-ECOM-0001"), 800); } }}
    related={buildRelated(doc)}
    activity={buildActivity(doc)}
  />;
}
function TaxInvoiceCreate() {
  return <DocCreate
    docTypeLabel="ใบกำกับภาษี / ใบส่งสินค้า"
    docTypeEn="TAX INVOICE"
    basePath="/tax-invoices"
    nextDocNo="05-2026-TI-ECOM-0002"
    validUntilLabel="—"
    validUntilOffsetDays={0}
    signRoles={["ผู้รับสินค้า / Receiver", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
  />;
}

// ─── Billing Note ─────────────────────────────────
function BillingNoteList() {
  return <DocList
    docTypeLabel="ใบแจ้งหนี้"
    docTypeEn="BILLING NOTES"
    items={BILLING_NOTES}
    statusFilters={["draft", "pending", "paid", "overdue", "cancelled"]}
    basePath="/billing-notes"
    createLabel="สร้างใบแจ้งหนี้"
    columns={[
      docNoCol("/billing-notes"),
      statusCol,
      customerCol,
      dateCol("date", "วันที่"),
      dateCol("dueDate", "ครบกำหนด"),
      totalCol,
    ]}
  />;
}
function BillingNoteDetail({ no }) {
  const doc = BILLING_NOTES.find(d => d.no === no) || BILLING_NOTES[0];
  return <DocDetail
    doc={doc}
    docTypeLabel="ใบแจ้งหนี้"
    docTypeEn="BILLING NOTE"
    basePath="/billing-notes"
    validUntilLabel="ครบกำหนดชำระ"
    signRoles={["ผู้รับใบแจ้งหนี้ / Customer", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
    primaryAction={{ label: "บันทึกการรับชำระ", onClick: (n, t) => { t("ออกใบเสร็จรับเงินแล้ว"); setTimeout(() => n("/receipts/05-2026-RC-ECOM-0001"), 800); } }}
    related={buildRelated(doc)}
    activity={buildActivity(doc)}
  />;
}
function BillingNoteCreate() {
  return <DocCreate
    docTypeLabel="ใบแจ้งหนี้"
    docTypeEn="BILLING NOTE"
    basePath="/billing-notes"
    nextDocNo="05-2026-BL-ECOM-0002"
    validUntilLabel="ครบกำหนดชำระ"
    validUntilOffsetDays={30}
    signRoles={["ผู้รับใบแจ้งหนี้ / Customer", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
  />;
}

// ─── Receipt ─────────────────────────────────
function ReceiptList() {
  return <DocList
    docTypeLabel="ใบเสร็จรับเงิน"
    docTypeEn="RECEIPTS"
    items={RECEIPTS}
    statusFilters={["draft", "paid", "cancelled"]}
    basePath="/receipts"
    createLabel="สร้างใบเสร็จรับเงิน"
    columns={[
      docNoCol("/receipts"),
      statusCol,
      customerCol,
      dateCol("date", "วันที่"),
      { key: "paymentMethod", label: "ช่องทาง", render: (d) => d.paymentMethod },
      totalCol,
    ]}
  />;
}
function ReceiptDetail({ no }) {
  const doc = RECEIPTS.find(d => d.no === no) || RECEIPTS[0];
  return <DocDetail
    doc={doc}
    docTypeLabel="ใบเสร็จรับเงิน"
    docTypeEn="OFFICIAL RECEIPT"
    basePath="/receipts"
    signRoles={["ผู้ชำระเงิน / Customer", "ผู้รับเงิน / Cashier"]}
    related={buildRelated(doc)}
    activity={buildActivity(doc)}
    extraMetaBlock={(d) => <>
      <dt>ช่องทางชำระ</dt><dd>{d.paymentMethod}</dd>
      <dt>อ้างอิง</dt><dd style={{ fontFamily: "var(--font-mono)", fontSize: 13 }}>{d.reference}</dd>
    </>}
  />;
}
function ReceiptCreate() {
  return <DocCreate
    docTypeLabel="ใบเสร็จรับเงิน"
    docTypeEn="OFFICIAL RECEIPT"
    basePath="/receipts"
    nextDocNo="05-2026-RC-ECOM-0002"
    validUntilLabel="—"
    validUntilOffsetDays={0}
    signRoles={["ผู้ชำระเงิน / Customer", "ผู้รับเงิน / Cashier"]}
    extraFields={(ex, setEx) => (
      <div className="form-row">
        <div className="field">
          <label>ช่องทางชำระ <span className="req">*</span></label>
          <select className="select" value={ex.payment || ""} onChange={(e) => setEx({ ...ex, payment: e.target.value })}>
            <option value="">— เลือก —</option>
            <option>เงินสด</option>
            <option>โอนผ่านธนาคาร (SCB)</option>
            <option>โอนผ่านธนาคาร (KBANK)</option>
            <option>บัตรเครดิต</option>
            <option>เช็ค</option>
          </select>
        </div>
        <div className="field">
          <label>เลขที่อ้างอิง</label>
          <input className="input" placeholder="FT2606xxxxx / เช็คเลขที่" value={ex.ref || ""} onChange={(e) => setEx({ ...ex, ref: e.target.value })} />
        </div>
      </div>
    )}
  />;
}

// ─── Credit Note ─────────────────────────────────
function CreditNoteList() {
  return <DocList
    docTypeLabel="ใบลดหนี้"
    docTypeEn="CREDIT NOTES"
    items={CREDIT_NOTES}
    statusFilters={["draft", "issued", "cancelled"]}
    basePath="/credit-notes"
    createLabel="สร้างใบลดหนี้"
    columns={[
      docNoCol("/credit-notes"),
      statusCol,
      customerCol,
      dateCol("date", "วันที่"),
      { key: "reason", label: "เหตุผล", render: (d) => d.reason },
      totalCol,
    ]}
  />;
}
function CreditNoteDetail({ no }) {
  const doc = CREDIT_NOTES.find(d => d.no === no) || CREDIT_NOTES[0];
  return <DocDetail
    doc={doc}
    docTypeLabel="ใบลดหนี้"
    docTypeEn="CREDIT NOTE"
    basePath="/credit-notes"
    signRoles={["ผู้รับใบลดหนี้ / Customer", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
    related={buildRelated(doc)}
    activity={buildActivity(doc)}
    extraMetaBlock={(d) => <><dt>เหตุผลการลดหนี้</dt><dd>{d.reason}</dd></>}
  />;
}
function CreditNoteCreate() {
  return <DocCreate
    docTypeLabel="ใบลดหนี้"
    docTypeEn="CREDIT NOTE"
    basePath="/credit-notes"
    nextDocNo="05-2026-CN-ECOM-0002"
    validUntilLabel="—"
    validUntilOffsetDays={0}
    signRoles={["ผู้รับใบลดหนี้ / Customer", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
    extraFields={(ex, setEx) => (
      <div className="form-row full">
        <div className="field">
          <label>เหตุผลการลดหนี้ <span className="req">*</span></label>
          <select className="select" value={ex.reason || ""} onChange={(e) => setEx({ ...ex, reason: e.target.value })}>
            <option value="">— เลือก —</option>
            <option>ลดหย่อนสินค้าชำรุดบางส่วน</option>
            <option>คืนสินค้า</option>
            <option>ลดราคาสินค้า</option>
            <option>ออกใบกำกับเกินจริง</option>
            <option>อื่น ๆ</option>
          </select>
        </div>
      </div>
    )}
  />;
}

// ─── Debit Note ─────────────────────────────────
function DebitNoteList() {
  return <DocList
    docTypeLabel="ใบเพิ่มหนี้"
    docTypeEn="DEBIT NOTES"
    items={DEBIT_NOTES}
    statusFilters={["draft", "issued", "cancelled"]}
    basePath="/debit-notes"
    createLabel="สร้างใบเพิ่มหนี้"
    columns={[
      docNoCol("/debit-notes"),
      statusCol,
      customerCol,
      dateCol("date", "วันที่"),
      { key: "reason", label: "เหตุผล", render: (d) => d.reason },
      totalCol,
    ]}
  />;
}
function DebitNoteDetail({ no }) {
  const doc = DEBIT_NOTES.find(d => d.no === no) || DEBIT_NOTES[0];
  return <DocDetail
    doc={doc}
    docTypeLabel="ใบเพิ่มหนี้"
    docTypeEn="DEBIT NOTE"
    basePath="/debit-notes"
    signRoles={["ผู้รับใบเพิ่มหนี้ / Customer", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
    related={buildRelated(doc)}
    activity={buildActivity(doc)}
    extraMetaBlock={(d) => <><dt>เหตุผลการเพิ่มหนี้</dt><dd>{d.reason}</dd></>}
  />;
}
function DebitNoteCreate() {
  return <DocCreate
    docTypeLabel="ใบเพิ่มหนี้"
    docTypeEn="DEBIT NOTE"
    basePath="/debit-notes"
    nextDocNo="05-2026-DN-ECOM-0002"
    validUntilLabel="—"
    validUntilOffsetDays={0}
    signRoles={["ผู้รับใบเพิ่มหนี้ / Customer", "ผู้มีอำนาจลงนาม / Authorized Signature"]}
    extraFields={(ex, setEx) => (
      <div className="form-row full">
        <div className="field">
          <label>เหตุผลการเพิ่มหนี้ <span className="req">*</span></label>
          <select className="select" value={ex.reason || ""} onChange={(e) => setEx({ ...ex, reason: e.target.value })}>
            <option value="">— เลือก —</option>
            <option>ค่าบริการขนส่งเพิ่มเติม</option>
            <option>ปรับเพิ่มราคาสินค้า</option>
            <option>ออกใบกำกับต่ำกว่าจริง</option>
            <option>เพิ่มสินค้า/บริการ</option>
            <option>อื่น ๆ</option>
          </select>
        </div>
      </div>
    )}
  />;
}

Object.assign(window, {
  QuotationList, QuotationDetail, QuotationCreate,
  SalesOrderList, SalesOrderDetail, SalesOrderCreate,
  DeliveryOrderList, DeliveryOrderDetail, DeliveryOrderCreate,
  TaxInvoiceList, TaxInvoiceDetail, TaxInvoiceCreate,
  BillingNoteList, BillingNoteDetail, BillingNoteCreate,
  ReceiptList, ReceiptDetail, ReceiptCreate,
  CreditNoteList, CreditNoteDetail, CreditNoteCreate,
  DebitNoteList, DebitNoteDetail, DebitNoteCreate,
});
