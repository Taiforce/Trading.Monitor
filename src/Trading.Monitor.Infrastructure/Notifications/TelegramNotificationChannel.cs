using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Notifications;

public sealed class TelegramNotificationChannel(HttpClient httpClient, TelegramOptions options, OpportunityProjectionService projectionService, IOptionsMonitor<ReportingOptions> reportingOptions)
    : INotificationChannel
{
    public string Name => "telegram";

    public async Task SendAsync(TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(options.BotToken) || string.IsNullOrWhiteSpace(options.ChatId))
            throw new InvalidOperationException("Telegram notification is enabled but BotToken or ChatId is missing.");

        var projection = projectionService.Project(opportunity, reportingOptions.CurrentValue);
        var endpoint = $"https://api.telegram.org/bot{options.BotToken}/sendMessage";

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = options.ChatId, ["text"] = AlertFormatter.ToPlainText(opportunity, projection), ["disable_web_page_preview"] = "true"
        });

        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Telegram returned {(int)response.StatusCode}: {body}");
    }
}