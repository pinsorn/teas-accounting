using System.Globalization;
using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Master;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Master;

public sealed class BranchService(AccountingDbContext db, ITenantContext tenant) : IBranchService
{
    public async Task<int> CreateAsync(CreateBranchRequest req, CancellationToken ct)
    {
        var exists = await db.Branches.AnyAsync(b => b.BranchCode == req.BranchCode, ct);
        if (exists) throw new DomainException("branch.duplicate", $"Branch code '{req.BranchCode}' already exists.");

        var entity = new Branch
        {
            CompanyId    = tenant.CompanyId,
            BranchCode   = req.BranchCode,
            NameTh       = req.NameTh,
            NameEn       = req.NameEn,
            IsHeadOffice = req.IsHeadOffice,
            AddressTh    = req.AddressTh,
        };
        db.Branches.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.BranchId;
    }

    public async Task UpdateAsync(int branchId, UpdateBranchRequest req, CancellationToken ct)
    {
        var e = await db.Branches.FirstOrDefaultAsync(b => b.BranchId == branchId, ct)
            ?? throw new DomainException("branch.not_found", $"Branch {branchId} not found.");
        e.NameTh = req.NameTh; e.NameEn = req.NameEn;
        e.IsHeadOffice = req.IsHeadOffice; e.AddressTh = req.AddressTh; e.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken ct) =>
        await db.Branches.OrderBy(b => b.BranchCode)
            .Select(b => new BranchDto(b.BranchId, b.BranchCode, b.NameTh, b.NameEn, b.IsHeadOffice, b.IsActive))
            .ToListAsync(ct);
}

public sealed class VendorService(AccountingDbContext db, ITenantContext tenant) : IVendorService
{
    public async Task<long> CreateAsync(CreateVendorRequest req, CancellationToken ct)
    {
        if (await db.Vendors.AnyAsync(v => v.VendorCode == req.VendorCode, ct))
            throw new DomainException("vendor.duplicate", $"Vendor code '{req.VendorCode}' already exists.");

        var e = new Vendor
        {
            CompanyId = tenant.CompanyId,
            VendorCode = req.VendorCode, VendorType = req.VendorType,
            NameTh = req.NameTh, NameEn = req.NameEn,
            TaxId = req.TaxId, BranchCode = req.BranchCode, BranchName = req.BranchName,
            Address = req.Address,
            ContactPerson = req.ContactPerson, Phone = req.Phone, Email = req.Email,
            PaymentTermDays = req.PaymentTermDays, DefaultCurrency = req.DefaultCurrency,
            DefaultWhtTypeCode = req.DefaultWhtTypeCode,
            // Sprint 8.7 — foreign vendor is always VAT-registered-equivalent.
            IsForeign = req.IsForeign, HasThaiVatDReg = req.HasThaiVatDReg,
            CountryCode = req.CountryCode,
            VatRegistered = req.IsForeign || req.VatRegistered,
            BankName = req.BankName, BankAccountNo = req.BankAccountNo,
            BankAccountName = req.BankAccountName, SwiftCode = req.SwiftCode,
        };
        db.Vendors.Add(e);
        await db.SaveChangesAsync(ct);
        return e.VendorId;
    }

    public async Task UpdateAsync(long vendorId, UpdateVendorRequest req, CancellationToken ct)
    {
        var e = await db.Vendors.FirstOrDefaultAsync(v => v.VendorId == vendorId, ct)
            ?? throw new DomainException("vendor.not_found", $"Vendor {vendorId} not found.");
        e.NameTh = req.NameTh; e.NameEn = req.NameEn; e.TaxId = req.TaxId;
        e.BranchCode = req.BranchCode; e.BranchName = req.BranchName;
        e.Address = req.Address;
        e.ContactPerson = req.ContactPerson; e.Phone = req.Phone; e.Email = req.Email;
        e.PaymentTermDays = req.PaymentTermDays; e.DefaultCurrency = req.DefaultCurrency;
        e.DefaultWhtTypeCode = req.DefaultWhtTypeCode; e.IsActive = req.IsActive;
        e.IsForeign = req.IsForeign; e.HasThaiVatDReg = req.HasThaiVatDReg;
        e.CountryCode = req.CountryCode;
        e.VatRegistered = req.IsForeign || req.VatRegistered;
        e.BankName = req.BankName; e.BankAccountNo = req.BankAccountNo;
        e.BankAccountName = req.BankAccountName; e.SwiftCode = req.SwiftCode;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<VendorDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 200);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = $"%{search.Trim()}%";
            var ilikeQuery = db.Vendors.Where(v => EF.Functions.ILike(v.NameTh, s) || EF.Functions.ILike(v.VendorCode, s));
            var rows = await ProjectPageAsync(ilikeQuery, page, pageSize, ct);
            if (rows.Count > 0)
                return rows;

