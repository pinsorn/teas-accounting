// === Dashboard page ===

function Dashboard() {
  const { nav } = useRoute();

  const stats = [
    { lbl: "ยอดขายเดือนนี้", val: "฿ 234,580", delta: "+12.4%", up: true, icon: "trending" },
    { lbl: "ใบเสนอราคาเปิด", val: "5", delta: "+2 จากสัปดาห์ก่อน", up: true, icon: "quote" },
    { lbl: "รอชำระเงิน", val: "฿ 89,420", delta: "เกินกำหนด 2 ใบ", up: false, icon: "alert" },
    { lbl: "ส่งของพรุ่งนี้", val: "8", delta: "ออเดอร์ใหม่ 3", up: true, icon: "truck" },
  ];

  const recentDocs = [
    { type: "ใบเสร็จ", no: "05-2026-RC-ECOM-0001", customer: "บริษัท สยาม อควาเรียม", amount: 3745, status: "paid", date: "12 มิ.ย. 2569" },
    { type: "ใบกำกับภาษี", no: "05-2026-TI-ECOM-0001", customer: "บริษัท สยาม อควาเรียม", amount: 3745, status: "posted", date: "28 พ.ค. 2569" },
    { type: "ใบเสนอราคา", no: "05-2026-QT-ECOM-0002", customer: "บริษัท แอคมี อีคอมเมิร์ซ", amount: 11352, status: "sent", date: "21 พ.ค. 2569" },
    { type: "ใบสั่งขาย", no: "05-2026-SO-ECOM-0001", customer: "บริษัท สยาม อควาเรียม", amount: 3745, status: "confirmed", date: "21 พ.ค. 2569" },
    { type: "ใบเสนอราคา", no: "05-2026-QT-ECOM-0003", customer: "บจก. นวัตกรรมดีเลิศ", amount: 14017, status: "draft", date: "22 พ.ค. 2569" },
  ];

  const tasks = [
    { icon: "warn", title: "ใบเสนอราคา 05-2026-QT-ECOM-0004 ใกล้หมดอายุ", meta: "ยืนราคาถึง 14 มิ.ย. · ครบ 24 ชม.", cls: "warning" },
    { icon: "alert", title: "ใบแจ้งหนี้ 05-2026-BL-ECOM-0001 เกินกำหนดชำระ", meta: "ครบกำหนด 29 มิ.ย. · เกิน 5 วัน", cls: "danger" },
    { icon: "truck", title: "นัดส่งของ 8 ออเดอร์ พรุ่งนี้ 14:00", meta: "TEAS Express · 3 ออเดอร์ต้องการ COD", cls: "info" },
  ];

  return (
    <div>
      <PageHeader
        title="สวัสดี, คุณสมชาย 👋"
        subtitle="วันนี้ พฤหัสบดี ที่ 21 พฤษภาคม 2569 · มีงานรออยู่ 3 รายการ"
        actions={<>
          <button className="btn btn-secondary"><Icon.download /> ส่งออกรายงาน</button>
          <button className="btn btn-primary" onClick={() => nav("/quotations/new")}><Icon.plus /> สร้างใบเสนอราคา</button>
        </>}
      />

      {/* Welcome banner with mascot */}
      <div className="card" style={{
        background: "linear-gradient(120deg, var(--peach-50) 0%, var(--peach-100) 60%, var(--peach-50) 100%)",
        border: "1px solid var(--peach-200)",
        padding: "20px 24px",
        display: "flex",
        alignItems: "center",
        gap: 20,
        marginBottom: 24,
        position: "relative",
        overflow: "hidden",
      }}>
        <Mascot size={80} />
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 17, fontWeight: 700, color: "var(--ink-900)", marginBottom: 4 }}>
            พร้อมทำงานวันที่ดี ๆ แล้วครับ
          </div>
          <div style={{ fontSize: 13.5, color: "var(--ink-700)" }}>
            เดือนนี้ปิดยอดขายไปแล้ว <b>฿234,580</b> เพิ่มขึ้น <b style={{ color: "var(--success)" }}>+12.4%</b> จากเดือนก่อน 🎉
          </div>
        </div>
        <button className="btn btn-secondary" style={{ background: "white" }}>ดูสรุปภาพรวม <Icon.arrowRight width="14" /></button>
        <svg viewBox="0 0 100 100" style={{ position: "absolute", right: -20, bottom: -20, width: 180, opacity: 0.08 }}>
          <circle cx="50" cy="50" r="40" fill="var(--peach-700)" />
        </svg>
      </div>

      {/* KPIs */}
      <div className="stat-grid">
        {stats.map((s, i) => {
          const I = Icon[s.icon];
          return (
            <div className="stat" key={i}>
              <div className="icon-wrap"><I width="18" /></div>
              <div className="lbl">{s.lbl}</div>
              <div className="val">{s.val}</div>
              <div className={`delta ${s.up ? "up" : "dn"}`}>{s.delta}</div>
            </div>
          );
        })}
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1.6fr 1fr", gap: 16 }}>
        {/* Recent activity */}
        <div className="card">
          <div className="card-h">
            <h3>เอกสารล่าสุด</h3>
            <Link to="/quotations" style={{ color: "var(--primary-ink)", fontSize: 12.5, fontWeight: 600 }}>ดูทั้งหมด →</Link>
          </div>
          <table className="tbl">
            <thead>
              <tr>
                <th>ประเภท</th>
                <th>เลขที่</th>
                <th>ลูกค้า</th>
                <th className="num">จำนวนเงิน</th>
                <th>สถานะ</th>
              </tr>
            </thead>
            <tbody>
              {recentDocs.map((d, i) => (
                <tr key={i}>
                  <td style={{ color: "var(--text-2)", fontSize: 12.5 }}>{d.type}</td>
                  <td><span className="docno">{d.no}</span></td>
                  <td style={{ maxWidth: 220, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{d.customer}</td>
                  <td className="num"><b>฿ {fmt(d.amount)}</b></td>
                  <td><Badge status={d.status} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Tasks */}
        <div className="card">
          <div className="card-h">
            <h3>สิ่งที่ต้องทำ</h3>
            <span className="badge badge-primary">3</span>
          </div>
          <div className="card-b" style={{ padding: 12 }}>
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              {tasks.map((t, i) => {
                const I = Icon[t.icon];
                return (
                  <div key={i} style={{
                    display: "flex",
                    gap: 12,
                    padding: 12,
                    borderRadius: 10,
                    background: `var(--${t.cls}-bg)`,
                    border: `1px solid var(--${t.cls})`,
                    borderLeftWidth: 4,
                  }}>
                    <div style={{ color: `var(--${t.cls})`, marginTop: 1 }}><I width="18" /></div>
                    <div>
                      <div style={{ fontSize: 13, fontWeight: 600, color: "var(--ink-900)" }}>{t.title}</div>
                      <div style={{ fontSize: 11.5, color: "var(--text-2)", marginTop: 2 }}>{t.meta}</div>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </div>

      {/* Sales flow diagram */}
      <div className="card" style={{ marginTop: 16 }}>
        <div className="card-h">
          <h3>กระแสเอกสารขาย / Sales Document Flow</h3>
          <span style={{ fontSize: 12, color: "var(--text-3)" }}>คลิกขั้นตอนเพื่อไปยังรายการ</span>
        </div>
        <div className="card-b">
          <div style={{ display: "flex", alignItems: "center", gap: 6, flexWrap: "wrap" }}>
            {[
              { label: "ใบเสนอราคา", path: "/quotations", icon: "quote", count: 5, color: "peach" },
              { label: "ใบสั่งขาย", path: "/sales-orders", icon: "cart", count: 1, color: "peach" },
              { label: "ใบส่งของ", path: "/delivery-orders", icon: "truck", count: 1, color: "peach" },
              { label: "ใบกำกับภาษี", path: "/tax-invoices", icon: "tax", count: 1, color: "peach" },
              { label: "ใบแจ้งหนี้", path: "/billing-notes", icon: "bill", count: 1, color: "peach" },
              { label: "ใบเสร็จรับเงิน", path: "/receipts", icon: "receipt", count: 1, color: "peach" },
            ].map((step, i, arr) => {
              const I = Icon[step.icon];
              return (
                <React.Fragment key={i}>
                  <button onClick={() => nav(step.path)} style={{
                    flex: 1,
                    minWidth: 110,
                    padding: "14px 12px",
                    borderRadius: 12,
                    border: "1px solid var(--border)",
                    background: "var(--surface)",
                    cursor: "pointer",
                    display: "flex",
                    flexDirection: "column",
                    alignItems: "center",
                    gap: 8,
                    transition: "all 150ms",
                    fontFamily: "inherit",
                  }}
                  onMouseEnter={(e) => { e.currentTarget.style.borderColor = "var(--peach-400)"; e.currentTarget.style.background = "var(--peach-50)"; }}
                  onMouseLeave={(e) => { e.currentTarget.style.borderColor = "var(--border)"; e.currentTarget.style.background = "var(--surface)"; }}>
                    <div style={{ width: 36, height: 36, borderRadius: 10, background: "var(--peach-100)", color: "var(--primary-ink)", display: "grid", placeItems: "center" }}>
                      <I width="20" />
                    </div>
                    <div style={{ fontSize: 12.5, fontWeight: 600, color: "var(--ink-900)" }}>{step.label}</div>
                    <div style={{ fontSize: 11, color: "var(--text-3)" }}>{step.count} เปิดอยู่</div>
                  </button>
                  {i < arr.length - 1 && <Icon.arrowRight width="16" style={{ color: "var(--ink-300)" }} />}
                </React.Fragment>
              );
            })}
          </div>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { Dashboard });
