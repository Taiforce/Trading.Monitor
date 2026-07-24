using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Reporting;

public sealed record OpportunityReportRow(Guid Id, string Symbol, MarketSide Side, OpportunityStatus Status, int Score, DateTimeOffset ObservedAt, DateTimeOffset ExpiresAt, DateTimeOffset? ExitTime,
    decimal LastPrice, decimal EntryLower, decimal EntryUpper, decimal EntryPrice, decimal StopLoss, decimal TakeProfit1, decimal TakeProfit2, decimal? ExitPrice, decimal Capital, decimal EstimatedQuantity,
    decimal EstimatedFees, decimal NetProfitAtTakeProfit1, decimal NetProfitAtTakeProfit2, decimal NetLossAtStop, decimal? RealizedNetPnL, decimal RiskReward, string ConfirmingIntervals, string Reasons,
    string Risks);