            // B1 — trigram fallback: the ILIKE pass found nothing for a non-empty search
            // term, so retry with typo-tolerant similarity (pg_trgm — 591_seed_pg_trgm.sql).
            var q = search.Trim();
            var trigramQuery = db.Vendors
                .Where(v => EF.Functions.TrigramsSimilarity(v.NameTh, q) > 0.25
                         || EF.Functions.TrigramsSimilarity(v.VendorCode, q) > 0.25)
                .OrderByDescending(v => EF.Functions.TrigramsSimilarity(v.NameTh, q));
            return await ProjectPageAsync(trigramQuery, page, pageSize, ct, alreadyOrdered: true);
        }
        return await ProjectPageAsync(db.Vendors.OrderBy(v => v.VendorCode), page, pageSize, ct, alreadyOrdered: true);
    }

    private static async Task<List<VendorDto>> ProjectPageAsync(
        IQueryable<Vendor> query, int page, int pageSize, CancellationToken ct, bool alreadyOrdered = false)
    {
        if (!alreadyOrdered)
            query = query.OrderBy(v => v.VendorCode);
        return await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(v => new VendorDto(v.VendorId, v.VendorCode, v.VendorType, v.NameTh, v.TaxId, v.VatRegistered, v.IsActive))
            .ToListAsync(ct);
    }

    public async Task<VendorDetailDto?> GetByIdAsync(long vendorId, CancellationToken ct) =>
        await db.Vendors.AsNoTracking()
            .Where(v => v.VendorId == vendorId)
            .Select(v => new VendorDetailDto(
                v.VendorId, v.VendorCode, v.VendorType, v.NameTh, v.NameEn, v.TaxId,
                v.BranchCode, v.BranchName, v.VatRegistered, v.Address, v.ContactPerson,
                v.Phone, v.Email, v.PaymentTermDays, v.DefaultCurrency,
                v.DefaultWhtTypeCode, v.IsActive,
                v.IsForeign, v.HasThaiVatDReg, v.CountryCode,
                v.BankName, v.BankAccountNo, v.BankAccountName, v.SwiftCode))
            .FirstOrDefaultAsync(ct);
}

public sealed class ChartOfAccountService(AccountingDbContext db, ITenantContext tenant) : IChartOfAccountService
{
    public async Task<long> CreateAsync(CreateAccountRequest req, CancellationToken ct)
    {
        if (await db.ChartOfAccounts.AnyAsync(a => a.AccountCode == req.AccountCode, ct))
            throw new DomainException("coa.duplicate", $"Account code '{req.AccountCode}' already exists.");

        var e = new ChartOfAccount
        {
            CompanyId = tenant.CompanyId,
            AccountCode = req.AccountCode,
            AccountNameTh = req.AccountNameTh, AccountNameEn = req.AccountNameEn,
            AccountType = req.AccountType, ParentId = req.ParentId,
            IsHeader = req.IsHeader, NormalBalance = req.NormalBalance,
        };
        db.ChartOfAccounts.Add(e);
        await db.SaveChangesAsync(ct);
        return e.AccountId;
    }

