using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Notifications;

public sealed class ConsoleNotificationChannel(OpportunityProjectionService projectionService, TradeInstructionService instructionService, IOptionsMonitor<ReportingOptions> reportingOptions) : INotificationChannel
{
    public string Name => "console";

    public Task SendAsync(TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        var projection = projectionService.Project(opportunity, reportingOptions.CurrentValue);
        var instruction = instructionService.Create(opportunity, projection);
        Console.WriteLine(AlertFormatter.ToPlainText(opportunity, projection, instruction));
        return Task.CompletedTask;
    }

    public Task SendExitAsync(OpportunityReportRow opportunity, OpportunityExit exit, decimal realizedNetPnL, CancellationToken cancellationToken)
    {
        var instruction = instructionService.CreateExit(opportunity, exit, realizedNetPnL);
        Console.WriteLine(AlertFormatter.ToExitPlainText(opportunity, exit, realizedNetPnL, instruction));
        return Task.CompletedTask;
    }
}
