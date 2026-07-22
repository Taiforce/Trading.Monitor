using System.Globalization;
using System.Text.Json;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.MarketData;

public sealed class BinanceRestMarketDataProvider(HttpClient httpClient, string name = "Binance") : IMarketDataProvider
{
    public string Name { get; } = name;

    public async Task<IReadOnlyList<MarketCandle>> GetCandlesAsync(string symbol, string interval, int limit, CancellationToken cancellationToken)
    {
        var requestUri = $"/api/v3/klines?symbol={Uri.EscapeDataString(symbol)}&interval={Uri.EscapeDataString(interval)}&limit={limit}";
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{Name} returned {(int)response.StatusCode}: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        var candles = new List<MarketCandle>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var values = item.EnumerateArray().ToArray();

            candles.Add(new MarketCandle(symbol, interval, FromUnixMilliseconds(values[0].GetInt64()), FromUnixMilliseconds(values[6].GetInt64()), ParseDecimal(values[1]), ParseDecimal(values[2]),
                ParseDecimal(values[3]), ParseDecimal(values[4]), ParseDecimal(values[5]), ParseDecimal(values[7]), ParseDecimal(values[9])));
        }

        return candles.OrderBy(candle => candle.OpenTime).ToArray();
    }

    private static DateTimeOffset FromUnixMilliseconds(long value)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(value);
    }

    private static decimal ParseDecimal(JsonElement element)
    {
        return decimal.Parse(element.GetString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
