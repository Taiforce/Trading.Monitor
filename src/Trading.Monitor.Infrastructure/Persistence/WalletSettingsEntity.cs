namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class WalletSettingsEntity
{
    public Guid Id { get; set; }

    public string Market { get; set; } = "";

    public decimal CashCapital { get; set; }

    public bool AutoTradingEnabled { get; set; }

    public decimal ManagedTargetNetPercent { get; set; } = 5m;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
