using System.Text.Json;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.News;

public sealed class FearGreedNewsProvider(HttpClient httpClient, ISourceTelemetryRecorder telemetryRecorder) : INewsProvider
{
    public string Name => "Alternative.me Fear & Greed";

    public async Task<IReadOnlyList<NewsItem>> GetLatestAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            using var response = await httpClient.GetAsync("/fng/?limit=1&format=json", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Fear & Greed returned {(int)response.StatusCode}: {body}");

            using var document = JsonDocument.Parse(body);
            var first = document.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault();

            if (first.ValueKind != JsonValueKind.Object)
                return [];

            var value = first.TryGetProperty("value", out var valueElement) ? valueElement.GetString() ?? "" : "";
            var classification = first.TryGetProperty("value_classification", out var classElement) ? classElement.GetString() ?? "Neutral" : "Neutral";
            var timestamp = first.TryGetProperty("timestamp", out var timestampElement) && long.TryParse(timestampElement.GetString(), out var epoch)
                ? DateTimeOffset.FromUnixTimeSeconds(epoch)
                : DateTimeOffset.UtcNow;

            var sentiment = classification.Contains("Greed", StringComparison.OrdinalIgnoreCase)
                ? NewsSentiment.Positive
                : classification.Contains("Fear", StringComparison.OrdinalIgnoreCase)
                    ? NewsSentiment.Negative
                    : NewsSentiment.Neutral;

            var item = new NewsItem(Name, $"Crypto Fear & Greed Index: {classification} ({value}/100)", "https://alternative.me/crypto/fear-and-greed-index/", timestamp, sentiment, symbols.ToArray());

            await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                Name,
                DataSourceKind.News,
                DataSourceStatus.Healthy,
                "https://api.alternative.me/fng/",
                item.Title,
                startedAt,
                DateTimeOffset.UtcNow,
                1), cancellationToken);

            return [item];
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                Name,
                DataSourceKind.News,
                DataSourceStatus.Failed,
                "https://api.alternative.me/fng/",
                exception.Message,
                startedAt,
                DateTimeOffset.UtcNow,
                0), cancellationToken);

            return [];
        }
    }
}
