// === Mock data for TEAS ===
const CUSTOMERS = [
  { id: "C001", name: "บริษัท แอคมี อีคอมเมิร์ซ จำกัด", taxId: "0-1055-56123-45-3", branch: "00000 (สำนักงานใหญ่)", addr: "99/9 หมู่ 2 ถนนสุขุมวิท ตำบลท้ายบ้าน อ.เมืองสมุทรปราการ จ.สมุทรปราการ 10280", contact: "คุณวรรณวิภา ใจดี", phone: "081-234-5678", email: "wannvipa@acme.co.th" },
  { id: "C002", name: "บริษัท สยาม อควาเรียม โซลูชั่น จำกัด", taxId: "0-1055-58999-22-7", branch: "00000 (สำนักงานใหญ่)", addr: "123/45 ถนนนราธิวาสราชนครินทร์ แขวงทุ่งมหาเมฆ เขตสาทร กรุงเทพมหานคร 10120", contact: "คุณพีระพล สุขใจ", phone: "02-345-6789", email: "contact@siamaquarium.com" },
  { id: "C003", name: "หจก. ทรัพย์ทวีรุ่งเรือง", taxId: "0-1055-49382-11-2", branch: "—", addr: "55 ถนนพระราม 9 แขวงห้วยขวาง เขตห้วยขวาง กรุงเทพมหานคร 10310", contact: "คุณสมศรี ทรัพย์ดี", phone: "089-876-5432", email: "som@suptawee.co.th" },
  { id: "C004", name: "บจก. นวัตกรรมดีเลิศ จำกัด", taxId: "0-1055-60111-33-8", branch: "00001 (สาขาลาดพร้าว)", addr: "789 ถนนลาดพร้าว แขวงจอมพล เขตจตุจักร กรุงเทพมหานคร 10900", contact: "คุณกิตติ นวัตกรรม", phone: "02-987-6543", email: "kitti@innovate.co.th" },
  { id: "C005", name: "บริษัท โซลูชั่นครบวงจร จำกัด", taxId: "0-1055-51777-44-9", branch: "00000", addr: "456/8 ถนนรามคำแหง แขวงหัวหมาก เขตบางกะปิ กรุงเทพมหานคร 10240", contact: "คุณนภา ครบวงจร", phone: "02-111-2222", email: "napa@allsolution.co.th" },
];

const PRODUCTS = [
  { code: "AQ-MED-001", name: "ตู้เลี้ยงปลาขนาดกลาง", desc: "กระจกนิรภัย 10 มม. ขนาด 60×30×36 ซม. พร้อมระบบกรองน้ำในตัว", unit: "ตู้", price: 3500 },
  { code: "AQ-LRG-001", name: "ตู้เลี้ยงปลาขนาดใหญ่", desc: "กระจกนิรภัย 12 มม. ขนาด 120×50×50 ซม. พร้อมไฟ LED RGB", unit: "ตู้", price: 8900 },
  { code: "PUMP-200", name: "ปั๊มน้ำ 200W", desc: "ปั๊มน้ำหมุนเวียน Eco-friendly 2000L/h", unit: "ตัว", price: 1850 },
  { code: "FOOD-PREM", name: "อาหารปลาพรีเมียม 5 กก.", desc: "อาหารเม็ดสำหรับปลาทุกชนิด ไม่ทำให้น้ำขุ่น", unit: "ถุง", price: 590 },
  { code: "SVC-INSTALL", name: "บริการติดตั้งและล้างตู้", desc: "ติดตั้งระบบกรอง ทำความสะอาด ตรวจคุณภาพน้ำ", unit: "ครั้ง", price: 2500 },
  { code: "FLT-PRO", name: "ชุดกรองน้ำมืออาชีพ", desc: "Canister filter รุ่นมืออาชีพ พร้อมสื่อกรอง 6 ชั้น", unit: "ชุด", price: 4200 },
];

// Sales doc thread (linked documents)
const QUOTATIONS = [
  {
    no: "05-2026-QT-ECOM-0001",
    status: "accepted",
    date: "2026-05-20", validUntil: "2026-06-19",
    customerId: "C002",
    salesperson: "กิตติพงษ์ สันติฤดี",
    terms: "เครดิต 30 วัน",
    items: [
      { code: "AQ-MED-001", name: "ตู้เลี้ยงปลาขนาดกลาง", desc: "กระจกนิรภัย 10 มม. พร้อมระบบกรองน้ำในตัว", qty: 1, unit: "ตู้", price: 3500, disc: 0 },
    ],
    notes: "ลูกค้านิติบุคคลหัก ณ ที่จ่าย 3% เฉพาะส่วนบริการ",
    linkedSO: "05-2026-SO-ECOM-0001",
  },
  {
    no: "05-2026-QT-ECOM-0002",
    status: "sent",
    date: "2026-05-21", validUntil: "2026-06-20",
    customerId: "C001",
    salesperson: "วันดี สมใจ",
    terms: "เครดิต 30 วัน",
    items: [
      { code: "AQ-LRG-001", name: "ตู้เลี้ยงปลาขนาดใหญ่", desc: "ขนาด 120×50×50 ซม. พร้อมไฟ LED", qty: 1, unit: "ตู้", price: 8900, disc: 5 },
      { code: "PUMP-200", name: "ปั๊มน้ำ 200W", desc: "Eco-friendly 2000L/h", qty: 1, unit: "ตัว", price: 1850, disc: 0 },
    ],
    notes: "ราคาพิเศษเฉพาะเดือนนี้",
  },
  {
    no: "05-2026-QT-ECOM-0003",
    status: "draft",
    date: "2026-05-22", validUntil: "2026-06-21",
    customerId: "C004",
    salesperson: "กิตติพงษ์ สันติฤดี",
    terms: "เครดิต 45 วัน",
    items: [
      { code: "AQ-LRG-001", name: "ตู้เลี้ยงปลาขนาดใหญ่", qty: 1, unit: "ตู้", price: 8900, disc: 0 },
      { code: "FLT-PRO", name: "ชุดกรองน้ำมืออาชีพ", qty: 1, unit: "ชุด", price: 4200, disc: 0 },
    ],
    notes: "",
  },
  {
    no: "05-2026-QT-ECOM-0004",
    status: "draft",
    date: "2026-05-15", validUntil: "2026-06-14",
    customerId: "C003",
    salesperson: "วันดี สมใจ",
    terms: "เงินสด",
    items: [
      { code: "AQ-MED-001", name: "ตู้เลี้ยงปลาขนาดกลาง", qty: 2, unit: "ตู้", price: 3500, disc: 0 },
      { code: "PUMP-200", name: "ปั๊มน้ำ 200W", qty: 1, unit: "ตัว", price: 1850, disc: 0 },
    ],
    notes: "ใกล้หมดอายุการยืนราคา",
  },
  {
    no: "05-2026-QT-ECOM-0005",
    status: "cancelled",
    date: "2026-05-10", validUntil: "2026-06-09",
    customerId: "C005",
    salesperson: "กิตติพงษ์ สันติฤดี",
    terms: "เครดิต 30 วัน",
    items: [
      { code: "AQ-LRG-001", name: "ตู้เลี้ยงปลาขนาดใหญ่", qty: 5, unit: "ตู้", price: 8900, disc: 10 },
    ],
    notes: "ลูกค้ายกเลิก เนื่องจากเลื่อนงบประมาณ",
  },
];

