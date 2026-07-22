using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.News;

public sealed class NoopNewsProvider : INewsProvider
{
    public string Name => "No news provider";

    public Task<IReadOnlyList<NewsItem>> GetLatestAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<NewsItem>>([]);
    }
}
