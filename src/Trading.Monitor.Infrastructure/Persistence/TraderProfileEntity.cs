namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class TraderProfileEntity
{
    public Guid Id { get; set; }

    public string Platform { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string ExternalId { get; set; } = "";

    public string ProfileUrl { get; set; } = "";

    public string Market { get; set; } = "";

    public string StrategyType { get; set; } = "";

    public string PopularityText { get; set; } = "";

    public string PerformanceText { get; set; } = "";

    public string DataAvailability { get; set; } = "";

    public string Notes { get; set; } = "";

    public DateTimeOffset? LastSyncedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
