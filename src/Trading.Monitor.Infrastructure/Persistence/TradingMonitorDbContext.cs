using Microsoft.EntityFrameworkCore;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class TradingMonitorDbContext : DbContext
{
    public TradingMonitorDbContext(DbContextOptions<TradingMonitorDbContext> options) : base(options) { }

    public DbSet<TradingOpportunityEntity> Opportunities => Set<TradingOpportunityEntity>();

    public DbSet<DataSourceEntity> DataSources => Set<DataSourceEntity>();

    public DbSet<IngestionEventEntity> IngestionEvents => Set<IngestionEventEntity>();

    public DbSet<ResearchItemEntity> ResearchItems => Set<ResearchItemEntity>();

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
        opportunity.Property(entity => entity.RealizedGrossPnL).HasColumnType("decimal(18,2)");
        opportunity.Property(entity => entity.RealizedNetPnL).HasColumnType("decimal(18,2)");

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
    }
}
