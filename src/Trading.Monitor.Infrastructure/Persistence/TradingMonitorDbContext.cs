using Microsoft.EntityFrameworkCore;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class TradingMonitorDbContext : DbContext
{
    public TradingMonitorDbContext(DbContextOptions<TradingMonitorDbContext> options) : base(options) { }

    public DbSet<TradingOpportunityEntity> Opportunities => Set<TradingOpportunityEntity>();

    public DbSet<DataSourceEntity> DataSources => Set<DataSourceEntity>();

    public DbSet<IngestionEventEntity> IngestionEvents => Set<IngestionEventEntity>();

    public DbSet<ResearchItemEntity> ResearchItems => Set<ResearchItemEntity>();

    public DbSet<TraderSourceEntity> TraderSources => Set<TraderSourceEntity>();

    public DbSet<TraderProfileEntity> TraderProfiles => Set<TraderProfileEntity>();

    public DbSet<TraderTradeEntity> TraderTrades => Set<TraderTradeEntity>();

    public DbSet<TradeExecutionEntity> TradeExecutions => Set<TradeExecutionEntity>();

    public DbSet<WalletSettingsEntity> WalletSettings => Set<WalletSettingsEntity>();

    public DbSet<WalletAssetEntity> WalletAssets => Set<WalletAssetEntity>();

    public DbSet<HistoricalMarketCandleEntity> HistoricalMarketCandles => Set<HistoricalMarketCandleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var opportunity = modelBuilder.Entity<TradingOpportunityEntity>();
        opportunity.ToTable("trading_opportunities");
        opportunity.HasKey(entity => entity.Id);
        opportunity.HasIndex(entity => entity.AlertKey).IsUnique();
        opportunity.HasIndex(entity => new { entity.Symbol, entity.Side, entity.ObservedAt });
        opportunity.HasIndex(entity => entity.Status);
        opportunity.Property(entity => entity.Symbol).HasMaxLength(32);
        opportunity.Property(entity => entity.AlertKey).HasMaxLength(128);
        opportunity.Property(entity => entity.Side).HasConversion<string>().HasMaxLength(16);
        opportunity.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(24);
        opportunity.Property(entity => entity.ExitReason).HasMaxLength(512);
        opportunity.Property(entity => entity.LastPrice).HasColumnType("decimal(18,8)");
        opportunity.Property(entity => entity.EntryLower).HasColumnType("decimal(18,8)");
        opportunity.Property(entity => entity.EntryUpper).HasColumnType("decimal(18,8)");
        opportunity.Property(entity => entity.EntryPrice).HasColumnType("decimal(18,8)");
        opportunity.Property(entity => entity.StopLoss).HasColumnType("decimal(18,8)");
        opportunity.Property(entity => entity.TakeProfit1).HasColumnType("decimal(18,8)");
        opportunity.Property(entity => entity.TakeProfit2).HasColumnType("decimal(18,8)");
        opportunity.Property(entity => entity.ExitPrice).HasColumnType("decimal(18,8)");
        opportunity.Property(entity => entity.RiskReward).HasColumnType("decimal(18,4)");
        opportunity.Property(entity => entity.Capital).HasColumnType("decimal(18,2)");
        opportunity.Property(entity => entity.EstimatedQuantity).HasColumnType("decimal(18,8)");
        opportunity.Property(entity => entity.EstimatedFees).HasColumnType("decimal(18,2)");
        opportunity.Property(entity => entity.NetProfitAtTakeProfit1).HasColumnType("decimal(18,2)");
        opportunity.Property(entity => entity.NetProfitAtTakeProfit2).HasColumnType("decimal(18,2)");
        opportunity.Property(entity => entity.NetLossAtStop).HasColumnType("decimal(18,2)");
        opportunity.Property(entity => entity.ManagedTargetNetPercent).HasColumnType("decimal(18,4)");
        opportunity.Property(entity => entity.ManagedTargetNetPnL).HasColumnType("decimal(18,2)");
        opportunity.Property(entity => entity.ManagedTargetExitPrice).HasColumnType("decimal(18,8)");
        opportunity.Property(entity => entity.RealizedGrossPnL).HasColumnType("decimal(18,2)");
        opportunity.Property(entity => entity.RealizedNetPnL).HasColumnType("decimal(18,2)");
        opportunity.Property(entity => entity.RealizedNetPercent).HasColumnType("decimal(18,4)");
        opportunity.Property(entity => entity.RealizedTotalObtained).HasColumnType("decimal(18,2)");

        var source = modelBuilder.Entity<DataSourceEntity>();
        source.ToTable("data_sources");
        source.HasKey(entity => entity.Id);
        source.HasIndex(entity => new { entity.Name, entity.Kind }).IsUnique();
        source.Property(entity => entity.Name).HasMaxLength(128);
        source.Property(entity => entity.Kind).HasConversion<string>().HasMaxLength(32);
        source.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(24);
        source.Property(entity => entity.Url).HasMaxLength(1024);
        source.Property(entity => entity.LastMessage).HasMaxLength(2048);

        var ingestionEvent = modelBuilder.Entity<IngestionEventEntity>();
        ingestionEvent.ToTable("ingestion_events");
        ingestionEvent.HasKey(entity => entity.Id);
        ingestionEvent.HasIndex(entity => new { entity.SourceName, entity.CompletedAt });
        ingestionEvent.Property(entity => entity.SourceName).HasMaxLength(128);
        ingestionEvent.Property(entity => entity.Kind).HasConversion<string>().HasMaxLength(32);
        ingestionEvent.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(24);
        ingestionEvent.Property(entity => entity.Url).HasMaxLength(1024);
        ingestionEvent.Property(entity => entity.Message).HasMaxLength(2048);

        var researchItem = modelBuilder.Entity<ResearchItemEntity>();
        researchItem.ToTable("research_items");
        researchItem.HasKey(entity => entity.Id);
        researchItem.HasIndex(entity => entity.Url).IsUnique();
        researchItem.HasIndex(entity => entity.PublishedAt);
        researchItem.Property(entity => entity.Source).HasMaxLength(128);
        researchItem.Property(entity => entity.Kind).HasConversion<string>().HasMaxLength(32);
        researchItem.Property(entity => entity.Title).HasMaxLength(2048);
        researchItem.Property(entity => entity.Url).HasMaxLength(2048);
        researchItem.Property(entity => entity.Sentiment).HasConversion<string>().HasMaxLength(16);

        var traderSource = modelBuilder.Entity<TraderSourceEntity>();
        traderSource.ToTable("trader_sources");
        traderSource.HasKey(entity => entity.Id);
        traderSource.HasIndex(entity => entity.Platform).IsUnique();
        traderSource.Property(entity => entity.Platform).HasMaxLength(64);
        traderSource.Property(entity => entity.Name).HasMaxLength(160);
        traderSource.Property(entity => entity.Market).HasMaxLength(128);
        traderSource.Property(entity => entity.Url).HasMaxLength(2048);
        traderSource.Property(entity => entity.DataAccess).HasMaxLength(256);
        traderSource.Property(entity => entity.DataQuality).HasMaxLength(256);
        traderSource.Property(entity => entity.Notes).HasMaxLength(2048);

        var traderProfile = modelBuilder.Entity<TraderProfileEntity>();
        traderProfile.ToTable("trader_profiles");
        traderProfile.HasKey(entity => entity.Id);
        traderProfile.HasIndex(entity => new { entity.Platform, entity.ExternalId }).IsUnique();
        traderProfile.Property(entity => entity.Platform).HasMaxLength(64);
        traderProfile.Property(entity => entity.DisplayName).HasMaxLength(160);
        traderProfile.Property(entity => entity.ExternalId).HasMaxLength(160);
        traderProfile.Property(entity => entity.ProfileUrl).HasMaxLength(2048);
        traderProfile.Property(entity => entity.Market).HasMaxLength(128);
        traderProfile.Property(entity => entity.StrategyType).HasMaxLength(160);
        traderProfile.Property(entity => entity.PopularityText).HasMaxLength(512);
        traderProfile.Property(entity => entity.PerformanceText).HasMaxLength(512);
        traderProfile.Property(entity => entity.DataAvailability).HasMaxLength(512);
        traderProfile.Property(entity => entity.Notes).HasMaxLength(2048);

        var traderTrade = modelBuilder.Entity<TraderTradeEntity>();
        traderTrade.ToTable("trader_trades");
        traderTrade.HasKey(entity => entity.Id);
        traderTrade.HasIndex(entity => new { entity.TraderProfileId, entity.OpenedAt });
        traderTrade.HasIndex(entity => new { entity.TraderProfileId, entity.ExternalTradeId }).IsUnique();
        traderTrade.Property(entity => entity.ExternalTradeId).HasMaxLength(160);
        traderTrade.Property(entity => entity.Symbol).HasMaxLength(32);
        traderTrade.Property(entity => entity.Side).HasConversion<string>().HasMaxLength(16);
        traderTrade.Property(entity => entity.Status).HasMaxLength(24);
        traderTrade.Property(entity => entity.EntryPrice).HasColumnType("decimal(18,8)");
        traderTrade.Property(entity => entity.ExitPrice).HasColumnType("decimal(18,8)");
        traderTrade.Property(entity => entity.Quantity).HasColumnType("decimal(18,8)");
        traderTrade.Property(entity => entity.PnLPercent).HasColumnType("decimal(18,4)");
        traderTrade.Property(entity => entity.NetPnL).HasColumnType("decimal(18,2)");
        traderTrade.Property(entity => entity.Leverage).HasColumnType("decimal(18,4)");
        traderTrade.Property(entity => entity.SourceUrl).HasMaxLength(2048);
        traderTrade.Property(entity => entity.Notes).HasMaxLength(2048);
        traderTrade.HasOne(entity => entity.TraderProfile)
            .WithMany()
            .HasForeignKey(entity => entity.TraderProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        var tradeExecution = modelBuilder.Entity<TradeExecutionEntity>();
        tradeExecution.ToTable("trade_executions");
        tradeExecution.HasKey(entity => entity.Id);
        tradeExecution.HasIndex(entity => new { entity.OpportunityId, entity.CreatedAt });
        tradeExecution.HasIndex(entity => entity.CreatedAt);
        tradeExecution.HasIndex(entity => entity.Status);
        tradeExecution.Property(entity => entity.Symbol).HasMaxLength(32);
        tradeExecution.Property(entity => entity.Side).HasConversion<string>().HasMaxLength(16);
        tradeExecution.Property(entity => entity.Action).HasConversion<string>().HasMaxLength(24);
        tradeExecution.Property(entity => entity.Mode).HasConversion<string>().HasMaxLength(16);
        tradeExecution.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(16);
        tradeExecution.Property(entity => entity.RequestedCapital).HasColumnType("decimal(18,2)");
        tradeExecution.Property(entity => entity.RequestedQuantity).HasColumnType("decimal(18,8)");
        tradeExecution.Property(entity => entity.ExecutedQuantity).HasColumnType("decimal(18,8)");
        tradeExecution.Property(entity => entity.ExecutedQuote).HasColumnType("decimal(18,2)");
        tradeExecution.Property(entity => entity.Price).HasColumnType("decimal(18,8)");
        tradeExecution.Property(entity => entity.ClientOrderId).HasMaxLength(64);
        tradeExecution.Property(entity => entity.ExchangeOrderId).HasMaxLength(128);
        tradeExecution.Property(entity => entity.Reason).HasMaxLength(512);
        tradeExecution.Property(entity => entity.Message).HasMaxLength(2048);
        tradeExecution.Property(entity => entity.RequestJson).HasColumnType("nvarchar(max)");
        tradeExecution.Property(entity => entity.ResponseJson).HasColumnType("nvarchar(max)");
        tradeExecution.HasOne(entity => entity.Opportunity)
            .WithMany()
            .HasForeignKey(entity => entity.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        var walletSettings = modelBuilder.Entity<WalletSettingsEntity>();
        walletSettings.ToTable("wallet_settings");
        walletSettings.HasKey(entity => entity.Id);
        walletSettings.HasIndex(entity => entity.Market).IsUnique();
        walletSettings.Property(entity => entity.Market).HasMaxLength(16);
        walletSettings.Property(entity => entity.CashCapital).HasColumnType("decimal(18,2)");

        var walletAsset = modelBuilder.Entity<WalletAssetEntity>();
        walletAsset.ToTable("wallet_assets");
        walletAsset.HasKey(entity => entity.Id);
        walletAsset.HasIndex(entity => entity.Symbol).IsUnique();
        walletAsset.Property(entity => entity.Market).HasMaxLength(16);
        walletAsset.Property(entity => entity.Symbol).HasMaxLength(32);
        walletAsset.Property(entity => entity.Asset).HasMaxLength(16);
        walletAsset.Property(entity => entity.CoinQuantity).HasColumnType("decimal(18,8)");

        var historicalCandle = modelBuilder.Entity<HistoricalMarketCandleEntity>();
        historicalCandle.ToTable("historical_market_candles");
        historicalCandle.HasKey(entity => entity.Id);
        historicalCandle.HasIndex(entity => new { entity.Symbol, entity.Interval, entity.OpenTime }).IsUnique();
        historicalCandle.HasIndex(entity => new { entity.Market, entity.Symbol, entity.Interval });
        historicalCandle.Property(entity => entity.Market).HasMaxLength(16);
        historicalCandle.Property(entity => entity.Source).HasMaxLength(128);
        historicalCandle.Property(entity => entity.Symbol).HasMaxLength(32);
        historicalCandle.Property(entity => entity.Interval).HasMaxLength(8);
        historicalCandle.Property(entity => entity.Open).HasColumnType("decimal(18,8)");
        historicalCandle.Property(entity => entity.High).HasColumnType("decimal(18,8)");
        historicalCandle.Property(entity => entity.Low).HasColumnType("decimal(18,8)");
        historicalCandle.Property(entity => entity.Close).HasColumnType("decimal(18,8)");
        historicalCandle.Property(entity => entity.Volume).HasColumnType("decimal(28,8)");
        historicalCandle.Property(entity => entity.QuoteVolume).HasColumnType("decimal(28,8)");
    }
}
