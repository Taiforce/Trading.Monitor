using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Notifications;

public sealed class TelegramNotificationChannel(HttpClient httpClient, TelegramOptions options, OpportunityProjectionService projectionService, TradeInstructionService instructionService,
    IOptionsMonitor<ReportingOptions> reportingOptions)
    : INotificationChannel
{
    public string Name => "telegram";

    public async Task SendAsync(TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return;

        var botToken = options.ResolveBotToken();
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(options.ChatId))
            throw new InvalidOperationException("Telegram notification is enabled but BotToken/BotTokenEnvironmentVariable or ChatId is missing.");

        var projection = projectionService.Project(opportunity, reportingOptions.CurrentValue);
        var instruction = instructionService.Create(opportunity, projection);

        await PostAsync(botToken, AlertFormatter.ToPlainText(opportunity, projection, instruction), cancellationToken);
    }

    public async Task SendExitAsync(OpportunityReportRow opportunity, OpportunityExit exit, decimal realizedNetPnL, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return;

        var botToken = options.ResolveBotToken();
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(options.ChatId))
            throw new InvalidOperationException("Telegram notification is enabled but BotToken/BotTokenEnvironmentVariable or ChatId is missing.");

        var instruction = instructionService.CreateExit(opportunity, exit, realizedNetPnL);

        await PostAsync(botToken, AlertFormatter.ToExitPlainText(opportunity, exit, realizedNetPnL, instruction), cancellationToken);
    }

    private async Task PostAsync(string botToken, string text, CancellationToken cancellationToken)
    {
        // The bot token only ever travels inside this request URI, never in a log message or
        // exception text, to avoid leaking it through proxy/telemetry logs.
        var endpoint = $"https://api.telegram.org/bot{botToken}/sendMessage";

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = options.ChatId, ["text"] = text, ["disable_web_page_preview"] = "true"
        });

        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Telegram returned HTTP {(int)response.StatusCode}.");
    }
}
