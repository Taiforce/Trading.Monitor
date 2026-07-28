using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface IHistoricalCandleRepository
{
    Task<int> UpsertAsync(string market, string source, IReadOnlyList<MarketCandle> candles, CancellationToken cancellationToken);
}
