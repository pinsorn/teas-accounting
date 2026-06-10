using Accounting.Domain.Tax;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests.Tax;

/// <summary>
/// Golden tests for ม.65ตรี(12) loss carry-forward: a tax loss of fiscal year Y may offset taxable
/// profit in the FIVE following accounting periods (Y+1 … Y+5); profits consume the OLDEST
/// non-expired loss first. CarryInFor returns the loss still available entering the target year.
/// </summary>
public class CitLossCarryForwardTests
{
    private static (int, decimal) Y(int year, decimal pl) => (year, pl);

    [Fact]
    public void No_history_carry_in_is_zero() =>
        CitLossCarryForward.CarryInFor(2026, []).Should().Be(0m);

    [Fact]
    public void Profit_only_history_carries_nothing() =>
        CitLossCarryForward.CarryInFor(2026, [Y(2024, 500_000m), Y(2025, 1m)]).Should().Be(0m);

    [Fact]
    public void Single_loss_within_window_carries_in_full() =>
        CitLossCarryForward.CarryInFor(2026, [Y(2024, -100_000m)]).Should().Be(100_000m);

    [Fact]
    public void Loss_usable_through_exactly_five_periods_then_expires()
    {
        // ม.65ตรี(12): loss of 2020 usable in 2021–2025 (5 periods) — alive at 2025, gone at 2026.
        CitLossCarryForward.CarryInFor(2025, [Y(2020, -100_000m)]).Should().Be(100_000m);
        CitLossCarryForward.CarryInFor(2026, [Y(2020, -100_000m)]).Should().Be(0m);
    }

    [Fact]
    public void Intervening_profit_consumes_loss_partially() =>
        CitLossCarryForward.CarryInFor(2026, [Y(2023, -100_000m), Y(2024, 60_000m)])
            .Should().Be(40_000m);

    [Fact]
    public void Profit_consumes_oldest_loss_first_so_newer_survives() =>
        // 2021 profit eats the 2019 loss (oldest, non-expired at 2021); the 2024 loss survives whole.
        CitLossCarryForward.CarryInFor(2026, [Y(2019, -50_000m), Y(2021, 50_000m), Y(2024, -80_000m)])
            .Should().Be(80_000m);

    [Fact]
    public void Expired_loss_is_skipped_but_profit_still_consumes_live_losses() =>
        // 2018 loss expired (2018+5=2023 < 2025) → the 2025 profit can't touch it, but DOES
        // consume the live 2024 loss in full. Nothing survives into 2026.
        CitLossCarryForward.CarryInFor(2026, [Y(2018, -100_000m), Y(2024, -30_000m), Y(2025, 70_000m)])
            .Should().Be(0m);

    [Fact]
    public void Multiple_losses_accumulate() =>
        CitLossCarryForward.CarryInFor(2026, [Y(2023, -100_000m), Y(2024, -50_000m)])
            .Should().Be(150_000m);

    [Fact]
    public void Profit_before_the_loss_year_consumes_nothing() =>
        // Carry-forward only — the 2022 profit precedes the 2024 loss.
        CitLossCarryForward.CarryInFor(2026, [Y(2022, 20_000m), Y(2024, -100_000m)])
            .Should().Be(100_000m);

    [Fact]
    public void Only_years_before_target_count() =>
        CitLossCarryForward.CarryInFor(2026, [Y(2026, -999m), Y(2027, -999m), Y(2024, -10_000m)])
            .Should().Be(10_000m);

    [Fact]
    public void Unordered_history_is_normalised_chronologically() =>
        // Same facts as Intervening_profit… but supplied out of order.
        CitLossCarryForward.CarryInFor(2026, [Y(2024, 60_000m), Y(2023, -100_000m)])
            .Should().Be(40_000m);

    [Fact]
    public void Profit_spanning_two_losses_consumes_oldest_then_next() =>
        // 2025 profit 120k: eats all of 2023 (100k) then 20k of 2024 → 30k remains.
        CitLossCarryForward.CarryInFor(2026,
            [Y(2023, -100_000m), Y(2024, -50_000m), Y(2025, 120_000m)])
            .Should().Be(30_000m);
}
