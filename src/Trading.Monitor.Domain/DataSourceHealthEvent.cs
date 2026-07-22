namespace Trading.Monitor.Domain;

public sealed record DataSourceHealthEvent(
    string SourceName,
    DataSourceKind Kind,
    DataSourceStatus Status,
    string? Url,
    string Message,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int ItemsCount);
