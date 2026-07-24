namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class TraderSourceEntity
{
    public Guid Id { get; set; }

    public string Platform { get; set; } = "";

    public string Name { get; set; } = "";

    public string Market { get; set; } = "";

    public string Url { get; set; } = "";

    public string DataAccess { get; set; } = "";

    public string DataQuality { get; set; } = "";

    public string Notes { get; set; } = "";

    public bool SupportsCopyTrading { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
