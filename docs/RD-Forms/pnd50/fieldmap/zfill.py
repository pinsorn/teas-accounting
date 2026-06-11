# Throwaway diagnostic: fill every TEXT widget on the given pages with a numbered marker, raster to PNG.
# Marker = the line number from _pnd50_fields_p{n}.txt (sorted same way) so the raster cross-refs the dump.
# Usage: python zfill.py [page ...]   (1-based; default 1 2)
import sys
import fitz

pages = [int(a) for a in sys.argv[1:]] or [1, 2]

doc = fitz.open("docs/RD-Forms/pnd50/pnd50_050369.pdf")
for pn in pages:
    page = doc[pn - 1]
    ws = sorted(page.widgets(), key=lambda w: (round(w.rect.y0, 1), w.rect.x0))
    for i, w in enumerate(ws, start=1):
        if w.field_type_string == "Text":
            w.field_value = f"#{i}"
            w.update()
doc.save("_pnd50_zfill.pdf")

d2 = fitz.open("_pnd50_zfill.pdf")
for pn in pages:
    pix = d2[pn - 1].get_pixmap(matrix=fitz.Matrix(2.2, 2.2))
    pix.save(f"_pnd50_zfill_p{pn}.png")
    print(f"p{pn} -> _pnd50_zfill_p{pn}.png", pix.width, "x", pix.height)
