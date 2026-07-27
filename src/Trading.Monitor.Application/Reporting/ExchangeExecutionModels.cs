using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Reporting;

public sealed record ExchangeOrderResult(
    TradeExecutionStatus Status,
    string ExchangeOrderId,
    decimal? ExecutedQuantity,
    decimal? ExecutedQuote,
    decimal? Price,
    string RawResponse,
    string Message);

public sealed record ExchangeBalance(string Asset, decimal Free, decimal Locked);

public sealed record SymbolTradeRules(
    string Symbol,
    decimal StepSize,
    decimal MinQuantity,
    decimal MinNotional,
    decimal TickSize);
