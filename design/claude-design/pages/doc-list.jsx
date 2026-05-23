// === Generic document list page ===
// Used by Quotations, SOs, DOs, Tax Invoices, etc.

function DocList({ docTypeLabel, docTypeEn, items, statusFilters, basePath, createLabel, columns, getCustomerNameById }) {
  const { nav } = useRoute();
  const [filterStatus, setFilterStatus] = useState("all");
  const [filterCustomer, setFilterCustomer] = useState("");
  const [bu, setBu] = useState("ECOM");
  const [from, setFrom] = useState("2026-05-01");
  const [to, setTo] = useState("2026-05-31");
  const [tab, setTab] = useState("overview");

  const filtered = items.filter(it => {
    if (filterStatus !== "all" && it.status !== filterStatus) return false;
    if (filterCustomer) {
      const c = getCustomer(it.customerId);
      if (!c.name.toLowerCase().includes(filterCustomer.toLowerCase())) return false;
    }
    return true;
  });

  return (
    <div>
      <PageHeader
        title={`${docTypeLabel} / ${docTypeEn}`}
        subtitle={`ทั้งหมด ${items.length} ฉบับ · เปิดอยู่ ${items.filter(i => !["cancelled", "rejected"].includes(i.status)).length} ฉบับ`}
        actions={<>
          <button className="btn btn-secondary"><Icon.download /> ส่งออก</button>
          <button className="btn btn-secondary"><Icon.print /> พิมพ์</button>
          <button className="btn btn-primary" onClick={() => nav(`${basePath}/new`)}>
            <Icon.plus /> {createLabel}
          </button>
        </>}
      />

      <div className="tabs">
        <button className={`tab ${tab === "overview" ? "active" : ""}`} onClick={() => setTab("overview")}>ภาพรวม</button>
        <button className={`tab ${tab === "report" ? "active" : ""}`} onClick={() => setTab("report")}>รายงาน</button>
        <button className={`tab ${tab === "settings" ? "active" : ""}`} onClick={() => setTab("settings")}>การตั้งค่า</button>
      </div>

      <div className="filter-bar">
        <div className="field">
          <label>สถานะ</label>
          <select className="select" value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)}>
            <option value="all">ทั้งหมด</option>
            {statusFilters.map(s => <option key={s} value={s}>{STATUS[s]?.label || s}</option>)}
          </select>
        </div>
        <div className="field">
          <label>หน่วยธุรกิจ BU</label>
          <select className="select" value={bu} onChange={(e) => setBu(e.target.value)}>
            <option value="ECOM">ECOM</option>
            <option value="RETAIL">RETAIL</option>
            <option value="WHOLESALE">WHOLESALE</option>
          </select>
        </div>
        <div className="field">
          <label>ลูกค้า</label>
          <div className="input-prefix">
            <span className="pfx"><Icon.search width="13" /></span>
            <input className="input" placeholder="ค้นหาลูกค้า..." value={filterCustomer} onChange={(e) => setFilterCustomer(e.target.value)} />
          </div>
        </div>
        <div className="field">
          <label>วันที่ตั้งแต่</label>
          <input className="input" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </div>
        <div className="field">
          <label>วันที่ถึง</label>
          <input className="input" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </div>
      </div>

      <div className="card">
        {filtered.length === 0 ? (
          <div className="empty-state">
            <Mascot size={120} />
            <h3>ยังไม่มี{docTypeLabel}ตามเงื่อนไข</h3>
            <p>ลองเปลี่ยนช่วงวันที่ หรือสร้างใหม่ได้เลย</p>
            <button className="btn btn-primary" onClick={() => nav(`${basePath}/new`)}>
              <Icon.plus /> {createLabel}
            </button>
          </div>
        ) : (
          <>
            <table className="tbl">
              <thead>
                <tr>
                  {columns.map(c => <th key={c.key} className={c.num ? "num" : ""} style={{ width: c.w }}>{c.label}</th>)}
                  <th style={{ width: 80 }}>จัดการ</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((d, i) => {
                  const cust = getCustomer(d.customerId);
                  const sum = computeSummary(d.items);
                  return (
                    <tr key={i}>
                      {columns.map(c => {
                        const val = c.render ? c.render(d, cust, sum) : d[c.key];
                        return <td key={c.key} className={c.num ? "num" : ""}>{val}</td>;
                      })}
                      <td>
                        <Link to={`${basePath}/${d.no}`} style={{ color: "var(--primary-ink)", fontWeight: 600, fontSize: 12.5 }}>
                          {d.status === "draft" ? "แก้ไข" : "เปิด"}
                        </Link>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
            <div className="pagination">
              <span className="info">แสดง {filtered.length} จาก {items.length} รายการ</span>
              <button className="pg"><Icon.chevronL width="14" /></button>
              <button className="pg active">1</button>
              <button className="pg">2</button>
              <button className="pg">3</button>
              <button className="pg"><Icon.chevron width="14" /></button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

// ─── Standard column sets ──────────────────────
const docNoCol = (basePath) => ({
  key: "no", label: "เลขที่", w: 220,
  render: (d) => <Link to={`${basePath}/${d.no}`} className="docno">{d.no}</Link>
});
const statusCol = { key: "status", label: "สถานะ", w: 140, render: (d) => <Badge status={d.status} /> };
const customerCol = { key: "customer", label: "ลูกค้า", render: (d, c) => c.name };
const dateCol = (key, label) => ({ key, label, w: 130, render: (d) => fmtDate(d[key]) });
const validUntilCol = {
  key: "validUntil", label: "ยืนราคาถึง", w: 140,
  render: (d) => {
    const expired = new Date(d.validUntil) < new Date("2026-05-22");
    return <span style={{ color: expired && d.status !== "cancelled" ? "var(--danger)" : "inherit", fontWeight: expired ? 600 : 400 }}>
      {expired && d.status !== "cancelled" && <Icon.warn width="12" style={{ verticalAlign: "middle", marginRight: 4 }} />}
      {fmtDate(d.validUntil)}
    </span>;
  }
};
const totalCol = { key: "total", label: "รวม", w: 130, num: true, render: (d, c, s) => <b>฿ {fmt(s.total)}</b> };

Object.assign(window, { DocList, docNoCol, statusCol, customerCol, dateCol, validUntilCol, totalCol });
