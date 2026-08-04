using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

/// <summary>
/// "Traders": produces signals derived from real, currently-open positions of top-performing
/// traders on a public exchange leaderboard (copy-trading style), tagged with
/// <see cref="SignalOriginKind.Trader"/>. Implementations should fail soft (return an empty
/// list and record telemetry) rather than throw, since these sources are typically unofficial
/// or rate-limited public endpoints that can change or go offline without notice.
/// </summary>
public interface ITraderSignalProvider
{
    string Name { get; }

    Task<IReadOnlyList<TradingOpportunity>> GetSignalsAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken);
}
