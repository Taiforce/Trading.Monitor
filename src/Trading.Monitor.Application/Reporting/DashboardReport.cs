namespace Trading.Monitor.Application.Reporting;

public sealed record DashboardReport(
    decimal Capital,
    int TotalSignals,
    int OpenSignals,
    int ClosedSignals,
    int Winners,
    int Losers,
    decimal WinRate,
    decimal RealizedNetPnL,
    decimal PotentialNetAtTakeProfit1,
    decimal PotentialNetAtTakeProfit2,
    decimal PotentialLossAtStop,
    decimal AverageScore,
    IReadOnlyList<OpportunityReportRow> RecentSignals,
    IReadOnlyList<SymbolReportRow> SymbolBreakdown,
    IReadOnlyList<DailyReportRow> DailyBreakdown,
    IReadOnlyList<SourceHealthReportRow> SourceHealth,
    IReadOnlyList<ResearchItemReportRow> RecentResearch);
