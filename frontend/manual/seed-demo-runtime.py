#!/usr/bin/env python
"""
seed-demo-runtime.py  —  manual demo: TRANSACTIONAL pre-capture data (co2).

Recreates the runtime-only demo data the ch6/ch7 walkthroughs READ but do not
create, on a freshly-wiped accounting_dev. Master data (employees, foreign
vendor) is seeded by SQL (561_seed_manual_demo_employees.sql + the foreign
vendor already in 400_seed_manual_demo_company.sql) — this script adds the data
that posts GL / consumes number sequences and therefore CANNOT be clean-SQL-seeded:

  1. DRAFT payroll run, period 202602 (co2) — auto-computes a payslip per active
     employee (MD-EMP-001/002). Kept DRAFT so re-capture is idempotent.
     Read by 06.01 (payroll run) + 07.06 (ภ.ง.ด.1 PDF).

  2. Foreign reverse-charge purchase (ม.83/6), period 202606 (co2) — a POSTED
     vendor invoice from Amazon Web Services (MV-FOR-001, foreign, no Thai VAT-D)
     for a cloud SERVICE. vendor.IsForeign && !HasThaiVatDReg ⟹
     RequiresPnd36ReverseCharge = true ⟹ the doc appears in the ภ.พ.36 preview.
     Read by 07.04 (ภ.พ.36 preview, throws on 0 rows).

  *** NO ม.70 / ภ.ง.ด.54 data is created here. *** A ม.70 foreign-WHT payment
  voucher previously polluted the load-bearing P&L (co2 demo). ภ.พ.36 is left as
  a PREVIEW only (mode=preview) — no JV is finalized, no ภ.ง.ด.54 header/line.

Idempotent: guards on an existing 202602 run and an existing Amazon VI (by the
vendor tax-invoice number) so re-runs neither duplicate rows nor burn extra
number sequences. Safe to re-run any time.

Run:  python frontend/manual/seed-demo-runtime.py
Requires the dev API on :5080 (Development) + the SQL seeds applied (boot the API
once after adding 561_seed_manual_demo_employees.sql).
"""
import sys, io, json
import requests

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

BASE = "http://localhost:5080"
# co2 demo super-admin (frontend/manual/lib/personas.ts → admin).
USER, PWD = "demo-admin", "Demo@1234"

# ── Fixed demo parameters ────────────────────────────────────────────────────
PAYROLL_PERIOD = "202602"          # ch6/7 walkthroughs hardcode this period
PAYROLL_PAYDATE = "2026-02-28"
FOREIGN_VENDOR_CODE = "MV-FOR-001"  # Amazon Web Services (foreign, no Thai VAT-D)
AWS_TAX_INVOICE_NO = "AWS-INV-2026-06"   # idempotency key for the foreign VI
AWS_DOC_DATE = "2026-06-15"         # period 202606 → ภ.พ.36 for 2026-06
AWS_SERVICE_AMOUNT = 30000          # THB, ex-VAT cloud service (→ ภ.พ.36 VAT = 2,100)
SVC_CATEGORY_CODE = "SVC"
BU_CODE = "ECOM"                    # co2 requires_business_unit = TRUE


def die(msg):
    print(f"FAIL: {msg}")
    sys.exit(1)


def login():
    r = requests.post(f"{BASE}/auth/login", json={"username": USER, "password": PWD}, timeout=30)
    if r.status_code != 200:
        die(f"login {r.status_code}: {r.text[:300]}")
    return {"Authorization": f"Bearer {r.json()['access_token']}"}


def get(h, path):
    r = requests.get(f"{BASE}{path}", headers=h, timeout=30)
    r.raise_for_status()
    return r.json()


# ── 1. DRAFT payroll run, period 202602 ──────────────────────────────────────
def ensure_payroll_run(h):
    runs = get(h, "/payroll/runs")
    existing = next((x for x in runs if str(x.get("periodYearMonth")) == PAYROLL_PERIOD), None)
    if existing:
        rid = existing["payrollRunId"]
        print(f"  payroll run {PAYROLL_PERIOD} already exists (id={rid}, status={existing.get('status')}) — skip")
        return rid
    r = requests.post(f"{BASE}/payroll/runs", headers=h, timeout=60, json={
        "periodYearMonth": PAYROLL_PERIOD,
        "payDate": PAYROLL_PAYDATE,
        "notes": "Manual demo seed — ch6/7 payroll walkthrough (DRAFT, do not post)",
    })
    if r.status_code not in (200, 201):
        die(f"create payroll run {r.status_code}: {r.text[:400]}")
    rid = int(r.headers.get("Location", "/0").rsplit("/", 1)[-1])
    print(f"  created DRAFT payroll run {PAYROLL_PERIOD} (id={rid})")
    return rid


