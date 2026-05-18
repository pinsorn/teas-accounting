using Accounting.Application.Reports;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Accounting.Workers.Jobs;

[DisallowConcurrentExecution]
public sealed class VatRegisterSnapshotJob : IJob
{
    private readonly IVatReportService _report;
    private readonly ILogger<VatRegisterSnapshotJob> _log;

    public VatRegisterSnapshotJob(IVatReportService report, ILogger<VatRegisterSnapshotJob> log)
    {
        _report = report; _log = log;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var bangkok = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Bangkok");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, bangkok);
        var summary = await _report.GetPnd30Async(now.Year, now.Month, ct);

        _log.LogInformation(
            "VAT snapshot {Year}-{Month:D2}: Sales={Sales:N2}, OutputVAT={OutputVat:N2}, Purchase={Purchase:N2}, InputVAT={InputVat:N2}, Net={Net:N2}",
            summary.Year, summary.Month, summary.Sales, summary.OutputVat,
            summary.Purchase, summary.InputVat,
            summary.NetVatPayable - summary.NetVatRefundable);
    }
}
