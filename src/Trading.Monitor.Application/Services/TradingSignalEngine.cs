using Trading.Monitor.Application.Analysis;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed class TradingSignalEngine(TechnicalAnalysisService technicalAnalysis)
{
    public TradingOpportunity? Evaluate(string symbol, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> candlesByInterval, IReadOnlyList<NewsItem> latestNews, TradingMonitorOptions monitorOptions,
        RiskOptions riskOptions, TradingHorizonOptions? horizonOptions = null)
    {
        var horizonName = string.IsNullOrWhiteSpace(horizonOptions?.Name) ? "Mercado" : horizonOptions.Name.Trim();
        var triggerInterval = NormalizeInterval(horizonOptions?.TriggerInterval ?? monitorOptions.TriggerInterval);
        var minimumScore = Math.Max(monitorOptions.MinimumScore, horizonOptions?.MinimumScore ?? monitorOptions.MinimumScore);
        var minimumConfirmedIntervals = Math.Max(2, horizonOptions?.MinimumConfirmedIntervals ?? 2);
        var expiryMinutes = Math.Max(1, horizonOptions?.SignalExpiryMinutes ?? monitorOptions.SignalExpiryMinutes);
        var requiredIntervals = (horizonOptions?.RequiredAlignedIntervals ?? [])
            .Where(interval => !string.IsNullOrWhiteSpace(interval))
            .Select(NormalizeInterval)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var snapshots = candlesByInterval.Where(pair => pair.Value.Count >= 60)
                                         .Select(pair => technicalAnalysis.CreateSnapshot(symbol, pair.Key, pair.Value))
                                         .OrderBy(snapshot => IntervalRank(snapshot.Interval))
                                         .ToArray();

        if (snapshots.Length == 0)
            return null;

        var longReasons = new List<string>();
        var shortReasons = new List<string>();
        var longRisks = new List<string>();
        var shortRisks = new List<string>();
        var longScore = 0;
        var shortScore = 0;

        foreach (var snapshot in snapshots)
        {
            var weight = IntervalWeight(snapshot.Interval);
            ScoreSnapshot(snapshot, weight, longReasons, shortReasons, longRisks, shortRisks, ref longScore, ref shortScore);
        }

        var relatedNews = latestNews.Where(item => item.Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase)).OrderByDescending(item => item.PublishedAt).Take(5).ToArray();

        ScoreNews(symbol, relatedNews, longReasons, shortReasons, longRisks, shortRisks, ref longScore, ref shortScore);

        longScore = Math.Clamp(longScore, 0, 100);
        shortScore = Math.Clamp(shortScore, 0, 100);

        if (Math.Abs(longScore - shortScore) < 8)
            return null;

        var side = longScore > shortScore ? MarketSide.Long : MarketSide.Short;
        var score = side == MarketSide.Long ? longScore : shortScore;

        if (score < minimumScore)
            return null;

        var trigger = snapshots.FirstOrDefault(snapshot => string.Equals(NormalizeInterval(snapshot.Interval), triggerInterval, StringComparison.Ordinal)) ?? snapshots.First();

        if (trigger.Atr14 <= 0m)
            return null;

        var selectedRisks = side == MarketSide.Long ? longRisks : shortRisks;
        var selectedReasons = side == MarketSide.Long ? longReasons : shortReasons;

        if (!IsAligned(trigger, side))
            return null;

        if (trigger.AtrPercent < riskOptions.MinimumAtrPercent)
        {
            selectedRisks.Add($"Volatilidad muy baja en {trigger.Interval}: ATR {trigger.AtrPercent:F2}%.");
            return null;
        }

        if (trigger.AtrPercent > riskOptions.MaximumAtrPercent)
        {
            selectedRisks.Add($"Volatilidad extrema en {trigger.Interval}: ATR {trigger.AtrPercent:F2}%.");
            return null;
        }

        var confirmedIntervals = snapshots.Where(snapshot => IsAligned(snapshot, side)).Select(snapshot => snapshot.Interval).Distinct(StringComparer.Ordinal).ToArray();

        if (confirmedIntervals.Length < minimumConfirmedIntervals)
            return null;

        if (requiredIntervals.Length > 0 && requiredIntervals.Any(required => !snapshots.Any(snapshot => string.Equals(NormalizeInterval(snapshot.Interval), required, StringComparison.Ordinal) && IsAligned(snapshot, side))))
            return null;

        if (IsOverextended(trigger, side))
        {
            selectedRisks.Add($"{trigger.Interval}: entrada descartada por precio sobreextendido.");
            return null;
        }

        if (trigger.Adx14 < 18m && trigger.RelativeVolume < 1.15m)
        {
            selectedRisks.Add($"{trigger.Interval}: fuerza insuficiente; ADX {trigger.Adx14:F1}, volumen {trigger.RelativeVolume:F2}x.");
            return null;
        }

        if (selectedRisks.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 5)
            return null;

        selectedReasons.Insert(0, $"Horizonte {horizonName}: trigger {trigger.Interval}, vida máxima {FormatDuration(expiryMinutes)}.");

        return BuildOpportunity(symbol, side, score, trigger, expiryMinutes, riskOptions, confirmedIntervals, selectedReasons, selectedRisks, relatedNews);
    }

    private static void ScoreSnapshot(TechnicalSnapshot snapshot, int weight, List<string> longReasons, List<string> shortReasons, List<string> longRisks, List<string> shortRisks, ref int longScore,
        ref int shortScore)
    {
        if (snapshot.Bias == MarketBias.Bullish)
            AddScore(ref longScore, weight, longReasons, $"{snapshot.Interval}: tendencia alcista alineada con EMAs y MACD.");
        else if (snapshot.Bias == MarketBias.Bearish)
            AddScore(ref shortScore, weight, shortReasons, $"{snapshot.Interval}: tendencia bajista alineada con EMAs y MACD.");

        if (snapshot.LastPrice > snapshot.Ema200)
            AddScore(ref longScore, weight / 3, longReasons, $"{snapshot.Interval}: precio sobre EMA 200.");
        else if (snapshot.LastPrice < snapshot.Ema200)
            AddScore(ref shortScore, weight / 3, shortReasons, $"{snapshot.Interval}: precio bajo EMA 200.");

        if (snapshot.MacdHistogram > 0m)
            AddScore(ref longScore, weight / 3, longReasons, $"{snapshot.Interval}: momentum MACD positivo.");
        else if (snapshot.MacdHistogram < 0m)
            AddScore(ref shortScore, weight / 3, shortReasons, $"{snapshot.Interval}: momentum MACD negativo.");

        if (snapshot.Rsi14 is >= 48m and <= 72m)
            AddScore(ref longScore, weight / 4, longReasons, $"{snapshot.Interval}: RSI saludable para continuidad alcista ({snapshot.Rsi14:F1}).");
        else if (snapshot.Rsi14 is >= 28m and <= 52m)
            AddScore(ref shortScore, weight / 4, shortReasons, $"{snapshot.Interval}: RSI favorable para presión bajista ({snapshot.Rsi14:F1}).");

        if (snapshot.Rsi14 > 78m)
            longRisks.Add($"{snapshot.Interval}: RSI sobreextendido ({snapshot.Rsi14:F1}).");
        else if (snapshot.Rsi14 < 22m)
            shortRisks.Add($"{snapshot.Interval}: RSI sobrevendido ({snapshot.Rsi14:F1}).");

        if (snapshot.LastPrice > snapshot.Vwap)
            AddScore(ref longScore, weight / 4, longReasons, $"{snapshot.Interval}: precio sobre VWAP.");
        else if (snapshot.LastPrice < snapshot.Vwap)
            AddScore(ref shortScore, weight / 4, shortReasons, $"{snapshot.Interval}: precio bajo VWAP.");

        if (snapshot.RelativeVolume >= 1.25m)
        {
            if (snapshot.LastPrice >= snapshot.Ema9)
                AddScore(ref longScore, weight / 3, longReasons, $"{snapshot.Interval}: volumen relativo elevado ({snapshot.RelativeVolume:F2}x).");
            else
                AddScore(ref shortScore, weight / 3, shortReasons, $"{snapshot.Interval}: volumen relativo elevado ({snapshot.RelativeVolume:F2}x).");
        }

        if (snapshot.Adx14 >= 20m)
        {
            if (snapshot.Bias == MarketBias.Bullish)
                AddScore(ref longScore, weight / 3, longReasons, $"{snapshot.Interval}: ADX confirma fuerza de tendencia ({snapshot.Adx14:F1}).");
            else if (snapshot.Bias == MarketBias.Bearish)
                AddScore(ref shortScore, weight / 3, shortReasons, $"{snapshot.Interval}: ADX confirma fuerza de tendencia ({snapshot.Adx14:F1}).");
        }

        if (snapshot.LastPrice > snapshot.RecentResistance)
            AddScore(ref longScore, weight / 2, longReasons, $"{snapshot.Interval}: ruptura sobre resistencia reciente.");
        else if (snapshot.LastPrice < snapshot.RecentSupport)
            AddScore(ref shortScore, weight / 2, shortReasons, $"{snapshot.Interval}: ruptura bajo soporte reciente.");
    }

    private static void ScoreNews(string symbol, IReadOnlyList<NewsItem> relatedNews, List<string> longReasons, List<string> shortReasons, List<string> longRisks, List<string> shortRisks, ref int longScore,
        ref int shortScore)
    {
        foreach (var item in relatedNews)
        {
            if (item.Sentiment == NewsSentiment.Positive)
            {
                AddScore(ref longScore, 4, longReasons, $"Noticia positiva para {symbol}: {item.Title}");
                shortRisks.Add($"Noticia positiva en contra de SHORT: {item.Title}");
            }
            else if (item.Sentiment == NewsSentiment.Negative)
            {
                AddScore(ref shortScore, 4, shortReasons, $"Noticia negativa para {symbol}: {item.Title}");
                longRisks.Add($"Noticia negativa en contra de LONG: {item.Title}");
            }
        }
    }

    private static TradingOpportunity? BuildOpportunity(string symbol, MarketSide side, int score, TechnicalSnapshot trigger, int expiryMinutes, RiskOptions riskOptions,
        IReadOnlyList<string> confirmedIntervals, IReadOnlyList<string> reasons, IReadOnlyList<string> risks, IReadOnlyList<NewsItem> relatedNews)
    {
        var price = trigger.LastPrice;
        var entryBuffer = trigger.Atr14 * riskOptions.EntryAtrBuffer;
        decimal entryLower;
        decimal entryUpper;
        decimal stopLoss;
        decimal takeProfit1;
        decimal takeProfit2;
        decimal riskPerUnit;

        if (side == MarketSide.Long)
        {
            entryLower = price - entryBuffer;
            entryUpper = price + entryBuffer;
            stopLoss = Math.Min(trigger.RecentSupport, price - trigger.Atr14 * riskOptions.AtrStopMultiplier);

            if (stopLoss >= price)
                stopLoss = price - trigger.Atr14 * riskOptions.AtrStopMultiplier;

            riskPerUnit = price - stopLoss;
            takeProfit1 = price + riskPerUnit * riskOptions.Target1R;
            takeProfit2 = price + riskPerUnit * riskOptions.Target2R;
        }
        else
        {
            entryLower = price - entryBuffer;
            entryUpper = price + entryBuffer;
            stopLoss = Math.Max(trigger.RecentResistance, price + trigger.Atr14 * riskOptions.AtrStopMultiplier);

            if (stopLoss <= price)
                stopLoss = price + trigger.Atr14 * riskOptions.AtrStopMultiplier;

            riskPerUnit = stopLoss - price;
            takeProfit1 = price - riskPerUnit * riskOptions.Target1R;
            takeProfit2 = price - riskPerUnit * riskOptions.Target2R;
        }

        if (riskPerUnit <= 0m)
            return null;

        var reward = Math.Abs(takeProfit1 - price);
        var riskReward = reward / riskPerUnit;

        if (riskReward < riskOptions.MinimumRiskReward)
            return null;

        var entryPrice = (entryLower + entryUpper) / 2m;
        if (!HasEnoughNetEdge(side, entryPrice, takeProfit1, riskOptions, out var netTargetPercent, out var estimatedCostPercent, out var breakEvenPrice))
            return null;

        var enrichedReasons = reasons
            .Append($"Rentabilidad neta: objetivo {netTargetPercent:F2}% después de costo estimado {estimatedCostPercent:F2}%.")
            .Append($"Precio mínimo para no perder: {RoundPrice(breakEvenPrice)}.")
            .Distinct()
            .Take(10)
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        var observedAt = trigger.ObservedAt > now ? now : trigger.ObservedAt;

        return new TradingOpportunity(symbol, side, score, observedAt, now.AddMinutes(expiryMinutes), RoundPrice(price), RoundPrice(entryLower), RoundPrice(entryUpper),
            RoundPrice(stopLoss), RoundPrice(takeProfit1), RoundPrice(takeProfit2), Math.Round(riskReward, 2), confirmedIntervals, enrichedReasons, risks.Distinct().Take(8).ToArray(),
            relatedNews);
    }

    private static bool HasEnoughNetEdge(MarketSide side, decimal entryPrice, decimal takeProfit1, RiskOptions riskOptions, out decimal netTargetPercent, out decimal estimatedCostPercent,
        out decimal breakEvenPrice)
    {
        netTargetPercent = 0m;
        estimatedCostPercent = Math.Max(0m, riskOptions.EstimatedFeePercentPerSide) * 2m + Math.Max(0m, riskOptions.EstimatedSpreadPercent);
        breakEvenPrice = entryPrice;

        if (entryPrice <= 0m || takeProfit1 <= 0m)
            return false;

        var grossTargetPercent = Math.Abs(takeProfit1 - entryPrice) / entryPrice * 100m;
        netTargetPercent = grossTargetPercent - estimatedCostPercent;
        var breakEvenMove = entryPrice * estimatedCostPercent / 100m;
        breakEvenPrice = side == MarketSide.Long ? entryPrice + breakEvenMove : entryPrice - breakEvenMove;

        return netTargetPercent >= Math.Max(0m, riskOptions.MinimumNetProfitPercentAfterCosts);
    }

    private static bool IsAligned(TechnicalSnapshot snapshot, MarketSide side)
    {
        return side == MarketSide.Long
                   ? snapshot.Bias == MarketBias.Bullish || (snapshot.LastPrice > snapshot.Ema20 && snapshot.MacdHistogram > 0m)
                   : snapshot.Bias == MarketBias.Bearish || (snapshot.LastPrice < snapshot.Ema20 && snapshot.MacdHistogram < 0m);
    }

    private static bool IsOverextended(TechnicalSnapshot snapshot, MarketSide side)
    {
        return side == MarketSide.Long
                   ? snapshot.Rsi14 > 78m && snapshot.LastPrice > snapshot.Ema9
                   : snapshot.Rsi14 < 22m && snapshot.LastPrice < snapshot.Ema9;
    }

    private static void AddScore(ref int score, int points, List<string> reasons, string reason)
    {
        if (points <= 0)
            return;

        score += points;
        reasons.Add(reason);
    }

    private static int IntervalWeight(string interval)
    {
        return NormalizeInterval(interval) switch { "1s" => 3, "1m" => 6, "3m" => 7, "5m" => 10, "15m" => 14, "30m" => 16, "1h" => 22, "2h" => 20, "4h" => 20, "1d" => 18, "1w" => 16, "1M" => 12, _ => 10 };
    }

    private static int IntervalRank(string interval)
    {
        return NormalizeInterval(interval) switch { "1s" => 0, "1m" => 1, "3m" => 2, "5m" => 3, "15m" => 4, "30m" => 5, "1h" => 6, "2h" => 7, "4h" => 8, "1d" => 9, "1w" => 10, "1M" => 11, _ => 12 };
    }

    private static string NormalizeInterval(string interval)
    {
        var value = interval.Trim();
        if (string.Equals(value, "1M", StringComparison.Ordinal) || string.Equals(value, "1mo", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "1month", StringComparison.OrdinalIgnoreCase))
            return "1M";

        return value.ToLowerInvariant() switch
        {
            "1s" => "1s",
            "1m" => "1m",
            "3m" => "3m",
            "5m" => "5m",
            "15m" => "15m",
            "30m" => "30m",
            "1hr" or "1h" => "1h",
            "2h" => "2h",
            "4h" => "4h",
            "1d" => "1d",
            "1w" => "1w",
            _ => "1m"
        };
    }

    private static string FormatDuration(int minutes)
    {
        if (minutes < 60)
            return $"{minutes} min";

        if (minutes < 1440)
            return $"{Math.Round(minutes / 60m, 1):0.#} h";

        return $"{Math.Round(minutes / 1440m, 1):0.#} dias";
    }

    private static decimal RoundPrice(decimal value)
    {
        var decimals = Math.Abs(value) switch { >= 1000m => 2, >= 1m => 4, _ => 8 };

        return Math.Round(value, decimals);
    }
}
