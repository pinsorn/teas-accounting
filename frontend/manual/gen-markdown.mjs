// Sprint 13g P3 — turn capture step-JSON into manual content.
// Reads  docs/manual/captures/<chap>/<id>.json   (written by run-capture)
// Writes docs/manual/generated/<chap>/<id>.md     (per walkthrough; for Sana
//                                                   include into chapter md)
//        docs/manual/generated/chapter-<NN>.md     (aggregate; MkDocs nav)
//        docs/manual/generated/print.html          (self-contained; P5 PDF)
//
// Node ESM, zero deps (no tsx / no md parser — we own the output format).

import {
  readdirSync, readFileSync, writeFileSync, mkdirSync, existsSync,
} from 'node:fs';
import { resolve, join } from 'node:path';
import { marked } from 'marked';

// Sprint 13g-followup — intros use GFM markdown (tables, lists, bold).
// marked escapes text content by default (safe for trusted Sana-authored
// intros). breaks:true keeps the old single-\n → <br> prose behavior.
marked.setOptions({ gfm: true, breaks: true });

const MANUAL = resolve(import.meta.dirname, '../../docs/manual');
const CAPTURES = join(MANUAL, 'captures');
const GENERATED = join(MANUAL, 'generated');

const esc = (s) =>
  String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const stripStep = (c) => String(c).replace(/^ขั้นที่\s*\d+\s*[:：]\s*/u, '').trim();

function loadWalkthroughs() {
  if (!existsSync(CAPTURES)) return [];
  const out = [];
  for (const chap of readdirSync(CAPTURES)) {
    const dir = join(CAPTURES, chap);
    for (const f of readdirSync(dir)) {
      if (!f.endsWith('.json')) continue;
      const data = JSON.parse(readFileSync(join(dir, f), 'utf8'));
      out.push({ chap, ...data }); // { chap, meta, steps[] }
    }
  }
  return out.sort((a, b) => a.meta.id.localeCompare(b.meta.id));
}

function walkthroughMarkdown(w, imgPrefix) {
  const L = [];
  L.push(`## ${w.meta.id} — ${esc(w.meta.title)}`, '');
  if (w.meta.prerequisites?.length) {
    L.push(`> **เงื่อนไขก่อนใช้งาน:** ${w.meta.prerequisites.map(esc).join(' · ')}`, '');
  }
  L.push(w.meta.intro, '');
  w.steps.forEach((s, i) => {
    L.push(`### ขั้นที่ ${i + 1}`, '');
    L.push('<figure markdown="span">');
    L.push(`  ![${esc(s.stepId)}](${imgPrefix}${s.imageRel})`);
    L.push(`  <figcaption>${esc(stripStep(s.caption))}</figcaption>`);
    L.push('</figure>', '');
  });
  return L.join('\n');
}

function chapterTitle(chap, sample) {
  // meta.chapter is like "2. ตั้งค่าระบบ"
  return sample?.meta?.chapter ?? `บทที่ ${chap}`;
}

const ws = loadWalkthroughs();
if (ws.length === 0) {
  console.error('gen-markdown: no capture JSON found under', CAPTURES);
  process.exit(1);
}

mkdirSync(GENERATED, { recursive: true });

// 1. per-walkthrough md (image path relative to generated/<chap>/<id>.md →
//    ../../captures/...). imageRel already starts with "captures/".
for (const w of ws) {
  const d = join(GENERATED, w.chap);
  mkdirSync(d, { recursive: true });
  writeFileSync(
    join(d, `${w.meta.id}.md`),
    walkthroughMarkdown(w, '../../'),
    'utf8',
  );
}

// 2. aggregate per chapter (image path relative to generated/chapter-NN.md →
//    ../captures/...).
const byChap = new Map();
for (const w of ws) {
  if (!byChap.has(w.chap)) byChap.set(w.chap, []);
  byChap.get(w.chap).push(w);
}
const navChapters = [];
for (const [chap, list] of [...byChap].sort()) {
  const title = chapterTitle(chap, list[0]);
  const body = [`# ${esc(title)}`, '']
    .concat(list.map((w) => walkthroughMarkdown(w, '../')))
    .join('\n');
  writeFileSync(join(GENERATED, `chapter-${chap}.md`), body, 'utf8');
  navChapters.push({ chap, title });
}

// 3. self-contained print.html (P5 PDF source — images via file paths
//    relative to docs/manual; opened from there by Playwright).
// print.html lives in docs/manual/generated/ → images one level up.
const stepHtml = (s) =>
  `<figure><img src="../${s.imageRel}" alt="${esc(s.stepId)}"/>` +
  `<figcaption>${esc(stripStep(s.caption))}</figcaption></figure>`;
const introHtml = (t) => marked.parse(t || '');

const sections = ws.map((w) => `
  <section class="wt">
    <h2>${w.meta.id} — ${esc(w.meta.title)}</h2>
    ${w.meta.prerequisites?.length
      ? `<p class="pre"><b>เงื่อนไขก่อนใช้งาน:</b> ${w.meta.prerequisites.map(esc).join(' · ')}</p>`
      : ''}
    <div class="intro">${introHtml(w.meta.intro)}</div>
    ${w.steps.map((s, i) =>
      `<h3>ขั้นที่ ${i + 1}</h3>${stepHtml(s)}`).join('\n')}
  </section>`).join('\n');

