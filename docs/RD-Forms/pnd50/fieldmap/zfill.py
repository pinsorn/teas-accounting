# Throwaway diagnostic: fill every TEXT widget on p1+p2 with a numbered marker, raster to PNG.
# Marker = the line number from _pnd50_fields_p{n}.txt (sorted same way) so the raster cross-refs the dump.
import fitz

doc = fitz.open("docs/RD-Forms/pnd50/pnd50_050369.pdf")
for pi in (0, 1):
    page = doc[pi]
    ws = sorted(page.widgets(), key=lambda w: (round(w.rect.y0, 1), w.rect.x0))
    for i, w in enumerate(ws, start=1):
        if w.field_type_string == "Text":
            w.field_value = f"#{i}"
            w.update()
doc.save("_pnd50_zfill.pdf")

d2 = fitz.open("_pnd50_zfill.pdf")
for pi in (0, 1):
    pix = d2[pi].get_pixmap(matrix=fitz.Matrix(2.2, 2.2))
    pix.save(f"_pnd50_zfill_p{pi+1}.png")
    print(f"p{pi+1} -> _pnd50_zfill_p{pi+1}.png", pix.width, "x", pix.height)
