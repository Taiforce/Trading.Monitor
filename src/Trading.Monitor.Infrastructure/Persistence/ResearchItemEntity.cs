using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class ResearchItemEntity
{
    public Guid Id { get; set; }

    public string Source { get; set; } = "";

    public DataSourceKind Kind { get; set; }

    public string Title { get; set; } = "";

    public string Url { get; set; } = "";

    public DateTimeOffset PublishedAt { get; set; }

    public NewsSentiment Sentiment { get; set; }

    public string SymbolsJson { get; set; } = "[]";

    public string RawJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}
