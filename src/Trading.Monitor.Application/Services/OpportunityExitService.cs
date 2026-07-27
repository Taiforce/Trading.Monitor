using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed class OpportunityExitService
{
    public OpportunityExit? ResolveExit(OpportunityReportRow opportunity, IReadOnlyList<MarketCandle> candles, RiskOptions riskOptions)
    {
        return riskOptions.ManagedProfitExitEnabled
            ? ResolveManagedProfitExit(opportunity, candles, riskOptions)
            : ResolveStaticExit(opportunity, candles);
    }

    private static OpportunityExit? ResolveManagedProfitExit(OpportunityReportRow opportunity, IReadOnlyList<MarketCandle> candles, RiskOptions riskOptions)
    {
        var relevantCandles = candles.Where(candle => candle.CloseTime > opportunity.ObservedAt).OrderBy(candle => candle.CloseTime).ToArray();
        if (relevantCandles.Length == 0)
            return null;

        var minimumNetPercent = Math.Max(0.01m, riskOptions.ManagedProfitExitPercentAfterCosts);
        var quickNetPercent = Math.Max(minimumNetPercent, riskOptions.ManagedQuickProfitExitPercentAfterCosts);
        var trailingGivebackPercent = Math.Max(0m, riskOptions.ManagedTrailingGivebackPercent);
        var peakNetPercent = decimal.MinValue;
        MarketCandle? previous = null;

        foreach (var candle in relevantCandles)
        {
            if (riskOptions.ManagedHardStopExitEnabled && TouchesStop(opportunity, candle))
                return new OpportunityExit(OpportunityStatus.HitStopLoss, candle.CloseTime, opportunity.StopLoss, "Salida de proteccion activada por perdida maxima configurada.");

            var favorablePrice = opportunity.Side == MarketSide.Long ? candle.High : candle.Low;
            var favorableNetPercent = NetPercent(opportunity, favorablePrice, riskOptions.EstimatedFeePercentPerSide);
            if (favorableNetPercent > peakNetPercent)
                peakNetPercent = favorableNetPercent;

            var currentNetPercent = NetPercent(opportunity, candle.Close, riskOptions.EstimatedFeePercentPerSide);
            var gaveBackFromPeak = peakNetPercent - currentNetPercent;
            var momentumWeakness = HasMomentumWeakness(opportunity.Side, candle, previous);
            var trailingExit = trailingGivebackPercent > 0m && gaveBackFromPeak >= trailingGivebackPercent;
            var canExitByMomentum = !riskOptions.ManagedExitRequiresMomentumWeakness || momentumWeakness || trailingExit;

            if (currentNetPercent >= quickNetPercent)
            {
                return new OpportunityExit(OpportunityStatus.ManagedProfitExit, candle.CloseTime, candle.Close,
                    $"Vender ahora: beneficio neto estimado {currentNetPercent:N2}% despues de comisiones. Se alcanzo salida rapida.");
            }

            if (currentNetPercent >= minimumNetPercent && canExitByMomentum)
            {
                var reason = trailingExit
                    ? $"Vender ahora: beneficio neto {currentNetPercent:N2}% y retroceso desde pico de {gaveBackFromPeak:N2}%."
                    : $"Vender ahora: beneficio neto {currentNetPercent:N2}% despues de comisiones y momentum perdiendo fuerza.";

                return new OpportunityExit(OpportunityStatus.ManagedProfitExit, candle.CloseTime, candle.Close, reason);
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
                : "La oportunidad vencio y la salida administrada por vencimiento esta activa.";

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
                    return new OpportunityExit(OpportunityStatus.HitStopLoss, candle.CloseTime, opportunity.StopLoss, "Perdida maxima tocada antes de la ganancia objetivo.");

                if (candle.High >= opportunity.TakeProfit2)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit2, candle.CloseTime, opportunity.TakeProfit2, "Ganancia extra alcanzada.");

                if (candle.High >= opportunity.TakeProfit1)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit1, candle.CloseTime, opportunity.TakeProfit1, "Ganancia objetivo alcanzada.");
            }
            else
            {
                if (candle.High >= opportunity.StopLoss)
                    return new OpportunityExit(OpportunityStatus.HitStopLoss, candle.CloseTime, opportunity.StopLoss, "Perdida maxima tocada antes de la ganancia objetivo.");

                if (candle.Low <= opportunity.TakeProfit2)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit2, candle.CloseTime, opportunity.TakeProfit2, "Ganancia extra alcanzada.");

                if (candle.Low <= opportunity.TakeProfit1)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit1, candle.CloseTime, opportunity.TakeProfit1, "Ganancia objetivo alcanzada.");
            }
        }

        if (DateTimeOffset.UtcNow > opportunity.ExpiresAt && relevantCandles.Length > 0)
        {
            var last = relevantCandles[^1];
            return new OpportunityExit(OpportunityStatus.Expired, last.CloseTime, last.Close, "La oportunidad vencio antes de tocar ganancia o perdida maxima.");
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

    private static decimal NetPercent(OpportunityReportRow opportunity, decimal exitPrice, decimal feePercentPerSide)
    {
        if (opportunity.Capital <= 0m)
            return 0m;

        return TradeCostCalculator.Build(opportunity.Side, opportunity.Capital, opportunity.EstimatedQuantity, opportunity.EntryPrice, exitPrice, feePercentPerSide).NetPercent;
    }
}
