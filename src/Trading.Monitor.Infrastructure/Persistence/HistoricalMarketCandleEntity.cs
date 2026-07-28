namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class HistoricalMarketCandleEntity
{
    public Guid Id { get; set; }

    public string Market { get; set; } = "";

    public string Source { get; set; } = "";

    public string Symbol { get; set; } = "";

    public string Interval { get; set; } = "";

    public DateTimeOffset OpenTime { get; set; }

    public DateTimeOffset CloseTime { get; set; }

    public decimal Open { get; set; }

    public decimal High { get; set; }

    public decimal Low { get; set; }

    public decimal Close { get; set; }

    public decimal Volume { get; set; }

    public decimal QuoteVolume { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
