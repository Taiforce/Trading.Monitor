using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface ITradeExecutionRepository
{
    Task SaveAsync(TradeExecutionAudit audit, CancellationToken cancellationToken);

    Task<TradeExecutionAudit?> GetLatestEntryAsync(Guid opportunityId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TradeExecutionAudit>> GetRecentAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<TradeExecutionAudit>> GetRecentByStatusAsync(TradeExecutionStatus? status, int limit, CancellationToken cancellationToken);
}
