// === TEAS shared components ===
const { useState, useEffect, useRef, useMemo, createContext, useContext } = React;

// ─── Icons (inline SVG, 18-20px) ─────────────────────────
const Icon = {
  dashboard: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><rect x="3" y="3" width="7" height="9"/><rect x="14" y="3" width="7" height="5"/><rect x="14" y="12" width="7" height="9"/><rect x="3" y="16" width="7" height="5"/></svg>,
  quote: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 13h6M9 17h4"/></svg>,
  cart: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><circle cx="9" cy="21" r="1"/><circle cx="20" cy="21" r="1"/><path d="M1 1h4l2.7 13.4a2 2 0 0 0 2 1.6h9.7a2 2 0 0 0 2-1.6L23 6H6"/></svg>,
  truck: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><rect x="1" y="6" width="13" height="11"/><path d="M14 9h5l3 4v4h-8"/><circle cx="6" cy="19" r="2"/><circle cx="18" cy="19" r="2"/></svg>,
  receipt: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M4 2v20l3-2 3 2 3-2 3 2 3-2 3 2V2H4z"/><path d="M8 7h10M8 11h10M8 15h6"/></svg>,
  invoice: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M21 12V7H5a2 2 0 0 1 0-4h14v4"/><path d="M3 5v14a2 2 0 0 0 2 2h16v-5"/><path d="M18 12a2 2 0 0 0-2 2c0 1.1.9 2 2 2h4v-4h-4z"/></svg>,
  tax: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M9 9l6 6M15 9l-6 6"/></svg>,
  cn: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 15h6M12 12v6"/><path d="M9 12l6 6" stroke="none" /></svg>,
  dn: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M12 12v6M9 15h6"/></svg>,
  bill: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><circle cx="12" cy="14" r="2"/><path d="M12 11v1M12 17v1"/></svg>,
  search: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>,
  bell: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.7 21a2 2 0 0 1-3.4 0"/></svg>,
  plus: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" {...p}><path d="M12 5v14M5 12h14"/></svg>,
  download: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="M7 10l5 5 5-5"/><path d="M12 15V3"/></svg>,
  print: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M6 9V2h12v7"/><rect x="2" y="9" width="20" height="9" rx="2"/><path d="M6 14h12v8H6z"/></svg>,
  email: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><rect x="2" y="4" width="20" height="16" rx="2"/><path d="M22 7l-10 6L2 7"/></svg>,
  link: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7"/><path d="M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7"/></svg>,
  chevron: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="m9 18 6-6-6-6"/></svg>,
  chevronL: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="m15 6-6 6 6 6"/></svg>,
  chevronD: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="m6 9 6 6 6-6"/></svg>,
  edit: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5z"/></svg>,
  trash: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/></svg>,
  copy: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>,
  more: (p) => <svg viewBox="0 0 24 24" fill="currentColor" {...p}><circle cx="5" cy="12" r="1.5"/><circle cx="12" cy="12" r="1.5"/><circle cx="19" cy="12" r="1.5"/></svg>,
  arrowRight: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M5 12h14M13 5l7 7-7 7"/></svg>,
  refresh: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M3 12a9 9 0 0 1 15-6.7L21 8M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-15 6.7L3 16M3 21v-5h5"/></svg>,
  check: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M20 6 9 17l-5-5"/></svg>,
  x: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M18 6 6 18M6 6l12 12"/></svg>,
  warn: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M10.3 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><path d="M12 9v4M12 17h.01"/></svg>,
  clock: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>,
  calendar: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/></svg>,
  user: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>,
  building: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><rect x="4" y="2" width="16" height="20"/><path d="M9 22v-4h6v4M8 6h.01M16 6h.01M12 6h.01M8 10h.01M16 10h.01M12 10h.01M8 14h.01M16 14h.01M12 14h.01"/></svg>,
  signature: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M3 17c5-3 6-7 9-7s4 7 9 4"/><path d="M2 21h20"/></svg>,
  collapse: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M15 3v18M9 8l-4 4 4 4"/></svg>,
  expand: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M9 3v18M15 8l4 4-4 4"/></svg>,
  settings: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>,
  trending: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M23 6l-9.5 9.5-5-5L1 18"/><path d="M17 6h6v6"/></svg>,
  baht: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M9 4h5a3 3 0 0 1 0 6H9zM9 10h6a3 3 0 0 1 0 6H9zM9 4v16M12 2v2M12 20v2"/></svg>,
  alert: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><circle cx="12" cy="12" r="9"/><path d="M12 8v4M12 16h.01"/></svg>,
  sparkle: (p) => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1"/></svg>,
};

