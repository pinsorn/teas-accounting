using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Accounting.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Register Infrastructure services. API layer adds tenant binding and middleware.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        var connStr = cfg.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");

        services.AddDbContext<AccountingDbContext>(opt =>
            opt.UseNpgsql(connStr, npg =>
            {
                npg.MigrationsHistoryTable("__ef_migrations", "sys");
                npg.MigrationsAssembly(typeof(AccountingDbContext).Assembly.FullName);
            })
            .UseSnakeCaseNamingConvention());

        services.AddSingleton<IClock, SystemClock>();

        // Identity primitives — see Program.cs for JWT bearer wire-up.
        services.AddOptions<Identity.JwtOptions>().Bind(cfg.GetSection("Jwt")).ValidateOnStart();
        services.AddOptions<Identity.MfaOptions>().Bind(cfg.GetSection("Mfa")).ValidateOnStart();
        services.AddSingleton<IPasswordHasher, Identity.BcryptPasswordHasher>();
        services.AddSingleton<ITotpService, Identity.OtpNetTotpService>();
        services.AddSingleton<IJwtTokenIssuer, Identity.JwtTokenIssuer>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPermissionLookup, PermissionLookup>();
        services.AddScoped<IApiKeyResolver, Identity.ApiKeyResolver>();   // Sprint 14
        services.AddScoped<Application.Identity.IApiKeyService, Identity.ApiKeyService>();
        services.AddScoped<IIdempotencyStore, Identity.IdempotencyStore>();   // Sprint 14 P4
        services.AddScoped<INumberSequenceService, Numbering.NumberSequenceService>();
        services.AddScoped<Application.Master.ICustomerService, Master.CustomerService>();
        services.AddScoped<Application.Ledger.IJournalService, Ledger.JournalService>();
        services.AddScoped<Application.Sales.ITaxInvoiceService, Sales.TaxInvoiceService>();

        services.AddScoped<Application.Master.IBranchService,           Master.BranchService>();
        services.AddScoped<Application.Master.IVendorService,           Master.VendorService>();
        services.AddScoped<Application.Master.IBusinessUnitService,      Master.BusinessUnitService>();
        services.AddScoped<Application.Master.IProductService,           Master.ProductService>();
        services.AddScoped<Application.Tax.IWhtTypeService,              Tax.WhtTypeService>();
        services.AddScoped<Application.Master.IChartOfAccountService,   Master.ChartOfAccountService>();
        services.AddScoped<Application.Master.ICompanyService,          Master.CompanyService>();
        services.AddScoped<Application.Master.IDocumentPrefixService,   Master.DocumentPrefixService>();
        services.AddScoped<Application.Master.IExpenseCategoryService,  Master.ExpenseCategoryService>();

        services.AddScoped<Application.Purchase.IPaymentVoucherService, Purchase.PaymentVoucherService>();
        services.AddScoped<Application.Purchase.IVendorInvoiceService,  Purchase.VendorInvoiceService>();
        services.AddScoped<Application.Purchase.IPurchaseOrderService,  Purchase.PurchaseOrderService>();
        services.AddScoped<Application.Purchase.IWhtCertificateService,  Purchase.WhtCertificateService>();
        services.AddScoped<Application.Sales.IReceiptService,            Sales.ReceiptService>();
        services.AddScoped<Application.Sales.ITaxAdjustmentNoteService,  Sales.TaxAdjustmentNoteService>();
        services.AddScoped<Application.Sales.IQuotationService,          Sales.QuotationService>();
        services.AddScoped<Application.Sales.ISalesOrderService,         Sales.SalesOrderService>();
        services.AddScoped<Application.Sales.IDeliveryOrderService,      Sales.DeliveryOrderService>();
        services.AddScoped<Application.Sales.ISalesChainPdfService,      Sales.SalesChainPdfService>();
        services.AddOptions<Storage.FileStorageOptions>().Bind(cfg.GetSection("FileStorage"));
        services.AddSingleton<Application.Abstractions.IFileStorageService, Storage.LocalDiskFileStorage>();
        services.AddScoped<Application.Attachments.IAttachmentService,    Attachments.AttachmentService>();
        services.AddScoped<Application.Reports.IVatReportService,        Reports.VatReportService>();
        services.AddScoped<Application.Reports.IGlReportService,         Reports.GlReportService>();
        services.AddScoped<Application.Reports.INumberGapReportService,  Reports.NumberGapReportService>();
        services.AddScoped<Application.Reports.IVatThresholdService,      Reports.VatThresholdService>();
        services.AddScoped<Application.Reports.IWhtReceivableReportService, Reports.WhtReceivableReportService>();
        services.AddScoped<Application.Reports.IFinancialReportService,     Reports.FinancialReportService>();
        services.AddScoped<Application.TaxFilings.IProportionalInputVatService, TaxFilings.ProportionalInputVatService>();
        services.AddScoped<Application.TaxFilings.ITaxFilingService,         TaxFilings.TaxFilingService>();
        services.AddScoped<Application.TaxFilings.IWhtFilingService,         TaxFilings.WhtFilingService>();
        services.AddScoped<Application.Ledger.IPeriodCloseService,       Ledger.PeriodCloseService>();

        // GL auto-posting — bind account-code map then register the poster.
        services.AddOptions<Ledger.GlAccountsOptions>().Bind(cfg.GetSection("GlAccounts"));
        services.AddScoped<Application.Ledger.IGlPostingService,         Ledger.GlPostingService>();

        // Sprint 8.5 — VAT-mode + non-VAT doc labels (bound from the same "Tax"
        // section as API TaxConfig; Infra can't reference the API assembly).
        services.AddOptions<VatModeOptions>().Bind(cfg.GetSection("Tax"));

        // e-Tax (XAdES + email). Section "ETax:Signing" / "ETax:Email" in appsettings + .env.
        services.AddOptions<ETax.ETaxSigningOptions>().Bind(cfg.GetSection("ETax:Signing"));
        services.AddOptions<ETax.ETaxEmailOptions>().Bind(cfg.GetSection("ETax:Email"));
        services.AddOptions<ETax.ETaxBehaviorOptions>().Bind(cfg.GetSection("ETax"));
        services.AddOptions<ETax.ETaxValidationOptions>().Bind(cfg.GetSection("ETax:Validation"));
        services.AddScoped<IETaxXmlBuilder, ETax.ETaxXmlBuilder>();
        services.AddSingleton<IETaxXmlValidator, ETax.LocalXsdValidator>();
        services.AddSingleton<ETax.QualifyingPropertiesBuilder>();
        services.AddSingleton<IETaxSigner, ETax.ETaxSigner>();
        services.AddSingleton<IETaxEmailSender, ETax.ETaxEmailSender>();
        services.AddScoped<IETaxSubmissionAudit, ETax.ETaxSubmissionAudit>();

        // RD e-Filing client — Tier 1 Mock vs Tier 2/3 real HTTP, by config.
        services.AddOptions<ETax.RdApiOptions>().Bind(cfg.GetSection("RdApi"));
        if ((cfg["RdApi:Provider"] ?? "Mock") == "Mock")
            services.AddSingleton<IRdEfilingClient, ETax.MockRdEfilingClient>();
        else
            services.AddHttpClient<IRdEfilingClient, ETax.RdHttpEfilingClient>();

        // Submission pipeline (Phase-1 best-effort). The retry BackgroundService
        // lives in the API composition root (Infra stays hosting-free).
        services.AddOptions<ETax.ETaxSubmissionOptions>().Bind(cfg.GetSection("ETax:Submission"));
        services.AddScoped<IETaxSubmissionPipeline, ETax.ETaxSubmissionPipeline>();

        return services;
    }
}
