namespace Trading.Monitor.Application.Configuration;

public sealed class NewsOptions
{
    public bool Enabled { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 10;

    public int MaxNewsAgeHours { get; set; } = 18;

    public string[] Feeds { get; set; } =
    [
        "https://www.coindesk.com/arc/outboundfeeds/rss/",
        "https://cointelegraph.com/rss",
        "https://decrypt.co/feed",
        "https://news.bitcoin.com/feed/",
        "https://beincrypto.com/feed/",
        "https://cryptobriefing.com/feed/",
        "https://blog.kraken.com/feed",
        "https://www.federalreserve.gov/feeds/press_all.xml",
        "https://www.sec.gov/news/pressreleases.rss"
    ];

    public Dictionary<string, string[]> SymbolKeywords { get; set; } = new(StringComparer.OrdinalIgnoreCase) { ["BTCUSDT"] = ["BTC", "Bitcoin"], ["ETHUSDT"] = ["ETH", "Ethereum"] };
}
