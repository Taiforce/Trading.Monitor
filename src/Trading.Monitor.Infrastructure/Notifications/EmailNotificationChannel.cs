using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Notifications;

public sealed class EmailNotificationChannel(EmailOptions options, OpportunityProjectionService projectionService, IOptionsMonitor<ReportingOptions> reportingOptions) : INotificationChannel
{
    public string Name => "email";

    public async Task SendAsync(TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.From) || string.IsNullOrWhiteSpace(options.To))
            throw new InvalidOperationException("Email notification is enabled but SMTP Host, From, or To is missing.");

        var projection = projectionService.Project(opportunity, reportingOptions.CurrentValue);

        using var message = new MailMessage(options.From, options.To)
        {
            Subject = $"{opportunity.Symbol} {opportunity.Side} {opportunity.Score}/100", Body = AlertFormatter.ToHtml(opportunity, projection), IsBodyHtml = true
        };

        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.UseSsl };

        if (!string.IsNullOrWhiteSpace(options.UserName))
            client.Credentials = new NetworkCredential(options.UserName, options.Password);

        await client.SendMailAsync(message, cancellationToken);
    }
}