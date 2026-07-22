namespace Trading.Monitor.Application.Reporting;

public sealed record DailyReportRow(DateOnly Day, int TotalSignals, int ClosedSignals, decimal RealizedNetPnL, decimal PotentialNetAtTakeProfit1, decimal PotentialLossAtStop);