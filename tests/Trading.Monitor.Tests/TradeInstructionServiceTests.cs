using Trading.Monitor.Application.Services;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public class TradeInstructionServiceTests
{
    [Fact]
    public void Create_HighlightsOnlyActionableHighConvictionOpportunity()
    {
        var service = new TradeInstructionService();
        var opportunity = new TradingOpportunity(
            "ETHUSDT",
            MarketSide.Long,
            94,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(8),
            2000m,
            1998m,
            2002m,
            1980m,
            2045m,
            2070m,
            2.2m,
            ["1m", "5m", "15m", "1h"],
            ["trend"],
            ["minor risk"],
            []);

        var projection = new OpportunityProjection(1000m, 2000m, 0.5m, 2m, 22.5m, 20.5m, 35m, 33m, -10m, -12m);

        var instruction = service.Create(opportunity, projection);

        Assert.True(instruction.Highlight);
        Assert.Equal("COMPRAR AHORA", instruction.ActionLabel);
        Assert.Contains("buscar mínimo", instruction.ProfitReport);
    }

    [Fact]
    public void Create_DoesNotHighlightExpiredOpportunity()
    {
        var service = new TradeInstructionService(new RiskOptions { ManagedProfitExitEnabled = false });
        var opportunity = new TradingOpportunity(
            "BTCUSDT",
            MarketSide.Long,
            99,
            DateTimeOffset.UtcNow.AddMinutes(-30),
            DateTimeOffset.UtcNow.AddMinutes(-20),
            50000m,
            49950m,
            50050m,
            49500m,
            51000m,
            52000m,
            2m,
            ["1m", "5m", "15m", "1h"],
            ["trend"],
            [],
            []);

        var projection = new OpportunityProjection(1000m, 50000m, 0.02m, 2m, 20m, 18m, 40m, 38m, -10m, -12m);

        var instruction = service.Create(opportunity, projection);

        Assert.False(instruction.Highlight);
        Assert.Equal("NO ENTRAR", instruction.ActionLabel);
    }
}
