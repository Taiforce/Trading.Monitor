namespace Trading.Monitor.Application.Configuration;

public sealed class ExchangeExecutionOptions
{
    public bool Enabled { get; set; }

    public string Provider { get; set; } = "BinanceSpot";

    public string Mode { get; set; } = "Paper";

    public string BaseUrl { get; set; } = "https://api.binance.com";

    public string ApiKeyEnvironmentVariable { get; set; } = "BINANCE_API_KEY";

    public string ApiSecretEnvironmentVariable { get; set; } = "BINANCE_API_SECRET";

    public bool AllowLiveOrders { get; set; }

    public bool UseTestOrderEndpoint { get; set; } = true;

    public decimal MaxCapitalPerTrade { get; set; } = 100m;

    public decimal DailyLossLimit { get; set; } = 50m;

    public int MinimumScoreToExecute { get; set; } = 95;

    public decimal MinimumExpectedNetProfitPercentAfterCosts { get; set; } = 0.6m;

    public bool EnableEntryOrders { get; set; } = true;

    public bool EnableExitOrders { get; set; } = true;

    public bool AllowShortSelling { get; set; }

    public int ReceiveWindowMilliseconds { get; set; } = 5000;

    public decimal MaxSlippagePercent { get; set; } = 0.25m;

    public string[] AllowedSymbols { get; set; } = ["BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "ADAUSDT"];

    /// <summary>
    /// Must be explicitly set to trade every scanned symbol. An empty <see cref="AllowedSymbols"/>
    /// list now fails closed (blocks everything) instead of silently allowing everything, so a
    /// misconfiguration can never widen the tradable universe by accident.
    /// </summary>
    public bool AllowAllSymbols { get; set; }

    /// <summary>Global manual stop: when true, no new entries are placed in any mode (Paper/Test/Live).</summary>
    public bool KillSwitchEnabled { get; set; }

    /// <summary>Maximum number of simultaneously open automatic positions (Simulated/Submitted/Filled) across all symbols.</summary>
    public int MaxOpenPositions { get; set; } = 3;

    /// <summary>Maximum total notional (sum of requested capital) that can be committed to new entries in a rolling 24h window. 0 disables the check.</summary>
    public decimal MaxDailyNotional { get; set; } = 500m;

    /// <summary>
    /// Required for Live mode as an extra, explicit acknowledgement beyond <see cref="AllowLiveOrders"/>
    /// and disabling <see cref="UseTestOrderEndpoint"/>. Must equal <c>"I_ACCEPT_LIVE_RISK"</c>
    /// (read from the <c>TRADING_MONITOR_LIVE_CONFIRM</c> environment variable) so real orders can
    /// never fire just because a config file was copied between environments.
    /// </summary>
    public string LiveConfirmationEnvironmentVariable { get; set; } = "TRADING_MONITOR_LIVE_CONFIRM";
}
