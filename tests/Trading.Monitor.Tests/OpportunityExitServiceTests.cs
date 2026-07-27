using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public class OpportunityExitServiceTests
{
    [Fact]
    public void ResolveExit_ManagedProfitExit_ClosesOnlyAfterConfiguredNetProfit()
    {
        var service = new OpportunityExitService();
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var opportunity = Row(observedAt);
        var candles = new[]
        {
            Candle(observedAt.AddMinutes(1), 100m, 103m, 99m, 103m),
            Candle(observedAt.AddMinutes(2), 103m, 106m, 102m, 105.5m)
        };

        var exit = service.ResolveExit(opportunity, candles, new RiskOptions
        {
            ManagedProfitExitEnabled = true,
            ManagedProfitExitPercentAfterCosts = 5m,
            ManagedQuickProfitExitPercentAfterCosts = 8m,
            ManagedExitRequiresMomentumWeakness = false
        });

        Assert.NotNull(exit);
        Assert.Equal(OpportunityStatus.ManagedProfitExit, exit.Status);
        Assert.Equal(105.5m, exit.ExitPrice);
    }

    [Fact]
    public void ResolveExit_ManagedProfitExit_DoesNotCloseWhenStopIsTouchedByDefault()
    {
        var service = new OpportunityExitService();
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var opportunity = Row(observedAt);
        var candles = new[]
        {
            Candle(observedAt.AddMinutes(1), 100m, 101m, 94m, 95m)
        };

        var exit = service.ResolveExit(opportunity, candles, new RiskOptions
        {
            ManagedProfitExitEnabled = true,
            ManagedProfitExitPercentAfterCosts = 5m,
            ManagedHardStopExitEnabled = false
        });

        Assert.Null(exit);
    }

    [Fact]
    public void ResolveExit_StaticMode_StillClosesAtStop()
    {
        var service = new OpportunityExitService();
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var opportunity = Row(observedAt);
        var candles = new[]
        {
            Candle(observedAt.AddMinutes(1), 100m, 101m, 94m, 95m)
        };

        var exit = service.ResolveExit(opportunity, candles, new RiskOptions
        {
            ManagedProfitExitEnabled = false
        });

        Assert.NotNull(exit);
        Assert.Equal(OpportunityStatus.HitStopLoss, exit.Status);
    }

    private static OpportunityReportRow Row(DateTimeOffset observedAt)
    {
        return new OpportunityReportRow(
            Guid.NewGuid(),
            "BTCUSDT",
            MarketSide.Long,
            OpportunityStatus.Open,
            95,
            observedAt,
            observedAt.AddMinutes(30),
            null,
            100m,
            99m,
            101m,
            100m,
            95m,
            106m,
            110m,
            null,
            1000m,
            10m,
            2m,
            58m,
            98m,
            -52m,
            null,
            2m,
            "1m | 5m | 15m",
            "trend",
            "");
    }

    private static MarketCandle Candle(DateTimeOffset closeTime, decimal open, decimal high, decimal low, decimal close)
    {
        return new MarketCandle("BTCUSDT", "1m", closeTime.AddMinutes(-1), closeTime, open, high, low, close, 1000m, 100000m);
    }
}
