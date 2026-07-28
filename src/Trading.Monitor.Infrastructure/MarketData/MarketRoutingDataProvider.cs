using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.MarketData;

public sealed class MarketRoutingDataProvider(
    IReadOnlyList<IMarketDataProvider> cryptoProviders,
    IReadOnlyList<IMarketDataProvider> forexProviders,
    ISourceTelemetryRecorder telemetryRecorder) : IMarketDataProvider
{
    public string Name => "Market routing data";

    public Task<IReadOnlyList<MarketCandle>> GetCandlesAsync(string symbol, string interval, int limit, CancellationToken cancellationToken)
    {
        var providers = MarketSymbolClassifier.GetMarketKind(symbol) == MarketKind.Forex
            ? forexProviders
            : cryptoProviders;

        if (providers.Count == 0)
            providers = cryptoProviders.Concat(forexProviders).ToArray();

        return new CompositeMarketDataProvider(providers, telemetryRecorder).GetCandlesAsync(symbol, interval, limit, cancellationToken);
    }
}
