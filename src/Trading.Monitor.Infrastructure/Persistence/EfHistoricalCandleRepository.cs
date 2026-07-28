using Microsoft.EntityFrameworkCore;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class EfHistoricalCandleRepository(TradingMonitorDbContext dbContext) : IHistoricalCandleRepository
{
    public async Task<int> UpsertAsync(string market, string source, IReadOnlyList<MarketCandle> candles, CancellationToken cancellationToken)
    {
        if (candles.Count == 0)
            return 0;

        var normalizedMarket = MarketSymbolClassifier.NormalizeMarket(market);
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "desconocida" : source.Trim();
        var symbol = MarketSymbolClassifier.NormalizeSymbol(candles[0].Symbol);
        var interval = candles[0].Interval;
        var openTimes = candles.Select(candle => candle.OpenTime).Distinct().ToArray();

        var existing = await dbContext.HistoricalMarketCandles
            .AsNoTracking()
            .Where(row => row.Symbol == symbol && row.Interval == interval && openTimes.Contains(row.OpenTime))
            .Select(row => row.OpenTime)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var candle in candles.OrderBy(candle => candle.OpenTime))
        {
            if (existingSet.Contains(candle.OpenTime))
                continue;

            dbContext.HistoricalMarketCandles.Add(new HistoricalMarketCandleEntity
            {
                Id = Guid.NewGuid(),
                Market = normalizedMarket,
                Source = normalizedSource,
                Symbol = symbol,
                Interval = interval,
                OpenTime = candle.OpenTime,
                CloseTime = candle.CloseTime,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Close = candle.Close,
                Volume = candle.Volume,
                QuoteVolume = candle.QuoteVolume,
                CreatedAt = now,
                UpdatedAt = now
            });
            added++;
        }

        if (added > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return added;
    }
}