// ─── Number formatting ─────────────────────────
const fmt = (n, dp = 2) => Number(n).toLocaleString("th-TH", { minimumFractionDigits: dp, maximumFractionDigits: dp });
const fmtInt = (n) => Number(n).toLocaleString("th-TH");
const fmtDate = (s) => {
  if (!s) return "";
  const d = new Date(s);
  const m = ["ม.ค.","ก.พ.","มี.ค.","เม.ย.","พ.ค.","มิ.ย.","ก.ค.","ส.ค.","ก.ย.","ต.ค.","พ.ย.","ธ.ค."];
  return `${d.getDate()} ${m[d.getMonth()]} ${d.getFullYear() + 543}`;
};
const fmtDateShort = (s) => {
  if (!s) return "";
  const d = new Date(s);
  return `${String(d.getDate()).padStart(2,"0")}/${String(d.getMonth()+1).padStart(2,"0")}/${d.getFullYear() + 543}`;
};

// Thai bath text (simplified for prototype — handles common amounts)
function bathText(n) {
  const txt = ["ศูนย์","หนึ่ง","สอง","สาม","สี่","ห้า","หก","เจ็ด","แปด","เก้า"];
  const pos = ["","สิบ","ร้อย","พัน","หมื่น","แสน","ล้าน"];
  function group(s) {
    let out = "";
    const len = s.length;
    for (let i = 0; i < len; i++) {
      const d = parseInt(s[i]);
      const p = len - i - 1;
      if (d === 0) continue;
      if (p === 1 && d === 1) out += "สิบ";
      else if (p === 1 && d === 2) out += "ยี่สิบ";
      else if (p === 0 && d === 1 && len > 1) out += "เอ็ด";
      else out += txt[d] + pos[p];
    }
    return out;
  }
  const baht = Math.floor(n);
  const satang = Math.round((n - baht) * 100);
  let result = "";
  if (baht === 0) result = "ศูนย์";
  else {
    let s = String(baht);
    if (s.length > 6) {
      const mil = s.slice(0, -6);
      const rest = s.slice(-6);
      result = group(mil) + "ล้าน" + (rest.replace(/^0+/, "") ? group(rest) : "");
    } else result = group(s);
  }
  result += "บาท";
  if (satang === 0) result += "ถ้วน";
  else result += group(String(satang).padStart(2,"0")) + "สตางค์";
  return result;
}

// ─── Mascot ─────────────────────────
function Mascot({ size = 48, style = {} }) {
  return (
    <span className="mascot" style={{ width: size, height: size, ...style }}>
      <img src="assets/teas-logo.png" alt="TEAS mascot" />
    </span>
  );
}

