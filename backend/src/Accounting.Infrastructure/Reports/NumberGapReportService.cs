using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Reports;

/// <summary>
/// Reads the <c>tax.v_number_gaps</c> audit view, scoped to the current tenant's
/// <c>company_id</c>. The view itself has no RLS (it spans companies for the auditor),
/// so the company filter is applied here explicitly — never trust it to be empty.
/// </summary>
public sealed class NumberGapReportService : INumberGapReportService
{
    private readonly AccountingDbContext _db;
    private readonly ITenantContext _tenant;

    public NumberGapReportService(AccountingDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    private sealed record Row(string Series, int MissingSeqNo);

    // H1 (specs/fix-duplicate-tax-doc-numbers.md) WP-3 — Tbl/DocNo/Copies/BranchIds map to
    // tax.v_duplicate_doc_numbers' tbl/doc_no/copies/branch_ids columns (EF snake-case convention).
    private sealed record DupRow(string Tbl, string DocNo, int Copies, int[] BranchIds);

    public async Task<NumberGapReport> GetGapsAsync(
        int? year, int? month, string? docType, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        // series shape = MM-YYYY-PREFIX (doc_no minus the trailing -NNNN). Compose the
        // WHERE dynamically and only bind a parameter when the filter is supplied —
        // passing untyped NULL parameters trips Npgsql type inference.
        var args = new List<object> { _tenant.CompanyId };
        var where = "company_id = {0}";

        if (year is { } y)
        {
            where += $" AND series LIKE '%-' || {{{args.Count}}} || '-%'";
            args.Add(y.ToString("D4"));
        }
        if (month is { } m)
        {
            where += $" AND series LIKE {{{args.Count}}} || '-%'";
            args.Add(m.ToString("D2"));
        }
        if (!string.IsNullOrWhiteSpace(docType))
        {
            where += $" AND series LIKE '%-' || {{{args.Count}}}";
            args.Add(docType);
        }

        // Column names must match the snake-case naming convention EF applies to Row
        // (Series→series, MissingSeqNo→missing_seq_no) — select them verbatim, no alias.
        var sql = $"""
            SELECT series, missing_seq_no
            FROM tax.v_number_gaps
            WHERE {where}
            ORDER BY series, missing_seq_no
            """;

        var rows = await _db.Database
            .SqlQueryRaw<Row>(sql, args.ToArray())
            .ToListAsync(ct);

        // tax.v_duplicate_doc_numbers has NO series column (F21 — no numeric cast, group on the
        // string doc_no itself), so the same year/month/docType filters apply to doc_no directly —
        // it starts with the identical "MM-YYYY-" prefix and ends "...-PREFIX[-SUB]-NNNN".
        var dupArgs = new List<object> { _tenant.CompanyId };
        var dupWhere = "company_id = {0}";

        if (year is { } dy)
        {
            dupWhere += $" AND doc_no LIKE '%-' || {{{dupArgs.Count}}} || '-%'";
            dupArgs.Add(dy.ToString("D4"));
        }
        if (month is { } dm)
        {
            dupWhere += $" AND doc_no LIKE {{{dupArgs.Count}}} || '-%'";
            dupArgs.Add(dm.ToString("D2"));
        }
        if (!string.IsNullOrWhiteSpace(docType))
        {
            dupWhere += $" AND doc_no LIKE '%-' || {{{dupArgs.Count}}} || '-%'";
            dupArgs.Add(docType);
        }

        var dupSql = $"""
            SELECT tbl, doc_no, copies, branch_ids
            FROM tax.v_duplicate_doc_numbers
            WHERE {dupWhere}
            ORDER BY tbl, doc_no
            """;

        var dupRows = await _db.Database
            .SqlQueryRaw<DupRow>(dupSql, dupArgs.ToArray())
            .ToListAsync(ct);

        return new NumberGapReport(year, month, docType,
            rows.Select(r => new NumberGapRow(r.Series, r.MissingSeqNo)).ToList(),
            dupRows.Select(r => new NumberDuplicateRow(r.Tbl, r.DocNo, r.Copies, r.BranchIds)).ToList());
    }
}
