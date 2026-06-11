"""ภ.ง.ด.50 visual gate: raster the emitted worked-case PDFs (full pages + per-box crops)
so every digit/tick can be Read-verified against the printed grid. Usage:
    python docs/RD-Forms/pnd50/fieldmap/render_check.py <pdf_dir> <out_dir>
"""
import sys, os
import fitz

pdf_dir, out_dir = sys.argv[1], sys.argv[2]
os.makedirs(out_dir, exist_ok=True)
ZOOM = 200 / 72.0

# regions of interest, in template coordinates (page, x0, y0, x1, y1, name)
ROIS = [
    (0,  30,  80, 580, 215, "p1_header_taxid_name_address"),
    (0,  30, 205, 345, 260, "p1_web_email"),
    (0, 345,  90, 580, 215, "p1_period_filing_status"),
    (0,  20, 265, 580, 420, "p1_company_type_m71bis"),
    (0,  90, 480, 320, 580, "p1_amount_pairs"),
    (1,  20,  60, 580, 120, "p2_currency_base_radios"),
    (1, 300, 330, 580, 680, "p2_boxes_661_672"),
    (1,  20, 330, 320, 680, "p2_rate_radios"),
]

for f in sorted(os.listdir(pdf_dir)):
    if not f.lower().endswith(".pdf") or not f.startswith("pnd50_"):
        continue
    tag = os.path.splitext(f)[0]
    doc = fitz.open(os.path.join(pdf_dir, f))
    for pi in (0, 1):
        pm = doc[pi].get_pixmap(matrix=fitz.Matrix(ZOOM, ZOOM))
        pm.save(os.path.join(out_dir, f"{tag}_p{pi+1}_full.png"))
    for (pi, x0, y0, x1, y1, name) in ROIS:
        pm = doc[pi].get_pixmap(matrix=fitz.Matrix(ZOOM, ZOOM), clip=fitz.Rect(x0, y0, x1, y1))
        pm.save(os.path.join(out_dir, f"{tag}_{name}.png"))
    doc.close()
    print("rastered", tag)
print("done ->", out_dir)
