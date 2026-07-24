namespace Trading.Monitor.Application.Configuration;

public sealed class ReportingOptions
{
    public decimal DefaultCapital { get; set; } = 1000m;

    public decimal EstimatedFeePercentPerSide { get; set; } = 0.10m;

    public decimal MinimumNetProfitPercentAfterCosts { get; set; } = 0.35m;
}
