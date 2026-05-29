# RD-Forms — Index

> เอกสารกลางอ้างอิงแบบฟอร์มของกรมสรรพากร (RD) ที่ TEAS ต้องใช้กรอก/ส่ง
> Drafted: 2026-05-29 by Sana
> Status: **Metadata only** — binary PDFs not downloaded (see REPORT.md)
> Cross-reference: see `docs/Tax-Reference-TH.md` for tax-law context

## ⚠️ สำคัญ — Read REPORT.md ก่อน

- Binary PDFs ไม่ได้ download ลง repo เพราะ Sana sandbox blocks binary fetches
- ทุก `_meta.md` มี **PDF URL ของกรมสรรพากร** — click เพื่อ download จาก official site
- URL ทุกตัว verified live ผ่าน WebFetch (ดูสถานะใน `_meta.md` แต่ละ folder)

---

## §1 VAT (ภาษีมูลค่าเพิ่ม) — แบบแสดงรายการ

| Code | Folder | ชื่อ | Frequency | Deadline (paper / e-filing) | TEAS Module |
|---|---|---|---|---|---|
| **ภ.พ.30** | [`pp30/`](./pp30/_meta.md) | VAT Monthly Return | Monthly | **15 / 25** | Tax / VAT |
| ใบแนบ ภ.พ.30 | [`pp30-attach/`](./pp30-attach/_meta.md) | Per-branch Output/Input breakdown | Monthly | 15 / 25 | Tax / VAT (multi-branch) |
| **ภ.พ.36** | [`pp36/`](./pp36/_meta.md) | Self-assess VAT (Import service) | Monthly | **7 / 15** | Tax / AP (foreign vendor) |
| ภ.พ.30.2 | [`pp30-2/`](./pp30-2/_meta.md) | Apportionment by revenue ratio | Annual adjustment | 25 (e-filing) | Out of scope (Phase 1) |

## §2 VAT (ภาษีมูลค่าเพิ่ม) — แบบคำร้อง/คำขอ

| Code | Folder | ชื่อ | Frequency | Deadline | TEAS Module |
|---|---|---|---|---|---|
| **ภ.พ.01** | [`pp01/`](./pp01/_meta.md) | VAT Registration | One-time | 30 days after threshold | Master / Onboarding |
| **ภ.พ.09** | [`pp09/`](./pp09/_meta.md) | VAT Change Notification | As-needed | 15 days after change | Master / Settings |
| ภ.พ.01.1 — ภ.พ.14 | (URLs in pp01/_meta.md) | Various registration variants | One-time | Various | Master |

## §3 WHT (ภาษีหัก ณ ที่จ่าย)

| Code | Folder | ชื่อ | Frequency | Deadline | TEAS Module |
|---|---|---|---|---|---|
| **ภ.ง.ด.1** | [`pnd1/`](./pnd1/_meta.md) | WHT — Employment (ม.40(1)(2)) | Monthly | **7 / 15** | HR / Payroll |
| **ภ.ง.ด.1ก** | [`pnd1a/`](./pnd1a/_meta.md) | WHT Annual Summary — Employment | Annual | End of Feb | HR / Year-end |
| **ภ.ง.ด.2** | [`pnd2/`](./pnd2/_meta.md) | WHT — Interest/Dividend/Royalty (ม.40(3)(4)) to Individuals | Monthly | 7 / 15 | Tax / Dividend |
| **ภ.ง.ด.3** | [`pnd3/`](./pnd3/_meta.md) | WHT — Individual contractor/professional (ม.40(5)-(8)) | Monthly | 7 / 15 | AP / Tax |
| **ภ.ง.ด.53** | [`pnd53/`](./pnd53/_meta.md) | WHT — Juristic Recipients (Thai) | Monthly | 7 / 15 | AP / Tax |
| **ภ.ง.ด.54** | [`pnd54/`](./pnd54/_meta.md) | WHT — Foreign Payment (ม.70) | Monthly | 7 / 15 | AP / Foreign |
| **50 ทวิ** | [`50tawi/`](./50tawi/_meta.md) | Withholding Certificate | Per transaction | Issue immediately | AP / HR / Auto-gen |

## §4 CIT (ภาษีเงินได้นิติบุคคล)

