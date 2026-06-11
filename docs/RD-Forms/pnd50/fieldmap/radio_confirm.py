# Radio choice↔option confirmation for pnd50 (never guess a radio — pnd51 flip lesson).
# For a given choice name, tick every radio widget whose on-state == choice, raster the radio
# regions, and stack them into one PNG per page: _r50_<choice>_p<n>.png
# Usage: python radio_confirm.py <choice> [page ...]   (1-based; default 1 2)
import sys
import fitz
from PIL import Image

choice = sys.argv[1]
pages = [int(a) for a in sys.argv[2:]] or [1, 2]
Z = 2.2

doc = fitz.open("docs/RD-Forms/pnd50/pnd50_050369.pdf")
ticked = []
for pi in [p - 1 for p in pages]:
    for w in doc[pi].widgets():
        if w.field_type_string != "RadioButton":
            continue
        states = [s for s in (w.button_states() or {}).get("normal", []) if s != "Off"]
        if choice in states:
            w.field_value = choice
            w.update()
            r = w.rect
            ticked.append(f"p{pi+1} {w.field_name} ({r.x0:.0f},{r.y0:.0f})")
doc.save("_r50_tmp.pdf")

d2 = fitz.open("_r50_tmp.pdf")
# (page, x0pt, y0pt, x1pt, y1pt) regions containing radio rows + their printed labels
regions = {
    1: [(20, 150, 590, 220), (20, 268, 320, 416)],          # Group1 · Group00-07
    2: [(20, 50, 590, 78), (20, 322, 590, 462), (20, 608, 320, 678)],  # Group4 · Group5/21/6 · Group7/8
    3: [(20, 172, 340, 200), (20, 290, 340, 316), (20, 590, 340, 618)],  # Group100 · Group101 · Group9
    6: [(20, 495, 340, 522), (20, 680, 360, 742)],          # Group91 · Group92+93
}
for pno, regs in {p: regions[p] for p in pages}.items():
    pix = d2[pno - 1].get_pixmap(matrix=fitz.Matrix(Z, Z))
    full = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
    crops = [full.crop((int(x0 * Z), int(y0 * Z), int(x1 * Z), int(y1 * Z)))
             for x0, y0, x1, y1 in regs]
    wmax = max(c.width for c in crops)
    out = Image.new("RGB", (wmax, sum(c.height for c in crops) + 8 * len(crops)), "white")
    y = 0
    for c in crops:
        out.paste(c, (0, y)); y += c.height + 8
    out.save(f"_r50_{choice}_p{pno}.png")

print(f"{choice}: ticked {len(ticked)} widgets")
for t in ticked:
    print(" ", t)