    public async Task UpdateAsync(long accountId, UpdateAccountRequest req, CancellationToken ct)
    {
        var e = await db.ChartOfAccounts.FirstOrDefaultAsync(a => a.AccountId == accountId, ct)
            ?? throw new DomainException("coa.not_found", $"Account {accountId} not found.");
        e.AccountNameTh = req.AccountNameTh; e.AccountNameEn = req.AccountNameEn;
        e.IsHeader = req.IsHeader; e.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AccountDto>> ListAsync(AccountType? type, bool activeOnly, CancellationToken ct)
    {
        var q = db.ChartOfAccounts.AsQueryable();
        if (type is { } t) q = q.Where(a => a.AccountType == t);
        if (activeOnly)    q = q.Where(a => a.IsActive);
        return await q.OrderBy(a => a.AccountCode)
            .Select(a => new AccountDto(a.AccountId, a.AccountCode, a.AccountNameTh, a.AccountNameEn,
                a.AccountType, a.IsHeader, a.NormalBalance, a.IsActive))
            .ToListAsync(ct);
    }
}

public sealed class CompanyService(AccountingDbContext db, IActivityRecorder activity) : ICompanyService
{
    public async Task<int> CreateAsync(CreateCompanyRequest req, CancellationToken ct)
    {
        if (await db.Companies.IgnoreQueryFilters().AnyAsync(c => c.TaxId == req.TaxId, ct))
            throw new DomainException("company.duplicate", $"Company with Tax ID '{req.TaxId}' already exists.");

        // specs/fix-company-create-rls-atomic.md — this method seeds rows FOR THE NEW company
        // (branch, profile, WHT types, CoA, tax codes, expense categories, per-company roles)
        // while the caller's DB session is pinned to their OWN (existing) app.company_id.
        // 600_superadmin_scoped_rls.sql retired the is_super_admin data-scope arm from every
        // company_isolation policy, so every one of those inserts 42501s unless we re-pin
        // app.company_id LOCAL to the brand-new id inside our own transaction — same idiom as
        // VatRegisterSnapshotJob.cs:95-98 (is_local=true auto-reverts at COMMIT/ROLLBACK, never
        // leaks onto the pooled connection). Wrapping everything in one transaction also fixes
        // the compounding atomicity bug for free: any failure now rolls back the companies-row
        // insert too, instead of leaving an orphan half-created tenant.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // ม.86/4 — compose the legacy free-text registered-address line from the granular parts
        // (shared composer, identical to the ภ.พ.09 hard-edit path). Falls back to the caller's
        // explicit AddressTh when no street-level parts were supplied.
        var composedLine1 = ThaiRegisteredAddress.ComposeLine1(
            req.RegHouseNo, req.RegBuilding, req.RegRoomNo, req.RegFloor,
            req.RegVillage, req.RegMoo, req.RegSoi, req.RegStreet);
        var anyStreetPart = !string.IsNullOrWhiteSpace(req.RegHouseNo) || !string.IsNullOrWhiteSpace(req.RegBuilding)
            || !string.IsNullOrWhiteSpace(req.RegRoomNo) || !string.IsNullOrWhiteSpace(req.RegFloor)
            || !string.IsNullOrWhiteSpace(req.RegVillage) || !string.IsNullOrWhiteSpace(req.RegMoo)
            || !string.IsNullOrWhiteSpace(req.RegSoi) || !string.IsNullOrWhiteSpace(req.RegStreet);
        // companies.AddressTh (general display) — prefer the composed line; keep the caller's
        // explicit AddressTh if they gave one and supplied no granular parts.
        var addressTh = anyStreetPart ? composedLine1 : (req.AddressTh ?? composedLine1);

        var e = new Company
        {
            TaxId = req.TaxId, NameTh = req.NameTh, NameEn = req.NameEn,
            LegalEntityType = req.LegalEntityType, RegistrationDate = req.RegistrationDate,
            VatRegistered = req.VatRegistered, VatRegisterDate = req.VatRegisterDate,
            VatRate = req.VatRate, Pnd30SubmissionMode = req.Pnd30SubmissionMode,
            FiscalYearStartMonth = req.FiscalYearStartMonth,
            AddressTh = addressTh, SubDistrict = req.SubDistrict, District = req.District,
            Province = req.Province, PostalCode = req.PostalCode,
            Phone = req.Phone, Email = req.Email,
            PaidUpCapital = req.PaidUpCapital,
        };
        db.Companies.Add(e);
        await db.SaveChangesAsync(ct);

        // master.companies carries no RLS (verified in pg_policies) — the insert above needed
        // no pin. Every row seeded below DOES carry RLS (G1/G2/G3), scoped to THIS new company,
        // not the caller's — LOCAL (transaction-scoped) so it reverts automatically at
        // tx.CommitAsync below and never leaks onto the pooled connection.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.company_id', {0}, true)",
            [e.CompanyId.ToString(CultureInfo.InvariantCulture)], ct);

        // MCP OAuth authorize (OAuthEndpoints.cs) requires an active HQ branch to pin the
        // token's branch_id — without this, every onboarded company 400s with
        // company_has_no_active_branch on first connector use. Created unconditionally,
        // 1:1 with the company (matches CompanyProfile.BranchCode + seeds 120/400).
        db.Branches.Add(new Branch
        {
            CompanyId    = e.CompanyId,
            BranchCode   = "00000",
            NameTh       = "สำนักงานใหญ่",
            NameEn       = "Head Office",
            IsHeadOffice = true,
            IsActive     = true,
            AddressTh    = addressTh,
        });

