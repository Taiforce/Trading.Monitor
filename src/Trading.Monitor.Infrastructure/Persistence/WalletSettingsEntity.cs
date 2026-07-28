namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class WalletSettingsEntity
{
    public Guid Id { get; set; }

    public decimal CashCapital { get; set; }

    public bool AutoTradingEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
