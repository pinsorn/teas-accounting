using Accounting.Infrastructure;
using Accounting.Workers.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture))
    .ConfigureServices((ctx, services) =>
    {
        services.AddInfrastructure(ctx.Configuration);

        services.AddQuartz(q =>
        {
            // Daily VAT register snapshot — 02:00 Asia/Bangkok
            var vatSnapshot = new JobKey(nameof(VatRegisterSnapshotJob));
            q.AddJob<VatRegisterSnapshotJob>(opts => opts.WithIdentity(vatSnapshot));
            q.AddTrigger(t => t
                .ForJob(vatSnapshot)
                .WithIdentity("vat_snapshot_daily")
                .WithCronSchedule("0 0 2 * * ?", c => c.InTimeZone(
                    TimeZoneInfo.FindSystemTimeZoneById(
                        OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Bangkok"))));

            // ภ.พ.30 deadline alert — every day at 09:00 between day 12 and 15
            var pnd30Alert = new JobKey(nameof(Pnd30DeadlineAlertJob));
            q.AddJob<Pnd30DeadlineAlertJob>(opts => opts.WithIdentity(pnd30Alert));
            q.AddTrigger(t => t
                .ForJob(pnd30Alert)
                .WithIdentity("pnd30_deadline_alert")
                .WithCronSchedule("0 0 9 12-15 * ?"));
        });

        services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);
    })
    .Build();

await host.RunAsync();