        // ม.86/4 — the new company's FOUNDING tax identity. Every RD form (ภ.พ.30 / ภ.ง.ด.3/53/54 /
        // 50ทวิ) reads these granular company_profile.Reg* boxes; without this row a freshly
        // onboarded company renders blank address boxes. Created unconditionally (1:1 with the
        // company) — the hard fields are read-only afterwards (changing them needs ภ.พ.09).
        var now = DateTimeOffset.UtcNow;
        db.CompanyProfiles.Add(new CompanyProfile
        {
            CompanyId = e.CompanyId,
            // HARD — mirror the companies row + the granular registered address.
            LegalName = req.NameTh,
            TaxId = req.TaxId,
            RegistrationNumber = req.TaxId,
            RegisteredAddressLine1 = composedLine1,
            RegisteredAddressLine2 = null,
            RegBuilding = req.RegBuilding, RegRoomNo = req.RegRoomNo, RegFloor = req.RegFloor,
            RegVillage = req.RegVillage, RegHouseNo = req.RegHouseNo, RegMoo = req.RegMoo,
            RegSoi = req.RegSoi, RegStreet = req.RegStreet,
            RegisteredSubdistrict = req.SubDistrict, RegisteredDistrict = req.District,
            RegisteredProvince = req.Province!, RegisteredPostalCode = req.PostalCode!,
            VatRegistrationDate = req.VatRegisterDate,
            BranchCode = "00000",
            // SOFT — contact mirrors the companies row; the rest set later via the profile UI.
            Phone = req.Phone, Email = req.Email,
            CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync(ct);

        // Sprint 8.6 (R-B5) — narrow default-set copy: the 13 standard WHT types
        // + the 1180 WHT-Receivable account into the new tenant. NOT a full
        // CoA/branch onboarding bootstrap (out of scope; demo company is seeded
        // by SQL scripts). Mirrors seed 220/230.
        var from = new DateOnly(2020, 1, 1);
        foreach (var (code, th, en, inc, form, rate) in DefaultWhtTypes)
            db.WhtTypes.Add(new WhtType
            {
                CompanyId = e.CompanyId, Code = code, NameTh = th, NameEn = en,
                IncomeTypeCode = inc, FormType = form, Rate = rate,
                EffectiveFrom = from, EffectiveTo = null, IsActive = true,
            });
        // §ledger — seed the FULL default chart of accounts the GL posting engine resolves
        // (every code in GlAccountsOptions). Previously only 1180 was added, on the assumption
        // a SQL demo seed supplied the rest — but those scripts only run for the demo company,
        // so a real onboarded tenant had an empty CoA and EVERY posting failed with
        // gl.account_missing (422). Kept in sync with 120_seed_demo_company.sql + the 230/240/482
        // account seeds and GlAccountsOptions.
        // WP1.5a — CoA entities are captured so their EF-generated AccountId can resolve the
        // expense-category default accounts below (after SaveChangesAsync populates the ids).
        var coaEntities = new List<ChartOfAccount>();
        foreach (var (code, th, en, type, normal) in DefaultChartOfAccounts)
        {
            var coa = new ChartOfAccount
            {
                CompanyId = e.CompanyId, AccountCode = code,
                AccountNameTh = th, AccountNameEn = en,
                AccountType = type, NormalBalance = normal,
                IsHeader = false, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
            };
            coaEntities.Add(coa);
            db.ChartOfAccounts.Add(coa);
        }

        // Sprint 9 B2 — copy the default VAT tax-code set into the new tenant
        // (mirrors seed 240 for company 1). Category is derived from the
        // exempt/zero-rated booleans (R-Q3); EnsureValid() rejects an
        // exempt+zero-rated conflict before it can reach the DB.
        foreach (var (code, th, dir, exempt, zero, legalRef) in DefaultTaxCodes)
        {
            var tc = new TaxCode
            {
                CompanyId = e.CompanyId, Code = code, NameTh = th,
                TaxType = TaxType.Vat, Direction = dir,
                IsRecoverable = true, IsExempt = exempt, IsZeroRated = zero,
                IsReverseCharge = false, IsActive = true, LegalRef = legalRef,
            };
            tc.EnsureValid();
            db.TaxCodes.Add(tc);
        }
        await db.SaveChangesAsync(ct);

        // WP1.5a (F20, D7) — auto-seed the 19 recommended expense categories (mirrors the
        // 430_seed_expense_categories_full.sql demo set) so a freshly onboarded company never
        // hits F20 (a savable category with no default GL account → vi/pv.expense_account_missing
        // at document save). coaEntities are tracked from the loop above; AccountId is now
        // populated post-SaveChangesAsync.
        var coaLookup = coaEntities.ToDictionary(c => c.AccountCode, c => c.AccountId);
        foreach (var cat in DefaultExpenseCategories(e.CompanyId, coaLookup))
            db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync(ct);

        // Sprint 13k — per-company RBAC: clone the standard role set + grants into the new
        // tenant from the templates captured in 510_per_company_roles_reconcile.sql. SUPER_ADMIN
        // is system-global and intentionally NOT copied (super-admin = is_super_admin user flag).
        // Done via the SQL fan-out function to stay DRY with the reconcile/bootstrap path.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT sys.seed_company_roles({e.CompanyId})", ct);

        await tx.CommitAsync(ct);
        return e.CompanyId;
    }

    // Default VAT tax-code set (kept in sync with seed 240). category derived
    // from (exempt, zero) — R-Q3 single source of truth, no category column.
    private static readonly (string Code, string Th, TaxDirection Dir,
        bool Exempt, bool Zero, string LegalRef)[] DefaultTaxCodes =
    [
        ("VAT7",              "ภาษีขาย 7%",                 TaxDirection.Output, false, false, "ม.80"),
        ("VAT-IN7",           "ภาษีซื้อ 7%",                TaxDirection.Input,  false, false, "ม.80"),
        ("VAT-OUT-0-EXP",     "ส่งออก",                     TaxDirection.Output, false, true,  "ม.80/1(1)"),
        ("VAT-OUT-0-SVC-ABR", "บริการในไทยใช้ในต่างประเทศ",  TaxDirection.Output, false, true,  "ม.80/1(2)"),
        ("EXEMPT-AGRI",       "พืชผลทางการเกษตร",           TaxDirection.Output, true,  false, "ม.81(1)(ก)"),
        ("EXEMPT-LIVE",       "สัตว์มีชีวิต",               TaxDirection.Output, true,  false, "ม.81(1)(ข)"),
        ("EXEMPT-FERT",       "ปุ๋ย",                       TaxDirection.Output, true,  false, "ม.81(1)(ค)"),
        ("EXEMPT-FEED",       "อาหารสัตว์",                 TaxDirection.Output, true,  false, "ม.81(1)(ง)"),
        ("EXEMPT-VETMED",     "ยาเคมีสัตว์/พืช",            TaxDirection.Output, true,  false, "ม.81(1)(จ)"),
        ("EXEMPT-BOOK",       "หนังสือ นิตยสาร",            TaxDirection.Output, true,  false, "ม.81(1)(ฉ)"),
        ("EXEMPT-EDU",        "การศึกษา",                   TaxDirection.Output, true,  false, "ม.81(1)(ช)"),
        ("EXEMPT-MED",        "การแพทย์",                   TaxDirection.Output, true,  false, "ม.81(1)(ญ)"),
    ];

    // Default chart of accounts seeded into every new tenant. MUST cover every code in
    // GlAccountsOptions — the posting engine resolves account_id by these codes, so a
    // missing one fails the whole journal entry (gl.account_missing, 422). Kept in sync
    // with 120_seed_demo_company.sql and the 230/240/482 account seeds.
    private static readonly (string Code, string Th, string? En, AccountType Type, NormalBalance Normal)[]
        DefaultChartOfAccounts =
    [
        ("1110", "เงินสด",                          "Cash",                      AccountType.Asset,     NormalBalance.Debit),
        ("1120", "เงินฝากธนาคาร",                   "Bank",                      AccountType.Asset,     NormalBalance.Debit),
        ("1130", "ลูกหนี้การค้า",                   "Accounts Receivable",       AccountType.Asset,     NormalBalance.Debit),
        ("1170", "ภาษีซื้อ",                        "Input VAT",                 AccountType.Asset,     NormalBalance.Debit),
        ("1180", "ภาษีหัก ณ ที่จ่ายค้างรับ",         "WHT Receivable",            AccountType.Asset,     NormalBalance.Debit),
        ("2110", "เจ้าหนี้การค้า",                  "Accounts Payable",          AccountType.Liability, NormalBalance.Credit),
        ("2151", "ภาษีขายค้างจ่าย",                "Output VAT Payable",        AccountType.Liability, NormalBalance.Credit),
        ("2152", "ภาษีหัก ณ ที่จ่ายค้างจ่าย",       "WHT Payable",               AccountType.Liability, NormalBalance.Credit),
        ("2153", "ภ.ง.ด.1 หัก ณ ที่จ่ายค้างนำส่ง",  "PIT Payable (PND1)",        AccountType.Liability, NormalBalance.Credit),
        ("2160", "เงินสมทบประกันสังคมค้างนำส่ง",    "SSO Payable",               AccountType.Liability, NormalBalance.Credit),
        ("2170", "เงินเดือนค้างจ่าย",              "Net Wages Payable",         AccountType.Liability, NormalBalance.Credit),
        // Year-end closing (specs/year-end-closing.md D2) — retained earnings; zero-balance
        // until a year is closed, dropped by the balance sheet's zero-row filter until then.
        ("3300", "กำไรสะสม",                       "Retained Earnings",         AccountType.Equity,    NormalBalance.Credit),
        ("4000", "รายได้จากการขาย",                "Sales Revenue",             AccountType.Revenue,   NormalBalance.Credit),
        ("4100", "รับคืน / ส่วนลด",                "Sales Return / Discount",   AccountType.Revenue,   NormalBalance.Debit),
        ("5000", "ต้นทุนขาย",                       "Cost of Goods Sold",        AccountType.Expense,   NormalBalance.Debit),
        ("5100", "ค่าใช้จ่ายค่าเช่า",               "Rental Expense",            AccountType.Expense,   NormalBalance.Debit),
        ("5200", "ค่าใช้จ่ายค่าบริการ",             "Service Expense",           AccountType.Expense,   NormalBalance.Debit),
        ("5300", "ค่าใช้จ่ายโฆษณา",                "Advertising Expense",       AccountType.Expense,   NormalBalance.Debit),
        ("5350", "ภาษีซื้อขอคืนไม่ได้",             "Irrecoverable Input VAT",   AccountType.Expense,   NormalBalance.Debit),
        ("5400", "เงินเดือนและค่าจ้าง",             "Salary & Wages",            AccountType.Expense,   NormalBalance.Debit),
        ("5410", "เงินสมทบประกันสังคม-นายจ้าง",     "Employer SSO Contribution", AccountType.Expense,   NormalBalance.Debit),
        // Cycle D — Fixed Assets (specs/fixed-assets.md §2). 1690 is a contra-asset:
        // AccountType.Asset with Credit normal balance (balance sheet computes Asset = Dr-Cr).
        ("1610", "อุปกรณ์และเครื่องใช้สำนักงาน",     "Office Equipment (Fixed Asset)", AccountType.Asset,   NormalBalance.Debit),
        ("1690", "ค่าเสื่อมราคาสะสม",               "Accumulated Depreciation",  AccountType.Asset,     NormalBalance.Credit),
        ("5450", "ค่าเสื่อมราคา",                   "Depreciation Expense",      AccountType.Expense,   NormalBalance.Debit),
        ("4200", "กำไรจากการจำหน่ายสินทรัพย์",       "Gain on Disposal of Assets", AccountType.Revenue,  NormalBalance.Credit),
        ("5460", "ขาดทุนจากการจำหน่ายสินทรัพย์",     "Loss on Disposal of Assets", AccountType.Expense,  NormalBalance.Debit),
    ];

    // WP1.5a (F20, D7) — the 19 recommended expense categories (§17.3 / mirrors
    // 430_seed_expense_categories_full.sql's code/name/recoverable/capex/cogs set), remapped
    // onto DefaultChartOfAccounts' coarser codes (CreateAsync seeds 51xx/52xx/53xx.., NOT the
    // granular 62xxx chart that only the demo company gets via ad-hoc seed scripts). "5200"
    // (Service Expense) is the universal fallback — present in every CreateAsync company —
    // mirroring 623_backfill_expense_category_accounts.sql's same fallback for existing tenants.
    private static readonly (string Code, string Th, string? En, string PreferredAcct,
        bool Recoverable, bool Capex, bool Cogs)[] DefaultExpenseCategorySpecs =
    [
        ("RENT",  "ค่าเช่าออฟฟิศ/อาคาร",         "Office/building rent",  "5100", true,  false, false),
        ("UTIL",  "ค่าสาธารณูปโภค",              "Utilities",             "5200", true,  false, false),
        ("SAL",   "เงินเดือน",                   "Salary",                "5400", false, false, false),
        ("WAGE",  "ค่าจ้างแรงงาน",               "Wages",                 "5400", true,  false, false),
        ("MARK",  "ค่าโฆษณา/Marketing",          "Advertising/marketing", "5300", true,  false, false),
        ("PROF",  "ค่าบริการวิชาชีพ",            "Professional services", "5200", true,  false, false),
        ("IT",    "ค่า IT / Cloud / Software",   "IT / Cloud / Software", "5200", true,  false, false),
        ("TRAV",  "ค่าเดินทาง / ที่พัก",         "Travel / lodging",      "5200", true,  false, false),
        ("COMM",  "ค่าโทรศัพท์/Internet",        "Telephone / Internet",  "5200", true,  false, false),
        ("OFFI",  "วัสดุสำนักงาน",               "Office supplies",       "5200", true,  false, false),
        ("ENT",   "ค่ารับรอง",                   "Entertainment",         "5200", false, false, false),
        ("VEHI",  "รถยนต์นั่ง (≤7 ที่นั่ง)",      "Passenger car (<=7)",   "5200", false, false, false),
        ("INSU",  "ค่าประกันภัย",                "Insurance",             "5200", true,  false, false),
        ("TRAIN", "ค่าอบรม",                     "Training",              "5200", true,  false, false),
        ("LEGAL", "ค่าทนาย/บัญชี",               "Legal / accounting",    "5200", true,  false, false),
        ("INTR",  "ดอกเบี้ยจ่าย",                "Interest expense",      "5200", false, false, false),
        ("COGS",  "ต้นทุนสินค้าขาย",             "Cost of goods sold",    "5000", true,  false, true),
        ("CAPEX", "สินทรัพย์ถาวร (capitalize)",  "Fixed asset (capex)",   "1610", true,  true,  false),
        ("MISC",  "อื่น ๆ",                      "Miscellaneous",         "5200", true,  false, false),
    ];

    /// <summary>Resolves <see cref="DefaultExpenseCategorySpecs"/> against a company's just-seeded
    /// CoA (account code → account_id). Falls back to "5200" (present in every DefaultChartOfAccounts
    /// company) when a spec's preferred code is absent from a caller-supplied lookup — defensive
    /// only; CreateAsync always passes its own freshly-seeded coaLookup which contains every code
    /// referenced above.</summary>
    private static IReadOnlyList<ExpenseCategory> DefaultExpenseCategories(
        int companyId, IReadOnlyDictionary<string, long> coaLookup)
    {
        var now = DateTimeOffset.UtcNow;
        return DefaultExpenseCategorySpecs.Select(spec => new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = spec.Code, NameTh = spec.Th, NameEn = spec.En,
            DefaultExpenseAccountId = coaLookup.TryGetValue(spec.PreferredAcct, out var id)
                ? id : coaLookup.GetValueOrDefault("5200"),
            DefaultIsRecoverableVat = spec.Recoverable, IsCapex = spec.Capex, IsCogs = spec.Cogs,
            IsActive = true, CreatedAt = now,
        }).ToList();
    }

