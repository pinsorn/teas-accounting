// Central registry of the Thai Revenue Department (RD) forms TEAS works with.
// Source of truth: docs/RD-Forms/INDEX.md + each folder's _meta.md (Source URLs section).
// Names + deadline text live in messages/{th,en}.json under `documents.form.<code>` so the
// /documents page is fully bilingual; this module holds only structural facts + official URLs.
// Ham's decision (2026-06-11): link the official RD URLs — do NOT serve the committed PDFs.

export type RdFormCategory = 'vat' | 'vatRequest' | 'wht' | 'cit' | 'pit' | 'sbt' | 'stamp';

// 1 = production must-have · 2 = TEAS scope conditional · 3 = context only (per INDEX.md).
export type RdFormTier = 1 | 2 | 3;

export interface RdForm {
  /** Stable key — also the i18n key under documents.form.<code>. */
  code: string;
  /** RD code as printed on the form (e.g. "ภ.พ.30"). */
  rdCode: string;
  category: RdFormCategory;
  tier: RdFormTier;
  /** i18n key under documents.frequency.* */
  frequency: 'monthly' | 'annual' | 'semiAnnual' | 'oneTime' | 'perTransaction' | 'adHoc';
  /** Direct PDF on the RD site (version-stamped). null → only a hub/source page exists. */
  pdfUrl: string | null;
  /** RD official source page (version-latest landing page). */
  sourceUrl: string;
}

