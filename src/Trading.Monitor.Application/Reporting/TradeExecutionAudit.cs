using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Reporting;

public sealed record TradeExecutionAudit(
    Guid Id,
    Guid OpportunityId,
    string Symbol,
    MarketSide Side,
    TradeExecutionAction Action,
    TradeExecutionMode Mode,
    TradeExecutionStatus Status,
    decimal RequestedCapital,
    decimal? RequestedQuantity,
    decimal? ExecutedQuantity,
    decimal? ExecutedQuote,
    decimal? Price,
    string ClientOrderId,
    string ExchangeOrderId,
    string Reason,
    string Message,
    string RequestJson,
    string ResponseJson,
    DateTimeOffset CreatedAt);