    // Canonical 13 domestic WHT types (kept in sync with seed 220).
    private static readonly (string Code, string Th, string? En, string Inc,
        WhtFormType Form, decimal Rate)[] DefaultWhtTypes =
    [
        ("RENT",      "ค่าเช่า",                       "Rental",               "5", WhtFormType.Pnd3,  0.05m),
        ("SVC",       "ค่าบริการ (นิติบุคคล)",          "Service (corporate)",  "8", WhtFormType.Pnd53, 0.03m),
        ("ADS",       "ค่าโฆษณา",                      "Advertising",          "8", WhtFormType.Pnd53, 0.02m),
        ("SVC-IND",   "ค่าบริการ (บุคคลธรรมดา)",        "Service (individual)", "8", WhtFormType.Pnd3,  0.03m),
        ("PROF",      "ค่าวิชาชีพอิสระ",                "Professional fee",     "6", WhtFormType.Pnd53, 0.03m),
        ("TRANS",     "ค่าขนส่ง",                      "Transport",            "8", WhtFormType.Pnd53, 0.01m),
        ("COMM",      "ค่านายหน้า / คอมมิชชั่น",         "Commission",           "2", WhtFormType.Pnd53, 0.03m),
        ("ROYAL",     "ค่าสิทธิ",                      "Royalty",              "3", WhtFormType.Pnd53, 0.03m),
        ("INT",       "ดอกเบี้ย",                      "Interest",             "4", WhtFormType.Pnd53, 0.01m),
        ("PRIZE",     "รางวัล / ส่วนลดส่งเสริมการขาย",   "Prize / incentive",    "8", WhtFormType.Pnd53, 0.05m),
        ("AGRI",      "ค่าซื้อพืชผลเกษตร",              "Agricultural produce", "8", WhtFormType.Pnd53, 0.0075m),
        ("ENTERTAIN", "ค่าจ้างนักแสดง / บันเทิง",        "Entertainer fee",      "8", WhtFormType.Pnd53, 0.05m),
        ("CONTRACT",  "ค่าจ้างทำของ / รับเหมา",          "Contract work",        "7", WhtFormType.Pnd53, 0.03m),
        // Sprint 9 C1 — foreign payee (ภ.ง.ด.54, 15%); kept in sync with seed 250.
        ("FOR-SVC",   "ค่าบริการ ต่างประเทศ",            "Foreign service",      "8", WhtFormType.Pnd54, 0.15m),
        ("FOR-ROYAL", "ค่าสิทธิ ต่างประเทศ",             "Foreign royalty",      "3", WhtFormType.Pnd54, 0.15m),
    ];

