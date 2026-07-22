namespace Trading.Monitor.Application.Reporting;

public sealed record SymbolReportRow(string Symbol, int TotalSignals, int OpenSignals, int Winners, int Losers, decimal RealizedNetPnL, decimal PotentialNetAtTakeProfit1, decimal PotentialLossAtStop);