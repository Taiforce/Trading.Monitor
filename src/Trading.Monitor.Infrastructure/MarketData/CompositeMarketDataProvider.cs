using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.MarketData;

public sealed class CompositeMarketDataProvider(
    IReadOnlyList<IMarketDataProvider> providers,
    ISourceTelemetryRecorder telemetryRecorder) : IMarketDataProvider
{
    public string Name => "Composite market data";

    public async Task<IReadOnlyList<MarketCandle>> GetCandlesAsync(string symbol, string interval, int limit, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        foreach (var provider in providers)
        {
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                var candles = await provider.GetCandlesAsync(symbol, interval, limit, cancellationToken);
                await telemetryRecorder.RecordAsync(
                    new DataSourceHealthEvent(
                        provider.Name,
                        DataSourceKind.MarketData,
                        candles.Count > 0 ? DataSourceStatus.Healthy : DataSourceStatus.Degraded,
                        null,
                        $"{symbol} {interval}: {candles.Count} candles.",
                        startedAt,
                        DateTimeOffset.UtcNow,
                        candles.Count),
                    cancellationToken);

                if (candles.Count > 0)
                {
                    return candles;
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{provider.Name}: {exception.Message}");
                await telemetryRecorder.RecordAsync(
                    new DataSourceHealthEvent(
                        provider.Name,
                        DataSourceKind.MarketData,
                        DataSourceStatus.Failed,
                        null,
                        $"{symbol} {interval}: {exception.Message}",
                        startedAt,
                        DateTimeOffset.UtcNow,
                        0),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException($"No market source returned {symbol} {interval}. {string.Join(" | ", errors)}");
    }
}
