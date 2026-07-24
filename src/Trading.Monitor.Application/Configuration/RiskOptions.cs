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
}
