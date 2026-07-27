using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed record TradeCostBreakdown(
    decimal Investment,
    decimal Quantity,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal EntryNotional,
    decimal ExitNotional,
    decimal EntryFee,
    decimal ExitFee,
    decimal TotalFees,
    decimal GrossBenefit,
    decimal NetBenefit,
    decimal TotalObtained,
    decimal NetPercent);

public static class TradeCostCalculator
{
    public static TradeCostBreakdown Build(MarketSide side, decimal investment, decimal quantity, decimal entryPrice, decimal exitPrice, decimal feePercentPerSide)
    {
        investment = Math.Max(0m, investment);
        quantity = Math.Max(0m, quantity);
        entryPrice = Math.Max(0m, entryPrice);
        exitPrice = Math.Max(0m, exitPrice);
        var feeRate = Math.Max(0m, feePercentPerSide) / 100m;
        var entryNotional = investment;
        var exitNotional = quantity * exitPrice;
        var entryFee = entryNotional * feeRate;
        var exitFee = exitNotional * feeRate;
        var grossBenefit = OpportunityProjectionService.CalculateGrossPnL(side, entryPrice, exitPrice, quantity);
        var netBenefit = grossBenefit - entryFee - exitFee;
        var totalObtained = investment + netBenefit;
        var netPercent = investment <= 0m ? 0m : netBenefit / investment * 100m;

        return new TradeCostBreakdown(
            Math.Round(investment, 2),
            Math.Round(quantity, 8),
            RoundPrice(entryPrice),
            RoundPrice(exitPrice),
            Math.Round(entryNotional, 2),
            Math.Round(exitNotional, 2),
            Math.Round(entryFee, 2),
            Math.Round(exitFee, 2),
            Math.Round(entryFee + exitFee, 2),
            Math.Round(grossBenefit, 2),
            Math.Round(netBenefit, 2),
            Math.Round(totalObtained, 2),
            Math.Round(netPercent, 4));
    }

    public static decimal ResolveExitPriceForNetPercent(MarketSide side, decimal investment, decimal quantity, decimal entryPrice, decimal targetNetPercent, decimal feePercentPerSide)
    {
        if (investment <= 0m || quantity <= 0m || entryPrice <= 0m)
            return entryPrice;

        var feeRate = Math.Max(0m, feePercentPerSide) / 100m;
        var targetNet = investment * targetNetPercent / 100m;
        var entryFee = investment * feeRate;
        decimal exitNotional;

        if (side == MarketSide.Long)
        {
            exitNotional = (investment + entryFee + targetNet) / Math.Max(0.00000001m, 1m - feeRate);
        }
        else
        {
            exitNotional = (investment - entryFee - targetNet) / (1m + feeRate);
        }

        if (exitNotional <= 0m)
            exitNotional = 0.00000001m;

        return RoundPrice(exitNotional / quantity);
    }

    private static decimal RoundPrice(decimal value)
    {
        var decimals = Math.Abs(value) switch { >= 1000m => 2, >= 1m => 4, _ => 8 };
        return Math.Round(value, decimals);
    }
}
