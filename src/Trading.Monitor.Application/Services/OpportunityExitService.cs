using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed class OpportunityExitService
{
    public OpportunityExit? ResolveExit(OpportunityReportRow opportunity, IReadOnlyList<MarketCandle> candles, RiskOptions riskOptions)
    {
        return riskOptions.ManagedProfitExitEnabled && opportunity.OperationKind == SignalOperationKind.Managed
            ? ResolveManagedProfitExit(opportunity, candles, riskOptions)
            : ResolveStaticExit(opportunity, candles);
    }

    public bool HasTouchedManagedTarget(OpportunityReportRow opportunity, IReadOnlyList<MarketCandle> candles, RiskOptions riskOptions)
    {
        if (!riskOptions.ManagedProfitExitEnabled || opportunity.OperationKind != SignalOperationKind.Managed)
            return false;

        var minimumNetPercent = ResolveManagedTargetPercent(opportunity, riskOptions);

        return candles
            .Where(candle => candle.CloseTime > opportunity.ObservedAt)
            .Any(candle => TouchesManagedTarget(opportunity, candle, minimumNetPercent, riskOptions.EstimatedFeePercentPerSide));
    }

    private static OpportunityExit? ResolveManagedProfitExit(OpportunityReportRow opportunity, IReadOnlyList<MarketCandle> candles, RiskOptions riskOptions)
    {
        var relevantCandles = candles.Where(candle => candle.CloseTime > opportunity.ObservedAt).OrderBy(candle => candle.CloseTime).ToArray();
        if (relevantCandles.Length == 0)
            return null;

        var minimumNetPercent = ResolveManagedTargetPercent(opportunity, riskOptions);
        var trailingGivebackPercent = Math.Max(0m, riskOptions.ManagedTrailingGivebackPercent);
        var requiredLowerCandles = Math.Max(1, riskOptions.ManagedProfitTrailCandlesAfterTarget);
        var peakNetPercent = decimal.MinValue;
        var targetWasReached = false;
        decimal? previousTargetCloseNetPercent = null;
        var lowerNetCloseCount = 0;
        MarketCandle? previous = null;

        foreach (var candle in relevantCandles)
        {
            if (riskOptions.ManagedHardStopExitEnabled && TouchesStop(opportunity, candle))
                return new OpportunityExit(OpportunityStatus.HitStopLoss, candle.CloseTime, opportunity.StopLoss, "Salida de protección activada por pérdida máxima configurada.");

            var favorablePrice = opportunity.Side == MarketSide.Long ? candle.High : candle.Low;
            var favorableNetPercent = NetPercent(opportunity, favorablePrice, riskOptions.EstimatedFeePercentPerSide);
            if (favorableNetPercent >= minimumNetPercent)
                targetWasReached = true;

            if (favorableNetPercent > peakNetPercent)
                peakNetPercent = favorableNetPercent;

            var currentNetPercent = NetPercent(opportunity, candle.Close, riskOptions.EstimatedFeePercentPerSide);
            if (currentNetPercent > peakNetPercent)
                peakNetPercent = currentNetPercent;

            var gaveBackFromPeak = peakNetPercent - currentNetPercent;
            var momentumWeakness = HasMomentumWeakness(opportunity.Side, candle, previous);
            var trailingExit = trailingGivebackPercent > 0m && gaveBackFromPeak >= trailingGivebackPercent;

            if (targetWasReached && currentNetPercent >= minimumNetPercent)
            {
                lowerNetCloseCount = previousTargetCloseNetPercent.HasValue && currentNetPercent < previousTargetCloseNetPercent.Value
                    ? lowerNetCloseCount + 1
                    : 0;

                previousTargetCloseNetPercent = currentNetPercent;

                var lowerCloseExit = lowerNetCloseCount >= requiredLowerCandles
                                     && (!riskOptions.ManagedExitRequiresMomentumWeakness || requiredLowerCandles <= 1 || momentumWeakness || trailingExit);

                if (lowerCloseExit || trailingExit)
                {
                    var reason = trailingExit
                        ? $"{ExitVerb(opportunity.Side)} ahora: beneficio neto {currentNetPercent:N2}% y retroceso desde pico de {gaveBackFromPeak:N2}%."
                        : $"{ExitVerb(opportunity.Side)} ahora: beneficio neto {currentNetPercent:N2}% después de comisiones; detecté {lowerNetCloseCount} velas seguidas con menor ganancia.";

                    return new OpportunityExit(OpportunityStatus.ManagedProfitExit, candle.CloseTime, candle.Close, reason);
                }
            }
            else if (targetWasReached)
            {
                lowerNetCloseCount = 0;
                previousTargetCloseNetPercent = null;
            }

            previous = candle;
        }

        if (riskOptions.ManagedExpiryExitEnabled && DateTimeOffset.UtcNow > opportunity.ExpiresAt)
        {
            var last = relevantCandles[^1];
            var netPercent = NetPercent(opportunity, last.Close, riskOptions.EstimatedFeePercentPerSide);
            var status = netPercent > 0m ? OpportunityStatus.ManagedProfitExit : OpportunityStatus.Expired;
            var reason = netPercent > 0m
                ? $"Salida administrada por vencimiento con beneficio neto {netPercent:N2}%."
                : "La oportunidad venció y la salida administrada por vencimiento está activa.";

            return new OpportunityExit(status, last.CloseTime, last.Close, reason);
        }

        return null;
    }

    private static OpportunityExit? ResolveStaticExit(OpportunityReportRow opportunity, IReadOnlyList<MarketCandle> candles)
    {
        var relevantCandles = candles.Where(candle => candle.CloseTime > opportunity.ObservedAt).OrderBy(candle => candle.CloseTime).ToArray();

        foreach (var candle in relevantCandles)
        {
            if (opportunity.Side == MarketSide.Long)
            {
                if (candle.Low <= opportunity.StopLoss)
                    return new OpportunityExit(OpportunityStatus.HitStopLoss, candle.CloseTime, opportunity.StopLoss, "Pérdida máxima tocada antes de la ganancia objetivo.");

                if (candle.High >= opportunity.TakeProfit2)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit2, candle.CloseTime, opportunity.TakeProfit2, "Ganancia extra alcanzada.");

                if (candle.High >= opportunity.TakeProfit1)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit1, candle.CloseTime, opportunity.TakeProfit1, "Ganancia objetivo alcanzada.");
            }
            else
            {
                if (candle.High >= opportunity.StopLoss)
                    return new OpportunityExit(OpportunityStatus.HitStopLoss, candle.CloseTime, opportunity.StopLoss, "Pérdida máxima tocada antes de la ganancia objetivo.");

                if (candle.Low <= opportunity.TakeProfit2)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit2, candle.CloseTime, opportunity.TakeProfit2, "Ganancia extra alcanzada.");

                if (candle.Low <= opportunity.TakeProfit1)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit1, candle.CloseTime, opportunity.TakeProfit1, "Ganancia objetivo alcanzada.");
            }
        }

        if (DateTimeOffset.UtcNow > opportunity.ExpiresAt && relevantCandles.Length > 0)
        {
            var last = relevantCandles[^1];
            return new OpportunityExit(OpportunityStatus.Expired, last.CloseTime, last.Close, "La oportunidad venció antes de tocar ganancia o pérdida máxima.");
        }

        return null;
    }

    private static bool TouchesStop(OpportunityReportRow opportunity, MarketCandle candle)
    {
        return opportunity.Side == MarketSide.Long
            ? candle.Low <= opportunity.StopLoss
            : candle.High >= opportunity.StopLoss;
    }

    private static bool HasMomentumWeakness(MarketSide side, MarketCandle current, MarketCandle? previous)
    {
        if (previous is null)
            return false;

        return side == MarketSide.Long
            ? current.Close < current.Open || current.Close < previous.Close
            : current.Close > current.Open || current.Close > previous.Close;
    }

    private static bool TouchesManagedTarget(OpportunityReportRow opportunity, MarketCandle candle, decimal targetNetPercent, decimal feePercentPerSide)
    {
        var exitPrice = TradeCostCalculator.ResolveExitPriceForNetPercent(
            opportunity.Side,
            opportunity.Capital,
            opportunity.EstimatedQuantity,
            opportunity.EntryPrice,
            targetNetPercent,
            feePercentPerSide);

        return opportunity.Side == MarketSide.Long
            ? candle.High >= exitPrice
            : candle.Low <= exitPrice;
    }

    private static string ExitVerb(MarketSide side)
    {
        return side == MarketSide.Long ? "Vender" : "Comprar bajo";
    }

    private static decimal NetPercent(OpportunityReportRow opportunity, decimal exitPrice, decimal feePercentPerSide)
    {
        if (opportunity.Capital <= 0m)
            return 0m;

        return TradeCostCalculator.Build(opportunity.Side, opportunity.Capital, opportunity.EstimatedQuantity, opportunity.EntryPrice, exitPrice, feePercentPerSide).NetPercent;
    }

    private static decimal ResolveManagedTargetPercent(OpportunityReportRow opportunity, RiskOptions riskOptions)
    {
        var signalTarget = opportunity.ManagedTargetNetPercent > 0m ? opportunity.ManagedTargetNetPercent : riskOptions.ManagedProfitExitPercentAfterCosts;
        return Math.Max(0.01m, signalTarget);
    }
}
