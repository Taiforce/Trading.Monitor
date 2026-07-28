using Microsoft.EntityFrameworkCore;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Reporting;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class EfWalletRepository(TradingMonitorDbContext dbContext) : IWalletRepository
{
    private static readonly Guid SettingsId = Guid.Parse("0fa2b2e6-35ec-4cc9-96b8-b8051eb4c2c5");

    public async Task<WalletSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.WalletSettings.AsNoTracking()
            .OrderBy(setting => setting.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var assets = await dbContext.WalletAssets.AsNoTracking()
            .OrderBy(asset => asset.Symbol)
            .Select(asset => new WalletAssetPosition(
                asset.Symbol,
                asset.Asset,
                asset.CoinQuantity,
                asset.AllowSellHighBuyLow,
                asset.AutoTradingEnabled,
                asset.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        return new WalletSnapshot(settings?.CashCapital ?? 0m, settings?.AutoTradingEnabled ?? false, assets);
    }

    public async Task SaveAsync(decimal cashCapital, bool autoTradingEnabled, IReadOnlyCollection<WalletAssetUpdate> assets, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = await dbContext.WalletSettings
            .OrderBy(setting => setting.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new WalletSettingsEntity
            {
                Id = SettingsId,
                CreatedAt = now
            };
            dbContext.WalletSettings.Add(settings);
        }

        settings.CashCapital = Math.Round(Math.Max(0m, cashCapital), 2);
        settings.AutoTradingEnabled = autoTradingEnabled;
        settings.UpdatedAt = now;

        var normalizedAssets = assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Symbol))
            .Select(asset => asset with
            {
                Symbol = WalletSnapshot.NormalizeSymbol(asset.Symbol),
                Asset = string.IsNullOrWhiteSpace(asset.Asset) ? WalletSnapshot.ResolveAsset(asset.Symbol) : asset.Asset.Trim().ToUpperInvariant(),
                CoinQuantity = Math.Max(0m, asset.CoinQuantity)
            })
            .DistinctBy(asset => asset.Symbol)
            .ToArray();

        var symbols = normalizedAssets.Select(asset => asset.Symbol).ToArray();
        var existingAssets = await dbContext.WalletAssets
            .Where(asset => symbols.Contains(asset.Symbol))
            .ToDictionaryAsync(asset => asset.Symbol, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var asset in normalizedAssets)
        {
            if (!existingAssets.TryGetValue(asset.Symbol, out var entity))
            {
                entity = new WalletAssetEntity
                {
                    Id = Guid.NewGuid(),
                    Symbol = asset.Symbol,
                    CreatedAt = now
                };
                dbContext.WalletAssets.Add(entity);
            }

            entity.Asset = asset.Asset;
            entity.CoinQuantity = Math.Round(asset.CoinQuantity, 8);
            entity.AllowSellHighBuyLow = asset.AllowSellHighBuyLow;
            entity.AutoTradingEnabled = asset.AutoTradingEnabled;
            entity.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
