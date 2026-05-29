-- 2026-05-29 — correct tax.wht_types.income_type_code to the ม.40 sub-section number.
--
-- WHY: WhtType.IncomeTypeCode is documented (Domain entity) as the ม.40 sub-section, and
-- both the official RD ภ.ง.ด.3 form and the 50ทวิ certificate label their income box
-- verbatim by ม.40 sub-section (box 1 = 40(1) เงินเดือน, 2 = 40(2) ค่าธรรมเนียม/นายหน้า,
-- 3 = 40(3) ลิขสิทธิ์, 4(ก) = 40(4)(ก) ดอกเบี้ย, 4(ข) = 40(4)(ข) ปันผล; the catch-all box =
-- ทำของ/โฆษณา/เช่า/ขนส่ง/บริการ = 40(5)–(8)). Seeds 220/250 carried several values that did
-- not match that scheme (PROF=2, ADS=4, SVC=3, COMM=3, AGRI=6, PRIZE=4, FOR-SVC=6, WAGE=2),
-- so the 50ทวิ printed a wrong section. Fixed at source in 220/250/460; this script repairs
-- DBs already seeded (DbInitializer re-runs every script each startup, but 220's INSERT is
-- ON CONFLICT DO NOTHING, so it never re-updates an existing row — hence this explicit UPDATE).
--
-- SAFE: issued 50ทวิ are immune — PaymentVoucherService snapshots income_type_code onto the
-- WhtCertificate row at PV-post, so only certificates generated AFTER this runs pick up the
-- corrected value. No certificate row is rewritten here.
--
-- Source per row = official RD ภ.ง.ด.3/53 booklet (ลำดับ) cross-checked with the ภ.ง.ด.3 form:
--   RENT 40(5) ลำดับ6 · PROF 40(6) ลำดับ7 · CONTRACT 40(7) ลำดับ8 (จัดหาสัมภาระ) ·
--   ADS 40(8) ลำดับ11 · SVC 40(8) ลำดับ12 · PRIZE 40(8) ลำดับ10/13 · TRANS 40(8) ลำดับ15 ·
--   AGRI 40(8) ลำดับ16 · ENTERTAIN 40(8) · COMM 40(2) ลำดับ1 · ROYAL 40(3) ลำดับ1 ·
--   INT 40(4) ลำดับ2–4 · FOR-ROYAL 40(3) · FOR-SVC 40(8) (บริการ ม.70)
--
-- JUDGMENT CALLS (flagged for CPA review — defaults below, override per-line on the PV):
--   WAGE   → 40(8) รับจ้างทำของ (labour, no materials). Alt: 40(2) if a pure service fee.
--   SVC-IND→ 40(8) บริการ (individual). Alt: 40(2) ค่าจ้างทั่วไป.
--   CONTRACT → 40(7) รับเหมา (supplies materials). Alt: 40(8) if pure work-for-hire.

UPDATE tax.wht_types w
   SET income_type_code = m.ma40
  FROM (VALUES
        ('PROF',      '6'),
        ('SVC',       '8'),
        ('SVC-IND',   '8'),
        ('ADS',       '8'),
        ('COMM',      '2'),
        ('PRIZE',     '8'),
        ('AGRI',      '8'),
        ('WAGE',      '8'),
        ('FOR-SVC',   '8')
       ) AS m(code, ma40)
 WHERE w.code = m.code
   AND w.income_type_code IS DISTINCT FROM m.ma40;
