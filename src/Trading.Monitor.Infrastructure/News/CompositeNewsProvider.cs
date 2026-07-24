using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.News;

public sealed class CompositeNewsProvider(IReadOnlyList<INewsProvider> providers, ISourceTelemetryRecorder telemetryRecorder) : INewsProvider
{
    public string Name => "Composite research";

    public async Task<IReadOnlyList<NewsItem>> GetLatestAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        var items = new List<NewsItem>();

        foreach (var provider in providers)
        {
            var startedAt = DateTimeOffset.UtcNow;

            try
            {
                var providerItems = await provider.GetLatestAsync(symbols, cancellationToken);
                items.AddRange(providerItems);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                    provider.Name,
                    DataSourceKind.News,
                    DataSourceStatus.Failed,
                    null,
                    exception.Message,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    0), cancellationToken);
            }
        }

        return items.GroupBy(item => string.IsNullOrWhiteSpace(item.Url) ? $"{item.Source}:{item.Title}" : item.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderByDescending(item => item.PublishedAt)
                    .ToArray();
    }
}