| Code | Folder | ชื่อ | Frequency | Deadline | TEAS Module |
|---|---|---|---|---|---|
| **ภ.ง.ด.50** | [`pnd50/`](./pnd50/_meta.md) | CIT Annual Return | Annual | **150 days** from year-end | Tax / Year-end / GL |
| **ภ.ง.ด.51** | [`pnd51/`](./pnd51/_meta.md) | CIT Mid-year Prepay | Semi-annual | 2 months after 6-month period | Tax / GL |
| ภ.ง.ด.52 | [`pnd52/`](./pnd52/_meta.md) | CIT — International Transport | Annual | 150 days | Out of scope |
| ภ.ง.ด.55 | [`pnd55/`](./pnd55/_meta.md) | Foundation/Association IT | Annual | 150 days | Conditional |

## §5 PIT (ภาษีเงินได้บุคคลธรรมดา) — context only

| Code | Folder | ชื่อ | Frequency | Deadline | TEAS Module |
|---|---|---|---|---|---|
| ภ.ง.ด.90 | [`pnd90/`](./pnd90/_meta.md) | PIT — Multiple income types | Annual | 31 มี.ค. | Outside (recipient files) |
| ภ.ง.ด.91 | [`pnd91/`](./pnd91/_meta.md) | PIT — Employment only | Annual | 31 มี.ค. | Outside (recipient files) |
| ภ.ง.ด.94 | [`pnd94/`](./pnd94/_meta.md) | PIT mid-year (ม.40(5)-(8)) | Semi-annual | สิ้น ก.ย. | Outside |

## §6 SBT (ภาษีธุรกิจเฉพาะ)

| Code | Folder | ชื่อ | Frequency | Deadline | TEAS Module |
|---|---|---|---|---|---|
| **ภ.ธ.40** | [`pt40/`](./pt40/_meta.md) | SBT Return | Monthly | **15 / 25** | Tax (conditional — SBT industries only) |

## §7 Stamp Duty (อากรแสตมป์)

| Code | Folder | ชื่อ | Frequency | Deadline | TEAS Module |
|---|---|---|---|---|---|
| **อ.ส.4** | [`os4/`](./os4/_meta.md) | Stamp Duty in Cash | Per instrument | 15 days after instrument | Tax / Contracts |
| อ.ส.9 (e-Stamp) | (mentioned in os4/) | e-Stamp portal | Per instrument | 15 days | Tax / Contracts (5 mandatory types) |

---

## Quick stats

- **Total forms catalogued:** 21 folders (+ many sub-variants noted inline)
- **Tier 1 (TEAS production must-have):** 11 — pp30, pp36, pp30-attach, pnd1, pnd1a, pnd2, pnd3, pnd53, pnd54, 50tawi, pnd50, pnd51, pt40
- **Tier 2 (TEAS scope, conditional):** pp01, pp09, os4, pnd55
- **Tier 3 (context only):** pnd90/91/94, pnd52, pp30-2

## Cross-references

- **Tax law context:** `docs/Tax-Reference-TH.md`
- **TEAS GL mapping (per tax):** `docs/Tax-Reference-TH.md` §15
- **Detailed report:** `REPORT.md` (this folder)

## Filing channels — summary

| Channel | URL | Use case |
|---|---|---|
| Paper | District Revenue Office | Default fallback |
| **e-Filing portal** | `https://efiling.rd.go.th/rd-cms/` | All forms · +8 days extension |
| **RD Open API** | `https://efiling.rd.go.th/rd-cms/openapi` | Direct submission · Service Provider auth |
| Service Providers | `https://efiling.rd.go.th/ef-cms-web/service-provider` | Outsource filing |
| e-Stamp portal | `https://efiling.rd.go.th/rd-stamp-os9-web/` | Stamp Duty 5 mandatory types |

## Update notes

- ปฏิทินภาษี: `https://www.rd.go.th/62348.html` (verify monthly for holiday shifts)
- VAT 7% extension: `https://www.rd.go.th/fileadmin/user_upload/news/2568thai/news34_2568.pdf`
- ปรับปรุงล่าสุดของ form versions: ดู Source page ใน `_meta.md` แต่ละ folder
