using Microsoft.EntityFrameworkCore;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class EfWalletRepository(TradingMonitorDbContext dbContext) : IWalletRepository
{
    private static readonly IReadOnlyDictionary<string, Guid> SettingsIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
    {
        [MarketSymbolClassifier.CryptoMarket] = Guid.Parse("0fa2b2e6-35ec-4cc9-96b8-b8051eb4c2c5"),
        [MarketSymbolClassifier.ForexMarket] = Guid.Parse("f8c2765c-0602-42d8-a76f-6510b2342c21")
    };

    public async Task<WalletSnapshot> GetSnapshotAsync(string market, CancellationToken cancellationToken)
    {
        var normalizedMarket = MarketSymbolClassifier.NormalizeMarket(market);
        var settings = await dbContext.WalletSettings.AsNoTracking()
            .Where(setting => setting.Market == normalizedMarket)
            .OrderBy(setting => setting.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var assets = await dbContext.WalletAssets.AsNoTracking()
            .Where(asset => asset.Market == normalizedMarket)
            .OrderBy(asset => asset.Symbol)
            .Select(asset => new WalletAssetPosition(
                asset.Symbol,
                asset.Asset,
                asset.CoinQuantity,
                asset.AllowSellHighBuyLow,
                asset.AutoTradingEnabled,
                asset.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        return new WalletSnapshot(
            settings?.CashCapital ?? 0m,
            settings?.AutoTradingEnabled ?? false,
            assets,
            settings?.ManagedTargetNetPercent > 0m ? settings.ManagedTargetNetPercent : 5m);
    }

    public async Task SaveAsync(string market, decimal cashCapital, bool autoTradingEnabled, decimal managedTargetNetPercent, IReadOnlyCollection<WalletAssetUpdate> assets, CancellationToken cancellationToken)
    {
        var normalizedMarket = MarketSymbolClassifier.NormalizeMarket(market);
        var now = DateTimeOffset.UtcNow;
        var settings = await dbContext.WalletSettings
            .Where(setting => setting.Market == normalizedMarket)
            .OrderBy(setting => setting.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new WalletSettingsEntity
            {
                Id = SettingsIds.GetValueOrDefault(normalizedMarket, Guid.NewGuid()),
                Market = normalizedMarket,
                CreatedAt = now
            };
            dbContext.WalletSettings.Add(settings);
        }

        settings.Market = normalizedMarket;
        settings.CashCapital = Math.Round(Math.Max(0m, cashCapital), 2);
        settings.AutoTradingEnabled = autoTradingEnabled;
        settings.ManagedTargetNetPercent = Math.Round(Math.Max(0.01m, managedTargetNetPercent), 4);
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
            .Where(asset => asset.Market == normalizedMarket && symbols.Contains(asset.Symbol))
            .ToDictionaryAsync(asset => asset.Symbol, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var asset in normalizedAssets)
        {
            if (!existingAssets.TryGetValue(asset.Symbol, out var entity))
            {
                entity = new WalletAssetEntity
                {
                    Id = Guid.NewGuid(),
                    Market = normalizedMarket,
                    Symbol = asset.Symbol,
                    CreatedAt = now
                };
                dbContext.WalletAssets.Add(entity);
            }

            entity.Market = normalizedMarket;
            entity.Asset = asset.Asset;
            entity.CoinQuantity = Math.Round(asset.CoinQuantity, 8);
            entity.AllowSellHighBuyLow = asset.AllowSellHighBuyLow;
            entity.AutoTradingEnabled = asset.AutoTradingEnabled;
            entity.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyFillAsync(string market, string symbol, decimal cashDelta, decimal assetQuantityDelta, CancellationToken cancellationToken)
    {
        var normalizedMarket = MarketSymbolClassifier.NormalizeMarket(market);
        var normalizedSymbol = WalletSnapshot.NormalizeSymbol(symbol);
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var settingsExists = await dbContext.WalletSettings.AnyAsync(setting => setting.Market == normalizedMarket, cancellationToken);
        if (!settingsExists)
        {
            dbContext.WalletSettings.Add(new WalletSettingsEntity
            {
                Id = SettingsIds.GetValueOrDefault(normalizedMarket, Guid.NewGuid()),
                Market = normalizedMarket,
                CashCapital = 0m,
                ManagedTargetNetPercent = 5m,
                CreatedAt = now,
                UpdatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (cashDelta != 0m)
        {
            // Single atomic UPDATE ... SET CashCapital = CashCapital + @delta: two concurrent
            // fills (e.g. two signals executing back-to-back) can never overwrite each other's
            // change, unlike a read-then-write pattern.
            await dbContext.WalletSettings
                .Where(setting => setting.Market == normalizedMarket)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(setting => setting.CashCapital, setting => setting.CashCapital + cashDelta < 0m ? 0m : setting.CashCapital + cashDelta)
                    .SetProperty(setting => setting.UpdatedAt, now), cancellationToken);
        }

        if (assetQuantityDelta != 0m && !string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            var assetExists = await dbContext.WalletAssets.AnyAsync(asset => asset.Market == normalizedMarket && asset.Symbol == normalizedSymbol, cancellationToken);

            if (!assetExists)
            {
                dbContext.WalletAssets.Add(new WalletAssetEntity
                {
                    Id = Guid.NewGuid(),
                    Market = normalizedMarket,
                    Symbol = normalizedSymbol,
                    Asset = WalletSnapshot.ResolveAsset(normalizedSymbol),
                    CoinQuantity = 0m,
                    AllowSellHighBuyLow = false,
                    AutoTradingEnabled = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await dbContext.WalletAssets
                .Where(asset => asset.Market == normalizedMarket && asset.Symbol == normalizedSymbol)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(asset => asset.CoinQuantity, asset => asset.CoinQuantity + assetQuantityDelta < 0m ? 0m : asset.CoinQuantity + assetQuantityDelta)
                    .SetProperty(asset => asset.UpdatedAt, now), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
