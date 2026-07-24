using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface IOpportunityRepository : ISignalStore
{
    Task<IReadOnlyList<OpportunityReportRow>> GetRecentAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<OpportunityReportRow>> GetSignalsAsync(decimal capital, CancellationToken cancellationToken);

    Task<IReadOnlyList<OpportunityReportRow>> GetOpenAsync(CancellationToken cancellationToken);

    Task UpdateExitAsync(Guid id, OpportunityExit exit, decimal realizedGrossPnL, decimal realizedNetPnL, CancellationToken cancellationToken);

    Task<DashboardReport> GetDashboardReportAsync(decimal capital, CancellationToken cancellationToken);
}
