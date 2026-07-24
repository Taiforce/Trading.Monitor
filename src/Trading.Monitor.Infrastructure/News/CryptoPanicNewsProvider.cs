using System.Text.Json;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.News;

public sealed class CryptoPanicNewsProvider(HttpClient httpClient, NewsOptions options, ISourceTelemetryRecorder telemetryRecorder) : INewsProvider
{
    public string Name => "CryptoPanic";

    public async Task<IReadOnlyList<NewsItem>> GetLatestAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var token = Environment.GetEnvironmentVariable(options.CryptoPanicAuthTokenEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(token))
        {
            await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                Name,
                DataSourceKind.News,
                DataSourceStatus.Degraded,
                "https://cryptopanic.com/developers/api/",
                $"Set {options.CryptoPanicAuthTokenEnvironmentVariable} to enable CryptoPanic structured news.",
                startedAt,
                DateTimeOffset.UtcNow,
                0), cancellationToken);

            return [];
        }

        try
        {
            var currencies = string.Join(",", symbols.Select(MapCurrency).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
            var requestUri = $"/api/v1/posts/?auth_token={Uri.EscapeDataString(token)}&public=true&kind=news&currencies={Uri.EscapeDataString(currencies)}";

            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"CryptoPanic returned {(int)response.StatusCode}: {body}");

            using var document = JsonDocument.Parse(body);
            var items = new List<NewsItem>();

            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return [];

            foreach (var result in results.EnumerateArray().Take(30))
            {
                var title = ReadString(result, "title");

                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var matchedSymbols = ResolveSymbols(result, title, symbols);

                if (matchedSymbols.Count == 0)
                    continue;

                items.Add(new NewsItem(
                    Name,
                    title,
                    ReadString(result, "url"),
                    DateTimeOffset.TryParse(ReadString(result, "published_at"), out var publishedAt) ? publishedAt.ToUniversalTime() : DateTimeOffset.UtcNow,
                    ResolveSentiment(result, title),
                    matchedSymbols));
            }

            await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                Name,
                DataSourceKind.News,
                items.Count > 0 ? DataSourceStatus.Healthy : DataSourceStatus.Degraded,
                "https://cryptopanic.com/developers/api/",
                $"{items.Count} structured news items.",
                startedAt,
                DateTimeOffset.UtcNow,
                items.Count), cancellationToken);

            return items;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                Name,
                DataSourceKind.News,
                DataSourceStatus.Failed,
                "https://cryptopanic.com/developers/api/",
                exception.Message,
                startedAt,
                DateTimeOffset.UtcNow,
                0), cancellationToken);

            return [];
        }
    }

    private static string MapCurrency(string symbol)
    {
        return symbol.ToUpperInvariant() switch
        {
            "BTCUSDT" or "BTCUSD" => "BTC",
            "ETHUSDT" or "ETHUSD" => "ETH",
            "SOLUSDT" or "SOLUSD" => "SOL",
            "XRPUSDT" or "XRPUSD" => "XRP",
            _ when symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) => symbol[..^4].ToUpperInvariant(),
            _ when symbol.EndsWith("USD", StringComparison.OrdinalIgnoreCase) => symbol[..^3].ToUpperInvariant(),
            _ => symbol.ToUpperInvariant()
        };
    }

    private IReadOnlyList<string> ResolveSymbols(JsonElement item, string title, IReadOnlyCollection<string> symbols)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (item.TryGetProperty("currencies", out var currencies) && currencies.ValueKind == JsonValueKind.Array)
        {
            foreach (var currency in currencies.EnumerateArray())
            {
                var code = ReadString(currency, "code");
                if (!string.IsNullOrWhiteSpace(code))
                    codes.Add(code);
            }
        }

        return symbols.Where(symbol =>
        {
            var mapped = MapCurrency(symbol);
            if (codes.Contains(mapped))
                return true;

            var keywords = options.SymbolKeywords.TryGetValue(symbol, out var configured) ? configured : [symbol, mapped];
            return keywords.Any(keyword => title.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }).ToArray();
    }

    private static NewsSentiment ResolveSentiment(JsonElement item, string title)
    {
        if (item.TryGetProperty("votes", out var votes) && votes.ValueKind == JsonValueKind.Object)
        {
            var positive = ReadInt(votes, "positive") + ReadInt(votes, "liked") + ReadInt(votes, "important");
            var negative = ReadInt(votes, "negative") + ReadInt(votes, "disliked");

            if (positive > negative)
                return NewsSentiment.Positive;

            if (negative > positive)
                return NewsSentiment.Negative;
        }

        return title.Contains("hack", StringComparison.OrdinalIgnoreCase) || title.Contains("lawsuit", StringComparison.OrdinalIgnoreCase) || title.Contains("outflow", StringComparison.OrdinalIgnoreCase)
            ? NewsSentiment.Negative
            : title.Contains("rally", StringComparison.OrdinalIgnoreCase) || title.Contains("inflow", StringComparison.OrdinalIgnoreCase) || title.Contains("breakout", StringComparison.OrdinalIgnoreCase)
                ? NewsSentiment.Positive
                : NewsSentiment.Neutral;
    }

    private static string ReadString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var property) ? property.GetString() ?? "" : "";
    }

    private static int ReadInt(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return 0;

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value) ? value : 0;
    }
}
