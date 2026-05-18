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

        return new NumberGapReport(year, month, docType,
            rows.Select(r => new NumberGapRow(r.Series, r.MissingSeqNo)).ToList());
    }
}
