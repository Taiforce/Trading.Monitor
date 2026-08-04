using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class EfOpportunityRepository : IOpportunityRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TradingMonitorDbContext _dbContext;
    private readonly OpportunityProjectionService _projectionService;
    private readonly IOptionsMonitor<ReportingOptions> _reportingOptions;
    private readonly IOptionsMonitor<RiskOptions> _riskOptions;

    public EfOpportunityRepository(TradingMonitorDbContext dbContext, OpportunityProjectionService projectionService, IOptionsMonitor<ReportingOptions> reportingOptions, IOptionsMonitor<RiskOptions> riskOptions)
    {
        _dbContext = dbContext;
        _projectionService = projectionService;
        _reportingOptions = reportingOptions;
        _riskOptions = riskOptions;
    }

    public async Task<bool> HasRecentSimilarSignalAsync(TradingOpportunity opportunity, TimeSpan duplicateWindow, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(duplicateWindow);

        var entities = await _dbContext.Opportunities.AsNoTracking()
            .Where(entity => entity.Symbol == opportunity.Symbol
                             && entity.Side == opportunity.Side
                             && entity.OperationKind == opportunity.OperationKind
                             && entity.OriginKind == opportunity.OriginKind
                             && entity.ObservedAt >= cutoff)
            .ToArrayAsync(cancellationToken);

        return entities.Any(entity => IsSameSignalFamily(entity.ObservedAt, entity.ExpiresAt, opportunity));
    }

    public async Task SaveAsync(TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Opportunities.AnyAsync(entity => entity.AlertKey == opportunity.AlertKey, cancellationToken);

        if (existing)
            return;

        var projection = _projectionService.Project(opportunity, _reportingOptions.CurrentValue);
        var defaultManagedTargetPercent = await ResolveDefaultManagedTargetPercentAsync(opportunity.Symbol, cancellationToken);
        var managedTarget = BuildManagedTarget(opportunity.Side, projection.Capital, projection.EstimatedQuantity, projection.EntryPrice, defaultManagedTargetPercent);
        var now = DateTimeOffset.UtcNow;

        _dbContext.Opportunities.Add(new TradingOpportunityEntity
        {
            Id = Guid.NewGuid(),
            AlertKey = opportunity.AlertKey,
            Symbol = opportunity.Symbol,
            Side = opportunity.Side,
            Status = OpportunityStatus.Open,
            OperationKind = opportunity.OperationKind,
            OriginKind = opportunity.OriginKind,
            Score = opportunity.Score,
            ObservedAt = opportunity.ObservedAt,
            ExpiresAt = opportunity.ExpiresAt,
            LastPrice = opportunity.LastPrice,
            EntryLower = opportunity.EntryLower,
            EntryUpper = opportunity.EntryUpper,
            EntryPrice = projection.EntryPrice,
            StopLoss = opportunity.StopLoss,
            TakeProfit1 = opportunity.TakeProfit1,
            TakeProfit2 = opportunity.TakeProfit2,
            RiskReward = opportunity.RiskReward,
            Capital = projection.Capital,
            EstimatedQuantity = projection.EstimatedQuantity,
            EstimatedFees = projection.EstimatedFees,
            NetProfitAtTakeProfit1 = projection.NetProfitAtTakeProfit1,
            NetProfitAtTakeProfit2 = projection.NetProfitAtTakeProfit2,
            NetLossAtStop = projection.NetLossAtStop,
            ManagedTargetNetPercent = managedTarget.TargetNetPercent,
            ManagedTargetNetPnL = managedTarget.TargetNetPnL,
            ManagedTargetExitPrice = managedTarget.TargetExitPrice,
            ConfirmingIntervalsJson = JsonSerializer.Serialize(opportunity.ConfirmingIntervals, JsonOptions),
            ReasonsJson = JsonSerializer.Serialize(opportunity.Reasons, JsonOptions),
            RisksJson = JsonSerializer.Serialize(opportunity.Risks, JsonOptions),
            RelatedNewsJson = JsonSerializer.Serialize(opportunity.RelatedNews, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OpportunityReportRow>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        var entities = (await _dbContext.Opportunities.AsNoTracking().ToArrayAsync(cancellationToken)).OrderByDescending(entity => entity.ObservedAt).Take(Math.Clamp(limit, 1, 500)).ToArray();

        return ApplyQualityFilter(entities.Select(entity => ToReportRow(entity, _reportingOptions.CurrentValue.DefaultCapital)).ToArray());
    }

    public async Task<IReadOnlyList<OpportunityReportRow>> GetSignalsAsync(decimal capital, CancellationToken cancellationToken)
    {
        capital = capital <= 0m ? _reportingOptions.CurrentValue.DefaultCapital : capital;
        var entities = (await _dbContext.Opportunities.AsNoTracking().ToArrayAsync(cancellationToken)).OrderByDescending(entity => entity.ObservedAt).ToArray();

        return ApplyQualityFilter(entities.Select(entity => ToReportRow(entity, capital)).ToArray());
    }

    public async Task<IReadOnlyList<OpportunityReportRow>> GetOpenAsync(CancellationToken cancellationToken)
    {
        var entities = (await _dbContext.Opportunities.AsNoTracking().Where(entity => entity.Status == OpportunityStatus.Open).ToArrayAsync(cancellationToken)).OrderBy(entity => entity.ObservedAt).ToArray();

        return entities.Select(entity => ToReportRow(entity, _reportingOptions.CurrentValue.DefaultCapital)).ToArray();
    }

    public async Task<OpportunityReportRow?> GetByAlertKeyAsync(string alertKey, decimal capital, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(alertKey))
            return null;

        capital = capital <= 0m ? _reportingOptions.CurrentValue.DefaultCapital : capital;
        var entity = await _dbContext.Opportunities.AsNoTracking().FirstOrDefaultAsync(item => item.AlertKey == alertKey, cancellationToken);

        return entity is null ? null : ToReportRow(entity, capital);
    }

    public async Task<OpportunityReportRow?> GetByIdAsync(Guid id, decimal capital, CancellationToken cancellationToken)
    {
        capital = capital <= 0m ? _reportingOptions.CurrentValue.DefaultCapital : capital;
        var entity = await _dbContext.Opportunities.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        return entity is null ? null : ToReportRow(entity, capital);
    }

    public async Task<decimal> GetRealizedNetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        return await _dbContext.Opportunities.AsNoTracking()
            .Where(entity => entity.ExitTime >= since && entity.RealizedNetPnL.HasValue)
            .SumAsync(entity => entity.RealizedNetPnL!.Value, cancellationToken);
    }

    public async Task UpdateManagedTargetAsync(Guid id, decimal targetNetPercent, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Opportunities.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (entity is null)
            return;

        var target = BuildManagedTarget(entity.Side, entity.Capital, entity.EstimatedQuantity, entity.EntryPrice, targetNetPercent);
        entity.ManagedTargetNetPercent = target.TargetNetPercent;
        entity.ManagedTargetNetPnL = target.TargetNetPnL;
        entity.ManagedTargetExitPrice = target.TargetExitPrice;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateExitAsync(Guid id, OpportunityExit exit, decimal realizedGrossPnL, decimal realizedNetPnL, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Opportunities.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (entity is null || entity.Status != OpportunityStatus.Open)
            return;

        entity.Status = exit.Status;
        entity.ExitTime = exit.ExitTime;
        entity.ExitPrice = exit.ExitPrice;
        entity.ExitReason = exit.Reason;
        entity.RealizedGrossPnL = Math.Round(realizedGrossPnL, 2);
        entity.RealizedNetPnL = Math.Round(realizedNetPnL, 2);
        entity.RealizedNetPercent = entity.Capital <= 0m ? 0m : Math.Round(realizedNetPnL / entity.Capital * 100m, 4);
        entity.RealizedTotalObtained = Math.Round(entity.Capital + realizedNetPnL, 2);
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DashboardReport> GetDashboardReportAsync(decimal capital, CancellationToken cancellationToken)
    {
        var entities = (await _dbContext.Opportunities.AsNoTracking().ToArrayAsync(cancellationToken)).OrderByDescending(entity => entity.ObservedAt).ToArray();

        capital = capital <= 0m ? _reportingOptions.CurrentValue.DefaultCapital : capital;
        var rows = ApplyQualityFilter(entities.Select(entity => ToReportRow(entity, capital)).ToArray());
        var closed = rows.Where(row => row.Status != OpportunityStatus.Open).ToArray();
        var winners = closed.Count(row => row.RealizedNetPnL > 0m);
        var losers = closed.Count(row => row.RealizedNetPnL < 0m);

        var symbolBreakdown = rows.GroupBy(row => row.Symbol)
                                  .Select(group => new SymbolReportRow(group.Key, group.Count(), group.Count(row => row.Status == OpportunityStatus.Open), group.Count(row => row.RealizedNetPnL > 0m),
                                       group.Count(row => row.RealizedNetPnL < 0m), group.Sum(row => row.RealizedNetPnL ?? 0m), group.Sum(row => row.NetProfitAtTakeProfit1),
                                       group.Sum(row => row.NetLossAtStop)))
                                  .OrderByDescending(row => row.TotalSignals)
                                  .ToArray();

        var dailyBreakdown = rows.GroupBy(row => DateOnly.FromDateTime(row.ObservedAt.LocalDateTime.Date))
                                 .Select(group => new DailyReportRow(group.Key, group.Count(), group.Count(row => row.Status != OpportunityStatus.Open), group.Sum(row => row.RealizedNetPnL ?? 0m),
                                      group.Sum(row => row.NetProfitAtTakeProfit1), group.Sum(row => row.NetLossAtStop)))
                                 .OrderByDescending(row => row.Day)
                                 .Take(30)
                                 .ToArray();

        var sourceHealth = (await _dbContext.DataSources.AsNoTracking().ToArrayAsync(cancellationToken))
            .OrderBy(row => row.Kind)
            .ThenBy(row => row.Name)
            .Select(row => new SourceHealthReportRow(
                row.Name,
                row.Kind,
                row.Status,
                row.LastSuccessAt,
                row.LastFailureAt,
                row.FailureCount,
                row.LastMessage,
                row.Url))
            .ToArray();

        var recentResearch = (await _dbContext.ResearchItems.AsNoTracking().ToArrayAsync(cancellationToken))
            .OrderByDescending(row => row.PublishedAt)
            .Take(30)
            .Select(row => new ResearchItemReportRow(
                row.Source,
                row.Kind,
                row.Title,
                row.Url,
                row.PublishedAt,
                row.Sentiment,
                string.Join(", ", ReadStringArray(row.SymbolsJson))))
            .ToArray();

        return new DashboardReport(capital, rows.Length, rows.Count(row => row.Status == OpportunityStatus.Open), closed.Length, winners, losers,
            closed.Length == 0 ? 0m : Math.Round((decimal)winners / closed.Length * 100m, 2), rows.Sum(row => row.RealizedNetPnL ?? 0m), rows.Sum(row => row.NetProfitAtTakeProfit1),
            rows.Sum(row => row.NetProfitAtTakeProfit2), rows.Sum(row => row.NetLossAtStop), rows.Length == 0 ? 0m : Math.Round(rows.Average(row => (decimal)row.Score), 2), rows.Take(100).ToArray(),
            symbolBreakdown, dailyBreakdown, sourceHealth, recentResearch);
    }

    private OpportunityReportRow ToReportRow(TradingOpportunityEntity entity, decimal capital)
    {
        var opportunity = ToOpportunity(entity);
        var projection = _projectionService.Project(opportunity, capital, _reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
        var realizedNet = entity.ExitPrice.HasValue ? CalculateNetPnL(entity, projection) : entity.RealizedNetPnL;
        var managedTarget = BuildManagedTarget(entity.Side, projection.Capital, projection.EstimatedQuantity, projection.EntryPrice, entity.ManagedTargetNetPercent);
        var realizedNetPercent = realizedNet.HasValue && projection.Capital > 0m
            ? Math.Round(realizedNet.Value / projection.Capital * 100m, 4)
            : entity.RealizedNetPercent;
        var realizedTotalObtained = realizedNet.HasValue
            ? Math.Round(projection.Capital + realizedNet.Value, 2)
            : entity.RealizedTotalObtained;

        return new OpportunityReportRow(entity.Id, entity.Symbol, entity.Side, entity.Status, entity.Score, entity.ObservedAt, entity.ExpiresAt, entity.ExitTime, entity.LastPrice, entity.EntryLower,
            entity.EntryUpper, projection.EntryPrice, entity.StopLoss, entity.TakeProfit1, entity.TakeProfit2, entity.ExitPrice, projection.Capital, projection.EstimatedQuantity, projection.EstimatedFees,
            projection.NetProfitAtTakeProfit1, projection.NetProfitAtTakeProfit2, projection.NetLossAtStop, managedTarget.TargetNetPercent, managedTarget.TargetNetPnL, managedTarget.TargetExitPrice, realizedNet,
            realizedNetPercent, realizedTotalObtained, entity.RiskReward, string.Join(" | ", ReadStringArray(entity.ConfirmingIntervalsJson)),
            string.Join(" | ", ReadStringArray(entity.ReasonsJson)), string.Join(" | ", ReadStringArray(entity.RisksJson)), entity.OperationKind, entity.OriginKind);
    }

    private OpportunityReportRow[] ApplyQualityFilter(IReadOnlyList<OpportunityReportRow> rows)
    {
        var minimumPercent = Math.Max(0m, _reportingOptions.CurrentValue.MinimumNetProfitPercentAfterCosts);
        if (minimumPercent <= 0m)
            return rows.ToArray();

        return rows
            .Where(row => row.Capital > 0m && row.NetProfitAtTakeProfit1 > 0m && row.NetProfitAtTakeProfit1 / row.Capital * 100m >= minimumPercent)
            .ToArray();
    }

    private decimal? CalculateNetPnL(TradingOpportunityEntity entity, OpportunityProjection projection)
    {
        if (!entity.ExitPrice.HasValue)
            return null;

        var breakdown = TradeCostCalculator.Build(
            entity.Side,
            projection.Capital,
            projection.EstimatedQuantity,
            projection.EntryPrice,
            entity.ExitPrice.Value,
            _reportingOptions.CurrentValue.EstimatedFeePercentPerSide);

        return breakdown.NetBenefit;
    }

    private ManagedTargetSnapshot BuildManagedTarget(MarketSide side, decimal capital, decimal quantity, decimal entryPrice, decimal targetNetPercent)
    {
        var resolvedTargetNetPercent = Math.Max(0.01m, targetNetPercent);
        var targetExitPrice = TradeCostCalculator.ResolveExitPriceForNetPercent(
            side,
            capital,
            quantity,
            entryPrice,
            resolvedTargetNetPercent,
            _reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
        var targetBreakdown = TradeCostCalculator.Build(
            side,
            capital,
            quantity,
            entryPrice,
            targetExitPrice,
            _reportingOptions.CurrentValue.EstimatedFeePercentPerSide);

        return new ManagedTargetSnapshot(
            resolvedTargetNetPercent,
            targetBreakdown.NetBenefit,
            targetExitPrice);
    }

    private async Task<decimal> ResolveDefaultManagedTargetPercentAsync(string symbol, CancellationToken cancellationToken)
    {
        var market = MarketSymbolClassifier.GetMarketKind(symbol) == MarketKind.Forex
            ? MarketSymbolClassifier.ForexMarket
            : MarketSymbolClassifier.CryptoMarket;
        var walletTarget = await _dbContext.WalletSettings.AsNoTracking()
            .Where(setting => setting.Market == market)
            .Select(setting => (decimal?)setting.ManagedTargetNetPercent)
            .FirstOrDefaultAsync(cancellationToken);
        var fallback = _riskOptions.CurrentValue.ManagedProfitExitPercentAfterCosts;

        return Math.Max(0.01m, walletTarget.GetValueOrDefault(fallback));
    }

    private static bool IsSameSignalFamily(DateTimeOffset observedAt, DateTimeOffset expiresAt, TradingOpportunity opportunity)
    {
        var existingMinutes = Math.Max(1m, (decimal)(expiresAt - observedAt).TotalMinutes);
        var incomingMinutes = Math.Max(1m, (decimal)(opportunity.ExpiresAt - opportunity.ObservedAt).TotalMinutes);
        var ratio = Math.Max(existingMinutes, incomingMinutes) / Math.Min(existingMinutes, incomingMinutes);

        return ratio < 2m;
    }

    private static TradingOpportunity ToOpportunity(TradingOpportunityEntity entity)
    {
        return new TradingOpportunity(entity.Symbol, entity.Side, entity.Score, entity.ObservedAt, entity.ExpiresAt, entity.LastPrice, entity.EntryLower, entity.EntryUpper, entity.StopLoss, entity.TakeProfit1,
            entity.TakeProfit2, entity.RiskReward, ReadStringArray(entity.ConfirmingIntervalsJson), ReadStringArray(entity.ReasonsJson), ReadStringArray(entity.RisksJson),
            ReadNewsArray(entity.RelatedNewsJson), entity.OperationKind, entity.OriginKind);
    }

    private static IReadOnlyList<string> ReadStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<NewsItem> ReadNewsArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<NewsItem[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

internal sealed record ManagedTargetSnapshot(decimal TargetNetPercent, decimal TargetNetPnL, decimal TargetExitPrice);
