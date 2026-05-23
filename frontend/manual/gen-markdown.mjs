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
<title>AccountProject User Manual (TH) v0.5</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=Sarabun:wght@400;600;700&display=swap" rel="stylesheet">
<style>
  *{box-sizing:border-box}
  body{font-family:'Sarabun','Noto Sans Thai',Tahoma,sans-serif;
    color:#0f172a;margin:0;padding:0;font-size:14px;line-height:1.6}
  .cover{height:100vh;display:flex;flex-direction:column;justify-content:center;
    align-items:center;text-align:center;page-break-after:always}
  .cover h1{font-size:34px;margin:0 0 8px}
  .cover .sub{color:#475569;font-size:16px}
  .wt{padding:24px 36px;page-break-before:always}
  h2{color:#1565C0;border-bottom:2px solid #e2e8f0;padding-bottom:6px;font-size:22px}
  h3{color:#0f766e;font-size:16px;margin-top:22px}
  .pre{background:#fff7ed;border-left:4px solid #f59e0b;padding:8px 12px;font-size:13px}
  .intro{color:#334155;font-size:13.5px;margin:8px 0 4px}
  figure{margin:8px 0 18px;page-break-inside:avoid}
  figure img{width:100%;border:1px solid #cbd5e1;border-radius:6px}
  figcaption{font-style:italic;color:#475569;font-size:12.5px;margin-top:6px}
  .intro table{border-collapse:collapse;margin:8px 0;font-size:13px}
  .intro th,.intro td{border:1px solid #cbd5e1;padding:4px 8px;text-align:left}
  .intro th{background:#f1f5f9;font-weight:600}
  .intro ul,.intro ol{margin:4px 0 8px 20px}
  .intro li{margin:2px 0}
  .intro code{background:#f1f5f9;padding:1px 4px;border-radius:3px;
    font-family:'Courier New',monospace;font-size:12px}
  .intro strong{color:#0f172a}
  .intro p{margin:6px 0}
  @page{size:A4;margin:14mm}
</style></head><body>
<div class="cover">
  <h1>คู่มือการใช้งาน TEAS</h1>
  <div class="sub">Thailand Enterprise Accounting System</div>
  <div class="sub">เวอร์ชัน 0.5 — บทที่ 1–2 (นำร่อง)</div>
</div>
${sections}
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
