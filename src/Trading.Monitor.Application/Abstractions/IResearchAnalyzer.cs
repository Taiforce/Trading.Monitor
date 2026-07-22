using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Abstractions;

public interface IResearchAnalyzer
{
    string Name { get; }

    Task<IReadOnlyList<NewsItem>> AnalyzeAsync(
        IReadOnlyCollection<string> symbols,
        IReadOnlyList<NewsItem> researchItems,
        CancellationToken cancellationToken);
}
