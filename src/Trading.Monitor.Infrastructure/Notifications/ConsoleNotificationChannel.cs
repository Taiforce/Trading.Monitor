using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Notifications;

public sealed class ConsoleNotificationChannel(OpportunityProjectionService projectionService, IOptionsMonitor<ReportingOptions> reportingOptions) : INotificationChannel
{
    public string Name => "console";

    public Task SendAsync(TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        var projection = projectionService.Project(opportunity, reportingOptions.CurrentValue);
        Console.WriteLine(AlertFormatter.ToPlainText(opportunity, projection));
        return Task.CompletedTask;
    }
}