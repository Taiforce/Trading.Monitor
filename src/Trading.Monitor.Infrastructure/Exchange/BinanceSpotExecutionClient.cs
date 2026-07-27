using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Exchange;

public sealed class BinanceSpotExecutionClient(HttpClient httpClient, IOptionsMonitor<ExchangeExecutionOptions> optionsMonitor) : IExchangeExecutionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SymbolTradeRules> GetSymbolRulesAsync(string symbol, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"/api/v3/exchangeInfo?symbol={Uri.EscapeDataString(NormalizeSymbol(symbol))}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Binance exchangeInfo returned {(int)response.StatusCode}: {Trim(body, 500)}");

        using var document = JsonDocument.Parse(body);
        var symbolElement = document.RootElement.GetProperty("symbols").EnumerateArray().First();
        var stepSize = 0m;
        var minQuantity = 0m;
        var minNotional = 0m;
        var tickSize = 0m;

        foreach (var filter in symbolElement.GetProperty("filters").EnumerateArray())
        {
            var type = filter.GetProperty("filterType").GetString();

            if (type == "LOT_SIZE")
            {
                stepSize = ReadDecimal(filter, "stepSize");
                minQuantity = ReadDecimal(filter, "minQty");
            }
            else if (type == "MARKET_LOT_SIZE" && stepSize <= 0m)
            {
                stepSize = ReadDecimal(filter, "stepSize");
                minQuantity = ReadDecimal(filter, "minQty");
            }
            else if (type is "MIN_NOTIONAL" or "NOTIONAL")
            {
                minNotional = ReadDecimal(filter, "minNotional");
            }
            else if (type == "PRICE_FILTER")
            {
                tickSize = ReadDecimal(filter, "tickSize");
            }
        }

        return new SymbolTradeRules(NormalizeSymbol(symbol), stepSize, minQuantity, minNotional, tickSize);
    }

    public async Task<ExchangeBalance?> GetBalanceAsync(string asset, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        var credentials = ReadCredentials(options);

        if (credentials is null)
            return null;

        var timestamp = await GetServerTimestampAsync(cancellationToken);
        var parameters = new Dictionary<string, string>
        {
            ["recvWindow"] = Math.Clamp(options.ReceiveWindowMilliseconds, 1000, 60000).ToString(CultureInfo.InvariantCulture),
            ["timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture)
        };

        using var response = await SendSignedAsync(HttpMethod.Get, "/api/v3/account", parameters, credentials.Value.ApiKey, credentials.Value.ApiSecret, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Binance account returned {(int)response.StatusCode}: {Trim(body, 500)}");

        using var document = JsonDocument.Parse(body);
        var normalizedAsset = asset.Trim().ToUpperInvariant();

        foreach (var balance in document.RootElement.GetProperty("balances").EnumerateArray())
        {
            if (!string.Equals(balance.GetProperty("asset").GetString(), normalizedAsset, StringComparison.OrdinalIgnoreCase))
                continue;

            return new ExchangeBalance(
                normalizedAsset,
                ReadDecimal(balance, "free"),
                ReadDecimal(balance, "locked"));
        }

        return new ExchangeBalance(normalizedAsset, 0m, 0m);
    }

    public Task<ExchangeOrderResult> PlaceMarketBuyAsync(string symbol, decimal quoteOrderQuantity, string clientOrderId, bool useTestEndpoint, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["symbol"] = NormalizeSymbol(symbol),
            ["side"] = "BUY",
            ["type"] = "MARKET",
            ["quoteOrderQty"] = FormatDecimal(quoteOrderQuantity),
            ["newClientOrderId"] = clientOrderId
        };

        return PlaceMarketOrderAsync(parameters, useTestEndpoint, cancellationToken);
    }

    public Task<ExchangeOrderResult> PlaceMarketSellAsync(string symbol, decimal quantity, string clientOrderId, bool useTestEndpoint, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["symbol"] = NormalizeSymbol(symbol),
            ["side"] = "SELL",
            ["type"] = "MARKET",
            ["quantity"] = FormatDecimal(quantity),
            ["newClientOrderId"] = clientOrderId
        };

        return PlaceMarketOrderAsync(parameters, useTestEndpoint, cancellationToken);
    }

    private async Task<ExchangeOrderResult> PlaceMarketOrderAsync(Dictionary<string, string> parameters, bool useTestEndpoint, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        var credentials = ReadCredentials(options);

        if (credentials is null)
        {
            return new ExchangeOrderResult(
                TradeExecutionStatus.Failed,
                "",
                null,
                null,
                null,
                "{}",
                $"Missing {options.ApiKeyEnvironmentVariable}/{options.ApiSecretEnvironmentVariable}.");
        }

        parameters["recvWindow"] = Math.Clamp(options.ReceiveWindowMilliseconds, 1000, 60000).ToString(CultureInfo.InvariantCulture);
        parameters["timestamp"] = (await GetServerTimestampAsync(cancellationToken)).ToString(CultureInfo.InvariantCulture);

        var endpoint = useTestEndpoint ? "/api/v3/order/test" : "/api/v3/order";
        using var response = await SendSignedAsync(HttpMethod.Post, endpoint, parameters, credentials.Value.ApiKey, credentials.Value.ApiSecret, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new ExchangeOrderResult(
                TradeExecutionStatus.Failed,
                "",
                null,
                null,
                null,
                body,
                $"Binance order returned {(int)response.StatusCode}: {Trim(body, 700)}");
        }

        if (useTestEndpoint)
        {
            return new ExchangeOrderResult(
                TradeExecutionStatus.Submitted,
                "",
                null,
                null,
                null,
                string.IsNullOrWhiteSpace(body) ? "{}" : body,
                "Orden validada con Binance /api/v3/order/test; no se ejecuto dinero real.");
        }

        return ParseOrderResult(body);
    }

    private async Task<long> GetServerTimestampAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("/api/v3/time", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("serverTime", out var serverTime)
                ? serverTime.GetInt64()
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        catch
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    private Task<HttpResponseMessage> SendSignedAsync(HttpMethod method, string endpoint, Dictionary<string, string> parameters, string apiKey, string apiSecret, CancellationToken cancellationToken)
    {
        var query = BuildQuery(parameters);
        var signature = Sign(query, apiSecret);
        var requestUri = $"{endpoint}?{query}&signature={signature}";
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Add("X-MBX-APIKEY", apiKey);

        return httpClient.SendAsync(request, cancellationToken);
    }

    private static ExchangeOrderResult ParseOrderResult(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : "";
            var executedQuantity = root.TryGetProperty("executedQty", out _) ? ReadDecimal(root, "executedQty") : (decimal?)null;
            var executedQuote = root.TryGetProperty("cummulativeQuoteQty", out _) ? ReadDecimal(root, "cummulativeQuoteQty") : (decimal?)null;
            var price = executedQuantity is > 0m && executedQuote.HasValue ? executedQuote.Value / executedQuantity.Value : (decimal?)null;

            return new ExchangeOrderResult(
                string.Equals(status, "FILLED", StringComparison.OrdinalIgnoreCase) ? TradeExecutionStatus.Filled : TradeExecutionStatus.Submitted,
                root.TryGetProperty("orderId", out var orderId) ? orderId.ToString() : "",
                executedQuantity,
                executedQuote,
                price,
                body,
                string.IsNullOrWhiteSpace(status) ? "Orden enviada a Binance." : $"Orden Binance en estado {status}.");
        }
        catch (JsonException)
        {
            return new ExchangeOrderResult(TradeExecutionStatus.Submitted, "", null, null, null, body, "Orden enviada a Binance; respuesta no se pudo resumir.");
        }
    }

    private static (string ApiKey, string ApiSecret)? ReadCredentials(ExchangeExecutionOptions options)
    {
        var apiKey = Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable);
        var apiSecret = Environment.GetEnvironmentVariable(options.ApiSecretEnvironmentVariable);

        return string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret)
            ? null
            : (apiKey, apiSecret);
    }

    private static string BuildQuery(Dictionary<string, string> parameters)
    {
        return string.Join("&", parameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static string Sign(string query, string apiSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0m;

        if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var fromString))
            return fromString;

        return property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var fromNumber) ? fromNumber : 0m;
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    private static string NormalizeSymbol(string symbol)
    {
        return symbol.Trim().ToUpperInvariant();
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
