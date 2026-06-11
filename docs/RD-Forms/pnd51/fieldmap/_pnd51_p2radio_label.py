import fitz

d = fitz.open('backend/src/Accounting.Infrastructure/Pdf/Templates/pnd51_main.pdf')
p = d[1]; H = p.rect.height
groups = {}
for w in (p.widgets() or []):
    if w.field_type_string in ('CheckBox', 'RadioButton'):
        groups.setdefault(w.field_name, []).append(w.rect)

shapes = p.new_shape()
for name, rects in groups.items():
    for i, r in enumerate(sorted(rects, key=lambda r: (H - r.y1, r.x0))):
        shapes.draw_rect(r)
        shapes.finish(color=(1, 0, 0), width=0.8)
        p.insert_text((r.x0 - 1, r.y0 - 1.5), f"{name[-2:]}#{i}", fontsize=4.2, color=(1, 0, 0))
shapes.commit()

# full page + two zoom bands (top method/รายการ1, bottom รายการ2) for legibility
p.get_pixmap(matrix=fitz.Matrix(2.4, 2.4)).save('_p2_radiomap.png')
top = fitz.Rect(0, 20, p.rect.width, 380)
bot = fitz.Rect(0, 400, p.rect.width, 700)
p.get_pixmap(matrix=fitz.Matrix(3.2, 3.2), clip=top).save('_p2_radiomap_top.png')
p.get_pixmap(matrix=fitz.Matrix(3.2, 3.2), clip=bot).save('_p2_radiomap_bot.png')
print('wrote _p2_radiomap.png + _top + _bot')
