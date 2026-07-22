namespace Trading.Monitor.Domain;

public sealed record OpportunityProjection(decimal Capital, decimal EntryPrice, decimal EstimatedQuantity, decimal EstimatedFees, decimal GrossProfitAtTakeProfit1, decimal NetProfitAtTakeProfit1,
    decimal GrossProfitAtTakeProfit2, decimal NetProfitAtTakeProfit2, decimal GrossLossAtStop, decimal NetLossAtStop);