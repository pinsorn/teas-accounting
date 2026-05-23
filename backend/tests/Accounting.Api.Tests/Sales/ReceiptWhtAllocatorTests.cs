using Accounting.Application.Sales;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Sales;

// Sprint (receipt itemize + multi-category WHT, 2026-05-22) — guards the pro-rata
// WHT base allocator. Pure (no DB) → runs without Postgres/Testcontainers.
// Compliance: a mixed goods+service bill with multiple service categories must
// withhold per income type, scaled by the amount actually paid (partial).
public class ReceiptWhtAllocatorTests
{
    private const int Svc = 10;   // service 3%
    private const int Rent = 20;  // rent 5%
    private const int Ads = 30;   // ads 2%

    private static WhtAllocLine Line(decimal exVat, string type, int? whtTypeId) =>
        new(exVat, type, whtTypeId);

    [Fact]
    public void Goods_lines_are_excluded()
    {
        var app = new WhtAllocApplication(10_700m, 10_700m, new[]
        {
            Line(8_000m, "GOOD", null),
            Line(2_000m, "SERVICE", Svc),
        });

        var result = ReceiptWhtAllocator.Allocate(new[] { app });

        result.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new WhtAllocResult(Svc, 2_000m));
    }

    [Fact]
    public void Multiple_service_categories_grouped_separately()
    {
        // rent 10,000 + service 5,000 + ads 2,000 + goods 3,000, full payment.
        var app = new WhtAllocApplication(20_000m, 20_000m, new[]
        {
            Line(10_000m, "SERVICE", Rent),
            Line(5_000m, "SERVICE", Svc),
            Line(2_000m, "SERVICE", Ads),
            Line(3_000m, "GOOD", null),
        });

        var result = ReceiptWhtAllocator.Allocate(new[] { app });

        result.Should().BeEquivalentTo(new[]
        {
            new WhtAllocResult(Svc, 5_000m),
            new WhtAllocResult(Rent, 10_000m),
            new WhtAllocResult(Ads, 2_000m),
        });
    }

    [Fact]
    public void Partial_payment_scales_each_category_base_pro_rata()
    {
        // TI total 20,000; paid 5,000 → fraction 0.25. Bases scale 1/4.
        var app = new WhtAllocApplication(5_000m, 20_000m, new[]
        {
            Line(10_000m, "SERVICE", Rent),  // → 2,500
            Line(5_000m, "SERVICE", Svc),    // → 1,250
            Line(3_000m, "GOOD", null),      // excluded
        });

        var result = ReceiptWhtAllocator.Allocate(new[] { app });

        result.Should().BeEquivalentTo(new[]
        {
            new WhtAllocResult(Svc, 1_250m),
            new WhtAllocResult(Rent, 2_500m),
        });
    }

    [Fact]
    public void Same_category_across_multiple_invoices_accumulates()
    {
        var a1 = new WhtAllocApplication(10_000m, 10_000m, new[] { Line(10_000m, "SERVICE", Svc) });
        var a2 = new WhtAllocApplication(5_000m, 5_000m, new[] { Line(5_000m, "SERVICE", Svc) });

        var result = ReceiptWhtAllocator.Allocate(new[] { a1, a2 });

        result.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new WhtAllocResult(Svc, 15_000m));
    }

    [Fact]
    public void Service_line_without_resolved_type_is_excluded()
    {
        var app = new WhtAllocApplication(5_000m, 5_000m, new[]
        {
            Line(5_000m, "SERVICE", null),   // unresolved → no auto WHT
        });

        ReceiptWhtAllocator.Allocate(new[] { app }).Should().BeEmpty();
    }

    [Fact]
    public void Zero_ti_total_does_not_throw_and_allocates_nothing()
    {
        var app = new WhtAllocApplication(5_000m, 0m, new[] { Line(5_000m, "SERVICE", Svc) });

        ReceiptWhtAllocator.Allocate(new[] { app }).Should().BeEmpty();
    }

    [Fact]
    public void Exempt_service_is_treated_as_service()
    {
        var app = new WhtAllocApplication(1_000m, 1_000m, new[] { Line(1_000m, "EXEMPT_SERVICE", Svc) });

        ReceiptWhtAllocator.Allocate(new[] { app }).Should().ContainSingle()
            .Which.BaseAmount.Should().Be(1_000m);
    }

    [Fact]
    public void Empty_input_yields_empty()
    {
        ReceiptWhtAllocator.Allocate(System.Array.Empty<WhtAllocApplication>())
            .Should().BeEmpty();
    }
}
