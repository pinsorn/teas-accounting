# -*- coding: utf-8 -*-
"""สปส.1-10 ส่วนที่ 1 (page 1) coordinate measurement + verification overlay.

The official PDF is flat (zero AcroForm widgets), so a filler must overlay text at
measured coordinates. Convention consumed by the filler engine:
  - all values in PDF points, top-left origin (pymupdf native): yTop = rect.y0
  - text baseline when stamping = yTop + h * 0.8
  - comb grids: per-cell centre X list (ascending) + grid yTop/h

Usage (from anywhere):
  python measure.py measure   -> derive boxes from the PDF, print candidate JSON
  python measure.py verify    -> load fieldmap/sps110_boxes.json, stamp distinct
                                 markers, raster crops to fieldmap/_scratch/ for
                                 visual verification
"""
import io
import json
import os
import sys

import fitz

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
PDF = os.path.join(HERE, "..", "sps1_10_part1.pdf")
JSON_PATH = os.path.join(HERE, "sps110_boxes.json")
SCRATCH = os.path.join(HERE, "_scratch")

PAD = 3.0          # gap between a printed label's end and the writing box start
LINE_H = 12.0      # text size for dotted-line fields
CELL_H = 14.0      # text size inside comb cells (grid boxes are 18pt tall)
DPI_ZOOM = 190 / 72.0


def r1(v):
    return round(v, 1)


def measure():
    doc = fitz.open(PDF)
    pg = doc[0]

    # ---- comb grids: drawn rects (get_drawings 're' items) --------------------
    cells_acct, cells_branch = [], []
    for d in pg.get_drawings():
        for item in d["items"]:
            if item[0] != "re":
                continue
            r = item[1]
            if 108 < r.y0 < 110 and r.width < 15:      # เลขที่บัญชี row (y0≈109.1)
                cells_acct.append(r)
            elif 131 < r.y0 < 134 and r.width < 15:    # ลำดับที่สาขา row (y0≈132.5)
                cells_branch.append(r)
    cells_acct.sort(key=lambda r: r.x0)
    cells_branch.sort(key=lambda r: r.x0)

    def grid(cells):
        y0 = min(c.y0 for c in cells)
        y1 = max(c.y1 for c in cells)
        return {
            "cells": [r1((c.x0 + c.x1) / 2) for c in cells],
            "yTop": r1(y0 + ((y1 - y0) - CELL_H) / 2),
            "h": CELL_H,
        }

    # ---- label anchors (search_for gives the rect of just the label text) -----
    def label(needle, ymin, ymax):
        for r in pg.search_for(needle):
            if ymin <= r.y0 <= ymax:
                return r
        raise SystemExit(f"label not found: {needle} in y[{ymin},{ymax}]")

    lbl_name = label("ชื่อสถานประกอบการ", 85, 95)
    lbl_branch = label("(ถ้ามี)", 105, 115)
    lbl_addr = label("านักงานใหญ่/สาขา", 130, 140)   # ำ is split; match the tail
    lbl_post = label("รหัสไปรษณีย์", 172, 180)
    lbl_tel = label("โทรศัพท์", 172, 180)
    lbl_fax = label("โทรสาร", 172, 180)
    lbl_month = label("ค่าจ้างเดือน", 196, 204)
    lbl_be = label("พ.ศ", 196, 204)

    # dotted-line rows: marker baseline ≈ 4pt above the printed word's bbox bottom
    def line(lbl, x_end, align="left"):
        return {
            "x": r1(lbl.x1 + PAD),
            "yTop": r1(lbl.y1 - 4 - LINE_H * 0.8),
            "w": r1(x_end - (lbl.x1 + PAD)),
            "h": LINE_H,
            "align": align,
        }

    # ---- contribution table (box 61.9,196.9 -> 385.9,375.4) -------------------
    # verticals: x=268.9 (labels | บาท), x=358.9 (บาท | สต.); horizontals are the
    # row separators measured from get_drawings line items.
    X_BAHT0, X_SPLIT, X_RIGHT = 268.9, 358.9, 385.9
    ROWS = {  # row name -> (band y0, band y1)
        "wage": (253.2, 275.1),
        "empContrib": (275.1, 296.1),
        "employerContrib": (296.1, 312.6),
        "total": (312.6, 332.8),
        "words": (332.8, 350.8),
        "count": (350.8, 375.4),
    }

    def row_ytop(band):
        y0, y1 = band
        return r1(y0 + ((y1 - y0) - LINE_H) / 2)

    def baht(band):
        return {"x": r1(X_BAHT0 + PAD), "yTop": row_ytop(band),
                "w": r1(X_SPLIT - 4 - (X_BAHT0 + PAD)), "h": LINE_H, "align": "right"}

    def satang(band):
        return {"x": r1(X_SPLIT + PAD), "yTop": row_ytop(band),
                "w": r1(X_RIGHT - 4 - (X_SPLIT + PAD)), "h": LINE_H, "align": "right"}

    boxes = {
        "employerName": line(lbl_name, 385.4),
        "branchName": line(lbl_branch, 384.4),
        "address": line(lbl_addr, 386.5),
        # continuation dotted line under ที่ตั้งฯ (full width, y≈154.5-173.0)
        "address2": {"x": 73.0, "yTop": r1(173.0 - 4 - LINE_H * 0.8),
                     "w": 310.0, "h": LINE_H, "align": "left"},
        "postalCode": line(lbl_post, lbl_tel.x0 - 2),
        "phone": line(lbl_tel, lbl_fax.x0 - 2),
        "accountNoCells": grid(cells_acct),
        "branchSeqCells": grid(cells_branch),
        "wageMonth": line(lbl_month, lbl_be.x0 - 2),
        "wageYear": line(lbl_be, 380.0),
        "tblWageBaht": baht(ROWS["wage"]),
        "tblWageSatang": satang(ROWS["wage"]),
        "tblEmpContribBaht": baht(ROWS["empContrib"]),
        "tblEmpContribSatang": satang(ROWS["empContrib"]),
        "tblEmployerContribBaht": baht(ROWS["employerContrib"]),
        "tblEmployerContribSatang": satang(ROWS["employerContrib"]),
        "tblTotalBaht": baht(ROWS["total"]),
        "tblTotalSatang": satang(ROWS["total"]),
        # row 5: number goes in the บาท-column cell; "คน" is pre-printed at x≈365.8
        "tblEmployeeCount": baht(ROWS["count"]),
        # (ตัวอักษร) row between parens "(" x1=87.8 and ")" x0=372.3
        "amountWords": {"x": 91.0, "yTop": row_ytop(ROWS["words"]),
                        "w": 278.0, "h": LINE_H, "align": "left"},
    }
    print(json.dumps(boxes, ensure_ascii=False, indent=2))
    doc.close()


