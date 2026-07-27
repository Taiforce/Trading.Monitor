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

    public string[] AllowedSymbols { get; set; } = ["BTCUSDT", "ETHUSDT"];
}
