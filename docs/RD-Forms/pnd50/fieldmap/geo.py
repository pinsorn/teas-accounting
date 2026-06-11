"""pnd50 cell-centre geometry extraction (generalised from _pnd51_geo.py).

Emits pnd50_cells.json: field name -> printed cell-centre X list (PDF points)
for the v1-filled comb boxes on pages 1-2. RdAcroFormFiller places each char
at the real printed cell centre via this map (non-uniform combs drift under
the generic equal-division placement).
"""
import fitz, json, collections, os

os.chdir(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
src = "docs/RD-Forms/pnd50/pnd50_050369.pdf"
OUT = "docs/RD-Forms/pnd50/fieldmap/pnd50_cells.json"
PAGES = (0, 1, 2, 5)  # v2: p1, p2, p3 (รายการที่ 2+3 col ③), p6 (งบฐานะ)
WANT = {"1",                                            # p1 taxid 13-cell grid
        "Text2000-1", "Text3", "Text2000", "Text3-2",   # p1 amount pairs
        "Text661", "662", "663", "664", "665", "666", "667", "668",
        "669", "670", "671", "672",                     # p2 รายการที่ 1 combs
        # p3 รายการที่ 2 ladder — column ③ (รวม) only (กรณีทั่วไป/ลดอัตรา per form rubric)
        "Text17.4", "Text17.7", "Text17.10", "Text17.13", "Text17.16",
        "Text17.19", "Text17.22", "Text17.25", "Text17.28", "Text17.31",
        "Text17.34", "Text17.37", "Text17.40", "Text17.43",
        "Text20", "Text23", "Text26", "Text29", "Text32", "Text35.1", "Text35.2",
        # p3 รายการที่ 3 ต้นทุนขาย — column ③
        "Text35.5", "Text35.8", "Text35.11", "Text35.14", "Text35.17",
        "Text35.20", "Text35.23", "Text35.26", "Text35.29",
        # p6 งบฐานะ
        "Text35.210", "Text35.211", "Text35.212", "Text35.213", "Text35.214",
        "Text35.215", "Text35.216", "Text35.217", "Text35.218", "Text35.219",
        "Text35.220", "Text35.221", "Text35.222", "Text35.223", "Text35.224",
        "Text35.225", "Text35.226", "Text35.227",
        "Text35.2241", "Text35.2251", "Text35.2261", "Text35.2242", "Text35.2252"}


def vbounds(page, rect):
    y0, y1 = rect.y0, rect.y1
    x0, x1 = rect.x0 - 2, rect.x1 + 2
    ymid = (y0 + y1) / 2
    xs = []
    for dr in page.get_drawings():
        for it in dr["items"]:
            if it[0] == "l":
                a, b = it[1], it[2]
                if abs(a.x - b.x) < 0.8 and min(a.y, b.y) <= ymid <= max(a.y, b.y) and x0 <= a.x <= x1:
                    xs.append(a.x)
            elif it[0] == "re":
                r = it[1]
                if r.y0 - 1 <= ymid <= r.y1 + 1:
                    if x0 - 1 <= r.x0 <= x1 + 1:
                        xs.append(r.x0)
                    if x0 - 1 <= r.x1 <= x1 + 1:
                        xs.append(r.x1)
    xs.sort()
    merged = []
    for x in xs:
        if not merged or x - merged[-1] > 1.5:
            merged.append(x)
    return merged


def centers(b):
    segs = [(b[i], b[i + 1]) for i in range(len(b) - 1)]
    segs = [(a, c) for a, c in segs if c - a > 2]
    if not segs:
        return []
    mx = max(c - a for a, c in segs)
    return [round((a + c) / 2, 1) for a, c in segs if (c - a) >= 0.62 * mx]


d = fitz.open(src)
flat, dupes = {}, collections.Counter()
for pi in PAGES:
    page = d[pi]
    for w in (page.widgets() or []):
        if w.field_type != fitz.PDF_WIDGET_TYPE_TEXT or w.field_name not in WANT:
            continue
        b = vbounds(page, w.rect)
        if not b:
            continue
        has_l = any(abs(x - w.rect.x0) < 3 for x in b)
        has_r = any(abs(x - w.rect.x1) < 3 for x in b)
        if not (has_l and has_r):
            continue
        c = centers(b)
        if not c:
            continue
        if len(c) < 2:  # plain bordered box, not a comb — per-char placement would stack chars
            print("plain field (skipped from cell map):", w.field_name, c)
            continue
        dupes[w.field_name] += 1
        flat[w.field_name] = c

missing = WANT - set(flat)
dup = [k for k, n in dupes.items() if n > 1]
print("missing (no printed grid found — OK only for plain fields):", sorted(missing))
print("DUPLICATE names across pages (FATAL if any):", dup)
assert not dup, "cellCenters is keyed by field name — duplicate names would collide"
json.dump(flat, open(OUT, "w"), ensure_ascii=False, indent=1)
print({k: len(v) for k, v in flat.items()})
