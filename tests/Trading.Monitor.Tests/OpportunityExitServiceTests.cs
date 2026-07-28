using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public class OpportunityExitServiceTests
{
    [Fact]
    public void ResolveExit_ManagedProfitExit_WaitsForThreeLowerClosesAfterTarget()
    {
        var service = new OpportunityExitService();
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var opportunity = Row(observedAt);
        var candles = new[]
        {
            Candle(observedAt.AddSeconds(1), 100m, 105.6m, 99m, 105.50m),
            Candle(observedAt.AddSeconds(2), 105.50m, 105.6m, 105.30m, 105.40m),
            Candle(observedAt.AddSeconds(3), 105.40m, 105.5m, 105.24m, 105.30m),
            Candle(observedAt.AddSeconds(4), 105.30m, 105.4m, 105.22m, 105.25m)
        };

        var exit = service.ResolveExit(opportunity, candles, new RiskOptions
        {
            ManagedProfitExitEnabled = true,
            ManagedProfitExitPercentAfterCosts = 5m,
            ManagedQuickProfitExitPercentAfterCosts = 8m,
            ManagedExitRequiresMomentumWeakness = false,
            ManagedProfitTrailCandlesAfterTarget = 3
        });

        Assert.NotNull(exit);
        Assert.Equal(OpportunityStatus.ManagedProfitExit, exit.Status);
        Assert.Equal(105.25m, exit.ExitPrice);
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
    public void ResolveExit_ManagedProfitExit_DoesNotCloseWhenMinimumTargetIsOnlyTouchedIntrabar()
    {
        var service = new OpportunityExitService();
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var opportunity = Row(observedAt);
        var candles = new[]
        {
            Candle(observedAt.AddMinutes(1), 100m, 105.3m, 99m, 104m)
        };

        var exit = service.ResolveExit(opportunity, candles, new RiskOptions
        {
            ManagedProfitExitEnabled = true,
            ManagedProfitExitPercentAfterCosts = 5m,
            ManagedQuickProfitExitPercentAfterCosts = 8m,
            ManagedExitRequiresMomentumWeakness = true
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
            5m,
            50m,
            105.21m,
            null,
            null,
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
