using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface ITradeExecutionService
{
    Task TryEnterAsync(OpportunityReportRow opportunity, CancellationToken cancellationToken);

    Task TryExitAsync(OpportunityReportRow opportunity, OpportunityExit exit, decimal realizedNetPnL, CancellationToken cancellationToken);
}
