using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed class MarketScanner(
    IMarketDataProvider marketDataProvider,
    INewsProvider newsProvider,
    IResearchAnalyzer researchAnalyzer,
    ISourceTelemetryRecorder telemetryRecorder,
    TradingSignalEngine signalEngine)
{
    public async Task<MarketScanResult> ScanAsync(TradingMonitorOptions monitorOptions, RiskOptions riskOptions, NewsOptions newsOptions, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var opportunities = new List<TradingOpportunity>();
        var symbols = monitorOptions.Symbols.Where(symbol => !string.IsNullOrWhiteSpace(symbol)).Select(symbol => symbol.Trim().ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var horizons = ResolveHorizons(monitorOptions);
        var intervals = monitorOptions.Intervals.Where(interval => !string.IsNullOrWhiteSpace(interval))
                                      .Concat(horizons.Select(horizon => horizon.TriggerInterval))
                                      .Concat(horizons.SelectMany(horizon => horizon.RequiredAlignedIntervals))
                                      .Where(interval => !string.IsNullOrWhiteSpace(interval))
                                      .Select(NormalizeInterval)
                                      .Distinct(StringComparer.Ordinal)
                                      .ToArray();

        var latestNews = Array.Empty<NewsItem>() as IReadOnlyList<NewsItem>;

        if (newsOptions.Enabled)
        {
            try
            {
                latestNews = await newsProvider.GetLatestAsync(symbols, cancellationToken);
                await telemetryRecorder.SaveResearchItemsAsync(latestNews, DataSourceKind.News, cancellationToken);
            }
            catch (Exception exception)
            {
                errors.Add($"News: {exception.Message}");
            }
        }

        try
        {
            var aiResearch = await researchAnalyzer.AnalyzeAsync(symbols, latestNews, cancellationToken);
            if (aiResearch.Count > 0)
            {
                await telemetryRecorder.SaveResearchItemsAsync(aiResearch, DataSourceKind.AiAnalysis, cancellationToken);
                latestNews = latestNews.Concat(aiResearch).ToArray();
            }
        }
        catch (Exception exception)
        {
            errors.Add($"{researchAnalyzer.Name}: {exception.Message}");
        }

        foreach (var symbol in symbols)
        {
            var candlesByInterval = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.Ordinal);

            foreach (var interval in intervals)
            {
                try
                {
                    var candles = await marketDataProvider.GetCandlesAsync(symbol, interval, monitorOptions.CandleLimit, cancellationToken);

                    if (candles.Count >= 60)
                        candlesByInterval[interval] = candles;
                    else
                        errors.Add($"{symbol} {interval}: only {candles.Count} candles returned.");
                }
                catch (Exception exception)
                {
                    errors.Add($"{symbol} {interval}: {exception.Message}");
                }
            }

            try
            {
                foreach (var horizon in horizons)
                {
                    var opportunity = signalEngine.Evaluate(symbol, candlesByInterval, latestNews, monitorOptions, riskOptions, horizon);

                    if (opportunity is not null)
                        opportunities.Add(opportunity);
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{symbol}: evaluation failed: {exception.Message}");
            }
        }

        return new MarketScanResult(opportunities, errors);
    }

    private static IReadOnlyList<TradingHorizonOptions> ResolveHorizons(TradingMonitorOptions monitorOptions)
    {
        if (monitorOptions.Horizons is { Length: > 0 })
            return monitorOptions.Horizons
                .Where(horizon => !string.IsNullOrWhiteSpace(horizon.TriggerInterval))
                .Select(horizon => new TradingHorizonOptions
                {
                    Name = string.IsNullOrWhiteSpace(horizon.Name) ? NormalizeInterval(horizon.TriggerInterval) : horizon.Name.Trim(),
                    TriggerInterval = NormalizeInterval(horizon.TriggerInterval),
                    SignalExpiryMinutes = horizon.SignalExpiryMinutes,
                    MinimumScore = horizon.MinimumScore,
                    MinimumConfirmedIntervals = horizon.MinimumConfirmedIntervals,
                    RequiredAlignedIntervals = horizon.RequiredAlignedIntervals.Select(NormalizeInterval).Distinct(StringComparer.Ordinal).ToArray()
                })
                .ToArray();

        return
        [
            new TradingHorizonOptions
            {
                Name = NormalizeInterval(monitorOptions.TriggerInterval),
                TriggerInterval = NormalizeInterval(monitorOptions.TriggerInterval),
                SignalExpiryMinutes = monitorOptions.SignalExpiryMinutes,
                MinimumScore = monitorOptions.MinimumScore,
                MinimumConfirmedIntervals = 2,
                RequiredAlignedIntervals = []
            }
        ];
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
}
