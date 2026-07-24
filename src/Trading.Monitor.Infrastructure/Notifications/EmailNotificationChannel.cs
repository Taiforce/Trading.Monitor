using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Notifications;

public sealed class EmailNotificationChannel(EmailOptions options, OpportunityProjectionService projectionService, TradeInstructionService instructionService, IOptionsMonitor<ReportingOptions> reportingOptions)
    : INotificationChannel
{
    public string Name => "email";

    public async Task SendAsync(TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.From) || string.IsNullOrWhiteSpace(options.To))
            throw new InvalidOperationException("Email notification is enabled but SMTP Host, From, or To is missing.");

        var projection = projectionService.Project(opportunity, reportingOptions.CurrentValue);
        var instruction = instructionService.Create(opportunity, projection);
        var signalType = SignalTypeDescriptor.Label(opportunity.Side);

        using var message = new MailMessage(options.From, options.To)
        {
            Subject = $"{instruction.ActionLabel}: {opportunity.Symbol} {signalType} {opportunity.Score}/100", Body = AlertFormatter.ToHtml(opportunity, projection, instruction), IsBodyHtml = true
        };

        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.UseSsl };

        if (!string.IsNullOrWhiteSpace(options.UserName))
            client.Credentials = new NetworkCredential(options.UserName, options.Password);

        await client.SendMailAsync(message, cancellationToken);
    }

    public async Task SendExitAsync(OpportunityReportRow opportunity, OpportunityExit exit, decimal realizedNetPnL, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.From) || string.IsNullOrWhiteSpace(options.To))
            throw new InvalidOperationException("Email notification is enabled but SMTP Host, From, or To is missing.");

        var instruction = instructionService.CreateExit(opportunity, exit, realizedNetPnL);
        var signalType = SignalTypeDescriptor.Label(opportunity.Side);

        using var message = new MailMessage(options.From, options.To)
        {
            Subject = $"{instruction.ActionLabel}: {opportunity.Symbol} {signalType}", Body = AlertFormatter.ToExitHtml(opportunity, exit, realizedNetPnL, instruction), IsBodyHtml = true
        };

        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.UseSsl };

        if (!string.IsNullOrWhiteSpace(options.UserName))
            client.Credentials = new NetworkCredential(options.UserName, options.Password);

        await client.SendMailAsync(message, cancellationToken);
    }
}
