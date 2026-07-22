using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class IngestionEventEntity
{
    public Guid Id { get; set; }

    public string SourceName { get; set; } = "";

    public DataSourceKind Kind { get; set; }

    public DataSourceStatus Status { get; set; }

    public string? Url { get; set; }

    public string Message { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public int ItemsCount { get; set; }
}
