using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface ISourceTelemetryRecorder
{
    Task RecordAsync(DataSourceHealthEvent healthEvent, CancellationToken cancellationToken);

    Task SaveResearchItemsAsync(IReadOnlyList<NewsItem> items, DataSourceKind kind, CancellationToken cancellationToken);
}
