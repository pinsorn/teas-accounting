namespace Accounting.Domain.Enums;

/// <summary>Legal form of the company (อักษรย่อตาม DBD).</summary>
public enum LegalEntityType
{
    /// <summary>บริษัทจำกัด</summary>
    LimitedCompany,
    /// <summary>บริษัทมหาชน</summary>
    PublicLimitedCompany,
    /// <summary>ห้างหุ้นส่วนจำกัด</summary>
    LimitedPartnership,
    /// <summary>ห้างหุ้นส่วนสามัญ</summary>
    OrdinaryPartnership,
    /// <summary>กิจการร่วมค้า</summary>
    JointVenture,
    /// <summary>เจ้าของคนเดียว</summary>
    SoleProprietor,
    /// <summary>นิติบุคคลอื่น</summary>
    Other,
}
