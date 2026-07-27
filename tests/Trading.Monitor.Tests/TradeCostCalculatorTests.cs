using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public class TradeCostCalculatorTests
{
    [Fact]
    public void Build_CalculatesLongCostsWithExitPriceSpecificFee()
    {
        var breakdown = TradeCostCalculator.Build(MarketSide.Long, 1000m, 10m, 100m, 110m, 0.1m);

        Assert.Equal(1000m, breakdown.Investment);
        Assert.Equal(10m, breakdown.Quantity);
        Assert.Equal(1000m, breakdown.EntryNotional);
        Assert.Equal(1100m, breakdown.ExitNotional);
        Assert.Equal(1m, breakdown.EntryFee);
        Assert.Equal(1.10m, breakdown.ExitFee);
        Assert.Equal(2.10m, breakdown.TotalFees);
        Assert.Equal(100m, breakdown.GrossBenefit);
        Assert.Equal(97.90m, breakdown.NetBenefit);
        Assert.Equal(1097.90m, breakdown.TotalObtained);
        Assert.Equal(9.79m, breakdown.NetPercent);
    }

    [Fact]
    public void ResolveExitPriceForNetPercent_ReturnsLongPriceAfterCommissions()
    {
        var exitPrice = TradeCostCalculator.ResolveExitPriceForNetPercent(MarketSide.Long, 1000m, 10m, 100m, 5m, 0.1m);
        var breakdown = TradeCostCalculator.Build(MarketSide.Long, 1000m, 10m, 100m, exitPrice, 0.1m);

        Assert.InRange(exitPrice, 105.20m, 105.21m);
        Assert.InRange(breakdown.NetBenefit, 49.99m, 50.01m);
        Assert.InRange(breakdown.NetPercent, 4.99m, 5.01m);
    }

    [Fact]
    public void ResolveExitPriceForNetPercent_ReturnsShortPriceAfterCommissions()
    {
        var exitPrice = TradeCostCalculator.ResolveExitPriceForNetPercent(MarketSide.Short, 1000m, 10m, 100m, 5m, 0.1m);
        var breakdown = TradeCostCalculator.Build(MarketSide.Short, 1000m, 10m, 100m, exitPrice, 0.1m);

        Assert.InRange(exitPrice, 94.80m, 94.81m);
        Assert.InRange(breakdown.NetBenefit, 49.99m, 50.01m);
        Assert.InRange(breakdown.NetPercent, 4.99m, 5.01m);
    }
}
