using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public static class MarketSymbolClassifier
{
    public const string CryptoMarket = "crypto";
    public const string ForexMarket = "forex";

    public static readonly string[] DefaultCryptoSymbols = ["BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "ADAUSDT"];

    public static readonly string[] DefaultForexSymbols =
    [
        "EURUSD",
        "GBPUSD",
        "USDJPY",
        "USDCHF",
        "AUDUSD",
        "USDCAD",
        "NZDUSD",
        "USDMXN",
        "EURMXN",
        "GBPJPY",
        "EURJPY",
        "EURGBP"
    ];

    private static readonly HashSet<string> CryptoSymbols = new(DefaultCryptoSymbols, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ForexSymbols = new(DefaultForexSymbols, StringComparer.OrdinalIgnoreCase);

    public static string NormalizeMarket(string? market)
    {
        return market?.Trim().ToLowerInvariant() switch
        {
            "forex" or "fx" or "divisas" => ForexMarket,
            _ => CryptoMarket
        };
    }

    public static string MarketLabel(string? market)
    {
        return NormalizeMarket(market) == ForexMarket ? "Forex" : "Crypto";
    }

    public static MarketKind GetMarketKind(string? symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
            return MarketKind.Other;

        if (ForexSymbols.Contains(normalized) || LooksLikeForexPair(normalized))
            return MarketKind.Forex;

        if (CryptoSymbols.Contains(normalized) || normalized.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            return MarketKind.Crypto;

        return MarketKind.Other;
    }

    public static bool MatchesMarket(string? symbol, string? market)
    {
        var normalizedMarket = NormalizeMarket(market);
        var kind = GetMarketKind(symbol);

        return normalizedMarket == ForexMarket
            ? kind == MarketKind.Forex
            : kind == MarketKind.Crypto;
    }

    public static IReadOnlyList<string> BuildSymbolList(IEnumerable<string> symbols, string? market)
    {
        var defaults = NormalizeMarket(market) == ForexMarket ? DefaultForexSymbols : DefaultCryptoSymbols;
        var configured = defaults
            .Concat(symbols.Where(symbol => MatchesMarket(symbol, market)).Select(NormalizeSymbol))
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return configured
            .OrderBy(symbol =>
            {
                var index = Array.FindIndex(defaults, configuredSymbol => string.Equals(configuredSymbol, symbol, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizeSymbol(string? symbol)
    {
        return string.IsNullOrWhiteSpace(symbol)
            ? ""
            : symbol.Trim().Replace("/", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToUpperInvariant();
    }

    public static string BaseAsset(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);

        if (GetMarketKind(normalized) == MarketKind.Forex && normalized.Length >= 6)
            return normalized[..3];

        if (normalized.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            return normalized[..^4];

        if (normalized.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
            return normalized[..^3];

        return normalized;
    }

    public static string QuoteAsset(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);

        if (GetMarketKind(normalized) == MarketKind.Forex && normalized.Length >= 6)
            return normalized.Substring(3, 3);

        if (normalized.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            return "USDT";

        if (normalized.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
            return "USD";

        return "";
    }

    private static bool LooksLikeForexPair(string symbol)
    {
        if (symbol.Length != 6)
            return false;

        var baseCurrency = symbol[..3];
        var quoteCurrency = symbol[3..];

        return IsCurrency(baseCurrency) && IsCurrency(quoteCurrency);
    }

    private static bool IsCurrency(string value)
    {
        return value is "USD" or "EUR" or "GBP" or "JPY" or "CHF" or "AUD" or "CAD" or "NZD" or "MXN";
    }
}