# ── 2. Foreign reverse-charge purchase, period 202606 ────────────────────────
def ensure_foreign_purchase(h):
    vendors = get(h, "/vendors")
    vendor = next((v for v in vendors if v.get("vendorCode") == FOREIGN_VENDOR_CODE), None)
    if not vendor:
        die(f"foreign vendor {FOREIGN_VENDOR_CODE} missing — apply 400_seed_manual_demo_company.sql")
    vid = vendor["vendorId"]

    cats = get(h, "/expense-categories")
    svc = next((c for c in cats if c.get("categoryCode") == SVC_CATEGORY_CODE), None)
    if not svc:
        die(f"expense category {SVC_CATEGORY_CODE} missing on co2")
    cat_id = svc["categoryId"]

    bus = get(h, "/business-units")
    bu = next((b for b in bus if b.get("code") == BU_CODE), None)
    if not bu:
        die(f"business unit {BU_CODE} missing on co2")
    bu_id = bu["businessUnitId"]

    # Idempotency: already-posted Amazon VI with our tax-invoice no?
    listing = get(h, "/vendor-invoices")
    items = listing.get("items", listing) if isinstance(listing, dict) else listing
    for it in items:
        if it.get("vendorTaxInvoiceNo") == AWS_TAX_INVOICE_NO:
            print(f"  foreign VI {AWS_TAX_INVOICE_NO} already exists (id={it.get('vendorInvoiceId')}, "
                  f"status={it.get('status')}) — skip")
            return it.get("vendorInvoiceId")

    # Draft — foreign no-VAT-D vendor: line VatRate = 0 (foreign invoice carries no
    # Thai VAT). HasInputVat auto-derives false; ภ.พ.36 computes 7% of subtotal itself.
    draft = {
        "docDate": AWS_DOC_DATE,
        "vendorId": vid,
        "vendorTaxInvoiceNo": AWS_TAX_INVOICE_NO,
        "vendorTaxInvoiceDate": AWS_DOC_DATE,
        "vatClaimPeriod": 202606,
        "currencyCode": "THB",
        "exchangeRate": 1,
        "notes": "Manual demo — AWS cloud service (reverse-charge ม.83/6, ภ.พ.36)",
        "businessUnitId": bu_id,
        "lines": [{
            "expenseCategoryId": cat_id,
            "expenseAccountId": None,
            "description": "AWS cloud hosting — June 2026",
            "amount": AWS_SERVICE_AMOUNT,
            "vatRate": 0,
            "productType": "SERVICE",
        }],
    }
    r = requests.post(f"{BASE}/vendor-invoices", headers=h, timeout=60, json=draft)
    if r.status_code not in (200, 201):
        die(f"create VI draft {r.status_code}: {r.text[:500]}")
    vi_id = int(r.headers.get("Location", "/0").rsplit("/", 1)[-1])
    print(f"  created foreign VI draft (id={vi_id})")

    r = requests.post(f"{BASE}/vendor-invoices/{vi_id}/post", headers=h, timeout=60)
    if r.status_code not in (200, 201):
        die(f"post VI {r.status_code}: {r.text[:500]}")
    posted = r.json() if r.text else {}
    print(f"  posted foreign VI (id={vi_id}, docNo={posted.get('docNo')})")
    return vi_id


# ── verification ─────────────────────────────────────────────────────────────
def verify(h, run_id):
    print("\n== VERIFY ==")
    run = get(h, f"/payroll/runs/{run_id}")
    slips = run.get("payslips", [])
    print(f"  payroll {PAYROLL_PERIOD}: status={run['status']} employees={len(slips)} "
          f"income={run.get('totalGrossTaxable')} PIT={run.get('totalPit')} net={run.get('totalNet')}")
    for s in slips:
        print(f"    - {s['employeeCode']} {s['employeeName']}: gross={s['grossTaxable']} PIT={s['pitWithheld']} "
              f"sso={s['ssoEmployee']} net={s['netPay']}")

    r = requests.post(f"{BASE}/tax-filings/pnd36?period=202606&mode=preview", headers=h, timeout=60)
    r.raise_for_status()
    p = r.json()
    rows = p.get("rows", [])
    print(f"  ภ.พ.36 2026-06: rows={len(rows)} totalService={p.get('totalServiceAmount', p.get('totalService'))} "
          f"totalVat={p.get('totalVatAmount', p.get('totalVat'))}")
    for row in rows:
        print(f"    - {json.dumps(row, ensure_ascii=False)}")
    if not rows:
        die("ภ.พ.36 preview returned 0 rows — 07.04 will throw")

    # ภ.ง.ด.1 PDF renders from the run (07.06)
    r = requests.get(f"{BASE}/payroll/runs/{run_id}/pnd1/pdf", headers=h, timeout=60)
    print(f"  ภ.ง.ด.1 PDF: {r.status_code} ({len(r.content)} bytes)")
    if r.status_code != 200:
        die(f"ภ.ง.ด.1 PDF failed: {r.text[:300]}")


def main():
    print("seed-demo-runtime.py — transactional demo data (co2)")
    h = login()
    print("step 1: payroll run 202602 (DRAFT)")
    run_id = ensure_payroll_run(h)
    print("step 2: foreign reverse-charge purchase (period 202606)")
    ensure_foreign_purchase(h)
    verify(h, run_id)
    print("\nDONE.")


if __name__ == "__main__":
    main()
