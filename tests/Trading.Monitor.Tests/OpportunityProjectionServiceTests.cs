using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public class OpportunityProjectionServiceTests
{
    [Fact]
    public void Project_CalculatesLongProfitAndStopLossForCapital()
    {
        var service = new OpportunityProjectionService();

        var opportunity = new TradingOpportunity("BTCUSDT", MarketSide.Long, 85, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(8), 100m, 99m, 101m, 95m, 110m,
            120m, 2m, ["5m", "15m"], ["test"], [], []);

        var projection = service.Project(opportunity, 1000m, 0.1m);

        Assert.Equal(100m, projection.EntryPrice);
        Assert.Equal(10m, projection.EstimatedQuantity);
        Assert.Equal(2m, projection.EstimatedFees);
        Assert.Equal(98m, projection.NetProfitAtTakeProfit1);
        Assert.Equal(-52m, projection.NetLossAtStop);
    }
}