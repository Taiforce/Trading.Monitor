using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Reporting;

/// <summary>
/// Comparative report across the three signal sources the system generates:
/// "Propias" (own self-learning engine), "Ajenas" (external ensemble of public strategies),
/// and "Traders" (real top-trader positions followed from a public leaderboard).
/// </summary>
public sealed record AiSourcesReport(AiSourceStats Own, AiSourceStats External, AiSourceStats Trader, string Summary)
{
    public IReadOnlyList<AiSourceStats> All => [Own, External, Trader];
}

public sealed record AiSourceStats(
    SignalOriginKind Origin,
    string Label,
    string Description,
    int TotalSignals,
    int OpenSignals,
    int ClosedSignals,
    int Winners,
    int Losers,
    decimal WinRatePercent,
    decimal NetPnL,
    decimal AverageScore,
    decimal AverageRiskReward,
    IReadOnlyList<AiSourceSymbolBreakdown> BySymbol,
    IReadOnlyList<OpportunityReportRow> RecentSignals,
    IReadOnlyList<string> LearningNotes);

public sealed record AiSourceSymbolBreakdown(string Symbol, int TotalSignals, int ClosedSignals, decimal WinRatePercent, decimal NetPnL);
