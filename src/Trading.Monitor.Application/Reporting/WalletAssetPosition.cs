using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Reporting;

public sealed record WalletAssetPosition(
    string Symbol,
    string Asset,
    decimal CoinQuantity,
    bool AllowSellHighBuyLow,
    bool AutoTradingEnabled,
    DateTimeOffset UpdatedAt);

public sealed record WalletAssetUpdate(
    string Symbol,
    string Asset,
    decimal CoinQuantity,
    bool AllowSellHighBuyLow,
    bool AutoTradingEnabled);

public sealed record WalletSnapshot(
    decimal CashCapital,
    bool AutoTradingEnabled,
    IReadOnlyList<WalletAssetPosition> Assets)
{
    public bool CanShowSignal(MarketSide side, string symbol)
    {
        return side != MarketSide.Short || CanSellHighBuyLow(symbol);
    }

    public bool CanSellHighBuyLow(string symbol)
    {
        var asset = FindAsset(symbol);
        return asset is { CoinQuantity: > 0m, AllowSellHighBuyLow: true };
    }

    public bool CanAutoTrade(string symbol)
    {
        var asset = FindAsset(symbol);
        return AutoTradingEnabled && asset is { AutoTradingEnabled: true };
    }

    public WalletAssetPosition? FindAsset(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        return Assets.FirstOrDefault(asset => string.Equals(asset.Symbol, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public decimal CoinQuantityFor(string symbol)
    {
        return FindAsset(symbol)?.CoinQuantity ?? 0m;
    }

    public static string NormalizeSymbol(string symbol)
    {
        return string.IsNullOrWhiteSpace(symbol) ? "" : symbol.Trim().ToUpperInvariant();
    }

    public static string ResolveAsset(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);

        if (normalized.EndsWith("USDT", StringComparison.Ordinal))
            return normalized[..^4];

        if (normalized.EndsWith("USD", StringComparison.Ordinal))
            return normalized[..^3];

        return normalized;
    }
}
