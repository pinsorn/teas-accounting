// === Main app shell + router ===
const TWEAK_DEFAULTS = /*EDITMODE-BEGIN*/{
  "theme": "orange-bold",
  "sidebarMode": "expanded",
  "showMascot": true
}/*EDITMODE-END*/;

function App() {
  const { path } = useRoute();
  const [t, setTweak] = useTweaks(TWEAK_DEFAULTS);
  const [collapsed, setCollapsed] = useState(t.sidebarMode === "compact");

  // Apply theme
  useEffect(() => {
    document.documentElement.setAttribute("data-theme", t.theme);
  }, [t.theme]);

  // Sync collapsed with tweak
  useEffect(() => {
    setCollapsed(t.sidebarMode === "compact");
  }, [t.sidebarMode]);

  // Route matching
  const route = matchRoute(path);

  // Crumbs
  const crumbs = buildCrumbs(route);

  return (
    <div className={`app-shell ${collapsed ? "collapsed" : ""}`}>
      <Sidebar collapsed={collapsed} setCollapsed={setCollapsed} />
      <main className="main">
        <Topbar crumbs={crumbs} />
        <div className="content">
          {renderRoute(route)}
        </div>
      </main>
      <TeasTweaks t={t} setTweak={setTweak} />
    </div>
  );
}

function matchRoute(path) {
  // /quotations/new
  // /quotations/:no
  // /quotations
  const segs = path.split("/").filter(Boolean);
  if (segs.length === 0) return { type: "dashboard" };
  const map = {
    "dashboard": "dashboard",
    "quotations": "quotation",
    "sales-orders": "salesorder",
    "delivery-orders": "deliveryorder",
    "tax-invoices": "taxinvoice",
    "billing-notes": "billingnote",
    "receipts": "receipt",
    "credit-notes": "creditnote",
    "debit-notes": "debitnote",
  };
  const docType = map[segs[0]] || "dashboard";
  if (segs.length === 1) return { type: docType, view: docType === "dashboard" ? null : "list" };
  if (segs[1] === "new") return { type: docType, view: "create" };
  return { type: docType, view: "detail", no: segs[1] };
}

function buildCrumbs(route) {
  const labels = {
    dashboard: "แดชบอร์ด",
    quotation: "ใบเสนอราคา",
    salesorder: "ใบสั่งขาย",
    deliveryorder: "ใบส่งของ",
    taxinvoice: "ใบกำกับภาษี",
    billingnote: "ใบแจ้งหนี้",
    receipt: "ใบเสร็จรับเงิน",
    creditnote: "ใบลดหนี้",
    debitnote: "ใบเพิ่มหนี้",
  };
  const crumbs = ["TEAS"];
  if (route.type !== "dashboard" || route.view) {
    crumbs.push(labels[route.type]);
  } else {
    crumbs.push(labels.dashboard);
  }
  if (route.view === "create") crumbs.push("สร้างใหม่");
  if (route.view === "detail") crumbs.push(route.no);
  return crumbs;
}

function renderRoute(route) {
  const map = {
    dashboard: { list: Dashboard, default: Dashboard },
    quotation: { list: QuotationList, detail: QuotationDetail, create: QuotationCreate },
    salesorder: { list: SalesOrderList, detail: SalesOrderDetail, create: SalesOrderCreate },
    deliveryorder: { list: DeliveryOrderList, detail: DeliveryOrderDetail, create: DeliveryOrderCreate },
    taxinvoice: { list: TaxInvoiceList, detail: TaxInvoiceDetail, create: TaxInvoiceCreate },
    billingnote: { list: BillingNoteList, detail: BillingNoteDetail, create: BillingNoteCreate },
    receipt: { list: ReceiptList, detail: ReceiptDetail, create: ReceiptCreate },
    creditnote: { list: CreditNoteList, detail: CreditNoteDetail, create: CreditNoteCreate },
    debitnote: { list: DebitNoteList, detail: DebitNoteDetail, create: DebitNoteCreate },
  };
  const group = map[route.type] || map.dashboard;
  const view = route.view || "list";
  const Comp = group[view] || group.list || group.default;
  if (view === "detail") return <Comp no={route.no} />;
  return <Comp />;
}

// ─── Tweaks panel ─────────────────────────
function TeasTweaks({ t, setTweak }) {
  return (
    <TweaksPanel title="Tweaks">
      <TweakSection label="โทนสี (Color Theme)">
        <TweakSelect
          label="Theme"
          value={t.theme}
          onChange={(v) => setTweak("theme", v)}
          options={[
            { value: "orange-bold", label: "ส้มเด่น · CTA Orange" },
            { value: "ink-bold", label: "ดำเด่น · Editorial Black" },
            { value: "bi-tone", label: "Bi-tone · ดำ + ส้ม Accent" },
          ]}
        />
      </TweakSection>
      <TweakSection label="Sidebar">
        <TweakRadio
          label="Density"
          value={t.sidebarMode}
          onChange={(v) => setTweak("sidebarMode", v)}
          options={[
            { value: "expanded", label: "ขยาย" },
            { value: "compact", label: "ย่อ" },
          ]}
        />
      </TweakSection>
      <TweakSection label="อื่น ๆ">
        <TweakToggle label="แสดง Mascot" value={t.showMascot} onChange={(v) => setTweak("showMascot", v)} />
      </TweakSection>
    </TweaksPanel>
  );
}

function Root() {
  return (
    <RouterProvider>
      <ToastProvider>
        <App />
      </ToastProvider>
    </RouterProvider>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<Root />);
