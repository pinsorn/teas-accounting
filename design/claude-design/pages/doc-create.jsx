// === Generic document create page (Form + Live Preview) ===

function DocCreate({ docTypeLabel, docTypeEn, basePath, nextDocNo, statusOnSave, defaultTerms, validUntilLabel, validUntilOffsetDays, signRoles, extraFields, conversionFromLabel }) {
  const { nav } = useRoute();
  const toast = useToast();

  const [docNo, setDocNo] = useState(nextDocNo || "DRAFT-NEW");
  const [date, setDate] = useState("2026-05-22");
  const [validUntil, setValidUntil] = useState(() => {
    const d = new Date("2026-05-22");
    d.setDate(d.getDate() + (validUntilOffsetDays || 30));
    return d.toISOString().slice(0, 10);
  });
  const [customerId, setCustomerId] = useState("");
  const [terms, setTerms] = useState(defaultTerms || "เครดิต 30 วัน");
  const [salesperson, setSalesperson] = useState("กิตติพงษ์ สันติฤดี");
  const [items, setItems] = useState([
    { code: "", name: "", desc: "", qty: 1, unit: "", price: 0, disc: 0 },
  ]);
  const [notes, setNotes] = useState("");
  const [extras, setExtras] = useState({});

  const customer = customerId ? getCustomer(customerId) : { name: "", addr: "", taxId: "", contact: "" };
  const summary = computeSummary(items);

  const addItem = () => setItems(its => [...its, { code: "", name: "", desc: "", qty: 1, unit: "", price: 0, disc: 0 }]);
  const removeItem = (i) => setItems(its => its.filter((_, idx) => idx !== i));
  const updateItem = (i, patch) => setItems(its => its.map((it, idx) => idx === i ? { ...it, ...patch } : it));
  const onProductSelect = (i, code) => {
    const p = PRODUCTS.find(p => p.code === code);
    if (p) updateItem(i, { code: p.code, name: p.name, desc: p.desc, unit: p.unit, price: p.price });
    else updateItem(i, { code });
  };

  const handleSave = (publish) => {
    toast(publish ? `บันทึก ${docTypeLabel} และส่งให้ลูกค้าแล้ว` : "บันทึกฉบับร่างแล้ว");
    setTimeout(() => nav(basePath), 800);
  };

  return (
    <div>
      <PageHeader
        title={`สร้าง${docTypeLabel}ใหม่`}
        subtitle={`New ${docTypeEn} · กรอกข้อมูลฝั่งซ้าย ดูตัวอย่างเอกสารจริงฝั่งขวา`}
        actions={<>
          <button className="btn btn-ghost" onClick={() => nav(basePath)}>ยกเลิก</button>
          <button className="btn btn-secondary" onClick={() => handleSave(false)}>บันทึกฉบับร่าง</button>
          <button className="btn btn-primary" onClick={() => handleSave(true)}>
            <Icon.check /> บันทึก{statusOnSave === "sent" ? "และส่งให้ลูกค้า" : ""}
          </button>
        </>}
      />

      <div className="create-grid">
        {/* ── LEFT: FORM ── */}
        <div className="card" style={{ padding: 0 }}>
          {conversionFromLabel && (
            <div style={{ padding: "12px 22px", background: "var(--peach-50)", borderBottom: "1px solid var(--peach-200)", display: "flex", alignItems: "center", gap: 10, fontSize: 13 }}>
              <Icon.sparkle width="16" style={{ color: "var(--primary-ink)" }} />
              <span><b>สร้างจาก</b> {conversionFromLabel}</span>
            </div>
          )}

          {/* Section 1: Document info */}
          <div className="form-section">
            <h4><span className="num">1</span> ข้อมูลเอกสาร</h4>
            <div className="form-row three">
              <div className="field">
                <label>เลขที่เอกสาร</label>
                <input className="input" value={docNo} onChange={(e) => setDocNo(e.target.value)} placeholder="ระบบจะออกให้อัตโนมัติ" />
              </div>
              <div className="field">
                <label>วันที่ออกเอกสาร <span className="req">*</span></label>
                <input className="input" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
              </div>
              <div className="field">
                <label>{validUntilLabel || "ยืนราคาถึง"} <span className="req">*</span></label>
                <input className="input" type="date" value={validUntil} onChange={(e) => setValidUntil(e.target.value)} />
              </div>
            </div>
            <div className="form-row" style={{ marginTop: 12 }}>
              <div className="field">
                <label>เงื่อนไขการชำระเงิน</label>
                <select className="select" value={terms} onChange={(e) => setTerms(e.target.value)}>
                  <option>เงินสด</option>
                  <option>เครดิต 15 วัน</option>
                  <option>เครดิต 30 วัน</option>
                  <option>เครดิต 45 วัน</option>
                  <option>เครดิต 60 วัน</option>
                </select>
              </div>
              <div className="field">
                <label>พนักงานขาย</label>
                <select className="select" value={salesperson} onChange={(e) => setSalesperson(e.target.value)}>
                  <option>กิตติพงษ์ สันติฤดี</option>
                  <option>วันดี สมใจ</option>
                  <option>สมชาย รักงาน</option>
                </select>
              </div>
            </div>
          </div>

          {/* Section 2: Customer */}
          <div className="form-section">
            <h4><span className="num">2</span> ข้อมูลลูกค้า</h4>
            <div className="form-row full">
              <div className="field">
                <label>เลือกลูกค้า <span className="req">*</span></label>
                <select className="select" value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
                  <option value="">— เลือกลูกค้าจากรายการ —</option>
                  {CUSTOMERS.map(c => <option key={c.id} value={c.id}>{c.name} · {c.taxId}</option>)}
                </select>
              </div>
            </div>
            {customer.name && (
              <div style={{ marginTop: 14, padding: 14, background: "var(--surface-alt)", borderRadius: 10, border: "1px solid var(--border)", fontSize: 13 }}>
                <div style={{ fontWeight: 600, color: "var(--ink-900)", marginBottom: 4 }}>{customer.name}</div>
                <div style={{ color: "var(--text-2)", lineHeight: 1.55 }}>
                  {customer.addr}<br/>
                  เลขประจำตัวผู้เสียภาษี: <b>{customer.taxId}</b> · {customer.branch}<br/>
                  ติดต่อ: {customer.contact} · {customer.phone}
                </div>
              </div>
            )}
          </div>

          {/* Section 3: Items */}
          <div className="form-section">
            <h4><span className="num">3</span> รายการสินค้า / บริการ</h4>
            <table className="items-editor">
              <thead>
                <tr>
                  <th>#</th>
                  <th>สินค้า/รายละเอียด</th>
                  <th className="num" style={{ width: 80 }}>จำนวน</th>
                  <th style={{ width: 80 }}>หน่วย</th>
                  <th className="num" style={{ width: 110 }}>ราคา/หน่วย</th>
                  <th className="num" style={{ width: 70 }}>ลด%</th>
                  <th className="num" style={{ width: 110 }}>รวม</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {items.map((it, i) => {
                  const lineTotal = it.qty * it.price * (1 - (it.disc || 0) / 100);
                  return (
                    <tr key={i}>
                      <td>{i + 1}</td>
                      <td>
                        <select className="input" style={{ marginBottom: 4, background: "var(--surface)", borderColor: "var(--border)" }} value={it.code} onChange={(e) => onProductSelect(i, e.target.value)}>
                          <option value="">— เลือกสินค้า —</option>
                          {PRODUCTS.map(p => <option key={p.code} value={p.code}>{p.code} · {p.name}</option>)}
                        </select>
                        {it.desc && <input className="input" placeholder="รายละเอียดเพิ่มเติม" value={it.desc} onChange={(e) => updateItem(i, { desc: e.target.value })} style={{ fontSize: 12, color: "var(--text-2)" }} />}
                      </td>
                      <td><input className="input num" type="number" min="0" value={it.qty} onChange={(e) => updateItem(i, { qty: parseFloat(e.target.value) || 0 })} /></td>
                      <td><input className="input" value={it.unit} onChange={(e) => updateItem(i, { unit: e.target.value })} placeholder="ชิ้น" /></td>
                      <td><input className="input num" type="number" min="0" step="0.01" value={it.price} onChange={(e) => updateItem(i, { price: parseFloat(e.target.value) || 0 })} /></td>
                      <td><input className="input num" type="number" min="0" max="100" value={it.disc} onChange={(e) => updateItem(i, { disc: parseFloat(e.target.value) || 0 })} /></td>
                      <td className="num" style={{ paddingRight: 10, fontWeight: 600, fontVariantNumeric: "tabular-nums" }}>{fmt(lineTotal)}</td>
                      <td>
                        {items.length > 1 && (
                          <button className="btn btn-ghost btn-sm" onClick={() => removeItem(i)} title="ลบ" style={{ padding: 6 }}>
                            <Icon.trash width="14" />
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
            <button className="btn btn-secondary btn-sm" onClick={addItem} style={{ marginTop: 12 }}>
              <Icon.plus width="14" /> เพิ่มรายการ
            </button>

            {/* Summary preview */}
            <div style={{ marginTop: 16, padding: "12px 16px", background: "var(--peach-50)", border: "1px solid var(--peach-200)", borderRadius: 10, display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
              <div>
                <div style={{ fontSize: 11, color: "var(--text-3)", fontWeight: 600 }}>ก่อนภาษี</div>
                <div style={{ fontSize: 15, fontWeight: 700, fontVariantNumeric: "tabular-nums" }}>฿ {fmt(summary.beforeVat)}</div>
              </div>
              <div>
                <div style={{ fontSize: 11, color: "var(--text-3)", fontWeight: 600 }}>ส่วนลด</div>
                <div style={{ fontSize: 15, fontWeight: 700, fontVariantNumeric: "tabular-nums", color: "var(--danger)" }}>− ฿ {fmt(summary.discountTotal)}</div>
              </div>
              <div>
                <div style={{ fontSize: 11, color: "var(--text-3)", fontWeight: 600 }}>VAT 7%</div>
                <div style={{ fontSize: 15, fontWeight: 700, fontVariantNumeric: "tabular-nums" }}>฿ {fmt(summary.vat)}</div>
              </div>
              <div>
                <div style={{ fontSize: 11, color: "var(--primary-ink)", fontWeight: 600 }}>รวมทั้งสิ้น</div>
                <div style={{ fontSize: 17, fontWeight: 800, fontVariantNumeric: "tabular-nums", color: "var(--primary-ink)" }}>฿ {fmt(summary.total)}</div>
              </div>
            </div>
          </div>

          {/* Section 4: Extra fields */}
          {extraFields && (
            <div className="form-section">
              <h4><span className="num">4</span> ข้อมูลเพิ่มเติม</h4>
              {extraFields(extras, setExtras)}
            </div>
          )}

          {/* Section 5: Notes */}
          <div className="form-section">
            <h4><span className="num">{extraFields ? "5" : "4"}</span> หมายเหตุ</h4>
            <div className="field">
              <textarea className="textarea" placeholder="หมายเหตุ เงื่อนไขพิเศษ หรือข้อความถึงลูกค้า..." value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>
          </div>
        </div>

        {/* ── RIGHT: LIVE PREVIEW ── */}
        <div className="preview-side">
          <div style={{ marginBottom: 12, display: "flex", alignItems: "center", justifyContent: "space-between" }}>
            <div style={{ fontSize: 12, color: "var(--text-3)", fontWeight: 600, textTransform: "uppercase", letterSpacing: 0.6 }}>ตัวอย่างเอกสาร · LIVE PREVIEW</div>
            <div className="badge badge-draft"><span className="dot"></span>ฉบับร่าง</div>
          </div>
          <PaperDocument
            docType={docTypeLabel}
            docTypeEn={docTypeEn}
            docNo={docNo || "PREVIEW"}
            date={date}
            validUntil={validUntil}
            validUntilLabel={validUntilLabel}
            customer={customer.name ? customer : { name: "— กรุณาเลือกลูกค้า —", addr: "ที่อยู่ลูกค้าจะแสดงเมื่อเลือกลูกค้าแล้ว", taxId: "—" }}
            terms={terms}
            items={items.filter(it => it.name)}
            summary={summary}
            notes={notes}
            signRoles={signRoles}
            watermark="ฉบับร่าง"
            watermarkClass="warning"
          />
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { DocCreate });
