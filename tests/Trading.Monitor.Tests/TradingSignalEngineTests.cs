using Trading.Monitor.Application.Analysis;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public class TradingSignalEngineTests
{
    [Fact]
    public void Evaluate_ReturnsLongOpportunityForAlignedBullishMarket()
    {
        var engine = new TradingSignalEngine(new TechnicalAnalysisService());

        var candles = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = BuildTrend("BTCUSDT", "1m", 50000m, 20m),
            ["5m"] = BuildTrend("BTCUSDT", "5m", 50000m, 25m),
            ["15m"] = BuildTrend("BTCUSDT", "15m", 50000m, 30m),
            ["1h"] = BuildTrend("BTCUSDT", "1h", 50000m, 35m)
        };

        var news = new[] { new NewsItem("Test", "Bitcoin gains as institutional inflows improve", "https://example.test/news", DateTimeOffset.UtcNow, NewsSentiment.Positive, ["BTCUSDT"]) };

        var opportunity = engine.Evaluate("BTCUSDT", candles, news, new TradingMonitorOptions { MinimumScore = 70, TriggerInterval = "5m", SignalExpiryMinutes = 8 }, new RiskOptions());

        Assert.NotNull(opportunity);
        Assert.Equal(MarketSide.Long, opportunity.Side);
        Assert.True(opportunity.Score >= 70);
        Assert.True(opportunity.TakeProfit1 > opportunity.EntryUpper);
        Assert.True(opportunity.StopLoss < opportunity.EntryLower);
    }

    [Fact]
    public void Evaluate_ReturnsNoOpportunityWhenVolatilityIsTooLow()
    {
        var engine = new TradingSignalEngine(new TechnicalAnalysisService());

        var candles = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = BuildFlat("ETHUSDT", "1m", 3000m), ["5m"] = BuildFlat("ETHUSDT", "5m", 3000m), ["15m"] = BuildFlat("ETHUSDT", "15m", 3000m)
        };

        var opportunity = engine.Evaluate("ETHUSDT", candles, [], new TradingMonitorOptions { MinimumScore = 70, TriggerInterval = "5m" }, new RiskOptions());

        Assert.Null(opportunity);
    }

    [Fact]
    public void Evaluate_ReturnsNoOpportunityWhenNetEdgeDoesNotClearCosts()
    {
        var engine = new TradingSignalEngine(new TechnicalAnalysisService());

        var candles = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = BuildTrend("BTCUSDT", "1m", 50000m, 20m),
            ["5m"] = BuildTrend("BTCUSDT", "5m", 50000m, 25m),
            ["15m"] = BuildTrend("BTCUSDT", "15m", 50000m, 30m),
            ["1h"] = BuildTrend("BTCUSDT", "1h", 50000m, 35m)
        };

        var opportunity = engine.Evaluate(
            "BTCUSDT",
            candles,
            [],
            new TradingMonitorOptions { MinimumScore = 70, TriggerInterval = "5m", SignalExpiryMinutes = 8 },
            new RiskOptions
            {
                EstimatedFeePercentPerSide = 2m,
                EstimatedSpreadPercent = 1m,
                MinimumNetProfitPercentAfterCosts = 25m
            });

        Assert.Null(opportunity);
    }

    private static IReadOnlyList<MarketCandle> BuildTrend(string symbol, string interval, decimal startPrice, decimal step)
    {
        var candles = new List<MarketCandle>();
        var price = startPrice;
        var now = DateTimeOffset.UtcNow.AddMinutes(-260);

        for (var i = 0; i < 250; i++)
        {
            var open = price;
            var move = i % 6 < 4 ? step * 0.95m : -step * 0.75m;

            var close = price + move;
            var high = Math.Max(open, close) + step * 0.8m;
            var low = Math.Min(open, close) - step * 0.8m;
            var volume = 110m + i % 10;

            candles.Add(new MarketCandle(symbol, interval, now.AddMinutes(i), now.AddMinutes(i + 1), open, high, low, close, volume, volume * close));

            price = close;
        }

        return candles;
    }

    private static IReadOnlyList<MarketCandle> BuildFlat(string symbol, string interval, decimal price)
    {
        var candles = new List<MarketCandle>();
        var now = DateTimeOffset.UtcNow.AddMinutes(-260);

        for (var i = 0; i < 250; i++)
            candles.Add(new MarketCandle(symbol, interval, now.AddMinutes(i), now.AddMinutes(i + 1), price, price + 0.01m, price - 0.01m, price, 100m, 100m * price));

        return candles;
    }
}
