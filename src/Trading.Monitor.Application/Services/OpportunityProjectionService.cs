using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed class OpportunityProjectionService
{
    public OpportunityProjection Project(TradingOpportunity opportunity, ReportingOptions options)
    {
        return Project(opportunity, options.DefaultCapital, options.EstimatedFeePercentPerSide);
    }

    public OpportunityProjection Project(TradingOpportunity opportunity, decimal capital, decimal feePercentPerSide)
    {
        if (capital <= 0m)
            capital = 1m;

        var entryPrice = ResolveEntryPrice(opportunity);
        var quantity = entryPrice <= 0m ? 0m : capital / entryPrice;
        var tp1 = TradeCostCalculator.Build(opportunity.Side, capital, quantity, entryPrice, opportunity.TakeProfit1, feePercentPerSide);
        var tp2 = TradeCostCalculator.Build(opportunity.Side, capital, quantity, entryPrice, opportunity.TakeProfit2, feePercentPerSide);
        var stop = TradeCostCalculator.Build(opportunity.Side, capital, quantity, entryPrice, opportunity.StopLoss, feePercentPerSide);

        return new OpportunityProjection(Math.Round(capital, 2), RoundPrice(entryPrice), Math.Round(quantity, 8), tp1.TotalFees, tp1.GrossBenefit, tp1.NetBenefit, tp2.GrossBenefit, tp2.NetBenefit,
            stop.GrossBenefit, stop.NetBenefit);
    }

    private static decimal ResolveEntryPrice(TradingOpportunity opportunity)
    {
        return (opportunity.EntryLower + opportunity.EntryUpper) / 2m;
    }

    public static decimal CalculateGrossPnL(MarketSide side, decimal entryPrice, decimal exitPrice, decimal quantity)
    {
        return side == MarketSide.Long ? (exitPrice - entryPrice) * quantity : (entryPrice - exitPrice) * quantity;
    }

    private static decimal RoundPrice(decimal value)
    {
        var decimals = Math.Abs(value) switch { >= 1000m => 2, >= 1m => 4, _ => 8 };

        return Math.Round(value, decimals);
    }
}
