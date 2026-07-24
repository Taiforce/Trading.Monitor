namespace Trading.Monitor.Application.Services;

public sealed record TradeInstruction(
    string ActionLabel,
    string ConvictionLabel,
    string CssClass,
    bool Highlight,
    string EntryTiming,
    string ExitTiming,
    string ProfitReport,
    string RiskReport,
    string ManagementPlan,
    string BeginnerReadout);
