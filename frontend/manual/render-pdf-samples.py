"""Render "filled RD form" sample PDFs (page 1) to PNG for the user manual.

The manual shows users what the system-generated tax PDFs look like. PNGs land
in `manual/pdf-samples/` and chapter-7 walkthroughs embed them via
`lib/pdf-sample.ts` -> showPdfSample(). Run this BEFORE capturing chapter 7:

    python manual/render-pdf-samples.py        # backend :5080 + co2 demo seed up

Requires PyMuPDF (fitz). Logs in as the co2 demo admin, calls each PDF endpoint,
renders page 1, and writes <key>-p1.png. Endpoints that 422/404 (insufficient
data) are reported and skipped, not fatal — the walkthrough guard surfaces a
missing sample at capture time.
"""
import json
import os
import sys
import urllib.error
import urllib.request

import fitz  # PyMuPDF

BASE = os.environ.get("TEAS_API", "http://localhost:5080")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "pdf-samples")
YEAR = 2026  # demo data lives in FY2026 (CE); co2 ภ.พ.30/CIT all reference it
DPI = 140    # crisp enough; showPdfSample down-scales to ~96vh in the capture


def call(method, path, token=None, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    if token:
        req.add_header("Authorization", "Bearer " + token)
    if data:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, resp.headers.get("Content-Type", ""), resp.read()
    except urllib.error.HTTPError as e:
        return e.code, e.headers.get("Content-Type", ""), e.read()


def login():
    st, _, b = call("POST", "/auth/login",
                    body={"username": "demo-admin", "password": "Demo@1234"})
    if st != 200:
        sys.exit(f"login failed {st}: {b[:200]!r}")
    return json.loads(b)["access_token"]


def find_rent_cert(token):
    """The clean 50ทวิ for the manual is the rent vendor cert (05.03), not an
    e2e leftover. Discover it by payee rather than hardcoding an id (ids shift)."""
    st, _, b = call("GET", "/wht-certificates?pageSize=50", token)
    if st != 200:
        return None
    items = json.loads(b)
    items = items if isinstance(items, list) else items.get("items", [])
    def cid(c):
        return c.get("whtCertificateId") or c.get("id")
    rent = next((c for c in items
                 if "เช่า" in (c.get("payeeName") or "") or "พร็อพเพอร์ตี้" in (c.get("payeeName") or "")),
                None)
    return cid(rent) if rent else (cid(items[0]) if items else None)


def find_payroll_run(token, period="202602"):
    """ภ.ง.ด.1 renders from a payroll run (draft is fine — it's a read). Find the
    seeded demo run by period rather than hardcoding an id."""
    st, _, b = call("GET", "/payroll/runs", token)
    if st != 200:
        return None
    runs = json.loads(b)
    runs = runs if isinstance(runs, list) else runs.get("items", [])
    run = next((r for r in runs if str(r.get("periodYearMonth")) == period), None)
    return run.get("payrollRunId") if run else None


def render(token, key, path):
    st, ct, b = call("GET", path, token)
    if st != 200 or "pdf" not in ct:
        print(f"  SKIP {key:12} {st} {b[:120].decode('utf-8', 'replace')}")
        return False
    doc = fitz.open(stream=b, filetype="pdf")
    pix = doc[0].get_pixmap(dpi=DPI)
    out = os.path.join(OUT, f"{key}-p1.png")
    pix.save(out)
    print(f"  OK   {key:12} {doc.page_count}p -> {os.path.basename(out)} ({pix.width}x{pix.height})")
    return True


def main():
    os.makedirs(OUT, exist_ok=True)
    tok = login()
    cert = find_rent_cert(tok)
    targets = [
        ("pnd51", f"/tax-filings/pnd51/pdf?year={YEAR}"),
        ("pnd50", f"/tax-filings/pnd50/pdf?year={YEAR}"
                  "&attestFirstFiling=true&attestBlankSchedules=true"),
        # ภ.พ.01 / ภ.พ.09 — VAT-registration applications; only the identity header
        # is filled (the rest is filled by hand), so these are sparse samples.
        ("pp01", "/tax-filings/pp01/pdf"),
        ("pp09", "/tax-filings/pp09/pdf"),
    ]
    if cert:
        targets.append(("wht50tawi", f"/wht-certificates/{cert}/pdf"))
    else:
        print("  WARN no wht certificate found — 50ทวิ sample skipped")
    run = find_payroll_run(tok)
    if run:
        targets.append(("pnd1", f"/payroll/runs/{run}/pnd1/pdf"))  # draft run is fine (read-only)
    else:
        print("  WARN no payroll run 202602 found — ภ.ง.ด.1 sample skipped")
    ok = sum(render(tok, k, p) for k, p in targets)
    print(f"rendered {ok}/{len(targets)} samples -> {OUT}")


if __name__ == "__main__":
    main()
