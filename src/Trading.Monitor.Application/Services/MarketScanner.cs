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

        var intervals = monitorOptions.Intervals.Where(interval => !string.IsNullOrWhiteSpace(interval))
                                      .Select(interval => interval.Trim().ToLowerInvariant())
                                      .Distinct(StringComparer.OrdinalIgnoreCase)
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
            var candlesByInterval = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase);

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
                var opportunity = signalEngine.Evaluate(symbol, candlesByInterval, latestNews, monitorOptions, riskOptions);

                if (opportunity is not null)
                    opportunities.Add(opportunity);
            }
            catch (Exception exception)
            {
                errors.Add($"{symbol}: evaluation failed: {exception.Message}");
            }
        }

        return new MarketScanResult(opportunities, errors);
    }
}