    public async Task UpdateAsync(int companyId, UpdateCompanyRequest req, CancellationToken ct)
    {
        var e = await db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.CompanyId == companyId, ct)
            ?? throw new DomainException("company.not_found", $"Company {companyId} not found.");

        // §4.6 (per-company-vat-mode spec) — every change of a tax-config field is audited.
        if (e.VatRegistered != req.VatRegistered || e.VatRate != req.VatRate
            || e.Pnd30SubmissionMode != req.Pnd30SubmissionMode)
        {
            activity.Record("company", companyId, null, companyId, "tax_config_change",
                note: $"vat_registered {e.VatRegistered}->{req.VatRegistered}; "
                    + $"vat_rate {e.VatRate}->{req.VatRate}; "
                    + $"pnd30_submission_mode {e.Pnd30SubmissionMode}->{req.Pnd30SubmissionMode}",
                module: "master");
        }

        e.NameTh = req.NameTh; e.NameEn = req.NameEn;
        e.VatRegistered = req.VatRegistered; e.VatRegisterDate = req.VatRegisterDate;
        e.VatRate = req.VatRate; e.Pnd30SubmissionMode = req.Pnd30SubmissionMode;
        e.AddressTh = req.AddressTh; e.SubDistrict = req.SubDistrict; e.District = req.District;
        e.Province = req.Province; e.PostalCode = req.PostalCode;
        e.Phone = req.Phone; e.Email = req.Email; e.IsActive = req.IsActive;
        e.PaidUpCapital = req.PaidUpCapital;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CompanyDto>> ListAsync(CancellationToken ct) =>
        await db.Companies.IgnoreQueryFilters().OrderBy(c => c.NameTh)
            .Select(c => new CompanyDto(c.CompanyId, c.TaxId, c.NameTh, c.NameEn, c.LegalEntityType,
                c.VatRegistered, c.BaseCurrency, c.IsActive, c.PaidUpCapital,
                c.VatRate, c.Pnd30SubmissionMode))
            .ToListAsync(ct);

