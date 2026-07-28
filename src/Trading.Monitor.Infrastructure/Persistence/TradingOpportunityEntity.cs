using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class TradingOpportunityEntity
{
    public Guid Id { get; set; }

    public string AlertKey { get; set; } = "";

    public string Symbol { get; set; } = "";

    public MarketSide Side { get; set; }

    public OpportunityStatus Status { get; set; }

    public int Score { get; set; }

    public DateTimeOffset ObservedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ExitTime { get; set; }

    public decimal LastPrice { get; set; }

    public decimal EntryLower { get; set; }

    public decimal EntryUpper { get; set; }

    public decimal EntryPrice { get; set; }

    public decimal StopLoss { get; set; }

    public decimal TakeProfit1 { get; set; }

    public decimal TakeProfit2 { get; set; }

    public decimal? ExitPrice { get; set; }

    public string ExitReason { get; set; } = "";

    public decimal RiskReward { get; set; }

    public decimal Capital { get; set; }

    public decimal EstimatedQuantity { get; set; }

    public decimal EstimatedFees { get; set; }

    public decimal NetProfitAtTakeProfit1 { get; set; }

    public decimal NetProfitAtTakeProfit2 { get; set; }

    public decimal NetLossAtStop { get; set; }

    public decimal ManagedTargetNetPercent { get; set; } = 5m;

    public decimal ManagedTargetNetPnL { get; set; }

    public decimal? ManagedTargetExitPrice { get; set; }

    public decimal? RealizedGrossPnL { get; set; }

    public decimal? RealizedNetPnL { get; set; }

    public decimal? RealizedNetPercent { get; set; }

    public decimal? RealizedTotalObtained { get; set; }

    public string ConfirmingIntervalsJson { get; set; } = "[]";

    public string ReasonsJson { get; set; } = "[]";

    public string RisksJson { get; set; } = "[]";

    public string RelatedNewsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