const SALES_ORDERS = [
  {
    no: "05-2026-SO-ECOM-0001",
    status: "confirmed",
    date: "2026-05-21", deliveryDate: "2026-05-28",
    customerId: "C002",
    salesperson: "กิตติพงษ์ สันติฤดี",
    terms: "เครดิต 30 วัน",
    items: QUOTATIONS[0].items,
    linkedQT: "05-2026-QT-ECOM-0001",
    linkedDO: "05-2026-DO-ECOM-0001",
  },
];

const DELIVERY_ORDERS = [
  {
    no: "05-2026-DO-ECOM-0001",
    status: "delivered",
    date: "2026-05-28",
    customerId: "C002",
    courier: "TEAS Express",
    tracking: "TX-26052800123",
    items: QUOTATIONS[0].items,
    linkedSO: "05-2026-SO-ECOM-0001",
    linkedTAX: "05-2026-TI-ECOM-0001",
  },
];

const TAX_INVOICES = [
  {
    no: "05-2026-TI-ECOM-0001",
    status: "posted",
    date: "2026-05-28",
    customerId: "C002",
    items: QUOTATIONS[0].items,
    linkedDO: "05-2026-DO-ECOM-0001",
    linkedRC: "05-2026-RC-ECOM-0001",
    linkedCN: "05-2026-CN-ECOM-0001",
  },
];

const BILLING_NOTES = [
  {
    no: "05-2026-BL-ECOM-0001",
    status: "pending",
    date: "2026-05-30", dueDate: "2026-06-29",
    customerId: "C002",
    items: QUOTATIONS[0].items,
    linkedTAX: "05-2026-TI-ECOM-0001",
  },
];

const RECEIPTS = [
  {
    no: "05-2026-RC-ECOM-0001",
    status: "paid",
    date: "2026-06-12",
    customerId: "C002",
    paymentMethod: "โอนผ่านธนาคาร (SCB)",
    reference: "FT2606120123",
    items: QUOTATIONS[0].items,
    linkedTAX: "05-2026-TI-ECOM-0001",
  },
];

const CREDIT_NOTES = [
  {
    no: "05-2026-CN-ECOM-0001",
    status: "issued",
    date: "2026-06-15",
    customerId: "C002",
    reason: "ลดหย่อนสินค้าชำรุดบางส่วน",
    items: [{ code: "AQ-MED-001", name: "ส่วนลดหย่อนสินค้าชำรุด", qty: 1, unit: "รายการ", price: 500, disc: 0 }],
    linkedTAX: "05-2026-TI-ECOM-0001",
  },
];

const DEBIT_NOTES = [
  {
    no: "05-2026-DN-ECOM-0001",
    status: "issued",
    date: "2026-06-18",
    customerId: "C001",
    reason: "ค่าบริการขนส่งเพิ่มเติม",
    items: [{ code: "SVC-DELIV", name: "ค่าขนส่งเพิ่มเติม", qty: 1, unit: "รายการ", price: 350, disc: 0 }],
  },
];

// Helper: lookup customer
function getCustomer(id) { return CUSTOMERS.find(c => c.id === id) || CUSTOMERS[0]; }

// Compute summary
function computeSummary(items) {
  const subtotal = items.reduce((s, it) => s + (it.qty * it.price), 0);
  const discountTotal = items.reduce((s, it) => s + (it.qty * it.price * (it.disc || 0) / 100), 0);
  const beforeVat = subtotal - discountTotal;
  const vat = beforeVat * 0.07;
  const total = beforeVat + vat;
  return { subtotal, discountTotal, beforeVat, vat, total };
}

// Export
Object.assign(window, {
  CUSTOMERS, PRODUCTS,
  QUOTATIONS, SALES_ORDERS, DELIVERY_ORDERS, TAX_INVOICES,
  BILLING_NOTES, RECEIPTS, CREDIT_NOTES, DEBIT_NOTES,
  getCustomer, computeSummary,
});