const printHtml = `<!doctype html><html lang="th"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>คู่มือการใช้งาน TEAS — Thailand Enterprise Accounting System</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=Sarabun:wght@400;500;600;700;800&display=swap" rel="stylesheet">
<style>
  :root{--brand:#0f766e;--brand2:#1565c0;--ink:#0f172a;--muted:#64748b;--line:#e6ebf2;--bg:#f6f8fb}
  *{box-sizing:border-box}
  html{scroll-behavior:smooth}
  body{font-family:'Sarabun','Noto Sans Thai',Tahoma,sans-serif;color:var(--ink);
    margin:0;font-size:14.5px;line-height:1.72;background:var(--bg);
    -webkit-font-smoothing:antialiased;text-rendering:optimizeLegibility}
  /* cover */
  .cover{min-height:100vh;display:flex;flex-direction:column;justify-content:center;
    align-items:center;text-align:center;page-break-after:always;padding:48px;
    background:linear-gradient(135deg,#0f766e 0%,#1565c0 100%);color:#fff}
  .cover .badge{font-size:12px;letter-spacing:4px;text-transform:uppercase;opacity:.82;margin-bottom:20px}
  .cover h1{font-size:46px;line-height:1.15;margin:0 0 12px;font-weight:800;letter-spacing:-.5px}
  .cover .sub{font-size:18px;opacity:.95;font-weight:500}
  .cover .meta{margin-top:30px;font-size:13.5px;opacity:.8;max-width:560px}
  /* content */
  main{max-width:900px;margin:0 auto;padding:30px 20px 72px}
  .wt{background:#fff;border:1px solid var(--line);border-radius:16px;padding:28px 32px;
    margin:0 0 28px;box-shadow:0 4px 18px rgba(15,23,42,.06);page-break-before:always}
  h2{color:var(--brand2);font-size:24px;font-weight:700;margin:0 0 16px;padding-bottom:12px;
    border-bottom:2px solid var(--line);letter-spacing:-.2px}
  /* step heading -> pill */
  h3{display:inline-block;color:#fff;background:var(--brand);font-size:12.5px;font-weight:600;
    padding:5px 15px;border-radius:999px;margin:26px 0 12px;box-shadow:0 2px 6px rgba(15,118,110,.25)}
  .pre{background:#fffbeb;border-left:4px solid #f59e0b;border-radius:0 10px 10px 0;
    padding:11px 16px;font-size:13.5px;color:#7c2d12;margin:0 0 16px}
  .intro{color:#334155;font-size:14.5px}
  .intro p{margin:9px 0}
  .intro strong{color:var(--ink);font-weight:600}
  .intro ul,.intro ol{margin:6px 0 12px 22px}
  .intro li{margin:4px 0}
  .intro table{border-collapse:collapse;margin:12px 0;font-size:13px;width:100%;
    border-radius:8px;overflow:hidden;box-shadow:0 0 0 1px var(--line)}
  .intro th,.intro td{border-bottom:1px solid var(--line);padding:8px 12px;text-align:left}
  .intro th{background:#f1f5f9;font-weight:600;color:var(--ink)}
  .intro tr:last-child td{border-bottom:0}
  .intro code{background:#eef2f7;padding:2px 7px;border-radius:5px;
    font-family:ui-monospace,'SFMono-Regular','Courier New',monospace;font-size:12.5px;color:#0b4f8a}
  figure{margin:16px 0 22px;page-break-inside:avoid}
  figure img{width:100%;border:1px solid var(--line);border-radius:12px;
    box-shadow:0 6px 20px rgba(15,23,42,.10)}
  figcaption{color:var(--muted);font-size:12.5px;margin-top:10px;padding-left:12px;
    border-left:3px solid var(--brand)}
  @media print{
    body{background:#fff}
    main{max-width:none;padding:0}
    .wt{box-shadow:none;border:0;border-radius:0;padding:0;page-break-before:always}
    figure img{box-shadow:none}
  }
  @page{size:A4;margin:14mm}
</style></head><body>
<div class="cover">
  <div class="badge">User Manual</div>
  <h1>คู่มือการใช้งาน TEAS</h1>
  <div class="sub">Thailand Enterprise Accounting System</div>
  <div class="meta">ระบบบัญชีวิสาหกิจ VAT-compliant สำหรับธุรกิจไทย — สายเอกสารขาย/ซื้อ, ภาษีหัก ณ ที่จ่าย, แบบยื่นสรรพากร, เงินเดือน และรายงาน</div>
</div>
<main>
${sections}
</main>
</body></html>`;

writeFileSync(join(GENERATED, 'print.html'), printHtml, 'utf8');

// nav manifest for mkdocs.yml generation step (P4 reads this if needed)
writeFileSync(
  join(GENERATED, 'nav.json'),
  JSON.stringify(navChapters, null, 2),
  'utf8',
);

console.log(
  `gen-markdown: ${ws.length} walkthroughs, ` +
  `${ws.reduce((n, w) => n + w.steps.length, 0)} steps → ` +
  `${GENERATED}`,
);
