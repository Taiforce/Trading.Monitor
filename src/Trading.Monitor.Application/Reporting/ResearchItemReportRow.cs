using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Reporting;

public sealed record ResearchItemReportRow(
    string Source,
    DataSourceKind Kind,
    string Title,
    string Url,
    DateTimeOffset PublishedAt,
    NewsSentiment Sentiment,
    string Symbols);
