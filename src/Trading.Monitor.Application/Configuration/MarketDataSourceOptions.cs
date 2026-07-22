namespace Trading.Monitor.Application.Configuration;

public sealed class MarketDataSourceOptions
{
    public bool BinanceEnabled { get; set; } = true;

    public bool BinanceUsEnabled { get; set; } = true;

    public bool CoinbaseEnabled { get; set; } = true;

    public bool KrakenEnabled { get; set; } = true;

    public string BinanceBaseUrl { get; set; } = "https://api.binance.com";

    public string BinanceUsBaseUrl { get; set; } = "https://api.binance.us";

    public string CoinbaseBaseUrl { get; set; } = "https://api.exchange.coinbase.com";

    public string KrakenBaseUrl { get; set; } = "https://api.kraken.com";

    public int TimeoutSeconds { get; set; } = 10;
}
