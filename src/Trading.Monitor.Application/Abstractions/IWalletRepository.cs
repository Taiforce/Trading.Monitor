using Trading.Monitor.Application.Reporting;

namespace Trading.Monitor.Application.Abstractions;

public interface IWalletRepository
{
    Task<WalletSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task SaveAsync(decimal cashCapital, bool autoTradingEnabled, IReadOnlyCollection<WalletAssetUpdate> assets, CancellationToken cancellationToken);
}