// ─── Status badge ─────────────────────────
const STATUS = {
  draft:     { cls: "badge-draft",   label: "ร่าง", labelEn: "Draft" },
  sent:      { cls: "badge-info",    label: "ส่งแล้ว", labelEn: "Sent" },
  accepted:  { cls: "badge-success", label: "ตอบรับแล้ว", labelEn: "Accepted" },
  rejected:  { cls: "badge-danger",  label: "ปฏิเสธ", labelEn: "Rejected" },
  cancelled: { cls: "badge-danger",  label: "ยกเลิก", labelEn: "Cancelled" },
  expired:   { cls: "badge-warning", label: "หมดอายุ", labelEn: "Expired" },
  pending:   { cls: "badge-warning", label: "รอดำเนินการ", labelEn: "Pending" },
  paid:      { cls: "badge-success", label: "ชำระแล้ว", labelEn: "Paid" },
  overdue:   { cls: "badge-danger",  label: "เกินกำหนด", labelEn: "Overdue" },
  partial:   { cls: "badge-warning", label: "ชำระบางส่วน", labelEn: "Partial" },
  posted:    { cls: "badge-success", label: "บันทึกแล้ว", labelEn: "Posted" },
  delivered: { cls: "badge-success", label: "ส่งของแล้ว", labelEn: "Delivered" },
  inTransit: { cls: "badge-info",    label: "อยู่ระหว่างขนส่ง", labelEn: "In Transit" },
  confirmed: { cls: "badge-success", label: "ยืนยันแล้ว", labelEn: "Confirmed" },
  issued:    { cls: "badge-info",    label: "ออกแล้ว", labelEn: "Issued" },
};
function Badge({ status, withEn }) {
  const s = STATUS[status] || { cls: "badge-draft", label: status };
  return (
    <span className={`badge ${s.cls}`}>
      <span className="dot"></span>
      {s.label}{withEn && s.labelEn ? ` · ${s.labelEn}` : ""}
    </span>
  );
}

// ─── Router (hash-based) ─────────────────────────
const Router = createContext({ path: "/", nav: () => {} });
function useRoute() { return useContext(Router); }
function RouterProvider({ children }) {
  const [path, setPath] = useState(window.location.hash.slice(1) || "/dashboard");
  useEffect(() => {
    const on = () => setPath(window.location.hash.slice(1) || "/dashboard");
    window.addEventListener("hashchange", on);
    return () => window.removeEventListener("hashchange", on);
  }, []);
  const nav = (p) => { window.location.hash = p; };
  return <Router.Provider value={{ path, nav }}>{children}</Router.Provider>;
}
function Link({ to, children, className, onClick, ...rest }) {
  const { nav } = useRoute();
  return (
    <a
      href={"#" + to}
      className={className}
      onClick={(e) => { e.preventDefault(); onClick && onClick(e); nav(to); }}
      {...rest}
    >
      {children}
    </a>
  );
}

// ─── Sidebar nav definition ─────────────────────────
const NAV_GROUPS = [
  { label: "ภาพรวม", items: [
    { id: "dashboard", path: "/dashboard", icon: "dashboard", label: "แดชบอร์ด" },
  ]},
  { label: "ขาย / SALES", items: [
    { id: "quotation",     path: "/quotations",      icon: "quote",  label: "ใบเสนอราคา", badge: "5" },
    { id: "salesorder",    path: "/sales-orders",    icon: "cart",   label: "ใบสั่งขาย" },
    { id: "deliveryorder", path: "/delivery-orders", icon: "truck",  label: "ใบส่งของ" },
  ]},
  { label: "ภาษี / TAX", items: [
    { id: "taxinvoice", path: "/tax-invoices", icon: "tax", label: "ใบกำกับภาษี" },
  ]},
  { label: "รับเงิน / AR", items: [
    { id: "billingnote", path: "/billing-notes", icon: "bill",    label: "ใบแจ้งหนี้", badge: "2" },
    { id: "receipt",     path: "/receipts",      icon: "receipt", label: "ใบเสร็จรับเงิน" },
  ]},
  { label: "ปรับปรุง / ADJUSTMENTS", items: [
    { id: "creditnote", path: "/credit-notes", icon: "cn", label: "ใบลดหนี้" },
    { id: "debitnote",  path: "/debit-notes",  icon: "dn", label: "ใบเพิ่มหนี้" },
  ]},
];

