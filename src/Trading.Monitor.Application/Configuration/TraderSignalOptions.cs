namespace Trading.Monitor.Application.Configuration;

/// <summary>Configuration for the "Traders" signal source (following real top-trader positions on public leaderboards).</summary>
public sealed class TraderSignalOptions
{
    public bool Enabled { get; set; } = true;

    public bool BinanceLeaderboardEnabled { get; set; } = true;

    public string BinanceLeaderboardBaseUrl { get; set; } = "https://www.binance.com";

    /// <summary>DAILY, WEEKLY, MONTHLY or ALL - matches Binance's own leaderboard period filter.</summary>
    public string PeriodType { get; set; } = "WEEKLY";

    /// <summary>How many top-ranked (by ROI) traders to follow.</summary>
    public int TopTraderCount { get; set; } = 8;

    public int SignalExpiryHours { get; set; } = 12;

    public int TimeoutSeconds { get; set; } = 10;
}
