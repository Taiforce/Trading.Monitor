using System.Globalization;
using System.Text.Json;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.MarketData;

public sealed class CoinbaseExchangeMarketDataProvider(HttpClient httpClient) : IMarketDataProvider
{
    public string Name => "Coinbase Exchange";

    public async Task<IReadOnlyList<MarketCandle>> GetCandlesAsync(string symbol, string interval, int limit, CancellationToken cancellationToken)
    {
        var productId = MapProduct(symbol);
        if (string.Equals(interval, "1s", StringComparison.OrdinalIgnoreCase))
            return await GetTradeCandlesAsync(symbol, productId, limit, cancellationToken);

        var granularity = MapGranularity(interval);
        var requestUri = $"/products/{Uri.EscapeDataString(productId)}/candles?granularity={granularity}";

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Coinbase returned {(int)response.StatusCode}: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var candles = new List<MarketCandle>();

        foreach (var item in document.RootElement.EnumerateArray().Take(Math.Clamp(limit, 1, 300)))
        {
            var values = item.EnumerateArray().ToArray();
            var openTime = DateTimeOffset.FromUnixTimeSeconds(values[0].GetInt64());
            var closeTime = openTime.AddSeconds(granularity);
            var low = ReadDecimal(values[1]);
            var high = ReadDecimal(values[2]);
            var open = ReadDecimal(values[3]);
            var close = ReadDecimal(values[4]);
            var volume = ReadDecimal(values[5]);

            candles.Add(new MarketCandle(symbol, interval, openTime, closeTime, open, high, low, close, volume, volume * close));
        }

        return candles.OrderBy(candle => candle.OpenTime).ToArray();
    }

    private async Task<IReadOnlyList<MarketCandle>> GetTradeCandlesAsync(string symbol, string productId, int limit, CancellationToken cancellationToken)
    {
        var requestUri = $"/products/{Uri.EscapeDataString(productId)}/trades?limit=1000";

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Coinbase returned {(int)response.StatusCode}: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var trades = new List<CoinbaseTradeTick>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("time", out var timeElement)
                || !item.TryGetProperty("price", out var priceElement)
                || !item.TryGetProperty("size", out var sizeElement))
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(timeElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
                continue;

            trades.Add(new CoinbaseTradeTick(time, ReadDecimal(priceElement), ReadDecimal(sizeElement)));
        }

        return trades
            .OrderBy(trade => trade.Time)
            .GroupBy(trade => trade.Time.ToUnixTimeSeconds())
            .Select(group =>
            {
                var values = group.OrderBy(trade => trade.Time).ToArray();
                var openTime = DateTimeOffset.FromUnixTimeSeconds(group.Key);
                var open = values[0].Price;
                var close = values[^1].Price;
                var high = values.Max(trade => trade.Price);
                var low = values.Min(trade => trade.Price);
                var volume = values.Sum(trade => trade.Size);

                return new MarketCandle(symbol, "1s", openTime, openTime.AddSeconds(1), open, high, low, close, volume, values.Sum(trade => trade.Size * trade.Price));
            })
            .TakeLast(Math.Clamp(limit, 1, 1000))
            .ToArray();
    }

    private static string MapProduct(string symbol)
    {
        return symbol.ToUpperInvariant() switch
        {
            "BTCUSDT" or "BTCUSD" => "BTC-USD",
            "ETHUSDT" or "ETHUSD" => "ETH-USD",
            "SOLUSDT" or "SOLUSD" => "SOL-USD",
            "XRPUSDT" or "XRPUSD" => "XRP-USD",
            "ADAUSDT" or "ADAUSD" => "ADA-USD",
            _ when symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) => $"{symbol[..^4].ToUpperInvariant()}-USD",
            _ when symbol.EndsWith("USD", StringComparison.OrdinalIgnoreCase) => $"{symbol[..^3].ToUpperInvariant()}-USD",
            _ => symbol.ToUpperInvariant()
        };
    }

    private static int MapGranularity(string interval)
    {
        return interval.ToLowerInvariant() switch
        {
            "1m" => 60,
            "5m" => 300,
            "15m" => 900,
            "1h" => 3600,
            "1d" => 86400,
            _ => throw new NotSupportedException($"Coinbase does not support interval {interval}.")
        };
    }

    private static decimal ReadDecimal(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String
            ? decimal.Parse(element.GetString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture)
            : element.GetDecimal();
    }
}

internal sealed record CoinbaseTradeTick(DateTimeOffset Time, decimal Price, decimal Size);