function Sidebar({ collapsed, setCollapsed }) {
  const { path, nav } = useRoute();
  return (
    <aside className="sidebar">
      <div className="sb-header">
        <div className="sb-mark"><img src="assets/teas-logo.png" alt="TEAS" /></div>
        <div className="sb-brand">
          <span className="name">TEAS</span>
          <span className="sub">ENTERPRISE ACCOUNTING</span>
        </div>
        <button className="sb-collapse" onClick={() => setCollapsed(c => !c)} title={collapsed ? "ขยาย" : "ย่อ"}>
          {collapsed ? <Icon.expand /> : <Icon.collapse />}
        </button>
      </div>
      <nav className="sb-nav">
        {NAV_GROUPS.map((g, gi) => (
          <div className="sb-group" key={gi}>
            <div className="sb-group-label">{g.label}</div>
            {g.items.map(it => {
              const I = Icon[it.icon];
              const active = path.startsWith(it.path);
              return (
                <button key={it.id} className={`sb-item ${active ? "active" : ""}`} onClick={() => nav(it.path)} title={it.label}>
                  <span className="icon"><I /></span>
                  <span className="label">{it.label}</span>
                  {it.badge && <span className="badge">{it.badge}</span>}
                </button>
              );
            })}
          </div>
        ))}
      </nav>
      <div className="sb-footer">
        <div className="sb-avatar">สม</div>
        <div className="sb-user">
          <span className="name">สมชาย รักงาน</span>
          <span className="role">Accountant Manager</span>
        </div>
      </div>
    </aside>
  );
}

// ─── Topbar ─────────────────────────
function Topbar({ crumbs = [] }) {
  return (
    <header className="topbar">
      <div className="crumbs">
        {crumbs.map((c, i) => (
          <React.Fragment key={i}>
            {i > 0 && <span className="sep"><Icon.chevron width="12" /></span>}
            <span className={i === crumbs.length - 1 ? "here" : ""}>{c}</span>
          </React.Fragment>
        ))}
      </div>
      <div className="topbar-search">
        <Icon.search />
        <input placeholder="ค้นหาเอกสาร, ลูกค้า, เลขที่..." />
        <span style={{ fontSize: 11, color: "var(--text-3)", border: "1px solid var(--border)", borderRadius: 4, padding: "1px 5px" }}>⌘K</span>
      </div>
      <button className="topbar-icon" title="การแจ้งเตือน"><Icon.bell /><span className="dot"></span></button>
      <button className="topbar-icon" title="ตั้งค่า"><Icon.settings /></button>
    </header>
  );
}

// ─── Toast ─────────────────────────
const ToastCtx = createContext(null);
function useToast() { return useContext(ToastCtx); }
function ToastProvider({ children }) {
  const [toasts, set] = useState([]);
  const push = (msg) => {
    const id = Math.random();
    set(t => [...t, { id, msg }]);
    setTimeout(() => set(t => t.filter(x => x.id !== id)), 2600);
  };
  return (
    <ToastCtx.Provider value={push}>
      {children}
      <div className="toast-wrap">
        {toasts.map(t => (
          <div className="toast" key={t.id}>
            <Icon.check className="check" /> {t.msg}
          </div>
        ))}
      </div>
    </ToastCtx.Provider>
  );
}

// ─── Page chrome ─────────────────────────
function PageHeader({ title, subtitle, actions }) {
  return (
    <div className="page-header">
      <div>
        <h1 className="page-title">{title}</h1>
        {subtitle && <div className="page-subtitle">{subtitle}</div>}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </div>
  );
}

