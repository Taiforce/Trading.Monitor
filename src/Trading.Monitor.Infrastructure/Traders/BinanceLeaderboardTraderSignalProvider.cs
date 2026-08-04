using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Traders;

/// <summary>
/// "Traders": follows the real, currently-open positions of the top-ranked traders on Binance's
/// public Futures leaderboard (https://www.binance.com/en/futures-activity/leaderboard). This is
/// a real exchange, real traders, and real open positions - not a simulation.
///
/// IMPORTANT: Binance does not publish an official/documented copy-trading API. The endpoints
/// used here (<c>getLeaderboardRank</c>, <c>getOtherPosition</c>) are the same unauthenticated
/// public endpoints Binance's own website calls, reverse-engineered and used by several open
/// source community projects (bfldb, binance-futures-bapi, etc.). No API key or login is
/// required, but Binance can change or rate-limit these endpoints at any time without notice.
/// Every call is defensive: failures are recorded as source telemetry and degrade to "no trader
/// signals this cycle" rather than breaking the scan.
/// </summary>
public sealed class BinanceLeaderboardTraderSignalProvider(HttpClient httpClient, TraderSignalOptions options, ISourceTelemetryRecorder telemetryRecorder,
    ILogger<BinanceLeaderboardTraderSignalProvider> logger) : ITraderSignalProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => "Binance Futures Leaderboard";

    public async Task<IReadOnlyList<TradingOpportunity>> GetSignalsAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var traders = await FetchTopTradersAsync(cancellationToken);
            var opportunities = new List<TradingOpportunity>();
            var symbolSet = symbols.Count == 0 ? null : new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);

            foreach (var trader in traders.Take(Math.Clamp(options.TopTraderCount, 1, 20)))
            {
                if (string.IsNullOrWhiteSpace(trader.EncryptedUid))
                    continue;

                IReadOnlyList<LeaderboardPosition> positions;

                try
                {
                    positions = await FetchPositionsAsync(trader.EncryptedUid, cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation(exception, "Could not fetch positions for leaderboard trader {NickName}.", trader.NickName);
                    continue;
                }

                foreach (var position in positions)
                {
                    if (string.IsNullOrWhiteSpace(position.Symbol) || position.Amount == 0m || position.EntryPrice <= 0m)
                        continue;

                    if (symbolSet is not null && !symbolSet.Contains(position.Symbol))
                        continue;

                    var opportunity = BuildOpportunity(trader, position);
                    if (opportunity is not null)
                        opportunities.Add(opportunity);
                }
            }

            await telemetryRecorder.RecordAsync(
                new DataSourceHealthEvent(Name, DataSourceKind.TraderSignal, DataSourceStatus.Healthy, "https://www.binance.com/en/futures-activity/leaderboard",
                    $"{traders.Count} traders consultados, {opportunities.Count} posiciones convertidas en señales.", startedAt, DateTimeOffset.UtcNow, opportunities.Count),
                cancellationToken);

            return opportunities;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Binance leaderboard trader signal fetch failed.");
            await telemetryRecorder.RecordAsync(
                new DataSourceHealthEvent(Name, DataSourceKind.TraderSignal, DataSourceStatus.Failed, "https://www.binance.com/en/futures-activity/leaderboard",
                    "No se pudo consultar el leaderboard publico de Binance en este ciclo (endpoint no oficial, puede cambiar sin aviso).", startedAt, DateTimeOffset.UtcNow, 0),
                cancellationToken);
            return [];
        }
    }

    private async Task<IReadOnlyList<LeaderboardTrader>> FetchTopTradersAsync(CancellationToken cancellationToken)
    {
        var request = new LeaderboardRankRequest("PERPETUAL", "ROI", options.PeriodType, true, false);
        using var response = await httpClient.PostAsJsonAsync("/bapi/futures/v3/public/future/leaderboard/getLeaderboardRank", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LeaderboardRankResponse>(JsonOptions, cancellationToken);
        return payload?.Data ?? [];
    }

    private async Task<IReadOnlyList<LeaderboardPosition>> FetchPositionsAsync(string encryptedUid, CancellationToken cancellationToken)
    {
        var request = new LeaderboardPositionRequest(encryptedUid, "PERPETUAL");
        using var response = await httpClient.PostAsJsonAsync("/bapi/futures/v1/public/future/leaderboard/getOtherPosition", request, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return [];

        var payload = await response.Content.ReadFromJsonAsync<LeaderboardPositionResponse>(JsonOptions, cancellationToken);
        return payload?.Data?.OtherPositionRetList ?? [];
    }

    private TradingOpportunity? BuildOpportunity(LeaderboardTrader trader, LeaderboardPosition position)
    {
        var side = position.Amount > 0m ? MarketSide.Long : MarketSide.Short;
        var entryPrice = position.EntryPrice;

        // Prefer the trader's own liquidation price as the stop reference when Binance reports
        // one; otherwise fall back to a conservative fixed adverse move. Either way this is a
        // rough mirror of a real position, not a precision stop computed from our own ATR.
        var fallbackStopDistance = entryPrice * 0.05m;
        var stopDistance = position.LiquidationPrice > 0m ? Math.Abs(entryPrice - position.LiquidationPrice) : fallbackStopDistance;
        stopDistance = stopDistance <= 0m ? fallbackStopDistance : Math.Min(stopDistance, entryPrice * 0.25m);

        var stopLoss = side == MarketSide.Long ? entryPrice - stopDistance : entryPrice + stopDistance;
        var takeProfit1 = side == MarketSide.Long ? entryPrice + stopDistance * 2m : entryPrice - stopDistance * 2m;
        var takeProfit2 = side == MarketSide.Long ? entryPrice + stopDistance * 3m : entryPrice - stopDistance * 3m;

        if (stopLoss <= 0m || takeProfit1 <= 0m)
            return null;

        var roiPercent = position.Roe * 100m;
        var score = Math.Clamp(58 + (int)Math.Round(Math.Clamp(roiPercent, -20m, 40m) / 2m), 55, 90);
        var nickname = string.IsNullOrWhiteSpace(trader.NickName) ? "Trader anonimo" : trader.NickName;
        var reasons = new List<string>
        {
            $"Señal de Trader: {nickname} (leaderboard publico de Binance Futures) tiene abierta esta posicion {(side == MarketSide.Long ? "LONG" : "SHORT")} en {position.Symbol}.",
            $"ROI actual reportado de la posicion: {roiPercent:N2}%. Apalancamiento: {position.Leverage}x.",
            "Fuente: endpoint publico no oficial del leaderboard de Binance (sin API key); puede dejar de estar disponible sin aviso."
        };

        var now = DateTimeOffset.UtcNow;

        return new TradingOpportunity(position.Symbol, side, score, now, now.AddHours(Math.Clamp(options.SignalExpiryHours, 1, 72)), Round(entryPrice), Round(entryPrice), Round(entryPrice),
            Round(stopLoss), Round(takeProfit1), Round(takeProfit2), 2m, [], reasons, [], [], SignalOperationKind.Trader, SignalOriginKind.Trader);
    }

    private static decimal Round(decimal value)
    {
        var decimals = Math.Abs(value) switch { >= 1000m => 2, >= 1m => 4, _ => 8 };
        return Math.Round(value, decimals);
    }

    private sealed record LeaderboardRankRequest(
        [property: JsonPropertyName("tradeType")] string TradeType,
        [property: JsonPropertyName("statisticsType")] string StatisticsType,
        [property: JsonPropertyName("periodType")] string PeriodType,
        [property: JsonPropertyName("isShared")] bool IsShared,
        [property: JsonPropertyName("isTrader")] bool IsTrader);

    private sealed record LeaderboardRankResponse([property: JsonPropertyName("data")] List<LeaderboardTrader>? Data);

    private sealed record LeaderboardTrader(
        [property: JsonPropertyName("encryptedUid")] string EncryptedUid,
        [property: JsonPropertyName("nickName")] string? NickName,
        [property: JsonPropertyName("rank")] int Rank);

    private sealed record LeaderboardPositionRequest(
        [property: JsonPropertyName("encryptedUid")] string EncryptedUid,
        [property: JsonPropertyName("tradeType")] string TradeType);

    private sealed record LeaderboardPositionResponse([property: JsonPropertyName("data")] LeaderboardPositionData? Data);

    private sealed record LeaderboardPositionData([property: JsonPropertyName("otherPositionRetList")] List<LeaderboardPosition>? OtherPositionRetList);

    private sealed record LeaderboardPosition(
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("entryPrice")] decimal EntryPrice,
        [property: JsonPropertyName("markPrice")] decimal MarkPrice,
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("leverage")] decimal Leverage,
        [property: JsonPropertyName("liquidationPrice")] decimal LiquidationPrice,
        [property: JsonPropertyName("roe")] decimal Roe);
}
