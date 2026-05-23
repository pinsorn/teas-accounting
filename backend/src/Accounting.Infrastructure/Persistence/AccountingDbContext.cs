using System.Reflection;
using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Audit;
using Accounting.Domain.Entities.ETax;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Entities.Tax;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for TEAS. Multi-tenant — global query filters drop rows that don't
/// belong to <see cref="ITenantContext.CompanyId"/>, and PostgreSQL RLS gives a second
/// belt-and-braces guarantee at the database layer.
/// </summary>
public class AccountingDbContext : DbContext
{
    private readonly ITenantContext? _tenant;

    public AccountingDbContext(DbContextOptions<AccountingDbContext> options, ITenantContext? tenant = null)
        : base(options)
    {
        _tenant = tenant;
    }

    // Identity / system
    public DbSet<User>            Users           => Set<User>();
    public DbSet<Role>            Roles           => Set<Role>();
    public DbSet<Permission>      Permissions     => Set<Permission>();
    public DbSet<RolePermission>  RolePermissions => Set<RolePermission>();
    public DbSet<UserRole>        UserRoles       => Set<UserRole>();
    public DbSet<ApiKey>          ApiKeys         => Set<ApiKey>();
    public DbSet<IdempotencyKey>  IdempotencyKeys => Set<IdempotencyKey>();

    // Sys
    public DbSet<DocumentPrefix>  DocumentPrefixes   => Set<DocumentPrefix>();
    public DbSet<ExpenseCategory> ExpenseCategories  => Set<ExpenseCategory>();
    public DbSet<NumberSequence>  NumberSequences    => Set<NumberSequence>();

    // Master
    public DbSet<Company>         Companies       => Set<Company>();
    public DbSet<Branch>          Branches        => Set<Branch>();
    public DbSet<ChartOfAccount>  ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<Customer>        Customers       => Set<Customer>();
    public DbSet<Vendor>          Vendors         => Set<Vendor>();
    public DbSet<BusinessUnit>    BusinessUnits   => Set<BusinessUnit>();
    public DbSet<Product>         Products        => Set<Product>();
    public DbSet<CompanyProfile>  CompanyProfiles => Set<CompanyProfile>();

    // Tax
    public DbSet<TaxCode>         TaxCodes        => Set<TaxCode>();
    public DbSet<TaxRate>         TaxRates        => Set<TaxRate>();
    public DbSet<WhtType>         WhtTypes        => Set<WhtType>();
    public DbSet<WhtCertificate>  WhtCertificates => Set<WhtCertificate>();
    public DbSet<TaxFiling>       TaxFilings      => Set<TaxFiling>();

    // Purchase
    public DbSet<PaymentVoucher>     PaymentVouchers     => Set<PaymentVoucher>();
    public DbSet<PaymentVoucherLine> PaymentVoucherLines => Set<PaymentVoucherLine>();
    public DbSet<VendorInvoice>      VendorInvoices      => Set<VendorInvoice>();
    public DbSet<VendorInvoiceLine>  VendorInvoiceLines  => Set<VendorInvoiceLine>();
    public DbSet<PurchaseOrder>      PurchaseOrders      => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine>  PurchaseOrderLines  => Set<PurchaseOrderLine>();
    public DbSet<PaymentVoucherApplication> PaymentVoucherApplications => Set<PaymentVoucherApplication>();

    // Ledger
    public DbSet<JournalEntry>     JournalEntries     => Set<JournalEntry>();
    public DbSet<JournalLine>      JournalLines       => Set<JournalLine>();
    public DbSet<AccountingPeriod> AccountingPeriods  => Set<AccountingPeriod>();

    // Sales
    public DbSet<TaxInvoice>          TaxInvoices         => Set<TaxInvoice>();
    public DbSet<TaxInvoiceLine>      TaxInvoiceLines     => Set<TaxInvoiceLine>();
    public DbSet<Receipt>             Receipts            => Set<Receipt>();
    public DbSet<ReceiptApplication>  ReceiptApplications => Set<ReceiptApplication>();
    public DbSet<ReceiptWhtLine>      ReceiptWhtLines     => Set<ReceiptWhtLine>();
    public DbSet<ReceiptLine>         ReceiptLines        => Set<ReceiptLine>();
    public DbSet<TaxAdjustmentNote>   TaxAdjustmentNotes  => Set<TaxAdjustmentNote>();
    public DbSet<Quotation>           Quotations          => Set<Quotation>();
    public DbSet<QuotationLine>       QuotationLines      => Set<QuotationLine>();
    public DbSet<SalesOrder>          SalesOrders         => Set<SalesOrder>();
    public DbSet<SalesOrderLine>      SalesOrderLines     => Set<SalesOrderLine>();
    public DbSet<DeliveryOrder>       DeliveryOrders      => Set<DeliveryOrder>();
    public DbSet<DeliveryOrderLine>   DeliveryOrderLines  => Set<DeliveryOrderLine>();
    // Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล)
    public DbSet<BillingNote>         BillingNotes        => Set<BillingNote>();
    public DbSet<BillingNoteLine>     BillingNoteLines    => Set<BillingNoteLine>();
    // Sprint 13i C7 — BN ↔ TI join table
    public DbSet<BillingNoteTaxInvoice> BillingNoteTaxInvoices => Set<BillingNoteTaxInvoice>();

    // Audit
    public DbSet<ActivityLog>     ActivityLogs    => Set<ActivityLog>();
    public DbSet<Accounting.Domain.Entities.Sys.Attachment> Attachments => Set<Accounting.Domain.Entities.Sys.Attachment>();

    // e-Tax (Sprint 13c) — append-only submission audit
    public DbSet<ETaxSubmission>  ETaxSubmissions => Set<ETaxSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountingDbContext).Assembly);

        ApplyTenantFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Attach a CompanyId-based query filter to every entity that implements <see cref="ITenantOwned"/>.
    /// Super-admins and the migration-time context (where _tenant is null) bypass the filter.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        var method = typeof(AccountingDbContext)
            .GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                method.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => _tenant == null || _tenant.IsSuperAdmin || e.CompanyId == _tenant.CompanyId);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = _tenant?.UserId;

        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    break;
            }
        }
    }
}
