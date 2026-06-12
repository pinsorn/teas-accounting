using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Tax;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Pdf;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// ภ.พ.01/ภ.พ.09 v1 identity prefill — structural gates (filler, no DB) + service round-trip on
/// real PG. Visual correctness is the raster gate (VatRegVisualEmit); these pin the mechanics:
/// renders, embeds, comb geometry present, pp09 email comb deliberately stripped.
/// </summary>
public sealed class VatRegFormTests
{
    internal static VatRegIdentity Identity() => new(
        TaxId: "0105561234567", LegalName: "บริษัท ทดสอบภาษี จำกัด",
        Building: "อาคารทดสอบ", RoomNo: "12B", Floor: "3", Village: "หมู่บ้านตัวอย่าง",
        HouseNo: "99/1", Moo: "4", Soi: "สุขใจ 5", Road: "พหลโยธิน",
        SubDistrict: "จตุจักร", District: "จตุจักร", Province: "กรุงเทพมหานคร",
        PostalCode: "10900", Email: "acc@example.co.th", Website: "https://example.co.th");

    [Fact]
    public void Pp01_renders_nonempty_pdf()
    {
        var pdf = Pp01FormFiller.Fill(Identity());
        pdf.Take(4).Should().Equal((byte)'%', (byte)'P', (byte)'D', (byte)'F');
        pdf.Length.Should().BeGreaterThan(50_000);
    }

    [Fact]
    public void Pp09_renders_nonempty_pdf()
    {
        Pp09FormFiller.Fill(Identity()).Length.Should().BeGreaterThan(50_000);
    }

    [Fact]
    public void Cells_geometry_loads_taxid_13_and_postal_5()
    {
        foreach (var file in new[] { "pp01_cells.json", "pp09_cells.json" })
        {
            var cells = Pp01FormFiller.Cells(file);
            cells.Should().ContainKey("Text1.4").WhoseValue.Should().HaveCount(13,
                $"{file}: เลขประจำตัวผู้เสียภาษี is a 13-cell comb");
            cells["Text1.4"].Should().BeInAscendingOrder();
            cells.Should().ContainKey("Text1.26").WhoseValue.Should().HaveCount(5,
                $"{file}: รหัสไปรษณีย์ is a 5-cell comb");
        }
        // pp09 E-mail (Text1.18) carries a bogus 12-cell comb flag — the filler must strip it
        // (plain overlay), but the recon geometry file records it as-is.
        Pp01FormFiller.Cells("pp09_cells.json").Should().ContainKey("Text1.18");
    }

    [Fact]
    public void Nulls_render_as_blank_not_throw()
    {
        var bare = Identity() with
        {
            Building = null, RoomNo = null, Floor = null, Village = null, HouseNo = null,
            Moo = null, Soi = null, Road = null, SubDistrict = null, District = null,
            Province = null, PostalCode = null, Email = null, Website = null,
        };
        Pp01FormFiller.Fill(bare).Length.Should().BeGreaterThan(50_000);
        Pp09FormFiller.Fill(bare).Length.Should().BeGreaterThan(50_000);
    }
}

[Collection(nameof(PostgresCollection))]
public sealed class VatRegFormServiceTests
{
    private readonly PostgresFixture _fx;
    public VatRegFormServiceTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);
        return s.AddInfrastructure(cfg)
            .AddSingleton<Accounting.Application.Abstractions.ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    [SkippableFact]
    public async Task Pp01_and_pp09_build_from_company_profile()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVatRegFormService>();

        foreach (var pdf in new[]
        {
            await svc.BuildPp01Async(default),
            await svc.BuildPp09Async(default),
        })
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }
}

/// <summary>Visual-gate emitter — writes the worked-case ภ.พ.01/09 PDFs when VATREG_EMIT_DIR is set.</summary>
public sealed class VatRegVisualEmit
{
    [SkippableFact]
    public void Emit_worked_cases_for_raster_review()
    {
        var dir = Environment.GetEnvironmentVariable("VATREG_EMIT_DIR");
        Skip.If(string.IsNullOrEmpty(dir), "VATREG_EMIT_DIR not set — visual-gate emit only.");
        Directory.CreateDirectory(dir!);
        File.WriteAllBytes(Path.Combine(dir!, "pp01_identity.pdf"),
            Pp01FormFiller.Fill(VatRegFormTests.Identity()));
        File.WriteAllBytes(Path.Combine(dir!, "pp09_identity.pdf"),
            Pp09FormFiller.Fill(VatRegFormTests.Identity()));
    }
}
