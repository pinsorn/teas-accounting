# Throwaway diagnostic: zoomed band crops from the zfill render for margin-number/label reading.
# Usage: python crop.py <pdf> <page> <y0> <y1> [zoom]  -> _crop_p<page>_<y0>_<y1>.png
import sys
import fitz

pdf, pn, y0, y1 = sys.argv[1], int(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4])
z = float(sys.argv[5]) if len(sys.argv) > 5 else 4.0

doc = fitz.open(pdf)
page = doc[pn - 1]
clip = fitz.Rect(0, y0, page.rect.width, y1)
pix = page.get_pixmap(matrix=fitz.Matrix(z, z), clip=clip)
out = f"_crop_p{pn}_{int(y0)}_{int(y1)}.png"
pix.save(out)
print(out, pix.width, "x", pix.height)
