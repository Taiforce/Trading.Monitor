using Trading.Monitor.Domain;
using Trading.Monitor.Application.Reporting;

namespace Trading.Monitor.Application.Abstractions;

public interface INotificationChannel
{
    string Name { get; }

    Task SendAsync(TradingOpportunity opportunity, CancellationToken cancellationToken);

    Task SendExitAsync(OpportunityReportRow opportunity, OpportunityExit exit, decimal realizedNetPnL, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
