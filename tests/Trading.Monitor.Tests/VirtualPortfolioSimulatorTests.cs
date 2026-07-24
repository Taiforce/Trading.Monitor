using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public class VirtualPortfolioSimulatorTests
{
    [Fact]
    public void Simulate_CompoundsFullBalanceAcrossClosedSignals()
    {
        var simulator = new VirtualPortfolioSimulator();
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        var signals = new[]
        {
            Row(Guid.NewGuid(), "BTCUSDT", MarketSide.Long, start, 100m, 110m),
            Row(Guid.NewGuid(), "ETHUSDT", MarketSide.Short, start.AddHours(1), 50m, 45m)
        };

        var report = simulator.Simulate(signals, 1000m, 0m);

        Assert.Equal(2, report.AppliedTrades);
        Assert.Equal(1210m, report.FinalBalance);
        Assert.Equal(210m, report.NetPnL);
        Assert.Equal(1100m, report.Trades[1].StartingBalance);
        Assert.Equal(1210m, report.Trades[1].EndingBalance);
    }

    [Fact]
    public void Simulate_SkipsOpenSignals()
    {
        var simulator = new VirtualPortfolioSimulator();
        var start = DateTimeOffset.UtcNow.AddMinutes(-15);
        var signals = new[]
        {
            Row(Guid.NewGuid(), "BTCUSDT", MarketSide.Long, start, 100m, null, OpportunityStatus.Open)
        };

        var report = simulator.Simulate(signals, 1000m, 0m);

        Assert.Equal(0, report.AppliedTrades);
        Assert.Equal(1, report.SkippedTrades);
        Assert.Equal(1000m, report.FinalBalance);
        Assert.Equal("Abierta", report.Trades[0].SkipReason);
    }

    private static OpportunityReportRow Row(Guid id, string symbol, MarketSide side, DateTimeOffset observedAt, decimal entryPrice, decimal? exitPrice, OpportunityStatus status = OpportunityStatus.HitTakeProfit1)
    {
        var capital = 1000m;
        var quantity = entryPrice <= 0m ? 0m : capital / entryPrice;
        var realized = exitPrice.HasValue ? OpportunityProjectionService.CalculateGrossPnL(side, entryPrice, exitPrice.Value, quantity) : (decimal?)null;

        return new OpportunityReportRow(
            id,
            symbol,
            side,
            status,
            90,
            observedAt,
            observedAt.AddMinutes(30),
            exitPrice.HasValue ? observedAt.AddMinutes(10) : null,
            entryPrice,
            entryPrice,
            entryPrice,
            entryPrice,
            side == MarketSide.Long ? entryPrice * 0.95m : entryPrice * 1.05m,
            side == MarketSide.Long ? entryPrice * 1.10m : entryPrice * 0.90m,
            side == MarketSide.Long ? entryPrice * 1.20m : entryPrice * 0.80m,
            exitPrice,
            capital,
            quantity,
            0m,
            100m,
            200m,
            -50m,
            realized,
            2m,
            "5m | 15m | 1h",
            "test",
            "");
    }
}
