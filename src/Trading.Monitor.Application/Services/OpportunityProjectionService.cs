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
        var roundTripFees = capital * (feePercentPerSide / 100m) * 2m;
        var tp1Gross = CalculateGrossPnL(opportunity.Side, entryPrice, opportunity.TakeProfit1, quantity);
        var tp2Gross = CalculateGrossPnL(opportunity.Side, entryPrice, opportunity.TakeProfit2, quantity);
        var stopGross = CalculateGrossPnL(opportunity.Side, entryPrice, opportunity.StopLoss, quantity);

        return new OpportunityProjection(Math.Round(capital, 2), RoundPrice(entryPrice), Math.Round(quantity, 8), Math.Round(roundTripFees, 2), Math.Round(tp1Gross, 2), Math.Round(tp1Gross - roundTripFees, 2),
            Math.Round(tp2Gross, 2), Math.Round(tp2Gross - roundTripFees, 2), Math.Round(stopGross, 2), Math.Round(stopGross - roundTripFees, 2));
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