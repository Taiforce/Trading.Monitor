using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class TraderTradeEntity
{
    public Guid Id { get; set; }

    public Guid TraderProfileId { get; set; }

    public TraderProfileEntity? TraderProfile { get; set; }

    public string ExternalTradeId { get; set; } = "";

    public string Symbol { get; set; } = "";

    public MarketSide Side { get; set; }

    public string Status { get; set; } = "";

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public decimal EntryPrice { get; set; }

    public decimal? ExitPrice { get; set; }

    public decimal? Quantity { get; set; }

    public decimal? PnLPercent { get; set; }

    public decimal? NetPnL { get; set; }

    public decimal? Leverage { get; set; }

    public string SourceUrl { get; set; } = "";

    public string Notes { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
