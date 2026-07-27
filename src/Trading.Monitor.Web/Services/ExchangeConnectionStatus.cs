namespace Trading.Monitor.Web.Services;

public sealed record ExchangeConnectionStatus(
    string Provider,
    string Mode,
    bool PublicApiHealthy,
    bool ApiKeyConfigured,
    bool ApiSecretConfigured,
    bool LiveTradingAllowed,
    decimal MaxCapitalPerTrade,
    decimal DailyLossLimit,
    int MinimumScoreToExecute,
    decimal MinimumExpectedNetProfitPercentAfterCosts,
    decimal MaxSlippagePercent,
    bool AllowShortSelling,
    IReadOnlyList<string> AllowedSymbols,
    string Safety,
    string Message);