// Ordered by category then INDEX.md row order.
export const RD_FORMS: readonly RdForm[] = [
  // §1 VAT — returns
  { code: 'pp30', rdCode: 'ภ.พ.30', category: 'vat', tier: 1, frequency: 'monthly',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/vat/2568/pp30_010968.pdf',
    sourceUrl: 'https://www.rd.go.th/62381.html' },
  { code: 'pp30Attach', rdCode: 'ใบแนบ ภ.พ.30', category: 'vat', tier: 1, frequency: 'monthly',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/vat/2568/AttachPP30_010968.pdf',
    sourceUrl: 'https://www.rd.go.th/62381.html' },
  { code: 'pp36', rdCode: 'ภ.พ.36', category: 'vat', tier: 1, frequency: 'monthly',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/vat/2568/pp36_010968.pdf',
    sourceUrl: 'https://www.rd.go.th/62381.html' },
  { code: 'pp302', rdCode: 'ภ.พ.30.2', category: 'vat', tier: 3, frequency: 'annual',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/vat/2568/PP30.2_010968.pdf',
    sourceUrl: 'https://www.rd.go.th/62381.html' },

  // §2 VAT — registration / change requests
  { code: 'pp01', rdCode: 'ภ.พ.01', category: 'vatRequest', tier: 2, frequency: 'oneTime',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/request/2568/pp01_010968.pdf',
    sourceUrl: 'https://www.rd.go.th/62386.html' },
  { code: 'pp09', rdCode: 'ภ.พ.09', category: 'vatRequest', tier: 2, frequency: 'adHoc',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/request/2568/pp09_010968.pdf',
    sourceUrl: 'https://www.rd.go.th/62386.html' },

  // §3 WHT
  { code: 'pnd1', rdCode: 'ภ.ง.ด.1', category: 'wht', tier: 1, frequency: 'monthly',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/withhold/200360_WHT1.pdf',
    sourceUrl: 'https://www.rd.go.th/62377.html' },
  { code: 'pnd1a', rdCode: 'ภ.ง.ด.1ก', category: 'wht', tier: 1, frequency: 'annual',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/withhold/210360_WHT1_kor.pdf',
    sourceUrl: 'https://www.rd.go.th/62377.html' },
  { code: 'pnd2', rdCode: 'ภ.ง.ด.2', category: 'wht', tier: 1, frequency: 'monthly',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/withhold/240360_WHT2.pdf',
    sourceUrl: 'https://www.rd.go.th/62377.html' },
  { code: 'pnd3', rdCode: 'ภ.ง.ด.3', category: 'wht', tier: 1, frequency: 'monthly',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/withhold/270360_WHT3.pdf',
    sourceUrl: 'https://www.rd.go.th/62377.html' },
  { code: 'pnd53', rdCode: 'ภ.ง.ด.53', category: 'wht', tier: 1, frequency: 'monthly',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/withhold/WHT53_041060.pdf',
    sourceUrl: 'https://www.rd.go.th/62377.html' },
  { code: 'pnd54', rdCode: 'ภ.ง.ด.54', category: 'wht', tier: 1, frequency: 'monthly',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/cit/2568/050369CIT54.pdf',
    sourceUrl: 'https://www.rd.go.th/62375.html' },
  { code: 'wtc50', rdCode: '50 ทวิ', category: 'wht', tier: 1, frequency: 'perTransaction',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/withhold/approve_wh3_081156.pdf',
    sourceUrl: 'https://www.rd.go.th/62377.html' },
  { code: 'popor01', rdCode: 'ป.ป.01', category: 'wht', tier: 2, frequency: 'adHoc',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/request/pp0153_130753.pdf',
    sourceUrl: 'https://www.rd.go.th/62388.html' },
  { code: 'popor02', rdCode: 'ป.ป.02', category: 'wht', tier: 2, frequency: 'adHoc',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/request/pp0253_130753.pdf',
    sourceUrl: 'https://www.rd.go.th/62388.html' },

  // §4 CIT
  { code: 'pnd50', rdCode: 'ภ.ง.ด.50', category: 'cit', tier: 1, frequency: 'annual',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/cit/2568/050369CIT50.pdf',
    sourceUrl: 'https://www.rd.go.th/62375.html' },
  { code: 'pnd51', rdCode: 'ภ.ง.ด.51', category: 'cit', tier: 1, frequency: 'semiAnnual',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/cit/2568/020768CIT51.pdf',
    sourceUrl: 'https://www.rd.go.th/62375.html' },
  { code: 'pnd52', rdCode: 'ภ.ง.ด.52', category: 'cit', tier: 3, frequency: 'annual',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/cit/2568/050369CIT52.pdf',
    sourceUrl: 'https://www.rd.go.th/62375.html' },
  { code: 'pnd55', rdCode: 'ภ.ง.ด.55', category: 'cit', tier: 2, frequency: 'annual',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/cit/2568/050369CIT55.pdf',
    sourceUrl: 'https://www.rd.go.th/62375.html' },

  // §5 PIT (context — employees/freelancers file themselves)
  { code: 'pnd90', rdCode: 'ภ.ง.ด.90', category: 'pit', tier: 3, frequency: 'annual',
    pdfUrl: null, sourceUrl: 'https://www.rd.go.th/62336.html' },
  { code: 'pnd91', rdCode: 'ภ.ง.ด.91', category: 'pit', tier: 3, frequency: 'annual',
    pdfUrl: null, sourceUrl: 'https://www.rd.go.th/62336.html' },
  { code: 'pnd93', rdCode: 'ภ.ง.ด.93', category: 'pit', tier: 3, frequency: 'adHoc',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/pit/2568/201267PIT93.pdf',
    sourceUrl: 'https://www.rd.go.th/67335.html' },
  { code: 'pnd94', rdCode: 'ภ.ง.ด.94', category: 'pit', tier: 3, frequency: 'semiAnnual',
    pdfUrl: null, sourceUrl: 'https://www.rd.go.th/62336.html' },
  { code: 'pnd95', rdCode: 'ภ.ง.ด.95', category: 'pit', tier: 3, frequency: 'annual',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/pit/2568/241268PIT95.pdf',
    sourceUrl: 'https://www.rd.go.th/67335.html' },

  // §6 SBT
  { code: 'pt40', rdCode: 'ภ.ธ.40', category: 'sbt', tier: 1, frequency: 'monthly',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/spec/2568/pt40_010968.pdf',
    sourceUrl: 'https://www.rd.go.th/62380.html' },

  // §7 Stamp Duty
  { code: 'os4', rdCode: 'อ.ส.4', category: 'stamp', tier: 2, frequency: 'perTransaction',
    pdfUrl: 'https://www.rd.go.th/fileadmin/tax_pdf/SD/OS4_150861.pdf',
    sourceUrl: 'https://www.rd.go.th/62374.html' },
];

export const RD_FORM_CATEGORIES: readonly RdFormCategory[] = [
  'vat', 'vatRequest', 'wht', 'cit', 'pit', 'sbt', 'stamp',
];

/** RD e-Filing / portal links shown at the top of the /documents page. */
export const RD_FILING_CHANNELS = [
  { code: 'efiling', url: 'https://efiling.rd.go.th/rd-cms/' },
  { code: 'openApi', url: 'https://efiling.rd.go.th/rd-cms/openapi' },
  { code: 'eStamp', url: 'https://efiling.rd.go.th/rd-stamp-os9-web/' },
  { code: 'dbd', url: 'https://efiling.dbd.go.th/' },
  { code: 'taxCalendar', url: 'https://www.rd.go.th/62348.html' },
] as const;
