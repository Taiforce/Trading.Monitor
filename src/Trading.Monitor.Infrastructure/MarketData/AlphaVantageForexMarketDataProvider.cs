using System.Globalization;
using System.Text.Json;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.MarketData;

public sealed class AlphaVantageForexMarketDataProvider(HttpClient httpClient, string apiKey) : IMarketDataProvider
{
    public string Name => "Alpha Vantage Forex";

    public async Task<IReadOnlyList<MarketCandle>> GetCandlesAsync(string symbol, string interval, int limit, CancellationToken cancellationToken)
    {
        var normalizedSymbol = MarketSymbolClassifier.NormalizeSymbol(symbol);
        if (MarketSymbolClassifier.GetMarketKind(normalizedSymbol) != MarketKind.Forex || string.IsNullOrWhiteSpace(apiKey))
            return [];

        var from = MarketSymbolClassifier.BaseAsset(normalizedSymbol);
        var to = MarketSymbolClassifier.QuoteAsset(normalizedSymbol);
        var request = BuildRequest(from, to, interval);
        if (request is null)
            return [];

        using var response = await httpClient.GetAsync(request.Value.RequestUri, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Alpha Vantage returned {(int)response.StatusCode}: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("Information", out var information))
            throw new InvalidOperationException(information.GetString() ?? "Alpha Vantage rate limit or entitlement response.");

        if (document.RootElement.TryGetProperty("Note", out var note))
            throw new InvalidOperationException(note.GetString() ?? "Alpha Vantage rate limit response.");

        var candles = ParseCandles(document, normalizedSymbol, request.Value.SeriesName, request.Value.SourceInterval);
        if (request.Value.TargetInterval != request.Value.SourceInterval)
            candles = AggregateCandles(candles, normalizedSymbol, request.Value.TargetInterval, TimeSpan.FromHours(4));

        return candles
            .OrderBy(candle => candle.OpenTime)
            .TakeLast(Math.Clamp(limit, 1, 500))
            .ToArray();
    }

    private AlphaVantageRequest? BuildRequest(string from, string to, string interval)
    {
        var normalizedInterval = NormalizeInterval(interval);

        return normalizedInterval switch
        {
            "1s" => null,
            "1m" => Intraday(from, to, "1min", "1m"),
            "5m" => Intraday(from, to, "5min", "5m"),
            "15m" => Intraday(from, to, "15min", "15m"),
            "30m" => Intraday(from, to, "30min", "30m"),
            "1h" => Intraday(from, to, "60min", "1h"),
            "4h" => Intraday(from, to, "60min", "4h"),
            "1d" => new AlphaVantageRequest($"/query?function=FX_DAILY&from_symbol={from}&to_symbol={to}&outputsize=full&apikey={Uri.EscapeDataString(apiKey)}", "Time Series FX (Daily)", "1d", "1d"),
            "1w" => new AlphaVantageRequest($"/query?function=FX_WEEKLY&from_symbol={from}&to_symbol={to}&apikey={Uri.EscapeDataString(apiKey)}", "Time Series FX (Weekly)", "1w", "1w"),
            "1M" => new AlphaVantageRequest($"/query?function=FX_MONTHLY&from_symbol={from}&to_symbol={to}&apikey={Uri.EscapeDataString(apiKey)}", "Time Series FX (Monthly)", "1M", "1M"),
            _ => Intraday(from, to, "1min", "1m")
        };
    }

    private AlphaVantageRequest Intraday(string from, string to, string interval, string targetInterval)
    {
        return new AlphaVantageRequest($"/query?function=FX_INTRADAY&from_symbol={from}&to_symbol={to}&interval={interval}&outputsize=full&apikey={Uri.EscapeDataString(apiKey)}",
            $"Time Series FX ({interval})",
            interval == "60min" ? "1h" : interval.Replace("min", "m", StringComparison.Ordinal),
            targetInterval);
    }

    private static IReadOnlyList<MarketCandle> ParseCandles(JsonDocument document, string symbol, string seriesName, string interval)
    {
        if (!document.RootElement.TryGetProperty(seriesName, out var series) || series.ValueKind != JsonValueKind.Object)
            return [];

        var candles = new List<MarketCandle>();
        foreach (var property in series.EnumerateObject())
        {
            if (!DateTimeOffset.TryParse(property.Name, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var openTime))
                continue;

            var item = property.Value;
            var open = ReadDecimal(item, "1. open");
            var high = ReadDecimal(item, "2. high");
            var low = ReadDecimal(item, "3. low");
            var close = ReadDecimal(item, "4. close");
            var volume = item.TryGetProperty("5. volume", out var volumeElement) ? ReadDecimal(volumeElement) : 0m;

            if (open <= 0m || high <= 0m || low <= 0m || close <= 0m)
                continue;

            candles.Add(new MarketCandle(symbol, interval, openTime.ToUniversalTime(), openTime.ToUniversalTime().Add(IntervalDuration(interval)), open, high, low, close, volume, volume * close));
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

                return new MarketCandle(symbol, targetInterval, open.OpenTime, close.CloseTime, open.Open, values.Max(candle => candle.High), values.Min(candle => candle.Low), close.Close, volume,
                    values.Sum(candle => candle.QuoteVolume));
            })
            .ToArray();
    }

    private static decimal ReadDecimal(JsonElement item, string property)
    {
        return item.TryGetProperty(property, out var element) ? ReadDecimal(element) : 0m;
    }

    private static decimal ReadDecimal(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Number
            ? element.GetDecimal()
            : decimal.Parse(element.GetString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
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

    private readonly record struct AlphaVantageRequest(string RequestUri, string SeriesName, string SourceInterval, string TargetInterval);
}
