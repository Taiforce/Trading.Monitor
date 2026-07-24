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
}
