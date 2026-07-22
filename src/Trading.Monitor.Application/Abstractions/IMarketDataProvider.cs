using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface IMarketDataProvider
{
    string Name { get; }

    Task<IReadOnlyList<MarketCandle>> GetCandlesAsync(string symbol, string interval, int limit, CancellationToken cancellationToken);
}
