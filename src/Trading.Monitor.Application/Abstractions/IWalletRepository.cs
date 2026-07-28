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
        return SaveAsync(MarketSymbolClassifier.CryptoMarket, cashCapital, autoTradingEnabled, assets, cancellationToken);
    }

    Task SaveAsync(string market, decimal cashCapital, bool autoTradingEnabled, IReadOnlyCollection<WalletAssetUpdate> assets, CancellationToken cancellationToken);
}
