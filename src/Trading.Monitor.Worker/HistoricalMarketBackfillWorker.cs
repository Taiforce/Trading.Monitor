using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Services;

namespace Trading.Monitor.Worker;

public sealed class HistoricalMarketBackfillWorker(IServiceScopeFactory scopeFactory, ILogger<HistoricalMarketBackfillWorker> logger) : BackgroundService
{
    private static readonly (string Interval, int Limit)[] Intervals =
    [
        ("1d", 760),
        ("1w", 120),
        ("1M", 36)
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(12), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BackfillOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Historical candle backfill failed. Worker will retry later.");
            }

            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }

    private async Task BackfillOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
        var repository = scope.ServiceProvider.GetRequiredService<IHistoricalCandleRepository>();
        var totalAdded = 0;

        totalAdded += await BackfillMarketAsync(MarketSymbolClassifier.CryptoMarket, MarketSymbolClassifier.DefaultCryptoSymbols, marketDataProvider, repository, cancellationToken);
        totalAdded += await BackfillMarketAsync(MarketSymbolClassifier.ForexMarket, MarketSymbolClassifier.DefaultForexSymbols, marketDataProvider, repository, cancellationToken);

        logger.LogInformation("Historical candle backfill completed. Added {AddedCount} new candles.", totalAdded);
    }

    private async Task<int> BackfillMarketAsync(string market, IReadOnlyList<string> symbols, IMarketDataProvider marketDataProvider, IHistoricalCandleRepository repository, CancellationToken cancellationToken)
    {
        var added = 0;

        foreach (var symbol in symbols)
        {
            foreach (var (interval, limit) in Intervals)
            {
                try
                {
                    var candles = await marketDataProvider.GetCandlesAsync(symbol, interval, limit, cancellationToken);
                    var saved = await repository.UpsertAsync(market, marketDataProvider.Name, candles, cancellationToken);
                    added += saved;
                    logger.LogInformation("Historical candles {Market} {Symbol} {Interval}: received {Received}, saved {Saved}.", market, symbol, interval, candles.Count, saved);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Could not backfill historical candles for {Market} {Symbol} {Interval}. Continuing.", market, symbol, interval);
                }
            }
        }

        return added;
    }
}
