using System.Globalization;
using System.Text.Json;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.MarketData;

public sealed class YahooFinanceForexMarketDataProvider(HttpClient httpClient) : IMarketDataProvider
{
    public string Name => "Yahoo Finance Forex chart";

    public async Task<IReadOnlyList<MarketCandle>> GetCandlesAsync(string symbol, string interval, int limit, CancellationToken cancellationToken)
    {
        var normalizedSymbol = MarketSymbolClassifier.NormalizeSymbol(symbol);
        if (MarketSymbolClassifier.GetMarketKind(normalizedSymbol) != MarketKind.Forex)
            return [];

        var request = BuildRequest(normalizedSymbol, interval);
        if (request is null)
            return [];

        using var response = await httpClient.GetAsync(request.Value.RequestUri, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Yahoo Finance returned {(int)response.StatusCode}: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        var candles = ParseCandles(document, normalizedSymbol, request.Value.SourceInterval);
        if (request.Value.TargetInterval != request.Value.SourceInterval)
            candles = AggregateCandles(candles, normalizedSymbol, request.Value.TargetInterval, request.Value.AggregateWindow);

        return candles
            .OrderBy(candle => candle.OpenTime)
            .TakeLast(Math.Clamp(limit, 1, 500))
            .ToArray();
    }

    private static YahooRequest? BuildRequest(string symbol, string interval)
    {
        var normalizedInterval = NormalizeInterval(interval);
        var yahooSymbol = $"{symbol}=X";

        return normalizedInterval switch
        {
            "1s" => null,
            "1m" => new YahooRequest($"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=5d&interval=1m", "1m", "1m", TimeSpan.FromMinutes(1)),
            "5m" => new YahooRequest($"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=1mo&interval=5m", "5m", "5m", TimeSpan.FromMinutes(5)),
            "15m" => new YahooRequest($"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=1mo&interval=15m", "15m", "15m", TimeSpan.FromMinutes(15)),
            "30m" => new YahooRequest($"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=1mo&interval=30m", "30m", "30m", TimeSpan.FromMinutes(30)),
            "1h" => new YahooRequest($"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=6mo&interval=1h", "1h", "1h", TimeSpan.FromHours(1)),
            "4h" => new YahooRequest($"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=6mo&interval=1h", "1h", "4h", TimeSpan.FromHours(4)),
            "1d" => new YahooRequest($"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=2y&interval=1d", "1d", "1d", TimeSpan.FromDays(1)),
            "1w" => new YahooRequest($"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=10y&interval=1wk", "1w", "1w", TimeSpan.FromDays(7)),
            "1M" => new YahooRequest($"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=10y&interval=1mo", "1M", "1M", TimeSpan.FromDays(30)),
            _ => new YahooRequest($"/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?range=5d&interval=1m", "1m", "1m", TimeSpan.FromMinutes(1))
        };
    }

    private static IReadOnlyList<MarketCandle> ParseCandles(JsonDocument document, string symbol, string interval)
    {
        if (!document.RootElement.TryGetProperty("chart", out var chart)
            || !chart.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array
            || result.GetArrayLength() == 0)
        {
            return [];
        }

        var series = result[0];
        if (!series.TryGetProperty("timestamp", out var timestamps)
            || !series.TryGetProperty("indicators", out var indicators)
            || !indicators.TryGetProperty("quote", out var quotes)
            || quotes.ValueKind != JsonValueKind.Array
            || quotes.GetArrayLength() == 0)
        {
            return [];
        }

        var quote = quotes[0];
        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.TryGetProperty("volume", out var volumeValues) ? volumeValues : default;
        var candles = new List<MarketCandle>();
        var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());

        for (var index = 0; index < count; index++)
        {
            if (!TryReadDecimal(opens[index], out var open)
                || !TryReadDecimal(highs[index], out var high)
                || !TryReadDecimal(lows[index], out var low)
                || !TryReadDecimal(closes[index], out var close))
            {
                continue;
            }

            var openTime = DateTimeOffset.FromUnixTimeSeconds(timestamps[index].GetInt64());
            var closeTime = openTime.Add(IntervalDuration(interval));
            var volume = volumes.ValueKind == JsonValueKind.Array && index < volumes.GetArrayLength() && TryReadDecimal(volumes[index], out var parsedVolume)
                ? parsedVolume
                : 0m;

            candles.Add(new MarketCandle(symbol, interval, openTime, closeTime, open, high, low, close, volume, volume * close));
        }

        return candles;
    }

    private static IReadOnlyList<MarketCandle> AggregateCandles(IReadOnlyList<MarketCandle> candles, string symbol, string targetInterval, TimeSpan window)
    {
        if (candles.Count == 0)
            return [];

        return candles
            .OrderBy(candle => candle.OpenTime)
            .Select((candle, index) => new { candle, Bucket = index / Math.Max(1, (int)Math.Round(window.TotalSeconds / IntervalDuration(candle.Interval).TotalSeconds)) })
            .GroupBy(item => item.Bucket)
            .Select(group =>
            {
                var values = group.Select(item => item.candle).OrderBy(candle => candle.OpenTime).ToArray();
                var open = values[0];
                var close = values[^1];
                var volume = values.Sum(candle => candle.Volume);

                return new MarketCandle(
                    symbol,
                    targetInterval,
                    open.OpenTime,
                    close.CloseTime,
                    open.Open,
                    values.Max(candle => candle.High),
                    values.Min(candle => candle.Low),
                    close.Close,
                    volume,
                    values.Sum(candle => candle.QuoteVolume));
            })
            .Where(candle => candle.OpenTime < candle.CloseTime)
            .ToArray();
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        value = 0m;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static string NormalizeInterval(string interval)
    {
        var value = interval.Trim();
        if (string.Equals(value, "1M", StringComparison.Ordinal) || string.Equals(value, "1mo", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "1month", StringComparison.OrdinalIgnoreCase))
            return "1M";

        return value.ToLowerInvariant() switch
        {
            "1s" => "1s",
            "1m" => "1m",
            "5m" => "5m",
            "15m" => "15m",
            "30m" => "30m",
            "1hr" or "1h" => "1h",
            "4h" => "4h",
            "1d" => "1d",
            "1w" => "1w",
            _ => "1m"
        };
    }

    private static TimeSpan IntervalDuration(string interval)
    {
        return interval switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h" => TimeSpan.FromHours(1),
            "4h" => TimeSpan.FromHours(4),
            "1d" => TimeSpan.FromDays(1),
            "1w" => TimeSpan.FromDays(7),
            "1M" => TimeSpan.FromDays(30),
            _ => TimeSpan.FromMinutes(1)
        };
    }

    private readonly record struct YahooRequest(string RequestUri, string SourceInterval, string TargetInterval, TimeSpan AggregateWindow);
}
