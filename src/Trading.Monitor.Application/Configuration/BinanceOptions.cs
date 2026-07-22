namespace Trading.Monitor.Application.Configuration;

public sealed class BinanceOptions
{
    public string BaseUrl { get; set; } = "https://api.binance.com";

    public int TimeoutSeconds { get; set; } = 10;
}