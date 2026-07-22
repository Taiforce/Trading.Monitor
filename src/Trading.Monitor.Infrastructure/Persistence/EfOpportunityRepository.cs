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

    public EfOpportunityRepository(TradingMonitorDbContext dbContext, OpportunityProjectionService projectionService, IOptionsMonitor<ReportingOptions> reportingOptions)
    {
        _dbContext = dbContext;
        _projectionService = projectionService;
        _reportingOptions = reportingOptions;
    }

    public async Task<bool> HasRecentSimilarSignalAsync(TradingOpportunity opportunity, TimeSpan duplicateWindow, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(duplicateWindow);

        var entities = await _dbContext.Opportunities.AsNoTracking().Where(entity => entity.Symbol == opportunity.Symbol && entity.Side == opportunity.Side).ToArrayAsync(cancellationToken);

        return entities.Any(entity => entity.ObservedAt >= cutoff);
    }

    public async Task SaveAsync(TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Opportunities.AnyAsync(entity => entity.AlertKey == opportunity.AlertKey, cancellationToken);

        if (existing)
            return;

        var projection = _projectionService.Project(opportunity, _reportingOptions.CurrentValue);
        var now = DateTimeOffset.UtcNow;

        _dbContext.Opportunities.Add(new TradingOpportunityEntity
        {
            Id = Guid.NewGuid(),
            AlertKey = opportunity.AlertKey,
            Symbol = opportunity.Symbol,
            Side = opportunity.Side,
            Status = OpportunityStatus.Open,
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

        return entities.Select(entity => ToReportRow(entity, _reportingOptions.CurrentValue.DefaultCapital)).ToArray();
    }

    public async Task<IReadOnlyList<OpportunityReportRow>> GetOpenAsync(CancellationToken cancellationToken)
    {
        var entities = (await _dbContext.Opportunities.AsNoTracking().Where(entity => entity.Status == OpportunityStatus.Open).ToArrayAsync(cancellationToken)).OrderBy(entity => entity.ObservedAt).ToArray();

        return entities.Select(entity => ToReportRow(entity, _reportingOptions.CurrentValue.DefaultCapital)).ToArray();
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
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DashboardReport> GetDashboardReportAsync(decimal capital, CancellationToken cancellationToken)
    {
        var entities = (await _dbContext.Opportunities.AsNoTracking().ToArrayAsync(cancellationToken)).OrderByDescending(entity => entity.ObservedAt).ToArray();

        capital = capital <= 0m ? _reportingOptions.CurrentValue.DefaultCapital : capital;
        var rows = entities.Select(entity => ToReportRow(entity, capital)).ToArray();
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

        return new OpportunityReportRow(entity.Id, entity.Symbol, entity.Side, entity.Status, entity.Score, entity.ObservedAt, entity.ExpiresAt, entity.ExitTime, entity.LastPrice, entity.EntryLower,
            entity.EntryUpper, projection.EntryPrice, entity.StopLoss, entity.TakeProfit1, entity.TakeProfit2, entity.ExitPrice, projection.Capital, projection.EstimatedQuantity, projection.EstimatedFees,
            projection.NetProfitAtTakeProfit1, projection.NetProfitAtTakeProfit2, projection.NetLossAtStop, realizedNet, entity.RiskReward, string.Join(" | ", ReadStringArray(entity.ReasonsJson)),
            string.Join(" | ", ReadStringArray(entity.RisksJson)));
    }

    private decimal? CalculateNetPnL(TradingOpportunityEntity entity, OpportunityProjection projection)
    {
        if (!entity.ExitPrice.HasValue)
            return null;

        var gross = OpportunityProjectionService.CalculateGrossPnL(entity.Side, projection.EntryPrice, entity.ExitPrice.Value, projection.EstimatedQuantity);

        return Math.Round(gross - projection.EstimatedFees, 2);
    }

    private static TradingOpportunity ToOpportunity(TradingOpportunityEntity entity)
    {
        return new TradingOpportunity(entity.Symbol, entity.Side, entity.Score, entity.ObservedAt, entity.ExpiresAt, entity.LastPrice, entity.EntryLower, entity.EntryUpper, entity.StopLoss, entity.TakeProfit1,
            entity.TakeProfit2, entity.RiskReward, ReadStringArray(entity.ConfirmingIntervalsJson), ReadStringArray(entity.ReasonsJson), ReadStringArray(entity.RisksJson),
            ReadNewsArray(entity.RelatedNewsJson));
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
