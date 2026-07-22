using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Reporting;

public sealed record SourceHealthReportRow(
    string SourceName,
    DataSourceKind Kind,
    DataSourceStatus Status,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int FailureCount,
    string LastMessage,
    string? Url);
