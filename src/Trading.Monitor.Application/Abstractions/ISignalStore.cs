using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface ISignalStore
{
    Task<bool> HasRecentSimilarSignalAsync(TradingOpportunity opportunity, TimeSpan duplicateWindow, CancellationToken cancellationToken);

    Task SaveAsync(TradingOpportunity opportunity, CancellationToken cancellationToken);
}