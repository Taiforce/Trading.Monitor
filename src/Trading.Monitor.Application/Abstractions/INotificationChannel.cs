using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface INotificationChannel
{
    string Name { get; }

    Task SendAsync(TradingOpportunity opportunity, CancellationToken cancellationToken);
}