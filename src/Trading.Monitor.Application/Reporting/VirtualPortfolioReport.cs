namespace Trading.Monitor.Application.Reporting;

public sealed record VirtualPortfolioReport(
    decimal InitialCapital,
    decimal FinalBalance,
    decimal NetPnL,
    decimal ReturnPercent,
    decimal PeakBalance,
    decimal MaxDrawdown,
    int TotalSignals,
    int AppliedTrades,
    int SkippedTrades,
    int Winners,
    int Losers,
    IReadOnlyList<VirtualPortfolioTradeRow> Trades,
    IReadOnlyList<VirtualPortfolioEquityPoint> EquityPoints)
{
    public static VirtualPortfolioReport Empty(decimal initialCapital)
    {
        return new VirtualPortfolioReport(initialCapital, initialCapital, 0m, 0m, initialCapital, 0m, 0, 0, 0, 0, 0, [], [new VirtualPortfolioEquityPoint(0, DateTimeOffset.UtcNow, initialCapital)]);
    }
}

public sealed record VirtualPortfolioTradeRow(
    int Sequence,
    Guid OpportunityId,
    string Symbol,
    string Horizon,
    string OperationType,
    DateTimeOffset SignalTime,
    DateTimeOffset EntryTime,
    DateTimeOffset? ExitTime,
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

public sealed record VirtualPortfolioEquityPoint(int Sequence, DateTimeOffset Time, decimal Balance);
