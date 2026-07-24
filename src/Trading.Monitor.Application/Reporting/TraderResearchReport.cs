using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Reporting;

public sealed record TraderResearchFilter(string? Platform, string? Search, string? TradeStatus, Guid? TraderId, bool OnlyWithHistory);

public sealed record TraderResearchReport(
    IReadOnlyList<TraderSourceReportRow> Sources,
    IReadOnlyList<TraderProfileReportRow> Traders,
    IReadOnlyList<TraderTradeReportRow> Trades,
    TraderProfileReportRow? SelectedTrader,
    int TotalSources,
    int TotalTraders,
    int TradersWithHistory,
    int OpenTrades,
    int ClosedTrades);

public sealed record TraderSourceReportRow(
    string Platform,
    string Name,
    string Market,
    string Url,
    string DataAccess,
    string DataQuality,
    string Notes,
    bool SupportsCopyTrading);

public sealed record TraderProfileReportRow(
    Guid Id,
    string Platform,
    string DisplayName,
    string ExternalId,
    string ProfileUrl,
    string Market,
    string StrategyType,
    string PopularityText,
    string PerformanceText,
    string DataAvailability,
    string Notes,
    int TrackedTrades,
    int OpenTrades,
    int ClosedTrades,
    decimal ReliabilityScore,
    decimal? WinRatePercent,
    decimal RealizedNetPnL,
    DateTimeOffset? LastSyncedAt);

public sealed record TraderTradeReportRow(
    Guid Id,
    Guid TraderId,
    string Platform,
    string TraderName,
    string Symbol,
    MarketSide Side,
    string SignalType,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal? Quantity,
    decimal? PnLPercent,
    decimal? NetPnL,
    decimal? Leverage,
    string SourceUrl,
    string Notes);

public sealed record TraderFollowSimulationReport(
    decimal InitialCapital,
    decimal FinalBalance,
    decimal NetPnL,
    decimal ReturnPercent,
    decimal PeakBalance,
    decimal MaxDrawdown,
    int TotalTrades,
    int AppliedTrades,
    int SkippedTrades,
    int Winners,
    int Losers,
    IReadOnlyList<TraderFollowTradeRow> Trades,
    IReadOnlyList<TraderFollowEquityPoint> EquityPoints)
{
    public static TraderFollowSimulationReport Empty(decimal initialCapital)
    {
        return new TraderFollowSimulationReport(initialCapital, initialCapital, 0m, 0m, initialCapital, 0m, 0, 0, 0, 0, 0, [], [new TraderFollowEquityPoint(0, DateTimeOffset.UtcNow, initialCapital)]);
    }
}

public sealed record TraderFollowTradeRow(
    int Sequence,
    string TraderName,
    string Platform,
    string Symbol,
    string SignalType,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal StartingBalance,
    decimal Quantity,
    decimal Fees,
    decimal NetPnL,
    decimal EndingBalance,
    decimal ReturnPercent,
    bool WasApplied,
    string SkipReason);

public sealed record TraderFollowEquityPoint(int Sequence, DateTimeOffset Time, decimal Balance);