# ---------------------------------------------------------------------------
MARKERS = {
    "employerName": "บริษัท ทดสอบ มาร์คเกอร์ จำกัด",
    "branchName": "สาขาทดสอบที่หนึ่ง",
    "address": "123/45 ถนนทดสอบ แขวงลองดี",
    "address2": "เขตวัดผล กรุงเทพมหานคร",
    "postalCode": "10110",
    "phone": "021234567",
    "wageMonth": "พฤษภาคม",
    "wageYear": "2569",
    "tblWageBaht": "1234567", "tblWageSatang": "89",
    "tblEmpContribBaht": "61728", "tblEmpContribSatang": "39",
    "tblEmployerContribBaht": "61728", "tblEmployerContribSatang": "39",
    "tblTotalBaht": "123456", "tblTotalSatang": "78",
    "tblEmployeeCount": "12",
    "amountWords": "หนึ่งแสนสองหมื่นสามพันสี่ร้อยห้าสิบหกบาทเจ็ดสิบแปดสตางค์",
}

CROPS = [  # (name, x0, y0, x1, y1)
    ("c1_header_lines", 55, 82, 400, 222),
    ("c2_account_grids", 440, 95, 780, 160),
    ("c3_table", 50, 215, 400, 382),
]


def verify():
    with open(JSON_PATH, encoding="utf-8") as f:
        boxes = json.load(f)
    doc = fitz.open(PDF)
    pg = doc[0]

    fontfile = None
    for cand in (r"C:\Windows\Fonts\tahoma.ttf", r"C:\Windows\Fonts\leelawui.ttf"):
        if os.path.exists(cand):
            fontfile = cand
            break
    if not fontfile:
        raise SystemExit("no Thai-capable font found")
    font = fitz.Font(fontfile=fontfile)
    pg.insert_font(fontname="thai", fontfile=fontfile)
    blue = (0, 0, 0.9)

    def put(text, x, ytop, h, align="left", w=0):
        size = h
        # shrink-to-fit: the filler engine must do the same for long values
        if w > 0 and font.text_length(text, fontsize=size) > w:
            size = h * w / font.text_length(text, fontsize=size)
        if align == "right":
            x = x + w - font.text_length(text, fontsize=size)
        pg.insert_text((x, ytop + h * 0.8), text, fontsize=size,
                       fontname="thai", color=blue)

    for key, spec in boxes.items():
        if "cells" in spec:
            for i, cx in enumerate(spec["cells"]):
                t = str(i % 10)
                put(t, cx - font.text_length(t, fontsize=spec["h"]) / 2,
                    spec["yTop"], spec["h"])
        else:
            put(MARKERS[key], spec["x"], spec["yTop"], spec["h"],
                spec.get("align", "left"), spec["w"])

    os.makedirs(SCRATCH, exist_ok=True)
    out_pdf = os.path.join(SCRATCH, "overlay.pdf")
    doc.save(out_pdf)
    for name, x0, y0, x1, y1 in CROPS:
        pm = pg.get_pixmap(matrix=fitz.Matrix(DPI_ZOOM, DPI_ZOOM),
                           clip=fitz.Rect(x0, y0, x1, y1))
        pm.save(os.path.join(SCRATCH, f"{name}.png"))
        print("crop", name)
    doc.close()
    print("done ->", SCRATCH)


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "measure"
    {"measure": measure, "verify": verify}[mode]()
