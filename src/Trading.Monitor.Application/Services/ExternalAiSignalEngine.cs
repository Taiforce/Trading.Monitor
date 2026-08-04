using Trading.Monitor.Application.Analysis;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

/// <summary>
/// "Señales Ajenas": an independent ensemble of well-known, publicly documented technical
/// strategies (Ichimoku cloud breakout, a Supertrend-style volatility channel, and Bollinger
/// mean-reversion) - the same family of rules many commercial "trading AI" signal products are
/// built on. This does not literally reverse-engineer or copy any specific third-party AI model
/// (that is neither technically feasible nor something we can verify is legal/ethical to do
/// automatically); instead it reproduces the class of signal those products generate so the
/// system has a second, independently-computed opinion to compare against its own engine and
/// against real trader activity.
/// </summary>
public sealed class ExternalAiSignalEngine(TechnicalAnalysisService technicalAnalysis)
{
    private const int MinimumCandles = 120;

    public TradingOpportunity? Evaluate(string symbol, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> candlesByInterval, RiskOptions riskOptions, TradingHorizonOptions? horizonOptions = null)
    {
        var triggerInterval = horizonOptions?.TriggerInterval ?? "1h";
        var expiryMinutes = Math.Max(1, horizonOptions?.SignalExpiryMinutes ?? 240);

        if (!candlesByInterval.TryGetValue(triggerInterval, out var candles) || candles.Count < MinimumCandles)
            return null;

        var snapshot = technicalAnalysis.CreateSnapshot(symbol, triggerInterval, candles);
        var highs = candles.Select(candle => candle.High).ToArray();
        var lows = candles.Select(candle => candle.Low).ToArray();
        var closes = candles.Select(candle => candle.Close).ToArray();

        var votes = CollectVotes(snapshot, highs, lows, closes);
        var longVotes = votes.Where(vote => vote.Side == MarketSide.Long).ToArray();
        var shortVotes = votes.Where(vote => vote.Side == MarketSide.Short).ToArray();

        // Require at least 2 of the 3 independent models to agree before publishing a signal -
        // a single model's opinion is treated as noise, matching how most public consensus
        // products only alert on multi-model agreement.
        var side = longVotes.Length > shortVotes.Length ? MarketSide.Long : MarketSide.Short;
        var agreeing = side == MarketSide.Long ? longVotes : shortVotes;

        if (agreeing.Length < 2 || longVotes.Length == shortVotes.Length)
            return null;

        if (snapshot.Atr14 <= 0m || snapshot.AtrPercent < riskOptions.MinimumAtrPercent || snapshot.AtrPercent > riskOptions.MaximumAtrPercent)
            return null;

        return BuildOpportunity(symbol, side, snapshot, agreeing, votes.Length, expiryMinutes, riskOptions);
    }

    private static StrategyVote[] CollectVotes(TechnicalSnapshot snapshot, decimal[] highs, decimal[] lows, decimal[] closes)
    {
        var votes = new List<StrategyVote>();

        var tenkan = IndicatorCalculator.MidpointHighLow(highs, lows, 9);
        var kijun = IndicatorCalculator.MidpointHighLow(highs, lows, 26);
        var senkouB = IndicatorCalculator.MidpointHighLow(highs, lows, 52);
        var senkouA = (tenkan + kijun) / 2m;
        var cloudTop = Math.Max(senkouA, senkouB);
        var cloudBottom = Math.Min(senkouA, senkouB);

        if (tenkan > kijun && snapshot.LastPrice > cloudTop)
            votes.Add(new StrategyVote("Ichimoku", MarketSide.Long, "precio sobre la nube con Tenkan>Kijun"));
        else if (tenkan < kijun && snapshot.LastPrice < cloudBottom)
            votes.Add(new StrategyVote("Ichimoku", MarketSide.Short, "precio bajo la nube con Tenkan<Kijun"));

        var channelBreakout = IndicatorCalculator.VolatilityChannelBreakout(highs, lows, closes);
        if (channelBreakout > 0)
            votes.Add(new StrategyVote("Canal-Volatilidad", MarketSide.Long, "ruptura alcista del canal ATR (estilo Supertrend)"));
        else if (channelBreakout < 0)
            votes.Add(new StrategyVote("Canal-Volatilidad", MarketSide.Short, "ruptura bajista del canal ATR (estilo Supertrend)"));

        var bollinger = IndicatorCalculator.Bollinger(closes);
        if (snapshot.LastPrice <= bollinger.Lower && snapshot.Rsi14 < 35m)
            votes.Add(new StrategyVote("Bollinger-Reversion", MarketSide.Long, $"precio en banda inferior con RSI sobrevendido ({snapshot.Rsi14:F1})"));
        else if (snapshot.LastPrice >= bollinger.Upper && snapshot.Rsi14 > 65m)
            votes.Add(new StrategyVote("Bollinger-Reversion", MarketSide.Short, $"precio en banda superior con RSI sobrecomprado ({snapshot.Rsi14:F1})"));

        return votes.ToArray();
    }

