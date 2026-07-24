using System.Globalization;
using System.Text.Json;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.MarketData;

public sealed class KrakenMarketDataProvider(HttpClient httpClient) : IMarketDataProvider
{
    public string Name => "Kraken";

    public async Task<IReadOnlyList<MarketCandle>> GetCandlesAsync(string symbol, string interval, int limit, CancellationToken cancellationToken)
    {
        var pair = MapPair(symbol);
        var minutes = MapIntervalMinutes(interval);
        var requestUri = $"/0/public/OHLC?pair={Uri.EscapeDataString(pair)}&interval={minutes}";

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Kraken returned {(int)response.StatusCode}: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var errors = document.RootElement.GetProperty("error").EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException($"Kraken error: {string.Join(", ", errors)}");
        }

        var result = document.RootElement.GetProperty("result");
        var data = result.EnumerateObject().FirstOrDefault(property => !string.Equals(property.Name, "last", StringComparison.OrdinalIgnoreCase));
        if (data.Value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var candles = new List<MarketCandle>();
        foreach (var item in data.Value.EnumerateArray().TakeLast(Math.Clamp(limit, 1, 720)))
        {
            var values = item.EnumerateArray().ToArray();
            var openTime = DateTimeOffset.FromUnixTimeSeconds(values[0].GetInt64());
            var closeTime = openTime.AddMinutes(minutes);
            var open = ParseDecimal(values[1]);
            var high = ParseDecimal(values[2]);
            var low = ParseDecimal(values[3]);
            var close = ParseDecimal(values[4]);
            var volume = ParseDecimal(values[6]);
            candles.Add(new MarketCandle(symbol, interval, openTime, closeTime, open, high, low, close, volume, volume * close));
        }

        return candles.OrderBy(candle => candle.OpenTime).ToArray();
    }

    private static string MapPair(string symbol)
    {
        return symbol.ToUpperInvariant() switch
        {
            "BTCUSDT" or "BTCUSD" => "XBTUSD",
            "ETHUSDT" or "ETHUSD" => "ETHUSD",
            "SOLUSDT" or "SOLUSD" => "SOLUSD",
            "XRPUSDT" or "XRPUSD" => "XRPUSD",
            _ when symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) => $"{symbol[..^4].ToUpperInvariant()}USD",
            _ => symbol.ToUpperInvariant()
        };
    }

    private static int MapIntervalMinutes(string interval)
    {
        return interval.ToLowerInvariant() switch
        {
            "1m" => 1,
            "5m" => 5,
            "15m" => 15,
            "1h" => 60,
            "4h" => 240,
            "1d" => 1440,
            "1w" => 10080,
            _ => throw new NotSupportedException($"Kraken does not support interval {interval}.")
        };
    }

    private static decimal ParseDecimal(JsonElement element)
    {
        return decimal.Parse(element.GetString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