// ─── Related Docs card ─────────────────────────
function RelatedDocs({ items }) {
  const { nav } = useRoute();
  if (!items || items.length === 0) return null;
  return (
    <div className="card">
      <div className="card-h">
        <h3 style={{ display: "flex", alignItems: "center", gap: 8 }}><Icon.link width="16" /> เอกสารที่เกี่ยวข้อง</h3>
        <span style={{ fontSize: 12, color: "var(--text-3)" }}>{items.length}</span>
      </div>
      <div className="card-b" style={{ padding: 12 }}>
        <div className="related">
          {items.map((it, i) => {
            const I = Icon[it.icon] || Icon.quote;
            return (
              <div className="link" key={i} onClick={() => it.path && nav(it.path)}>
                <span className="icon"><I width="16" /></span>
                <div className="info">
                  <div className="typ">{it.type}</div>
                  <div className="no">{it.no}</div>
                </div>
                <Icon.arrowRight width="14" className="arrow" />
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

// ─── Activity log card ─────────────────────────
function ActivityLog({ entries }) {
  return (
    <div className="card">
      <div className="card-h"><h3 style={{ display: "flex", alignItems: "center", gap: 8 }}><Icon.clock width="16" /> ประวัติกิจกรรม</h3></div>
      <div className="card-b">
        <div className="activity">
          {entries.map((e, i) => {
            const I = Icon[e.icon] || Icon.check;
            return (
              <div className={`item ${i === 0 ? "active" : ""}`} key={i}>
                <div className="dot"><I width="14" /></div>
                <div className="body">
                  <div className="title">{e.title}</div>
                  <div className="meta">{e.when} · {e.by}</div>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

// ─── Paper document (shared) ─────────────────────────
const COMPANY = {
  name: "บริษัท ทีส โซลูชั่น จำกัด",
  nameEn: "TEAS Solution Co., Ltd.",
  addr: "99/1 อาคารทีส ชั้น 12 ถนนสาทรใต้ แขวงทุ่งมหาเมฆ เขตสาทร กรุงเทพมหานคร 10120",
  taxId: "0-1055-60000-00-1",
  branch: "00000 (สำนักงานใหญ่)",
  phone: "02-123-4567",
  email: "contact@teas.co.th",
};

function PaperDocument({ docType, docTypeEn, docNo, date, validUntil, validUntilLabel, customer, terms, items, summary, notes, signRoles, watermark, watermarkClass, extraMetaBlock, signatureImg }) {
  const subtotal = items.reduce((s, it) => s + (it.qty * it.price * (1 - (it.disc || 0) / 100)), 0);
  const discountTotal = 0;
  const beforeVat = subtotal - discountTotal;
  const vat = beforeVat * 0.07;
  const total = beforeVat + vat;
  const s = summary || { subtotal, discountTotal, beforeVat, vat, total };

  return (
    <div className="paper">
      {watermark && <div className={`paper-wm ${watermarkClass || ""}`}>{watermark}</div>}
      <div className="paper-head">
        <div className="paper-company">
          <div className="mark"><img src="assets/teas-logo.png" alt="TEAS" /></div>
          <div className="info">
            <div className="name">{COMPANY.name}</div>
            <div className="addr">
              {COMPANY.addr}<br/>
              เลขประจำตัวผู้เสียภาษี: {COMPANY.taxId} · สาขา {COMPANY.branch}<br/>
              โทร {COMPANY.phone} · {COMPANY.email}
            </div>
          </div>
        </div>
        <div className="paper-title">
          <div className="label-en">{docTypeEn}</div>
          <div className="label-th">{docType}</div>
          <div className="docno">{docNo || "—"}</div>
        </div>
      </div>

      <div className="paper-meta">
        <div className="block">
          <div className="lbl">ลูกค้า / Customer</div>
          <div className="val" style={{ fontWeight: 700, marginBottom: 4 }}>{customer.name || "—"}</div>
          <div className="val" style={{ fontSize: 14, color: "var(--ink-700)" }}>
            {customer.addr || "—"}<br/>
            {customer.taxId && <>เลขประจำตัวผู้เสียภาษี: {customer.taxId}<br/></>}
            {customer.branch && <>สาขา: {customer.branch}</>}
          </div>
        </div>
        <div className="block">
          <dl className="kv">
            <dt>วันที่ / Date</dt><dd>{fmtDateShort(date)}</dd>
            {validUntil && <><dt>{validUntilLabel || "ยืนราคาถึง"}</dt><dd>{fmtDateShort(validUntil)}</dd></>}
            {terms && <><dt>เงื่อนไขชำระ</dt><dd>{terms}</dd></>}
            {customer.contact && <><dt>ผู้ติดต่อ</dt><dd>{customer.contact}</dd></>}
            {extraMetaBlock}
          </dl>
        </div>
      </div>

      <table className="paper-items">
        <thead>
          <tr>
            <th>#</th>
            <th>รายการ / Description</th>
            <th className="num" style={{ width: 70 }}>จำนวน</th>
            <th style={{ width: 60 }}>หน่วย</th>
            <th className="num" style={{ width: 100 }}>ราคา/หน่วย</th>
            <th className="num" style={{ width: 70 }}>ส่วนลด</th>
            <th className="num" style={{ width: 110 }}>จำนวนเงิน</th>
          </tr>
        </thead>
        <tbody>
          {items.map((it, i) => (
            <tr key={i}>
              <td>{i + 1}</td>
              <td>
                <div style={{ fontWeight: 600 }}>{it.name || "—"}</div>
                {it.desc && <div style={{ fontSize: 13, color: "var(--ink-600)", marginTop: 2 }}>{it.desc}</div>}
              </td>
              <td className="num">{it.qty ? fmt(it.qty, 0) : "—"}</td>
              <td>{it.unit || "—"}</td>
              <td className="num">{it.price ? fmt(it.price) : "—"}</td>
              <td className="num">{it.disc ? `${it.disc}%` : "—"}</td>
              <td className="num"><b>{fmt(it.qty * it.price * (1 - (it.disc || 0) / 100))}</b></td>
            </tr>
          ))}
          {items.length === 0 && <tr><td colSpan="7" style={{ textAlign: "center", color: "var(--ink-400)", padding: 30 }}>ยังไม่มีรายการสินค้า</td></tr>}
          {Array.from({ length: Math.max(0, 3 - items.length) }).map((_, i) => (
            <tr key={`e${i}`} className="empty-row"><td colSpan="7"></td></tr>
          ))}
        </tbody>
      </table>

      <div className="paper-foot">
        <div>
          {notes && (
            <div className="paper-notes">
              <div className="lbl">หมายเหตุ / Notes</div>
              {notes}
            </div>
          )}
        </div>
        <div className="paper-totals">
          <div className="row"><span>มูลค่าก่อนหักส่วนลด · Subtotal</span><span className="v">{fmt(s.subtotal)}</span></div>
          <div className="row"><span>ส่วนลดรวม · Discount</span><span className="v">{fmt(s.discountTotal)}</span></div>
          <div className="row"><span>มูลค่าก่อนภาษี · Before VAT</span><span className="v">{fmt(s.beforeVat)}</span></div>
          <div className="row"><span>ภาษีมูลค่าเพิ่ม 7% · VAT</span><span className="v">{fmt(s.vat)}</span></div>
          <div className="row total"><span>รวมทั้งสิ้น · Total (THB)</span><span className="v">฿ {fmt(s.total)}</span></div>
          <div className="amount-words">({bathText(s.total)})</div>
        </div>
      </div>

      <div className="paper-sign">
        <div className="box">
          <div style={{ height: 50 }}></div>
          <div className="role">{signRoles?.[0] || "ผู้รับสินค้า / Receiver"}</div>
          <div className="sub">วันที่ ___ / ___ / ______</div>
        </div>
        <div className="box">
          <div style={{ height: 50, display: "grid", placeItems: "center" }}>
            {signatureImg && (
              <span style={{ fontFamily: "cursive", fontSize: 22, color: "var(--ink-700)", transform: "rotate(-6deg)" }}>{signatureImg}</span>
            )}
          </div>
          <div className="role">{signRoles?.[1] || "ผู้มีอำนาจลงนาม / Authorized Signature"}</div>
          <div className="sub">{COMPANY.name}</div>
        </div>
      </div>
    </div>
  );
}

// Export to window
Object.assign(window, {
  Icon, fmt, fmtInt, fmtDate, fmtDateShort, bathText,
  Mascot, Badge, STATUS, COMPANY,
  Router, useRoute, RouterProvider, Link,
  Sidebar, Topbar, ToastProvider, useToast,
  PageHeader, RelatedDocs, ActivityLog,
  PaperDocument, NAV_GROUPS,
});