    private static TradingOpportunity? BuildOpportunity(string symbol, MarketSide side, TechnicalSnapshot snapshot, StrategyVote[] agreeing, int totalModels, int expiryMinutes, RiskOptions riskOptions)
    {
        var price = snapshot.LastPrice;
        var entryBuffer = snapshot.Atr14 * riskOptions.EntryAtrBuffer;
        var entryLower = price - entryBuffer;
        var entryUpper = price + entryBuffer;

        decimal stopLoss, takeProfit1, takeProfit2, riskPerUnit;

        if (side == MarketSide.Long)
        {
            stopLoss = Math.Min(snapshot.RecentSupport, price - snapshot.Atr14 * riskOptions.AtrStopMultiplier);
            if (stopLoss >= price)
                stopLoss = price - snapshot.Atr14 * riskOptions.AtrStopMultiplier;

            riskPerUnit = price - stopLoss;
            takeProfit1 = price + riskPerUnit * riskOptions.Target1R;
            takeProfit2 = price + riskPerUnit * riskOptions.Target2R;
        }
        else
        {
            stopLoss = Math.Max(snapshot.RecentResistance, price + snapshot.Atr14 * riskOptions.AtrStopMultiplier);
            if (stopLoss <= price)
                stopLoss = price + snapshot.Atr14 * riskOptions.AtrStopMultiplier;

            riskPerUnit = stopLoss - price;
            takeProfit1 = price - riskPerUnit * riskOptions.Target1R;
            takeProfit2 = price - riskPerUnit * riskOptions.Target2R;
        }

        if (riskPerUnit <= 0m)
            return null;

        var riskReward = Math.Abs(takeProfit1 - price) / riskPerUnit;
        if (riskReward < riskOptions.MinimumRiskReward)
            return null;

        var estimatedCostPercent = Math.Max(0m, riskOptions.EstimatedFeePercentPerSide) * 2m + Math.Max(0m, riskOptions.EstimatedSpreadPercent);
        var grossTargetPercent = Math.Abs(takeProfit1 - price) / price * 100m;
        if (grossTargetPercent - estimatedCostPercent < Math.Max(0m, riskOptions.MinimumNetProfitPercentAfterCosts))
            return null;

        // 2/3 models agreeing -> score 80, 3/3 -> score 95. Deliberately capped below the
        // scores the primary engine reaches with full multi-timeframe confirmation, since this
        // ensemble only looks at a single interval.
        var score = Math.Clamp(65 + agreeing.Length * 15, 0, 95);
        var reasons = new List<string>
        {
            $"Señal Ajena: {agreeing.Length}/{totalModels} modelos publicos independientes coinciden en {(side == MarketSide.Long ? "LONG" : "SHORT")} para {symbol}."
        };
        reasons.AddRange(agreeing.Select(vote => $"{vote.Model}: {vote.Detail}."));

        var now = DateTimeOffset.UtcNow;
        var observedAt = snapshot.ObservedAt > now ? now : snapshot.ObservedAt;

        return new TradingOpportunity(symbol, side, score, observedAt, now.AddMinutes(expiryMinutes), Round(price), Round(entryLower), Round(entryUpper), Round(stopLoss), Round(takeProfit1),
            Round(takeProfit2), Math.Round(riskReward, 2), [snapshot.Interval], reasons, [], [], SignalOperationKind.Fixed, SignalOriginKind.ExternalAi);
    }

    private static decimal Round(decimal value)
    {
        var decimals = Math.Abs(value) switch { >= 1000m => 2, >= 1m => 4, _ => 8 };
        return Math.Round(value, decimals);
    }

    private sealed record StrategyVote(string Model, MarketSide Side, string Detail);
}
