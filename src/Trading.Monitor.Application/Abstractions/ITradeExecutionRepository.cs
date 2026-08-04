using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface ITradeExecutionRepository
{
    Task SaveAsync(TradeExecutionAudit audit, CancellationToken cancellationToken);

    Task<TradeExecutionAudit?> GetLatestEntryAsync(Guid opportunityId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TradeExecutionAudit>> GetRecentAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<TradeExecutionAudit>> GetRecentByStatusAsync(TradeExecutionStatus? status, int limit, CancellationToken cancellationToken);

    /// <summary>Number of opportunities with a successful open (Simulated/Submitted/Filled) that have no matching successful close yet.</summary>
    Task<int> GetOpenPositionCountAsync(CancellationToken cancellationToken);

    /// <summary>Sum of requested capital for successful entries created since <paramref name="since"/>, used to enforce a rolling daily notional cap.</summary>
    Task<decimal> GetEntryNotionalSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
