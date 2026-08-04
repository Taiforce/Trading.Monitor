using Trading.Monitor.Application.Analysis;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public class ExternalAiSignalEngineTests
{
    [Fact]
    public void Evaluate_ReturnsExternalAiOriginForAStrongSustainedUptrend()
    {
        var engine = new ExternalAiSignalEngine(new TechnicalAnalysisService());
        var candles = BuildUptrend("BTCUSDT", "1h", 30000m, 40m);
        var candlesByInterval = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase) { ["1h"] = candles };
        var horizon = new TradingHorizonOptions { Name = "Swing", TriggerInterval = "1h", SignalExpiryMinutes = 240 };

        var opportunity = engine.Evaluate("BTCUSDT", candlesByInterval, new RiskOptions(), horizon);

        Assert.NotNull(opportunity);
        Assert.Equal(SignalOriginKind.ExternalAi, opportunity.OriginKind);
        Assert.Equal(MarketSide.Long, opportunity.Side);
        Assert.Contains(opportunity.Reasons, reason => reason.Contains("Señal Ajena", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_ReturnsNullWhenNotEnoughCandles()
    {
        var engine = new ExternalAiSignalEngine(new TechnicalAnalysisService());
        var candlesByInterval = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase)
        {
            ["1h"] = BuildUptrend("ETHUSDT", "1h", 2000m, 10m).Take(50).ToArray()
        };

        var opportunity = engine.Evaluate("ETHUSDT", candlesByInterval, new RiskOptions(), new TradingHorizonOptions { TriggerInterval = "1h" });

        Assert.Null(opportunity);
    }

    private static IReadOnlyList<MarketCandle> BuildUptrend(string symbol, string interval, decimal startPrice, decimal step)
    {
        var candles = new List<MarketCandle>();
        var price = startPrice;
        var now = DateTimeOffset.UtcNow.AddHours(-200);

        for (var i = 0; i < 180; i++)
        {
            var open = price;
            // A late acceleration gives the volatility-channel (Supertrend-style) model a clear
            // breakout to vote on, in addition to Ichimoku, so at least 2 of the 3 independent
            // models agree - the ensemble deliberately requires multi-model agreement.
            var effectiveStep = i >= 170 ? step * 3m : step;
            var move = i % 7 < 5 ? effectiveStep : -effectiveStep * 0.5m;
            var close = price + move;
            var high = Math.Max(open, close) + effectiveStep * 0.3m;
            var low = Math.Min(open, close) - effectiveStep * 0.2m;
            var volume = 500m + i % 20;

            candles.Add(new MarketCandle(symbol, interval, now.AddHours(i), now.AddHours(i + 1), open, high, low, close, volume, volume * close));
            price = close;
        }

        return candles;
    }
}
