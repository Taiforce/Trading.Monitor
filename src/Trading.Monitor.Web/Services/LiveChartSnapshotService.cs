using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Services;

public sealed class LiveChartSnapshotService(
    HttpClient httpClient,
    LiveOperationsSnapshotService operationsSnapshotService,
    IOptionsMonitor<ReportingOptions> reportingOptions)
{
    public async Task<LiveChartSnapshot> GetAsync(string? symbol, string? interval, decimal? capital, string? estado, string? tipoSenal, string? mode, string? selectedSignalId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var resolvedSymbol = string.IsNullOrWhiteSpace(symbol) ? "BTCUSDT" : symbol.Trim().ToUpperInvariant();
        var resolvedInterval = NormalizeInterval(interval);
        var resolvedCapital = capital.GetValueOrDefault();
        if (resolvedCapital <= 0m)
            resolvedCapital = reportingOptions.CurrentValue.DefaultCapital;

        var range = ResolveCandleRange(resolvedInterval, from, to);
        var candles = await GetCandlesAsync(resolvedSymbol, resolvedInterval, range.From, range.To, cancellationToken);
        var operations = await operationsSnapshotService.GetAsync(resolvedCapital, estado, resolvedSymbol, tipoSenal, mode, selectedSignalId, cancellationToken);
        var currentPrice = candles.LastOrDefault()?.Close;
        var matchingOperations = operations.Operations
            .Where(operation => string.Equals(operation.Symbol, resolvedSymbol, StringComparison.OrdinalIgnoreCase))
            .Select(operation => RefreshConversion(operation, currentPrice, reportingOptions.CurrentValue.EstimatedFeePercentPerSide))
            .Take(8)
            .ToArray();

        return new LiveChartSnapshot(
            resolvedSymbol,
            resolvedInterval,
            DateTimeOffset.UtcNow,
            Analyze(resolvedInterval, candles, resolvedCapital, reportingOptions.CurrentValue.EstimatedFeePercentPerSide),
            candles,
            matchingOperations);
    }

    private static LiveOperationDto RefreshConversion(LiveOperationDto operation, decimal? currentPrice, decimal feePercentPerSide)
    {
        var side = string.Equals(operation.Side, nameof(MarketSide.Long), StringComparison.OrdinalIgnoreCase) ? MarketSide.Long : MarketSide.Short;
        var markPrice = operation.ExitPrice ?? currentPrice ?? operation.LastPrice;
        var breakdown = TradeCostCalculator.Build(side, operation.Capital, operation.EstimatedQuantity, operation.EntryPrice, markPrice, feePercentPerSide);
        var conversion = TradeConversionCalculator.Build(
            operation.Symbol,
            side,
            operation.Capital,
            operation.EstimatedQuantity,
            operation.EntryPrice,
            operation.ExitPrice,
            markPrice,
            operation.ExitPrice.HasValue ? ParseMoney(operation.RealizedText) : null,
            breakdown.TotalFees);

        return operation with
        {
            LastPrice = currentPrice ?? operation.LastPrice,
            MarkPrice = markPrice,
            EstimatedFees = breakdown.TotalFees,
            EntryFee = breakdown.EntryFee,
            ExitFee = breakdown.ExitFee,
            CurrentNetBenefit = breakdown.NetBenefit,
            CurrentNetPercent = breakdown.NetPercent,
            CurrentTotalObtained = breakdown.TotalObtained,
            ConversionHeadline = conversion.DetailText,
            EntryConversionText = conversion.EntryText,
            ExitConversionText = conversion.ExitText,
            FinalConversionText = conversion.ResultText,
            CostText = $"Comisiones: entrada {TradeConversionCalculator.Money(breakdown.EntryFee)} | salida {TradeConversionCalculator.Money(breakdown.ExitFee)} | total {TradeConversionCalculator.Money(breakdown.TotalFees)}",
            BreakEvenText = conversion.BreakEvenText
        };
    }

    private static decimal? ParseMoney(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "Abierta", StringComparison.OrdinalIgnoreCase))
            return null;

        return decimal.TryParse(value, NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"), out var parsed) ? parsed : null;
    }

    private async Task<IReadOnlyList<LiveCandleDto>> GetCandlesAsync(string symbol, string interval, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        foreach (var provider in CandleProviders)
        {
            var candles = await provider(this, symbol, interval, from, to, cancellationToken);
            if (candles.Count > 0)
                return candles;
        }

        return [];
    }

    private static decimal ReadDecimal(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Number
            ? element.GetDecimal()
            : decimal.Parse(element.GetString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static readonly Func<LiveChartSnapshotService, string, string, DateTimeOffset?, DateTimeOffset?, CancellationToken, Task<IReadOnlyList<LiveCandleDto>>>[] CandleProviders =
    [
        static (service, symbol, interval, from, to, cancellationToken) => service.GetBinanceCandlesAsync(symbol, interval, from, to, cancellationToken),
        static (service, symbol, interval, from, to, cancellationToken) => service.GetCoinbaseCandlesAsync(symbol, interval, from, to, cancellationToken),
        static (service, symbol, interval, from, to, cancellationToken) => service.GetKrakenCandlesAsync(symbol, interval, from, to, cancellationToken)
    ];

    private async Task<IReadOnlyList<LiveCandleDto>> GetBinanceCandlesAsync(string symbol, string interval, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var requestUri = $"https://api.binance.com/api/v3/klines?symbol={Uri.EscapeDataString(symbol)}&interval={Uri.EscapeDataString(interval)}&limit=180";
        if (from.HasValue)
            requestUri += $"&startTime={from.Value.ToUnixTimeMilliseconds()}";

        if (to.HasValue)
            requestUri += $"&endTime={to.Value.ToUnixTimeMilliseconds()}";

        var document = await GetJsonDocumentAsync(requestUri, cancellationToken);
        if (document is null)
            return [];

        using (document)
        {
            var candles = new List<LiveCandleDto>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var values = item.EnumerateArray().ToArray();
                candles.Add(new LiveCandleDto(
                    DateTimeOffset.FromUnixTimeMilliseconds(values[0].GetInt64()),
                    DateTimeOffset.FromUnixTimeMilliseconds(values[6].GetInt64()),
                    ReadDecimal(values[1]),
                    ReadDecimal(values[2]),
                    ReadDecimal(values[3]),
                    ReadDecimal(values[4]),
                    ReadDecimal(values[5])));
            }

            return candles.OrderBy(candle => candle.OpenTime).TakeLast(180).ToArray();
        }
    }

    private async Task<IReadOnlyList<LiveCandleDto>> GetCoinbaseCandlesAsync(string symbol, string interval, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var product = ToCoinbaseProduct(symbol);
        if (product is null)
            return [];

        var granularity = ToCoinbaseGranularity(interval);
        var end = to ?? DateTimeOffset.UtcNow;
        var start = from ?? end.AddSeconds(-granularity * 180);
        var requestUri = string.Create(
            CultureInfo.InvariantCulture,
            $"https://api.exchange.coinbase.com/products/{Uri.EscapeDataString(product)}/candles?granularity={granularity}&start={Uri.EscapeDataString(start.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}&end={Uri.EscapeDataString(end.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}");
        var document = await GetJsonDocumentAsync(requestUri, cancellationToken);
        if (document is null)
            return [];

        using (document)
        {
            var candles = new List<LiveCandleDto>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var values = item.EnumerateArray().ToArray();
                if (values.Length < 6)
                    continue;

                var openTime = DateTimeOffset.FromUnixTimeSeconds(values[0].GetInt64());
                candles.Add(new LiveCandleDto(
                    openTime,
                    openTime.AddSeconds(granularity),
                    ReadDecimal(values[3]),
                    ReadDecimal(values[2]),
                    ReadDecimal(values[1]),
                    ReadDecimal(values[4]),
                    ReadDecimal(values[5])));
            }

            return candles.OrderBy(candle => candle.OpenTime).TakeLast(180).ToArray();
        }
    }

    private async Task<IReadOnlyList<LiveCandleDto>> GetKrakenCandlesAsync(string symbol, string interval, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var pair = ToKrakenPair(symbol);
        if (pair is null)
            return [];

        var krakenInterval = ToKrakenInterval(interval);
        var requestUri = $"https://api.kraken.com/0/public/OHLC?pair={Uri.EscapeDataString(pair)}&interval={krakenInterval}";
        if (from.HasValue)
            requestUri += $"&since={from.Value.ToUnixTimeSeconds()}";

        var document = await GetJsonDocumentAsync(requestUri, cancellationToken);
        if (document is null)
            return [];

        using (document)
        {
            if (!document.RootElement.TryGetProperty("result", out var result))
                return [];

            JsonElement? series = null;
            foreach (var property in result.EnumerateObject())
            {
                if (!string.Equals(property.Name, "last", StringComparison.OrdinalIgnoreCase))
                {
                    series = property.Value;
                    break;
                }
            }

            if (series is null)
                return [];

            var candles = new List<LiveCandleDto>();
            foreach (var item in series.Value.EnumerateArray())
            {
                var values = item.EnumerateArray().ToArray();
                if (values.Length < 7)
                    continue;

                var openTime = DateTimeOffset.FromUnixTimeSeconds(values[0].GetInt64());
                candles.Add(new LiveCandleDto(
                    openTime,
                    openTime.AddMinutes(krakenInterval),
                    ReadDecimal(values[1]),
                    ReadDecimal(values[2]),
                    ReadDecimal(values[3]),
                    ReadDecimal(values[4]),
                    ReadDecimal(values[6])));
            }

            var filtered = candles.AsEnumerable();
            if (to.HasValue)
                filtered = filtered.Where(candle => candle.OpenTime <= to.Value);

            return filtered.OrderBy(candle => candle.OpenTime).TakeLast(180).ToArray();
        }
    }

    private async Task<JsonDocument?> GetJsonDocumentAsync(string requestUri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ToCoinbaseProduct(string symbol)
    {
        return symbol switch
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

    private static string? ToKrakenPair(string symbol)
    {
        return symbol switch
        {
            "BTCUSDT" or "BTCUSD" => "XBTUSD",
            "ETHUSDT" or "ETHUSD" => "ETHUSD",
            "SOLUSDT" or "SOLUSD" => "SOLUSD",
            "XRPUSDT" or "XRPUSD" => "XRPUSD",
            "ADAUSDT" or "ADAUSD" => "ADAUSD",
            _ when symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) => $"{symbol[..^4].ToUpperInvariant()}USD",
            _ => symbol.ToUpperInvariant()
        };
    }

    private static int ToCoinbaseGranularity(string interval)
    {
        return interval switch
        {
            "5m" => 300,
            "15m" => 900,
            "1h" => 3600,
            "2h" or "4h" => 21600,
            "1d" or "1w" or "1M" => 86400,
            _ => 60
        };
    }

    private static int ToKrakenInterval(string interval)
    {
        return interval switch
        {
            "5m" => 5,
            "15m" => 15,
            "30m" => 30,
            "1h" => 60,
            "2h" or "4h" => 240,
            "1d" or "1w" or "1M" => 1440,
            _ => 1
        };
    }

    private static CandleRange ResolveCandleRange(string interval, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (!from.HasValue && !to.HasValue)
            return new CandleRange(null, null);

        var seconds = interval == "1s" ? 60 : ToCoinbaseGranularity(interval);
        var end = to ?? DateTimeOffset.UtcNow;
        var start = from ?? end.AddSeconds(-seconds * 180);
        if (end <= start)
            end = start.AddSeconds(seconds * 60);

        var maxWindow = TimeSpan.FromSeconds(seconds * 280);
        if (end - start > maxWindow)
        {
            var center = start.AddTicks((end - start).Ticks / 2);
            start = center.Subtract(TimeSpan.FromTicks(maxWindow.Ticks / 2));
            end = center.Add(TimeSpan.FromTicks(maxWindow.Ticks / 2));
        }

        return new CandleRange(start, end);
    }

    private static LiveChartAnalysisDto Analyze(string interval, IReadOnlyList<LiveCandleDto> candles, decimal capital, decimal feePercentPerSide)
    {
        var now = DateTimeOffset.UtcNow;

        if (candles.Count < 2)
        {
            return new LiveChartAnalysisDto(
                HorizonName(interval),
                "Neutral",
                "Esperar",
                "Esperar",
                0m,
                0m,
                "Sin datos suficientes.",
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                now,
                now,
                now,
                "Sin datos");
        }

        var first = candles[0];
        var last = candles[^1];
        var changePercent = first.Open <= 0m ? 0m : (last.Close - first.Open) / first.Open * 100m;
        var high = candles.Max(candle => candle.High);
        var low = candles.Min(candle => candle.Low);
        var rangePercent = last.Close <= 0m ? 0m : (high - low) / last.Close * 100m;
        var shortAverage = candles.TakeLast(Math.Min(12, candles.Count)).Average(candle => candle.Close);
        var longAverage = candles.TakeLast(Math.Min(48, candles.Count)).Average(candle => candle.Close);
        var bias = shortAverage > longAverage && changePercent > 0m ? "Alcista" : shortAverage < longAverage && changePercent < 0m ? "Bajista" : "Lateral";
        var side = bias == "Alcista" ? MarketSide.Long : bias == "Bajista" ? MarketSide.Short : (MarketSide?)null;
        var action = bias switch
        {
            "Alcista" when rangePercent <= 12m => "Compra bajo - vende alto",
            "Bajista" when rangePercent <= 12m => "Vende alto - compra bajo",
            "Alcista" => "Esperar retroceso",
            "Bajista" => "Esperar rebote",
            _ => "Esperar confirmacion"
        };
        var atr = AverageTrueRange(candles);
        if (atr <= 0m)
            atr = last.Close * 0.002m;

        var entryBuffer = atr * 0.15m;
        var entryLower = RoundPrice(last.Close - entryBuffer);
        var entryUpper = RoundPrice(last.Close + entryBuffer);
        var entryPrice = (entryLower + entryUpper) / 2m;
        var stopLoss = 0m;
        var takeProfit1 = 0m;
        var takeProfit2 = 0m;
        var estimatedQuantity = entryPrice <= 0m ? 0m : Math.Round(capital / entryPrice, 8);
        var potentialTp1 = 0m;
        var potentialTp2 = 0m;
        var potentialStop = 0m;

        if (side.HasValue)
        {
            var riskAtr = atr * RiskMultiplier(interval);
            if (side.Value == MarketSide.Long)
            {
                stopLoss = RoundPrice(entryPrice - riskAtr);
                var risk = entryPrice - stopLoss;
                takeProfit1 = RoundPrice(entryPrice + risk * 2m);
                takeProfit2 = RoundPrice(entryPrice + risk * 3m);
            }
            else
            {
                stopLoss = RoundPrice(entryPrice + riskAtr);
                var risk = stopLoss - entryPrice;
                takeProfit1 = RoundPrice(entryPrice - risk * 2m);
                takeProfit2 = RoundPrice(entryPrice - risk * 3m);
            }

            var tp1Breakdown = TradeCostCalculator.Build(side.Value, capital, estimatedQuantity, entryPrice, takeProfit1, feePercentPerSide);
            var tp2Breakdown = TradeCostCalculator.Build(side.Value, capital, estimatedQuantity, entryPrice, takeProfit2, feePercentPerSide);
            var stopBreakdown = TradeCostCalculator.Build(side.Value, capital, estimatedQuantity, entryPrice, stopLoss, feePercentPerSide);
            potentialTp1 = tp1Breakdown.NetBenefit;
            potentialTp2 = tp2Breakdown.NetBenefit;
            potentialStop = stopBreakdown.NetBenefit;

            if (potentialTp1 <= 0m)
            {
                action = "Esperar: no cubre comisiones";
                side = null;
            }
        }

        var changeText = changePercent.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
        var readout = $"{HorizonName(interval)} | {bias} | {action} | {changeText}% | rango {rangePercent:N2}%";
        var entryAt = now;
        var entryUntil = now.AddMinutes(3);
        var exitBy = now.Add(HoldingWindow(interval));

        return new LiveChartAnalysisDto(
            HorizonName(interval),
            bias,
            action,
            side?.ToString() ?? "Esperar",
            Math.Round(changePercent, 2),
            Math.Round(rangePercent, 2),
            readout,
            RoundPrice(last.Close),
            entryLower,
            entryUpper,
            stopLoss,
            takeProfit1,
            takeProfit2,
            estimatedQuantity,
            Math.Round(potentialTp1, 2),
            Math.Round(potentialTp2, 2),
            Math.Round(potentialStop, 2),
            entryAt,
            entryUntil,
            exitBy,
            HoldingText(interval));
    }

    private static string NormalizeInterval(string? interval)
    {
        if (string.IsNullOrWhiteSpace(interval))
            return "1m";

        var value = interval.Trim();
        if (string.Equals(value, "1M", StringComparison.Ordinal) || string.Equals(value, "1mo", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "1month", StringComparison.OrdinalIgnoreCase))
            return "1M";

        return value.ToLowerInvariant() switch
        {
            "1s" => "1s",
            "1m" => "1m",
            "3m" => "3m",
            "5m" => "5m",
            "15m" => "15m",
            "30m" => "30m",
            "1hr" or "1h" => "1h",
            "2h" => "2h",
            "4h" => "4h",
            "1d" => "1d",
            "1w" => "1w",
            _ => "1m"
        };
    }

    private static string HorizonName(string interval)
    {
        return interval switch
        {
            "1s" or "1m" or "3m" or "5m" => "Scalping",
            "15m" or "30m" or "1h" => "Intradia",
            "4h" or "1d" => "Swing",
            "1w" or "1M" => "Posicional",
            _ => "Mercado"
        };
    }

    private static decimal AverageTrueRange(IReadOnlyList<LiveCandleDto> candles)
    {
        if (candles.Count < 2)
            return 0m;

        var start = Math.Max(1, candles.Count - 14);
        var ranges = new List<decimal>();
        for (var index = start; index < candles.Count; index++)
        {
            var current = candles[index];
            var previousClose = candles[index - 1].Close;
            var trueRange = Math.Max(current.High - current.Low, Math.Max(Math.Abs(current.High - previousClose), Math.Abs(current.Low - previousClose)));
            ranges.Add(trueRange);
        }

        return ranges.Count == 0 ? 0m : ranges.Average();
    }

    private static decimal RiskMultiplier(string interval)
    {
        return interval switch
        {
            "1s" or "1m" => 1.2m,
            "15m" or "30m" or "1h" => 1.6m,
            "1d" or "1w" or "1M" => 2.2m,
            _ => 1.5m
        };
    }

    private static TimeSpan HoldingWindow(string interval)
    {
        return interval switch
        {
            "1s" => TimeSpan.FromMinutes(3),
            "1m" or "3m" or "5m" => TimeSpan.FromMinutes(12),
            "15m" or "30m" => TimeSpan.FromHours(2),
            "1h" or "2h" => TimeSpan.FromHours(8),
            "4h" => TimeSpan.FromDays(2),
            "1d" => TimeSpan.FromDays(7),
            "1w" => TimeSpan.FromDays(30),
            "1M" => TimeSpan.FromDays(90),
            _ => TimeSpan.FromMinutes(12)
        };
    }

    private static string HoldingText(string interval)
    {
        return interval switch
        {
            "1s" => "hasta 3 min",
            "1m" or "3m" or "5m" => "hasta 12 min",
            "15m" or "30m" => "hasta 2 h",
            "1h" or "2h" => "hasta 8 h",
            "4h" => "hasta 2 dias",
            "1d" => "hasta 7 dias",
            "1w" => "hasta 30 dias",
            "1M" => "hasta 90 dias",
            _ => "segun confirmacion"
        };
    }

    private static decimal RoundPrice(decimal value)
    {
        var decimals = Math.Abs(value) switch { >= 1000m => 2, >= 1m => 4, _ => 8 };
        return Math.Round(value, decimals);
    }
}

internal sealed record CandleRange(DateTimeOffset? From, DateTimeOffset? To);
