using Microsoft.EntityFrameworkCore;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class EfTraderResearchRepository : ITraderResearchRepository
{
    private readonly TradingMonitorDbContext _dbContext;

    public EfTraderResearchRepository(TradingMonitorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TraderResearchReport> GetReportAsync(TraderResearchFilter filter, CancellationToken cancellationToken)
    {
        var sources = await _dbContext.TraderSources.AsNoTracking()
            .OrderBy(row => row.Platform)
            .Select(row => new TraderSourceReportRow(row.Platform, row.Name, row.Market, row.Url, row.DataAccess, row.DataQuality, row.Notes, row.SupportsCopyTrading))
            .ToArrayAsync(cancellationToken);

        var trades = await GetTradeRowsAsync(cancellationToken);
        var traders = await GetTraderRowsAsync(trades, cancellationToken);
        traders = ApplyTraderFilters(traders, filter).ToArray();
        var selectedTrader = filter.TraderId.HasValue ? traders.FirstOrDefault(row => row.Id == filter.TraderId.Value) : null;
        var filteredTrades = ApplyTradeFilters(trades, filter).ToArray();

        return new TraderResearchReport(
            sources,
            traders,
            filteredTrades,
            selectedTrader,
            sources.Length,
            traders.Length,
            traders.Count(row => row.TrackedTrades > 0),
            filteredTrades.Count(row => string.Equals(row.Status, "Abierta", StringComparison.OrdinalIgnoreCase)),
            filteredTrades.Count(row => string.Equals(row.Status, "Cerrada", StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<IReadOnlyList<TraderProfileReportRow>> GetTradersAsync(CancellationToken cancellationToken)
    {
        var trades = await GetTradeRowsAsync(cancellationToken);
        return await GetTraderRowsAsync(trades, cancellationToken);
    }

    public async Task<IReadOnlyList<TraderTradeReportRow>> GetTradesAsync(Guid traderId, DateOnly? desde, DateOnly? hasta, CancellationToken cancellationToken)
    {
        var trades = await GetTradeRowsAsync(cancellationToken);

        if (desde.HasValue)
            trades = trades.Where(row => DateOnly.FromDateTime(row.OpenedAt.LocalDateTime) >= desde.Value).ToArray();

        if (hasta.HasValue)
            trades = trades.Where(row => DateOnly.FromDateTime(row.OpenedAt.LocalDateTime) <= hasta.Value).ToArray();

        return trades.Where(row => row.TraderId == traderId).OrderBy(row => row.OpenedAt).ToArray();
    }

    private async Task<TraderProfileReportRow[]> GetTraderRowsAsync(IReadOnlyList<TraderTradeReportRow> trades, CancellationToken cancellationToken)
    {
        var profiles = await _dbContext.TraderProfiles.AsNoTracking()
            .OrderBy(row => row.Platform)
            .ThenBy(row => row.DisplayName)
            .ToArrayAsync(cancellationToken);

        return profiles.Select(profile =>
            {
                var traderTrades = trades.Where(trade => trade.TraderId == profile.Id).ToArray();
                var closed = traderTrades.Where(trade => string.Equals(trade.Status, "Cerrada", StringComparison.OrdinalIgnoreCase)).ToArray();
                var winners = closed.Count(trade => trade.NetPnL > 0m || trade.PnLPercent > 0m);
                var realized = closed.Sum(trade => trade.NetPnL ?? 0m);

                return new TraderProfileReportRow(
                    profile.Id,
                    profile.Platform,
                    profile.DisplayName,
                    profile.ExternalId,
                    profile.ProfileUrl,
                    profile.Market,
                    profile.StrategyType,
                    profile.PopularityText,
                    profile.PerformanceText,
                    profile.DataAvailability,
                    profile.Notes,
                    traderTrades.Length,
                    traderTrades.Count(trade => string.Equals(trade.Status, "Abierta", StringComparison.OrdinalIgnoreCase)),
                    closed.Length,
                    ReliabilityScore(profile, traderTrades, closed),
                    closed.Length == 0 ? null : Math.Round((decimal)winners / closed.Length * 100m, 2),
                    Math.Round(realized, 2),
                    profile.LastSyncedAt);
            })
            .OrderByDescending(row => row.TrackedTrades > 0)
            .ThenByDescending(row => row.ReliabilityScore)
            .ThenBy(row => row.Platform)
            .ThenBy(row => row.DisplayName)
            .ToArray();
    }

    private async Task<TraderTradeReportRow[]> GetTradeRowsAsync(CancellationToken cancellationToken)
    {
        var entities = await _dbContext.TraderTrades.AsNoTracking()
            .Include(row => row.TraderProfile)
            .OrderByDescending(row => row.OpenedAt)
            .ToArrayAsync(cancellationToken);

        return entities.Select(entity => new TraderTradeReportRow(
                entity.Id,
                entity.TraderProfileId,
                entity.TraderProfile?.Platform ?? "",
                entity.TraderProfile?.DisplayName ?? "",
                entity.Symbol,
                entity.Side,
                SignalTypeDescriptor.Label(entity.Side),
                NormalizeStatus(entity.Status),
                entity.OpenedAt,
                entity.ClosedAt,
                entity.EntryPrice,
                entity.ExitPrice,
                entity.Quantity,
                entity.PnLPercent,
                entity.NetPnL,
                entity.Leverage,
                entity.SourceUrl,
                entity.Notes))
            .ToArray();
    }

    private static IEnumerable<TraderProfileReportRow> ApplyTraderFilters(IEnumerable<TraderProfileReportRow> rows, TraderResearchFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Platform))
            rows = rows.Where(row => string.Equals(row.Platform, filter.Platform.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            rows = rows.Where(row =>
                row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Platform.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Market.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.StrategyType.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.OnlyWithHistory)
            rows = rows.Where(row => row.TrackedTrades > 0);

        return rows;
    }

    private static IEnumerable<TraderTradeReportRow> ApplyTradeFilters(IEnumerable<TraderTradeReportRow> rows, TraderResearchFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.TradeStatus) && !string.Equals(filter.TradeStatus, "todas", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(row => string.Equals(row.Status, NormalizeStatus(filter.TradeStatus), StringComparison.OrdinalIgnoreCase));

        return rows;
    }

    private static decimal ReliabilityScore(TraderProfileEntity profile, IReadOnlyList<TraderTradeReportRow> allTrades, IReadOnlyList<TraderTradeReportRow> closedTrades)
    {
        var score = 30m;

        if (!string.IsNullOrWhiteSpace(profile.ProfileUrl))
            score += 10m;

        if (allTrades.Count > 0)
            score += 20m;

        if (closedTrades.Count >= 10)
            score += 20m;
        else if (closedTrades.Count >= 3)
            score += 10m;

        if (closedTrades.Count > 0)
        {
            var winners = closedTrades.Count(trade => trade.NetPnL > 0m || trade.PnLPercent > 0m);
            var winRate = (decimal)winners / closedTrades.Count * 100m;
            score += winRate >= 55m ? 15m : winRate >= 45m ? 8m : 0m;
        }

        if (profile.Platform is "Darwinex" or "eToro" or "ZuluTrade")
            score += 5m;

        return Math.Clamp(score, 0m, 100m);
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "open" or "abierta" or "abierto" => "Abierta",
            "closed" or "cerrada" or "cerrado" => "Cerrada",
            _ => "Pendiente"
        };
    }
}
