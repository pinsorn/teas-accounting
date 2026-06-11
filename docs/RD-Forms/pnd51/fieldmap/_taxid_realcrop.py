import fitz, os

TPL = 'backend/src/Accounting.Infrastructure/Pdf/Templates'
TEMP = os.environ.get('TEMP') or os.environ.get('TMP')
ZOOM = 5.0

def field_rect(template, fname):
    d = fitz.open(f'{TPL}/{template}')
    for pno in range(d.page_count):
        for w in (d[pno].widgets() or []):
            if w.field_name == fname:
                r = fitz.Rect(w.rect)
                d.close()
                return pno, r
    raise SystemExit(f'{fname} not found in {template}')

def crop(rendered, template, fname, out):
    pno, r = field_rect(template, fname)
    d = fitz.open(os.path.join(TEMP, rendered))
    m = 5
    clip = fitz.Rect(r.x0 - m, r.y0 - m, r.x1 + m, r.y1 + m)
    pix = d[pno].get_pixmap(matrix=fitz.Matrix(ZOOM, ZOOM), clip=clip)
    pix.save(out)
    d.close()
    print('saved', out, f'({pix.width}x{pix.height}) rect={r}')

crop('_render_pnd1.pdf',   'pnd1_main.pdf',  'Text1.0', '_real_pnd1_taxid.png')
crop('_render_50tawi.pdf', 'wht_50tawi.pdf', 'id1',     '_real_50tawi_taxid.png')
