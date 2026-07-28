namespace Trading.Monitor.Application.Configuration;

public sealed class MarketDataSourceOptions
{
    public bool BinanceEnabled { get; set; } = true;

    public bool BinanceUsEnabled { get; set; } = true;

    public bool CoinbaseEnabled { get; set; } = true;

    public bool KrakenEnabled { get; set; } = true;

    public bool YahooFinanceForexEnabled { get; set; } = true;

    public bool AlphaVantageForexEnabled { get; set; } = true;

    public string BinanceBaseUrl { get; set; } = "https://api.binance.com";

    public string BinanceUsBaseUrl { get; set; } = "https://api.binance.us";

    public string CoinbaseBaseUrl { get; set; } = "https://api.exchange.coinbase.com";

    public string KrakenBaseUrl { get; set; } = "https://api.kraken.com";

    public string YahooFinanceBaseUrl { get; set; } = "https://query1.finance.yahoo.com";

    public string AlphaVantageBaseUrl { get; set; } = "https://www.alphavantage.co";

    public string AlphaVantageApiKeyEnvironmentVariable { get; set; } = "ALPHA_VANTAGE_API_KEY";

    public int TimeoutSeconds { get; set; } = 10;
}
