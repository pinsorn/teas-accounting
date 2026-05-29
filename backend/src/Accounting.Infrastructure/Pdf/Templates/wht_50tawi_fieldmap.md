# 50ทวิ AcroForm field map — `wht_50tawi.pdf`

> Self-verified 2026-05-29 by filling every `pay1.X` with marker `PX` + `dateN` with `N`,
> rendering via pypdfium2, and reading the placement. Use this to drive `Wht50TawiFormFiller`.
> The income box is the ม.40 sub-section (per RD guide guide100248 + the ภ.ง.ด.3 form).

## Header
| field | source (WhtCertificateDetail) |
|---|---|
| `book_no` | (optional volume; leave blank or split from DocNo) |
| `run_no` | `DocNo` |
| `name1` | `PayerName` (ผู้มีหน้าที่หักภาษี = our company) |
| `tin1` | `PayerTaxId` (13-digit, 1 char/box — see note) |
| `id1` | payer national-ID (company: blank; use tin1) |
| `add1` | `PayerAddress` |
| `name2` | `PayeeName` (ผู้ถูกหักภาษี = vendor) |
| `tin1_2` | `PayeeTaxId` |
| `id1_2` | payee national-ID (individual) else blank |
| `add2` | `PayeeAddress` |

## Form-type checkbox (set /V=/Yes, /AS=/Yes)
| FormType | checkbox | form |
|---|---|---|
| Pnd1 | `chk1` | ภ.ง.ด.1ก |
| (1ก พิเศษ) | `chk2` | — |
| Pnd2 | `chk3` | ภ.ง.ด.2 |
| Pnd3 | `chk4` | ภ.ง.ด.3 |
| (2ก) | `chk5` | — |
| (3ก) | `chk6` | — |
| Pnd53 | `chk7` | ภ.ง.ด.53 |

## Income table — by ม.40 sub-section (`IncomeTypeCode`)
| ม.40 | row label | date | amount | tax |
|---|---|---|---|---|
| `1` | 1. เงินเดือน ค่าจ้าง ม.40(1) | `date1` | `pay1.0` | `tax1.0` |
| `2` | 2. ค่าธรรมเนียม/นายหน้า ม.40(2) | `date2` | `pay1.1` | `tax1.1` |
| `3` | 3. ค่าลิขสิทธิ์ ม.40(3) | `date3` | `pay1.2` | `tax1.2` |
| `4` | 4.(ก) ดอกเบี้ย ม.40(4)(ก) | `date4` | `pay1.3` | `tax1.3` |
| (4ข ปันผล) | 4.(ข) เงินปันผล — sub-rows 1.1–2.5 | date5–13 | `pay1.4`–`pay1.12` | `tax1.4`–`tax1.12` |
| `5`,`6`,`7`,`8` | **5. ม.3 เตรส** (ทำของ/โฆษณา/เช่า/ขนส่ง/บริการ/วิชาชีพ/รับเหมา) | `date14.0` | `pay1.13.0` | `tax1.13.0` |
| (other) | 6. เงินได้นอกจาก 1.–5. (ระบุ) | — | `pay1.14` | `tax1.14` |

- For ม.40(5)–(8) → write the income description into **`spec3`** (the "(ระบุ)" blank on row 5).
- TEAS WHT types resolve: COMM→`2`, ROYAL→`3`, INT→`4`, and RENT/PROF/SVC/SVC-IND/ADS/TRANS/PRIZE/AGRI/ENTERTAIN/WAGE/CONTRACT→`5` (ม.3 เตรส). (See `460`/`470` seeds.)

## Footer
| field | source |
|---|---|
| `total` | `WhtAmount` (sum; one income type per cert today) |
| `Text1.0.0` | BahtText(`WhtAmount`) — จำนวนเงินภาษีเป็นตัวอักษร |
| `chk8` | /Yes — "(1) หักภาษี ณ ที่จ่าย" (TEAS always withholds) |
| `date_pay` / `month_pay` / `year_pay` | `CertDate` day / month / year (พ.ศ.) |

## Mechanism (PdfSharp 6.2 — see `_PdfSharpProbe`)
- PdfSharp's typed `PdfTextField` ctor throws on this form → use the raw `/Fields`+`/Kids` dict walk.
- Text: set `/V`; drop `/AP`; set form `/NeedAppearances=true` (viewer renders Thai via its font).
- Checkbox: set widget `/V=/Yes` and `/AS=/Yes`.
- TIN comb fields (`tin1`,`tin1_2`) are single fields here (not 13 split boxes) → write the plain 13-digit string.
- Issue **2 copies** (ฉบับ1 "ใช้แนบพร้อมกับแบบแสดงรายการ" / ฉบับ2 "เก็บไว้เป็นหลักฐาน") per RD guide — the form top has both labels; generate the page twice with the copy label set, or stamp the หัว text.
