using System.Globalization;

namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// Page-2 รายการที่ 1 การคำนวณภาษี figures, all derived by <c>Pnd50FilingService.BuildSheet</c>
/// (which enforces the ภ.ง.ด.50 §4 refuse-on-unrenderable guard before this record can exist).
/// Box numbers refer to the form's printed margin numbering (spec: pnd50-fieldmap-recon.md).
/// </summary>
public sealed record Pnd50Sheet(
    decimal BaseAmount,      // box 48-49 (Text661): TaxableProfit, or |TaxableBeforeLoss| on the loss path
    bool    IsLoss,          // Group5: false→Choice1 กำไรสุทธิ, true→Choice2 ขาดทุนสุทธิ
    decimal TaxComputed,     // box 50-51 (662) = CitComputation.TaxBeforeCredits
    decimal WhtCredit,       // box 54 (665) ภาษีหัก ณ ที่จ่าย
    decimal Pnd51Prepaid,    // box 55 (666) ภาษีที่ชำระแล้วตาม ภ.ง.ด.51
    decimal CreditsTotal,    // รวม (669) = 665 + 666 (663/664/667/668 are 0 in the v1 scope)
    decimal NetAmount,       // box 58-59 (670) = |TaxBeforeCredits − CreditsTotal|
    bool    PayMore,         // Group7/Group8 Choice1 ชำระเพิ่มเติม vs Choice2 ชำระไว้เกิน + the p1 pair
    decimal Surcharge,       // box 60 (671) — ม.67ตรี under-estimate penalty (0 when none)
    decimal TotalAmount,     // box 61-62 (672) = NetAmount + Surcharge (PayMore) / NetAmount (overpaid)
    bool    IsSme);          // Group21: false→Choice1 ทั่วไป, true→Choice2 + Group6 Choice1 SMEs

/// <summary>
/// Model for ภ.ง.ด.50 v1 (annual CIT return — page 1 header + page 2 รายการที่ 1).
/// Address block = same CompanyProfile source as ภ.ง.ด.51. Company type is fixed to
/// (1) บริษัท/ห้างฯ ตั้งขึ้นตามกฎหมายไทย (Group00) — TEAS targets Thai juristic companies.
/// </summary>
public sealed record Pnd50Model(
    string TaxId, string CompanyName,
    DateOnly PeriodStart, DateOnly PeriodEnd,
    string? Building, string? RoomNo, string? Floor, string? Village,
    string? HouseNo, string? Moo, string? Soi, string? Road,
    string? SubDistrict, string? District, string? Province, string? PostalCode,
    string? Website, string? Email,
    bool HasRelatedPartyOver200M,     // ม.71ทวิ: true→Group06 มี, false→Group07 ไม่มี/รายได้≤200M
    Pnd50Sheet Sheet);
