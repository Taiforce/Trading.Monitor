using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;

namespace Trading.Monitor.Application.Abstractions;

public interface IWalletRepository
{
    Task<WalletSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return GetSnapshotAsync(MarketSymbolClassifier.CryptoMarket, cancellationToken);
    }

    Task<WalletSnapshot> GetSnapshotAsync(string market, CancellationToken cancellationToken);

    Task SaveAsync(decimal cashCapital, bool autoTradingEnabled, IReadOnlyCollection<WalletAssetUpdate> assets, CancellationToken cancellationToken)
    {
        return SaveAsync(MarketSymbolClassifier.CryptoMarket, cashCapital, autoTradingEnabled, 5m, assets, cancellationToken);
    }

    Task SaveAsync(decimal cashCapital, bool autoTradingEnabled, decimal managedTargetNetPercent, IReadOnlyCollection<WalletAssetUpdate> assets, CancellationToken cancellationToken)
    {
        return SaveAsync(MarketSymbolClassifier.CryptoMarket, cashCapital, autoTradingEnabled, managedTargetNetPercent, assets, cancellationToken);
    }

    Task SaveAsync(string market, decimal cashCapital, bool autoTradingEnabled, IReadOnlyCollection<WalletAssetUpdate> assets, CancellationToken cancellationToken)
    {
        return SaveAsync(market, cashCapital, autoTradingEnabled, 5m, assets, cancellationToken);
    }

    Task SaveAsync(string market, decimal cashCapital, bool autoTradingEnabled, decimal managedTargetNetPercent, IReadOnlyCollection<WalletAssetUpdate> assets, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically applies a fill to the wallet ledger: debits/credits cash and the base-asset
    /// quantity for <paramref name="symbol"/> in one database round-trip, so concurrent fills
    /// (e.g. two signals executing at once) never read-modify-write a stale balance.
    /// </summary>
    Task ApplyFillAsync(string market, string symbol, decimal cashDelta, decimal assetQuantityDelta, CancellationToken cancellationToken);
}
