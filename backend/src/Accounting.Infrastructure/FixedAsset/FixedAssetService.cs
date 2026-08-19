using Accounting.Application.Abstractions;
using Accounting.Application.FixedAsset;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Numbering;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.FixedAsset;

/// <summary>
/// Cycle D (specs/fixed-assets.md) — asset register + straight-line monthly depreciation +
/// disposal/write-off. Acquisition posts NO journal entry (FA-A — the linked Vendor Invoice
/// already booked the cost). Depreciation and disposal post ONLY via the EXISTING
/// <see cref="IGlPostingService.PostManualEntryAsync"/> — zero edits to GlPostingService.
/// </summary>
public sealed class FixedAssetService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, IGlPostingService gl, IPeriodCloseService period,
    IOptions<GlAccountsOptions> accountsOptions)
    : IFixedAssetService
{
    private const string FaPrefix = "FA";
    private readonly GlAccountsOptions _accounts = accountsOptions.Value;

    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    /// <summary>Mirrors ExpenseClaimService.SaveGuardedAsync (FOOTGUN 8) — every FA state
    /// transition increments Version, so a losing concurrent write actually throws
    /// DbUpdateConcurrencyException (unlike PV's inert token); translated to a
    /// ".locked_mismatch" code, which DomainExceptionMiddleware's suffix rule maps to 409.</summary>
    private async Task SaveGuardedAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("fixed_asset.locked_mismatch",
                "This fixed asset was changed by someone else. Reload and try again.");
        }
    }

    private async Task<Domain.Entities.FixedAsset.FixedAsset> LoadAsync(long id, CancellationToken ct) =>
        await db.FixedAssets.FirstOrDefaultAsync(a => a.FixedAssetId == id, ct)
        ?? throw new DomainException("fixed_asset.not_found", $"Fixed Asset {id} not found.");

    /// <summary>Same resolve-by-code convention as GlPostingService.ResolveAccountIdAsync
    /// (private there — this is a small standalone copy, not a GlPostingService edit).</summary>
    private async Task<long> ResolveAccountIdAsync(int companyId, string code, CancellationToken ct)
    {
        var account = await db.ChartOfAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AccountCode == code, ct);
        if (account is null)
            throw new DomainException("gl.account_missing",
                $"Configured GL account '{code}' is missing from chart_of_accounts for company {companyId}. " +
                "Seed the CoA or update GlAccounts in appsettings.");
        return account.AccountId;
    }

    /// <summary>Codex review finding #5 (2026-07-10) — mirrors BankReconciliationService
    /// .CreateJournalAsync's contra-account check: exists tenant-scoped, active, non-header, AND
    /// (new here) the CORRECT account type for the slot — a client override could otherwise pick
    /// a Revenue/Liability account for the asset-cost line and silently corrupt the trial
    /// balance's Dr/Cr classification.</summary>
    private async Task<long> EnsureAccountAsync(long accountId, int companyId, AccountType expectedType, CancellationToken ct)
    {
        var account = await db.ChartOfAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountId == accountId && a.CompanyId == companyId, ct)
            ?? throw new DomainException("fixed_asset.account_invalid",
                $"Account {accountId} not found for company {companyId}.");
        if (!account.IsActive)
            throw new DomainException("fixed_asset.account_invalid",
                $"Account {accountId} is not active.");
        if (account.IsHeader)
            throw new DomainException("fixed_asset.account_invalid",
                $"Account {accountId} is a header account — pick a postable (non-header) account.");
        if (account.AccountType != expectedType)
            throw new DomainException("fixed_asset.account_invalid",
                $"Account {accountId} must be a {expectedType} account (got {account.AccountType}).");
        return accountId;
    }

    /// <summary>Codex review finding #5b (2026-07-10) — a client-supplied VendorInvoiceId was
    /// stored/posted with no existence/tenant check (a DB FK accepts another company's row id,
    /// since journal_lines.account_id-style FK trust boundaries are all app-enforced here, not
    /// DB-enforced). Required when non-null; makes the MCP create_fixed_asset_draft Description
    /// ("the service applies its own check") true.</summary>
    private async Task EnsureVendorInvoiceAsync(long? vendorInvoiceId, int companyId, CancellationToken ct)
    {
        if (vendorInvoiceId is not { } id) return;
        var exists = await db.VendorInvoices.AsNoTracking()
            .AnyAsync(v => v.VendorInvoiceId == id && v.CompanyId == companyId, ct);
        if (!exists)
            throw new DomainException("fixed_asset.vendor_invoice_invalid",
                $"Vendor Invoice {id} not found for company {companyId}.");
    }

    /// <summary>§1 design note — the 3 accounts are per-asset, defaulted from GlAccountsOptions,
    /// editable while Draft (override ?? default, never re-resolved after Activate freezes them).</summary>
    private async Task<(long AssetCostAccountId, long AccumDepAccountId, long DepExpenseAccountId)> ResolveAssetAccountsAsync(
        int companyId, long? assetCostOverride, long? accumDepOverride, long? depExpenseOverride, CancellationToken ct)
    {
        var assetCost = assetCostOverride is { } a1
            ? await EnsureAccountAsync(a1, companyId, AccountType.Asset, ct)
            : await ResolveAccountIdAsync(companyId, _accounts.FixedAssetCostAccount, ct);
        var accumDep = accumDepOverride is { } a2
            ? await EnsureAccountAsync(a2, companyId, AccountType.Asset, ct)
            : await ResolveAccountIdAsync(companyId, _accounts.AccumulatedDepreciationAccount, ct);
        var depExpense = depExpenseOverride is { } a3
            ? await EnsureAccountAsync(a3, companyId, AccountType.Expense, ct)
            : await ResolveAccountIdAsync(companyId, _accounts.DepreciationExpenseAccount, ct);
        return (assetCost, accumDep, depExpense);
    }

    // ── Draft / activate / cancel ───────────────────────────────────────────────────

    public async Task<long> CreateDraftAsync(CreateFixedAssetRequest req, CancellationToken ct)
    {
        Auth();
        await EnsureVendorInvoiceAsync(req.VendorInvoiceId, tenant.CompanyId, ct);
        var (assetCostId, accumDepId, depExpenseId) = await ResolveAssetAccountsAsync(
            tenant.CompanyId, req.AssetCostAccountId, req.AccumDepAccountId, req.DepExpenseAccountId, ct);

        var depreciableBase = req.Cost - req.SalvageValue;
        var monthlyAmount = Math.Round(depreciableBase / req.UsefulLifeMonths, 2, MidpointRounding.AwayFromZero);

        var asset = new Domain.Entities.FixedAsset.FixedAsset
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId, BusinessUnitId = req.BusinessUnitId,
            Name = req.Name, Category = req.Category, AcquireDate = req.AcquireDate,
            VendorInvoiceId = req.VendorInvoiceId, Cost = req.Cost, SalvageValue = req.SalvageValue,
            UsefulLifeMonths = req.UsefulLifeMonths, DepreciableBase = depreciableBase, MonthlyAmount = monthlyAmount,
            DepreciationStartDate = req.DepreciationStartDate ?? req.AcquireDate,
            AssetCostAccountId = assetCostId, AccumDepAccountId = accumDepId, DepExpenseAccountId = depExpenseId,
            Notes = req.Notes,
        };
        db.FixedAssets.Add(asset);
        await db.SaveChangesAsync(ct);
        return asset.FixedAssetId;
    }

    public async Task UpdateDraftAsync(long id, UpdateFixedAssetRequest req, CancellationToken ct)
    {
        Auth();
        var asset = await LoadAsync(id, ct);
        if (asset.Status != FixedAssetStatus.Draft)
            throw new DomainException("fixed_asset.not_editable",
                $"Cannot edit a fixed asset in status {asset.Status}.");

        await EnsureVendorInvoiceAsync(req.VendorInvoiceId, asset.CompanyId, ct);
        var (assetCostId, accumDepId, depExpenseId) = await ResolveAssetAccountsAsync(
            asset.CompanyId, req.AssetCostAccountId, req.AccumDepAccountId, req.DepExpenseAccountId, ct);

        asset.Name = req.Name;
        asset.Category = req.Category;
        asset.AcquireDate = req.AcquireDate;
        asset.VendorInvoiceId = req.VendorInvoiceId;
        asset.Cost = req.Cost;
        asset.SalvageValue = req.SalvageValue;
        asset.UsefulLifeMonths = req.UsefulLifeMonths;
        // Recomputed on every Draft edit (§1 design note); frozen at Activate.
        asset.DepreciableBase = req.Cost - req.SalvageValue;
        asset.MonthlyAmount = Math.Round(asset.DepreciableBase / req.UsefulLifeMonths, 2, MidpointRounding.AwayFromZero);
        asset.DepreciationStartDate = req.DepreciationStartDate ?? req.AcquireDate;
        asset.AssetCostAccountId = assetCostId;
        asset.AccumDepAccountId = accumDepId;
        asset.DepExpenseAccountId = depExpenseId;
        asset.BusinessUnitId = req.BusinessUnitId;
        asset.Notes = req.Notes;

        // Codex review finding #6 (2026-07-10) — every OTHER state transition (Activate/Cancel/
        // Dispose/WriteOff) increments Version + saves via SaveGuardedAsync; a Draft edit racing
        // a concurrent Activate was the one path that skipped both, so it would silently
        // overwrite an already-activated asset instead of 409-ing.
        asset.Version++;
        await SaveGuardedAsync(ct);
    }

    public async Task ActivateAsync(long id, CancellationToken ct)
    {
        Auth();
        var asset = await LoadAsync(id, ct);
        var activateDate = clock.TodayInBangkok();
        var activatedAt = clock.UtcNow;
        // FA-A — no JE posted here. The linked Vendor Invoice (or an opening-balance JE)
        // already booked the cost; this only assigns the doc number and starts the clock.
        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); a genuine optimistic-concurrency conflict (unrelated to
        // doc_no) still surfaces as the same clean domain error SaveGuardedAsync gives elsewhere.
        try
        {
            await NumberedDocumentWriter.AllocateAndSaveAsync(
                db,
                c => numbers.NextAsync(asset.CompanyId, FaPrefix, subPrefix: null, activateDate, c),
                (v, first) => { if (first) asset.Activate(v.Value, tenant.UserId, activatedAt); else asset.DocNo = v.Value; },
                ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("fixed_asset.locked_mismatch",
                "This fixed asset was changed by someone else. Reload and try again.");
        }
    }

    public async Task CancelAsync(long id, CancellationToken ct)
    {
        Auth();
        var asset = await LoadAsync(id, ct);
        asset.Cancel();
        await SaveGuardedAsync(ct);
    }

    // ── Dispose / write-off (§5) ────────────────────────────────────────────────────

    public Task<DisposeFixedAssetResult> DisposeAsync(long id, DisposeFixedAssetRequest req, CancellationToken ct) =>
        DisposeCoreAsync(id, req.DisposalDate, req.Proceeds, req.VatAmount ?? 0m, req.BuyerName, writeOffReason: null, ct);

    public Task<DisposeFixedAssetResult> WriteOffAsync(long id, WriteOffFixedAssetRequest req, CancellationToken ct) =>
        DisposeCoreAsync(id, req.Date, proceeds: 0m, vat: 0m, buyerName: null, writeOffReason: req.Reason, ct);

    /// <summary>Shared money mechanics for Dispose and WriteOff (§5.1/§5.2 — WriteOff is Dispose
    /// with proceeds/VAT forced to 0, so the "drop any zero line" rule already produces the
    /// exact §5.2 JE shape with zero special-casing needed here).</summary>
    private async Task<DisposeFixedAssetResult> DisposeCoreAsync(
        long id, DateOnly date, decimal proceeds, decimal vat, string? buyerName, string? writeOffReason,
        CancellationToken ct)
    {
        Auth();
        await period.EnsureOpenAsync(date, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Re-load + re-guard inside the tx (mirrors ExpenseClaimService.PayAsync / FA §6 race
        // safety) — the loser of a concurrent double-dispose race is caught by the Version
        // optimistic-concurrency check at SaveGuardedAsync below.
        var asset = await LoadAsync(id, ct);
        if (asset.Status != FixedAssetStatus.Active)
            throw new DomainException("fixed_asset.not_active",
                $"Cannot {(writeOffReason is null ? "dispose" : "write off")} a fixed asset in status {asset.Status}.");

        // L3-9 (PLAN-fix-findings-r2.md U5) — a disposal/write-off dated before the asset's own
        // AcquireDate or DepreciationStartDate is a data-integrity gap (the asset's own register
        // row says it did not exist yet, or had not started depreciating yet, on that date); the
        // original bug posted a real balanced JE for exactly this case (findings-r2/findings-leg3.md
        // L3-9). Same-day (== ) is the inclusive boundary, not refused.
        if (date < asset.AcquireDate)
            throw new DomainException("fixed_asset.disposal_date_invalid",
                $"{(writeOffReason is null ? "Disposal" : "Write-off")} date {date:yyyy-MM-dd} cannot be before " +
                $"the asset's acquisition date {asset.AcquireDate:yyyy-MM-dd}.");
        if (date < asset.DepreciationStartDate)
            throw new DomainException("fixed_asset.disposal_date_invalid",
                $"{(writeOffReason is null ? "Disposal" : "Write-off")} date {date:yyyy-MM-dd} cannot be before " +
                $"the asset's depreciation start date {asset.DepreciationStartDate:yyyy-MM-dd}.");

        // FA-F — reads the asset's CURRENT accumulated depreciation; no auto catch-up run.
        var nbv = asset.Cost - asset.AccumulatedDepreciation;
        var gainLoss = proceeds - nbv;
        var cashReceived = proceeds + vat;

        var lines = new List<(long AccountId, decimal Debit, decimal Credit)>();
        if (cashReceived > 0m)
            lines.Add((await ResolveAccountIdAsync(asset.CompanyId, _accounts.CashAccount, ct), cashReceived, 0m));
        if (asset.AccumulatedDepreciation > 0m)
            lines.Add((asset.AccumDepAccountId, asset.AccumulatedDepreciation, 0m));
        lines.Add((asset.AssetCostAccountId, 0m, asset.Cost));
        if (vat > 0m)
            lines.Add((await ResolveAccountIdAsync(asset.CompanyId, _accounts.OutputVatAccount, ct), 0m, vat));
        if (gainLoss > 0m)
            lines.Add((await ResolveAccountIdAsync(asset.CompanyId, _accounts.GainOnAssetDisposalAccount, ct), 0m, gainLoss));
        else if (gainLoss < 0m)
            lines.Add((await ResolveAccountIdAsync(asset.CompanyId, _accounts.LossOnAssetDisposalAccount, ct), -gainLoss, 0m));

        var description = writeOffReason is null
            ? $"จำหน่ายสินทรัพย์ {asset.DocNo} / Dispose asset {asset.DocNo}"
            : $"ตัดจำหน่ายสินทรัพย์ {asset.DocNo} / Write off asset {asset.DocNo}";
        var journalId = await gl.PostManualEntryAsync(
            asset.CompanyId, asset.BranchId, date, description, asset.DocNo, lines, ct);

        var userId = tenant.UserId;
        var now = clock.UtcNow;
        if (writeOffReason is null)
            asset.Dispose(date, proceeds, vat, gainLoss, buyerName, journalId, userId, now);
        else
            asset.WriteOff(date, writeOffReason, -gainLoss, journalId, userId, now);

        await SaveGuardedAsync(ct);
        await tx.CommitAsync(ct);

        return new DisposeFixedAssetResult(asset.FixedAssetId, nbv, gainLoss, journalId, asset.Status.ToString());
    }

    // ── Depreciation engine (§3) — GETS FABLE LINE-BY-LINE REVIEW ───────────────────

    public async Task<DepreciationRunResult> GenerateDepreciationAsync(int year, int month, CancellationToken ct)
    {
        Auth();
        var runDate = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        // FA-D — depreciation posts into an OPEN month only.
        await period.EnsureOpenAsync(runDate, ct);

        // FA-E step 2 — belt-and-braces idempotent early return (no new JE).
        var existing = await db.DepreciationRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.PeriodYear == year && r.PeriodMonth == month, ct);
        if (existing is not null)
            return new DepreciationRunResult(existing.DepreciationRunId, year, month, existing.RunDate,
                existing.TotalAmount, existing.AssetCount, existing.JournalEntryId, AlreadyExisted: true);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var branchId = await db.Branches
            .Where(b => b.CompanyId == tenant.CompanyId && b.IsHeadOffice)
            .Select(b => (int?)b.BranchId).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("fixed_asset.hq_branch_missing",
                "No head-office branch found for this company.");

        var assets = await db.FixedAssets
            .Where(a => a.Status == FixedAssetStatus.Active
                     && a.DepreciationStartDate <= runDate
                     && a.AccumulatedDepreciation < a.DepreciableBase)
            .OrderBy(a => a.FixedAssetId)
            .ToListAsync(ct);

        // §3.2 grandfather resolve — legacy rows (MonthsDepreciated NULL, predating this
        // feature) derive their units-charged from their posted run-line count. Self-healing:
        // after an asset's first post-deploy charge MonthsDepreciated is non-null, so this list
        // (and the query below) empties out for it.
        var legacyIds = assets.Where(a => a.MonthsDepreciated is null).Select(a => a.FixedAssetId).ToList();
        var priorLineCounts = legacyIds.Count == 0
            ? new Dictionary<long, decimal>()
            : await db.DepreciationRunLines.AsNoTracking()
                .Where(l => legacyIds.Contains(l.FixedAssetId))
                .GroupBy(l => l.FixedAssetId)
                .Select(g => new { g.Key, N = (decimal)g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.N, ct);

        var runLines = new List<Domain.Entities.FixedAsset.DepreciationRunLine>();
        var byExpenseAcct = new Dictionary<long, decimal>();
        var byAccumAcct = new Dictionary<long, decimal>();
        decimal total = 0m;

        foreach (var asset in assets)
        {
            var remaining = asset.DepreciableBase - asset.AccumulatedDepreciation;
            var life = (decimal)asset.UsefulLifeMonths;

            // Units of LIFE already charged to this asset. NULL = a row that predates this feature
            // (or predates its own first charge) -> derive from its posted run-line count, where
            // every legacy line is one full month by definition (no proration existed). See §3.4 —
            // this is why the migration needs no DML (RLS would have silently no-op'd a backfill
            // in prod).
            var unitsBefore = asset.MonthsDepreciated ?? priorLineCounts.GetValueOrDefault(asset.FixedAssetId, 0m);

            // First charge is day-prorated (§3.1); every later charge is one whole month; the
            // last one is trimmed so the total is EXACTLY `life` units — that trim is what
            // replaces the old calendar plug, and it can never absorb a skipped month because it
            // is bounded by one unit.
            var delta = Math.Min(unitsBefore == 0m
                    ? Domain.Entities.FixedAsset.FixedAsset.FirstMonthFraction(asset.DepreciationStartDate)
                    : 1m,
                life - unitsBefore);
            var unitsAfter = unitsBefore + delta;

            // The FINAL charge is the remaining balance -> sum-to-exact holds by construction, in
            // BOTH rounding directions, with no dribble month and no multi-month lump.
            var charge = unitsAfter >= life
                ? remaining
                : Math.Round(asset.MonthlyAmount * delta, 2, MidpointRounding.AwayFromZero);

            // I4 — an eligible asset must never produce a 0.00 charge: a run with no lines creates
            // no run row, and PeriodCloseService's hook would then refuse to close that month
            // forever (no in-app exit). Self-correcting: the final charge absorbs the satang.
            if (charge < 0.01m && remaining >= 0.01m) charge = 0.01m;
            charge = Math.Min(charge, remaining);
            if (charge <= 0m) continue;

            asset.MonthsDepreciated = unitsAfter;
            asset.AccumulatedDepreciation += charge;
            asset.Version++;
            total += charge;

            runLines.Add(new Domain.Entities.FixedAsset.DepreciationRunLine
            {
                CompanyId = asset.CompanyId, FixedAssetId = asset.FixedAssetId,
                Amount = charge, AccumulatedAfter = asset.AccumulatedDepreciation,
            });

            byExpenseAcct[asset.DepExpenseAccountId] = byExpenseAcct.GetValueOrDefault(asset.DepExpenseAccountId) + charge;
            byAccumAcct[asset.AccumDepAccountId] = byAccumAcct.GetValueOrDefault(asset.AccumDepAccountId) + charge;
        }

        if (runLines.Count == 0)
        {
            // Nothing due this month (no active assets, or none owe a charge). Do NOT post an
            // empty/unbalanced JE and do NOT create a run row — PostManualEntryAsync's balance
            // check requires totalD>0, and an empty run would permanently occupy the
            // (company,year,month) unique slot for a month where nothing actually happened.
            // Not explicitly covered by §3.1's pseudocode (which assumes assets are always due);
            // this is the implementer's minimal defensive read — see report.
            return new DepreciationRunResult(null, year, month, runDate, 0m, 0, null, AlreadyExisted: false);
        }

        var lines = new List<(long AccountId, decimal Debit, decimal Credit)>();
        foreach (var kv in byExpenseAcct) lines.Add((kv.Key, kv.Value, 0m));
        foreach (var kv in byAccumAcct) lines.Add((kv.Key, 0m, kv.Value));

        var run = new Domain.Entities.FixedAsset.DepreciationRun
        {
            CompanyId = tenant.CompanyId, BranchId = branchId,
            PeriodYear = year, PeriodMonth = month, RunDate = runDate,
            TotalAmount = total, AssetCount = runLines.Count,
            CreatedBy = tenant.UserId,
            Lines = runLines,
        };

        long journalId;
        try
        {
            // NOTE: gl.PostManualEntryAsync's own SaveChangesAsync flushes the WHOLE change
            // tracker on this shared DbContext — including the AccumulatedDepreciation/Version
            // mutations applied to `assets` above. So in a genuine two-transaction race, the
            // FixedAsset.Version optimistic-concurrency check (F8) actually fires HERE, on the
            // loser's stale in-memory Version, before the DepreciationRun unique-index insert is
            // even attempted. Both failure modes are the SAME race (FA-E) and map to the same
            // domain error; the whole ambient transaction (including the JE just posted) rolls
            // back on either — the loser posts no surviving JE.
            journalId = await gl.PostManualEntryAsync(
                tenant.CompanyId, branchId, runDate,
                $"ค่าเสื่อมราคา {year}-{month:D2} / Depreciation {year}-{month:D2}",
                $"DEP-{year}{month:D2}", lines, ct);

            run.JournalEntryId = journalId;
            db.DepreciationRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("depreciation.already_posted",
                $"Depreciation for {year}-{month:D2} was already posted by a concurrent request.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // FA-E — the unique (company_id, period_year, period_month) index is the concurrent-
            // race backstop for the (rarer) case where both loaders raced past the Version check.
            throw new DomainException("depreciation.already_posted",
                $"Depreciation for {year}-{month:D2} was already posted by a concurrent request.");
        }

        await tx.CommitAsync(ct);
        return new DepreciationRunResult(run.DepreciationRunId, year, month, runDate, total, runLines.Count, journalId, AlreadyExisted: false);
    }

    public async Task<IReadOnlyList<DepreciationRunListItem>> ListDepreciationRunsAsync(CancellationToken ct)
    {
        Auth();
        return await db.DepreciationRuns.AsNoTracking()
            .OrderByDescending(r => r.PeriodYear).ThenByDescending(r => r.PeriodMonth)
            .Select(r => new DepreciationRunListItem(
                r.DepreciationRunId, r.PeriodYear, r.PeriodMonth, r.RunDate, r.TotalAmount, r.AssetCount, r.JournalEntryId))
            .ToListAsync(ct);
    }

    public async Task<DepreciationRunDetail?> GetDepreciationRunAsync(int year, int month, CancellationToken ct)
    {
        Auth();
        var run = await db.DepreciationRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.PeriodYear == year && r.PeriodMonth == month, ct);
        if (run is null) return null;

        var lines = await (from l in db.DepreciationRunLines.AsNoTracking()
                            join a in db.FixedAssets.AsNoTracking() on l.FixedAssetId equals a.FixedAssetId
                            where l.DepreciationRunId == run.DepreciationRunId
                            select new DepreciationRunLineItem(a.FixedAssetId, a.DocNo, a.Name, l.Amount, l.AccumulatedAfter))
                           .ToListAsync(ct);

        return new DepreciationRunDetail(run.DepreciationRunId, run.PeriodYear, run.PeriodMonth, run.RunDate,
            run.TotalAmount, run.AssetCount, run.JournalEntryId, lines);
    }

    // ── Read/list (§9.1/9.2) ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FixedAssetListItem>> ListAsync(
        string? status, string? category, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        Auth();
        var q = db.FixedAssets.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<FixedAssetStatus>(status, ignoreCase: true, out var st))
            q = q.Where(a => a.Status == st);
        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(a => a.Category == category);
        if (from is { } f) q = q.Where(a => a.AcquireDate >= f);
        if (to is { } t) q = q.Where(a => a.AcquireDate <= t);

        return await q.OrderByDescending(a => a.AcquireDate).ThenByDescending(a => a.FixedAssetId)
            .Select(a => new FixedAssetListItem(a.FixedAssetId, a.DocNo, a.Name, a.Category, a.AcquireDate,
                a.Cost, a.AccumulatedDepreciation, a.Cost - a.AccumulatedDepreciation, a.Status.ToString()))
            .ToListAsync(ct);
    }

    public async Task<FixedAssetDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        Auth();
        var asset = await db.FixedAssets.AsNoTracking().FirstOrDefaultAsync(a => a.FixedAssetId == id, ct);
        if (asset is null) return null;

        var runLines = await (from l in db.DepreciationRunLines.AsNoTracking()
                               join r in db.DepreciationRuns.AsNoTracking() on l.DepreciationRunId equals r.DepreciationRunId
                               where l.FixedAssetId == id
                               orderby r.PeriodYear, r.PeriodMonth
                               select new DepreciationRunLineDetail(
                                   r.DepreciationRunId, r.PeriodYear, r.PeriodMonth, r.RunDate, l.Amount, l.AccumulatedAfter))
                              .ToListAsync(ct);

        return new FixedAssetDetail(
            asset.FixedAssetId, asset.DocNo, asset.Name, asset.Category, asset.AcquireDate, asset.VendorInvoiceId,
            asset.Cost, asset.SalvageValue, asset.UsefulLifeMonths, asset.DepreciableBase, asset.MonthlyAmount,
            asset.DepreciationStartDate, asset.AssetCostAccountId, asset.AccumDepAccountId, asset.DepExpenseAccountId,
            asset.AccumulatedDepreciation, asset.Cost - asset.AccumulatedDepreciation, asset.Status.ToString(),
            asset.DisposalDate, asset.DisposalProceeds, asset.DisposalVatAmount, asset.DisposalGainLoss,
            asset.DisposalBuyerName, asset.DisposalJournalEntryId, asset.WriteoffReason, asset.Notes,
            asset.BusinessUnitId, asset.Version, runLines, asset.MonthsDepreciated);
    }

    // ── Reports (§7) — query-only, no posting ───────────────────────────────────────

    public async Task<IReadOnlyList<FixedAssetRegisterItem>> GetRegisterReportAsync(DateOnly asOf, CancellationToken ct)
    {
        Auth();
        // Active + Disposed per §7.1; WrittenOff included too (a written-off asset's cost/
        // depreciation history is still audit-relevant — a conservative reading of "include
        // Active + Disposed", not a narrower one).
        var assets = await db.FixedAssets.AsNoTracking()
            .Where(a => a.Status == FixedAssetStatus.Active || a.Status == FixedAssetStatus.Disposed
                     || a.Status == FixedAssetStatus.WrittenOff)
            .Where(a => a.AcquireDate <= asOf)
            .ToListAsync(ct);
        var assetIds = assets.Select(a => a.FixedAssetId).ToList();

        var accumByAsset = await (from l in db.DepreciationRunLines.AsNoTracking()
                                   join r in db.DepreciationRuns.AsNoTracking() on l.DepreciationRunId equals r.DepreciationRunId
                                   where assetIds.Contains(l.FixedAssetId) && r.RunDate <= asOf
                                   group l by l.FixedAssetId into g
                                   select new { FixedAssetId = g.Key, Total = g.Sum(x => x.Amount) })
                                  .ToDictionaryAsync(x => x.FixedAssetId, x => x.Total, ct);

        return assets
            .Select(a =>
            {
                var accum = accumByAsset.GetValueOrDefault(a.FixedAssetId, 0m);
                return new FixedAssetRegisterItem(a.FixedAssetId, a.DocNo, a.Name, a.Category, a.AcquireDate,
                    a.Cost, accum, a.Cost - accum, a.Status.ToString(), a.DisposalDate);
            })
            .OrderBy(x => x.FixedAssetId)
            .ToList();
    }

    public async Task<IReadOnlyList<AccumulatedDepreciationReportItem>> GetAccumulatedDepreciationReportAsync(
        int year, CancellationToken ct)
    {
        Auth();
        var rows = await (from l in db.DepreciationRunLines.AsNoTracking()
                           join r in db.DepreciationRuns.AsNoTracking() on l.DepreciationRunId equals r.DepreciationRunId
                           join a in db.FixedAssets.AsNoTracking() on l.FixedAssetId equals a.FixedAssetId
                           where r.PeriodYear == year
                           select new { a.FixedAssetId, a.DocNo, a.Name, r.PeriodMonth, l.Amount })
                          .ToListAsync(ct);

        return rows
            .GroupBy(x => new { x.FixedAssetId, x.DocNo, x.Name })
            .Select(g => new AccumulatedDepreciationReportItem(
                g.Key.FixedAssetId, g.Key.DocNo, g.Key.Name,
                g.Select(x => new MonthlyCharge(x.PeriodMonth, x.Amount)).OrderBy(m => m.Month).ToList(),
                g.Sum(x => x.Amount)))
            .OrderBy(x => x.FixedAssetId)
            .ToList();
    }
}
