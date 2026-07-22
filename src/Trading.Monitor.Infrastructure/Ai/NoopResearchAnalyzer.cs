using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Ai;

public sealed class NoopResearchAnalyzer : IResearchAnalyzer
{
    public string Name => "No AI research analyzer";

    public Task<IReadOnlyList<NewsItem>> AnalyzeAsync(IReadOnlyCollection<string> symbols, IReadOnlyList<NewsItem> researchItems, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<NewsItem>>([]);
    }
}