    public async Task<CompanyDetailDto> GetAsync(int companyId, CancellationToken ct) =>
        await db.Companies.IgnoreQueryFilters().Where(c => c.CompanyId == companyId)
            .Select(c => new CompanyDetailDto(c.CompanyId, c.TaxId, c.NameTh, c.NameEn,
                c.LegalEntityType, c.RegistrationDate, c.VatRegistered, c.VatRegisterDate,
                c.FiscalYearStartMonth, c.AddressTh, c.SubDistrict, c.District, c.Province,
                c.PostalCode, c.Phone, c.Email, c.IsActive, c.PaidUpCapital,
                c.VatRate, c.Pnd30SubmissionMode))
            .FirstOrDefaultAsync(ct)
        ?? throw new DomainException("company.not_found", $"Company {companyId} not found.");
}

public sealed class DocumentPrefixService(AccountingDbContext db) : IDocumentPrefixService
{
    public async Task<int> CreateAsync(CreateDocumentPrefixRequest req, CancellationToken ct)
    {
        if (await db.DocumentPrefixes.AnyAsync(p => p.PrefixCode == req.PrefixCode, ct))
            throw new DomainException("prefix.duplicate", $"Prefix '{req.PrefixCode}' already exists.");
        var e = new DocumentPrefix
        {
            PrefixCode = req.PrefixCode, DocumentType = req.DocumentType,
            DescriptionTh = req.DescriptionTh, DescriptionEn = req.DescriptionEn,
            RequiresEtax = req.RequiresEtax, IsFiscalDoc = req.IsFiscalDoc, IsExpense = req.IsExpense,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.DocumentPrefixes.Add(e);
        await db.SaveChangesAsync(ct);
        return e.PrefixId;
    }

    public async Task<IReadOnlyList<DocumentPrefixDto>> ListAsync(CancellationToken ct) =>
        await db.DocumentPrefixes.OrderBy(p => p.PrefixCode)
            .Select(p => new DocumentPrefixDto(p.PrefixId, p.PrefixCode, p.DocumentType, p.DescriptionTh,
                p.RequiresEtax, p.IsFiscalDoc, p.IsExpense, p.IsActive))
            .ToListAsync(ct);
}

public sealed class ExpenseCategoryService(AccountingDbContext db, ITenantContext tenant) : IExpenseCategoryService
{
    public async Task<int> CreateAsync(CreateExpenseCategoryRequest req, CancellationToken ct)
    {
        if (await db.ExpenseCategories.AnyAsync(c => c.CategoryCode == req.CategoryCode, ct))
            throw new DomainException("expense_category.duplicate", $"Category '{req.CategoryCode}' already exists.");

        var e = new ExpenseCategory
        {
            CompanyId = tenant.CompanyId,
            CategoryCode = req.CategoryCode,
            NameTh = req.NameTh, NameEn = req.NameEn, Description = req.Description,
            DefaultExpenseAccountId = req.DefaultExpenseAccountId,
            DefaultTaxCodeId = req.DefaultTaxCodeId,
            DefaultIsRecoverableVat = req.DefaultIsRecoverableVat,
            DefaultWhtTypeId = req.DefaultWhtTypeId,
            IsCapex = req.IsCapex, IsCogs = req.IsCogs,
            ParentCategoryId = req.ParentCategoryId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ExpenseCategories.Add(e);
        await db.SaveChangesAsync(ct);
        return e.CategoryId;
    }

    // BP-01: scope to the ACTIVE company explicitly. The global query filter
    // bypasses on a super-admin (admin), which made this list (and the PV picker)
    // show every company's categories — surfacing apparent "duplicate" codes
    // (company 1's ADS + company 2's ADS, etc.). A settings list must show only
    // the current tenant's set. Narrowing only — never widens, so no §4.7 leak.
    public async Task<IReadOnlyList<ExpenseCategoryDto>> ListAsync(CancellationToken ct) =>
        await db.ExpenseCategories.Where(c => c.CompanyId == tenant.CompanyId)
            .OrderBy(c => c.CategoryCode)
            .Select(c => new ExpenseCategoryDto(c.CategoryId, c.CategoryCode, c.NameTh, c.NameEn,
                c.DefaultIsRecoverableVat, c.IsCapex, c.IsCogs, c.IsActive,
                c.DefaultExpenseAccountId, c.DefaultTaxCodeId, c.DefaultWhtTypeId))
            .ToListAsync(ct);
}

// specs/mcp-error-surfacing.md §2 — read-only resolver for list_tax_codes. Rate is NOT a
// scalar on tax.tax_codes (see SqlScripts/450_seed_demo_company_tax_codes.sql's header
// comment: held in companies.vat_rate + the per-line snapshot; CreateAsync never seeds
// tax.tax_rates), so an exempt/zero-rated code reports 0 and every other (VAT-taxable) code
// reports the company's single vat_rate — this mirrors how VAT is actually derived
// elsewhere (TaxInvoiceService etc.), not a fictional per-code rate table join.
public sealed class TaxCodeService(AccountingDbContext db, ITenantContext tenant) : ITaxCodeService
{
    public async Task<IReadOnlyList<TaxCodeListItem>> ListAsync(CancellationToken ct)
    {
        var vatRate = await db.Companies.AsNoTracking()
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.VatRate)
            .FirstOrDefaultAsync(ct);

        // c.Category is [NotMapped] (computed from IsExempt/IsZeroRated) — can't be
        // translated inside the SQL projection, so project the raw mapped columns first
        // and compute Category client-side, same shape as TaxCode.Category's own logic.
        var rows = await db.TaxCodes.AsNoTracking()
            .Where(c => c.CompanyId == tenant.CompanyId && c.IsActive)
            .OrderBy(c => c.Code)
            .Select(c => new
            {
                c.TaxCodeId, c.Code, c.NameTh, c.IsExempt, c.IsZeroRated, c.TaxType, c.Direction,
            })
            .ToListAsync(ct);

        return rows.Select(c => new TaxCodeListItem(
            c.TaxCodeId, c.Code, c.NameTh,
            (c.IsExempt || c.IsZeroRated) ? 0m : vatRate,
            c.TaxType.ToString(), c.Direction.ToString(),
            c.IsExempt ? "EXEMPT" : c.IsZeroRated ? "ZERO_RATED" : "TAXABLE"))
            .ToList();
    }
}
