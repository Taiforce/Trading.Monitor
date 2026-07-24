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
    string Safety,
    string Message);
