namespace Trading.Monitor.Domain;

public sealed record TradingOpportunity(string Symbol, MarketSide Side, int Score, DateTimeOffset ObservedAt, DateTimeOffset ExpiresAt, decimal LastPrice, decimal EntryLower, decimal EntryUpper,
    decimal StopLoss, decimal TakeProfit1, decimal TakeProfit2, decimal RiskReward, IReadOnlyList<string> ConfirmingIntervals, IReadOnlyList<string> Reasons, IReadOnlyList<string> Risks,
    IReadOnlyList<NewsItem> RelatedNews)
{
    public string AlertKey => $"{Symbol}:{Side}:{ObservedAt:yyyyMMddHHmm}:{Math.Round(LastPrice, 2)}";
}