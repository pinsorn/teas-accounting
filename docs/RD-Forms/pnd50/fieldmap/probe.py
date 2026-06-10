# Throwaway recon: dump pnd50 widgets per page with nearby printed text (label join).
# Output: _pnd50_fields_p{n}.txt — field name | type | rect | text spans on the same row.
import fitz

doc = fitz.open("docs/RD-Forms/pnd50/pnd50_050369.pdf")

for pi, page in enumerate(doc):
    spans = []
    for b in page.get_text("dict")["blocks"]:
        for l in b.get("lines", []):
            for s in l.get("spans", []):
                if s["text"].strip():
                    spans.append((fitz.Rect(s["bbox"]), s["text"].strip()))

    def row_text(r, max_chars=110):
        cy = (r.y0 + r.y1) / 2
        same = [(sr.x0, t) for sr, t in spans
                if abs(((sr.y0 + sr.y1) / 2) - cy) < 6 and sr.x0 < r.x0 + 200]
        same.sort()
        out = " ".join(t for _, t in same)
        return out[:max_chars]

    lines = []
    ws = sorted(page.widgets(), key=lambda w: (round(w.rect.y0, 1), w.rect.x0))
    for w in ws:
        r = w.rect
        extra = ""
        if w.field_type_string == "RadioButton":
            extra = f" on={w.button_states()}" if hasattr(w, "button_states") else ""
        lines.append(f"{w.field_name} | {w.field_type_string}{extra} | "
                     f"({r.x0:.1f},{r.y0:.1f},{r.x1:.1f},{r.y1:.1f}) | {row_text(r)}")
    with open(f"_pnd50_fields_p{pi+1}.txt", "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print(f"p{pi+1}: {len(ws)} widgets -> _pnd50_fields_p{pi+1}.txt")
