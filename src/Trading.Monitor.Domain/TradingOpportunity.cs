namespace Trading.Monitor.Domain;

public sealed record TradingOpportunity(string Symbol, MarketSide Side, int Score, DateTimeOffset ObservedAt, DateTimeOffset ExpiresAt, decimal LastPrice, decimal EntryLower, decimal EntryUpper,
    decimal StopLoss, decimal TakeProfit1, decimal TakeProfit2, decimal RiskReward, IReadOnlyList<string> ConfirmingIntervals, IReadOnlyList<string> Reasons, IReadOnlyList<string> Risks,
    IReadOnlyList<NewsItem> RelatedNews, SignalOperationKind OperationKind = SignalOperationKind.Fixed, SignalOriginKind OriginKind = SignalOriginKind.OwnAi)
{
    public string AlertKey => $"{Symbol}:{Side}:{OperationKind}:{OriginKind}:{ObservedAt:yyyyMMddHHmm}:{ExpiresAt:yyyyMMddHHmm}:{Math.Round(LastPrice, 2)}";
}
