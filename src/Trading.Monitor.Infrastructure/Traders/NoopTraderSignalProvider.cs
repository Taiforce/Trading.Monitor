using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Traders;

public sealed class NoopTraderSignalProvider : ITraderSignalProvider
{
    public string Name => "trader-signals-disabled";

    public Task<IReadOnlyList<TradingOpportunity>> GetSignalsAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<TradingOpportunity>>([]);
    }
}
