namespace Trading.Monitor.Application.Configuration;

public sealed class NewsOptions
{
    public bool Enabled { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 10;

    public int MaxNewsAgeHours { get; set; } = 18;

    public bool FearGreedEnabled { get; set; } = true;

    public string FearGreedBaseUrl { get; set; } = "https://api.alternative.me";

    public bool CryptoPanicEnabled { get; set; } = true;

    public string CryptoPanicBaseUrl { get; set; } = "https://cryptopanic.com";

    public string CryptoPanicAuthTokenEnvironmentVariable { get; set; } = "CRYPTOPANIC_AUTH_TOKEN";

    public string[] Feeds { get; set; } =
    [
        "https://www.coindesk.com/arc/outboundfeeds/rss/",
        "https://cointelegraph.com/rss",
        "https://decrypt.co/feed",
        "https://news.bitcoin.com/feed/",
        "https://beincrypto.com/feed/",
        "https://cryptobriefing.com/feed/",
        "https://cryptoslate.com/feed/",
        "https://u.today/rss",
        "https://ambcrypto.com/feed/",
        "https://blog.kraken.com/feed",
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=BTC-USD,ETH-USD,SOL-USD,XRP-USD,ADA-USD&region=US&lang=en-US",
        "https://search.cnbc.com/rs/search/combinedcms/view.xml?partnerId=wrss01&id=100003114",
        "https://www.investing.com/rss/news_25.rss",
        "https://www.investing.com/rss/news_301.rss",
        "https://feeds.marketwatch.com/marketwatch/topstories/",
        "https://www.federalreserve.gov/feeds/press_all.xml",
        "https://www.sec.gov/news/pressreleases.rss"
    ];

    public Dictionary<string, string[]> SymbolKeywords { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTCUSDT"] = ["BTC", "Bitcoin", "BTC-USD", "crypto", "cryptocurrency", "digital asset", "ETF", "Fed", "Federal Reserve", "inflation", "CPI", "rates", "SEC"],
        ["ETHUSDT"] = ["ETH", "Ethereum", "ETH-USD", "crypto", "cryptocurrency", "digital asset", "ETF", "Fed", "Federal Reserve", "inflation", "CPI", "rates", "SEC"],
        ["SOLUSDT"] = ["SOL", "Solana", "SOL-USD", "crypto", "cryptocurrency", "DeFi", "ETF", "Fed", "Federal Reserve", "inflation", "CPI", "rates", "SEC"],
        ["XRPUSDT"] = ["XRP", "Ripple", "XRP-USD", "crypto", "cryptocurrency", "payments", "regulation", "Fed", "Federal Reserve", "inflation", "CPI", "rates", "SEC"],
        ["ADAUSDT"] = ["ADA", "Cardano", "ADA-USD", "crypto", "cryptocurrency", "DeFi", "staking", "Fed", "Federal Reserve", "inflation", "CPI", "rates", "SEC"]
    };
}
