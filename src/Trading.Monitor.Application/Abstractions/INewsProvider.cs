using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface INewsProvider
{
    string Name { get; }

    Task<IReadOnlyList<NewsItem>> GetLatestAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken);
}
