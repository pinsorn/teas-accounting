# RESEARCH — cancel + reissue of ใบกำกับภาษี / ใบเสร็จ (AGY/Gemini, 2026-10-05)

> Status: **UNVERIFIED** — AGY output; several rulings cite the same rd.go.th URL. Must be confirmed by the
> company's CPA (and spot-checked against the ป.86/2542 text) before Feature A ships. Answers
> `specs/doc-lifecycle-cancel-reissue-backdate.md` §1.6.

## Claims (AGY's labels)
1. **Wording** (ป.86/2542 ข้อ 25, "confirmed"): replacement notes "เป็นการยกเลิกและออกใบกำกับภาษีฉบับใหม่แทนฉบับเดิม
   เลขที่... เล่มที่..."; adding "วันที่..." is common practice. Reason NOT required on the face (unlike CN ม.86/10).
2. **Date / number** ("confirmed", ข้อ 25 + กค 0706/พ./7836, กค 0702/พ./3959): replacement carries the
   **ORIGINAL document's date**; number = **new running number**.
3. **Original**: stamp ยกเลิก / cross out, keep with the seller's copy; buyer keeps a photocopy.
4. **Cancel allowed** for errors in essential particulars (ม.86/4: name, address, tax id, branch, description)
   with the sale unchanged. **Value change → CN/DN** (ม.82/10, 86/9, 86/10). CN cannot fix a name; cancel cannot
   grant a discount.
5. **ภ.พ.30 / รายงานภาษีขาย**:
   - same month: cancelled TI listed in sequence as ยกเลิก with 0.00; replacement listed with real amounts,
     remark "ออกแทนเลขที่...".
   - original filed in a prior month: **no amended ภ.พ.30**, nothing added to the current month (would double
     count). Note the cancellation in the sales-tax report of the month the replacement is made
     (ข้อ 25 "ให้หมายเหตุการยกเลิก...ไว้ในรายงานภาษีขายของเดือนภาษีที่จัดทำใบกำกับภาษีฉบับใหม่").
     Cancelled TI is never omitted from the report.
6. **Customer never paid → cancelling is NOT allowed** (bad-debt route, ม.82/11). Deal aborted / service not
   performed → CN (ม.82/10). Amount typo caught before delivery: cancel+reissue "tolerated".
7. **Non-VAT receipt**: no specific decree; practice = same as ข้อ 25 by analogy, same wording.

## Design consequences (Fable)
- Replacement DocDate = original DocDate (legal requirement) — conflicts with the "backdate only inside an open
  period" rule (Ham §6 Q2, Feature B) when the original's month is closed. Needs Ham ruling.
- Replacement DocNo = new number; DocNo prefix is derived from DocDate (MM-YYYY) → a replacement for a closed
  month gets an out-of-order number in that month's sequence. Ham/CPA to accept or pick another scheme.
- GL: reversal + replacement net to zero; if the original month is closed both post in the current open period.
- VAT reports filter Posted-by-DocDate: replacement (same date, same amount) substitutes the original — no
  double count. Sales-tax report needs ยกเลิก rows (0.00) + ออกแทน remark rows.
- Standalone cancel of a TI (no reissue) is legally narrow — see claim 6.
