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

    public Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken ct) =>
        db.Branches.OrderBy(b => b.BranchCode)
            .Select(b => new BranchDto(b.BranchId, b.BranchCode, b.NameTh, b.NameEn, b.IsHeadOffice, b.IsActive))
            .ToListAsync(ct).ContinueWith<IReadOnlyList<BranchDto>>(t => t.Result, TaskContinuationOptions.OnlyOnRanToCompletion);
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
        var q = db.Vendors.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = $"%{search.Trim()}%";
            q = q.Where(v => EF.Functions.ILike(v.NameTh, s) || EF.Functions.ILike(v.VendorCode, s));
        }
        return await q.OrderBy(v => v.VendorCode).Skip((page - 1) * pageSize).Take(pageSize)
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
        db.ChartOfAccounts.Add(new ChartOfAccount
        {
            CompanyId = e.CompanyId, AccountCode = "1180",
            AccountNameTh = "ภาษีหัก ณ ที่จ่ายค้างรับ", AccountNameEn = "WHT Receivable",
            AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
            IsHeader = false, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });

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

        // Sprint 13k — per-company RBAC: clone the standard role set + grants into the new
        // tenant from the templates captured in 510_per_company_roles_reconcile.sql. SUPER_ADMIN
        // is system-global and intentionally NOT copied (super-admin = is_super_admin user flag).
        // Done via the SQL fan-out function to stay DRY with the reconcile/bootstrap path.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT sys.seed_company_roles({e.CompanyId})", ct);

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

    public Task<IReadOnlyList<CompanyDto>> ListAsync(CancellationToken ct) =>
        db.Companies.IgnoreQueryFilters().OrderBy(c => c.NameTh)
            .Select(c => new CompanyDto(c.CompanyId, c.TaxId, c.NameTh, c.NameEn, c.LegalEntityType,
                c.VatRegistered, c.BaseCurrency, c.IsActive, c.PaidUpCapital,
                c.VatRate, c.Pnd30SubmissionMode))
            .ToListAsync(ct).ContinueWith<IReadOnlyList<CompanyDto>>(t => t.Result, TaskContinuationOptions.OnlyOnRanToCompletion);

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

    public Task<IReadOnlyList<DocumentPrefixDto>> ListAsync(CancellationToken ct) =>
        db.DocumentPrefixes.OrderBy(p => p.PrefixCode)
            .Select(p => new DocumentPrefixDto(p.PrefixId, p.PrefixCode, p.DocumentType, p.DescriptionTh,
                p.RequiresEtax, p.IsFiscalDoc, p.IsExpense, p.IsActive))
            .ToListAsync(ct).ContinueWith<IReadOnlyList<DocumentPrefixDto>>(t => t.Result, TaskContinuationOptions.OnlyOnRanToCompletion);
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
    public Task<IReadOnlyList<ExpenseCategoryDto>> ListAsync(CancellationToken ct) =>
        db.ExpenseCategories.Where(c => c.CompanyId == tenant.CompanyId)
            .OrderBy(c => c.CategoryCode)
            .Select(c => new ExpenseCategoryDto(c.CategoryId, c.CategoryCode, c.NameTh, c.NameEn,
                c.DefaultIsRecoverableVat, c.IsCapex, c.IsCogs, c.IsActive))
            .ToListAsync(ct).ContinueWith<IReadOnlyList<ExpenseCategoryDto>>(t => t.Result, TaskContinuationOptions.OnlyOnRanToCompletion);
}
