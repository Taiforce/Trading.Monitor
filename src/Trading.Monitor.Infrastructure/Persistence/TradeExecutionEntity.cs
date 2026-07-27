using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class TradeExecutionEntity
{
    public Guid Id { get; set; }

    public Guid OpportunityId { get; set; }

    public string Symbol { get; set; } = "";

    public MarketSide Side { get; set; }

    public TradeExecutionAction Action { get; set; }

    public TradeExecutionMode Mode { get; set; }

    public TradeExecutionStatus Status { get; set; }

    public decimal RequestedCapital { get; set; }

    public decimal? RequestedQuantity { get; set; }

    public decimal? ExecutedQuantity { get; set; }

    public decimal? ExecutedQuote { get; set; }

    public decimal? Price { get; set; }

    public string ClientOrderId { get; set; } = "";

    public string ExchangeOrderId { get; set; } = "";

    public string Reason { get; set; } = "";

    public string Message { get; set; } = "";

    public string RequestJson { get; set; } = "{}";

    public string ResponseJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public TradingOpportunityEntity? Opportunity { get; set; }
}
