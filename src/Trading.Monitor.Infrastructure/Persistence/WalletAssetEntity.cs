namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class WalletAssetEntity
{
    public Guid Id { get; set; }

    public string Market { get; set; } = "";

    public string Symbol { get; set; } = "";

    public string Asset { get; set; } = "";

    public decimal CoinQuantity { get; set; }

    public bool AllowSellHighBuyLow { get; set; }

    public bool AutoTradingEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
