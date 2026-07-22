using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed record MarketScanResult(IReadOnlyList<TradingOpportunity> Opportunities, IReadOnlyList<string> Errors);