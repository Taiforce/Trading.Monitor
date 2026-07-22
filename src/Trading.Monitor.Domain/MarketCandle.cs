namespace Trading.Monitor.Domain;

public sealed record MarketCandle(string Symbol, string Interval, DateTimeOffset OpenTime, DateTimeOffset CloseTime, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, decimal QuoteVolume,
    decimal? TakerBuyBaseVolume = null)
{
    public decimal TypicalPrice => (High + Low + Close) / 3m;

    public bool IsBullish => Close >= Open;
}