namespace Trading.Monitor.Application.Configuration;

public sealed class RiskOptions
{
    public decimal EntryAtrBuffer { get; set; } = 0.20m;

    public decimal AtrStopMultiplier { get; set; } = 1.50m;

    public decimal Target1R { get; set; } = 2.00m;

    public decimal Target2R { get; set; } = 3.00m;

    public decimal MinimumRiskReward { get; set; } = 2.00m;

    public decimal MinimumAtrPercent { get; set; } = 0.05m;

    public decimal MaximumAtrPercent { get; set; } = 8.00m;

    public decimal EstimatedFeePercentPerSide { get; set; } = 0.10m;

    public decimal EstimatedSpreadPercent { get; set; } = 0.05m;

    public decimal MinimumNetProfitPercentAfterCosts { get; set; } = 0.35m;

    public bool ManagedProfitExitEnabled { get; set; } = true;

    public decimal ManagedProfitExitPercentAfterCosts { get; set; } = 5.00m;

    public decimal ManagedQuickProfitExitPercentAfterCosts { get; set; } = 8.00m;

    public decimal ManagedTrailingGivebackPercent { get; set; } = 0.75m;

    public int ManagedProfitTrailCandlesAfterTarget { get; set; } = 1;

    public bool ManagedExitRequiresMomentumWeakness { get; set; }

    public bool ManagedHardStopExitEnabled { get; set; }

    public bool ManagedExpiryExitEnabled { get; set; }
}
