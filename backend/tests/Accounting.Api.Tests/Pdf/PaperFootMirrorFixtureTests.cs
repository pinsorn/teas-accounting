using System.IO;
using System.Linq;
using System.Text.Json;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Pdf;
using Accounting.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Pdf;

/// <summary>
/// PLAN-test-hardening.md WS-4 / C4 — mirror-contract test for the foot math (the declared
/// mirror pair PaperFootPlan.cs / PaperFoot.tsx). The 700/850 drift: FE and BE each read
/// PaperSummary.Total with the OPPOSITE meaning, and each side's OWN tests passed because each
/// side tested against its own (mis-)understanding — no test compared them to EACH OTHER. This
/// test emits the REAL PaperFootPlan.Build(...) Grand/Net output for a small, representative set
/// of PaperSummary cases to a COMMITTED JSON fixture (frontend/fixtures/paper-foot-plan.json); a
/// vitest test on the FE side (frontend/components/paper/PaperFoot.test.ts) reads the SAME
/// fixture and asserts computeFootTotals(summary) — PaperFoot's own extracted math — produces
/// identical grand/net values. Both sides now test against a SHARED artifact instead of against
/// themselves; the 700/850 class of drift can never silently return without failing HERE first.
/// No DB — pure C# against PaperFootPlan.Build, cheap and always runs.
/// </summary>
public sealed class PaperFootMirrorFixtureTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static PaperSummary S(bool showVat, decimal total, decimal? wht = null,
        decimal subtotal = 0, decimal vat = 0) =>
        new(Subtotal: subtotal, Discount: null, BeforeVat: null, Vat: vat, Total: total,
            VatRate: 7m, ShowVat: showVat, Wht: wht);

    private sealed record Case(string Name, PaperSummary Summary, decimal Grand, decimal Net, bool HasWht);

    [Fact]
    public void Emits_the_shared_grand_net_fixture_and_pins_the_expected_values()
    {
        var cases = new[]
        {
            new Case("nonVat_noWht", S(showVat: false, total: 1000m),
                Grand: 1000m, Net: 1000m, HasWht: false),
            new Case("vat_noWht", S(showVat: true, total: 1070m, subtotal: 1000m, vat: 70m),
                Grand: 1070m, Net: 1070m, HasWht: false),
            // The exact regression case for the 700/850 drift: net(Total)=850, wht=150 → grand=1000.
            new Case("nonVat_withWht_850net_150wht", S(showVat: false, total: 850m, wht: 150m),
                Grand: 1000m, Net: 850m, HasWht: true),
            new Case("vat_withWht", S(showVat: true, total: 1040m, wht: 30m, subtotal: 1000m, vat: 70m),
                Grand: 1070m, Net: 1040m, HasWht: true),
        };

        // Pin each case's expected Grand/Net against PaperFootPlan.Build's OWN output (not
        // hand-computed) — if Build()'s math ever drifts, this fails here, not silently.
        foreach (var c in cases)
        {
            var rows = PaperFootPlan.Build(c.Summary);
            var grand = rows.Single(r => r.Line == FootLine.GrandTotal).Value;
            var net = c.HasWht ? rows.Single(r => r.Line == FootLine.Net).Value : grand;

            grand.Should().Be(c.Grand, $"{c.Name}: PaperFootPlan.Build's Grand Total");
            net.Should().Be(c.Net, $"{c.Name}: PaperFootPlan.Build's Net");
        }

        var fixture = new
        {
            cases = cases.Select(c => new
            {
                name = c.Name,
                summary = new
                {
                    subtotal = c.Summary.Subtotal,
                    discount = c.Summary.Discount,
                    beforeVat = c.Summary.BeforeVat,
                    vat = c.Summary.Vat,
                    total = c.Summary.Total,
                    vatRate = c.Summary.VatRate,
                    showVat = c.Summary.ShowVat,
                    wht = c.Summary.Wht,
                    nonTaxable = c.Summary.NonTaxable,
                },
                grandTotal = c.Grand,
                netTotal = c.Net,
                hasWht = c.HasWht,
            }),
        };

        var json = JsonSerializer.Serialize(fixture, JsonOpts);
        var path = Path.Combine(RbacTestPaths.RepoRoot(), "frontend", "fixtures", "paper-foot-plan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json + "\n");
    }
}
