using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class SourceTelemetryRecorder(
    IServiceScopeFactory scopeFactory,
    ILogger<SourceTelemetryRecorder> logger) : ISourceTelemetryRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(DataSourceHealthEvent healthEvent, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingMonitorDbContext>();
            var now = DateTimeOffset.UtcNow;

            var source = await dbContext.DataSources
                .FirstOrDefaultAsync(
                    item => item.Name == healthEvent.SourceName && item.Kind == healthEvent.Kind,
                    cancellationToken);

            if (source is null)
            {
                source = new DataSourceEntity
                {
                    Id = Guid.NewGuid(),
                    Name = healthEvent.SourceName,
                    Kind = healthEvent.Kind,
                    CreatedAt = now
                };
                dbContext.DataSources.Add(source);
            }

            source.Status = healthEvent.Status;
            source.Url = healthEvent.Url;
            source.LastMessage = healthEvent.Message;
            source.UpdatedAt = now;

            if (healthEvent.Status == DataSourceStatus.Healthy)
            {
                source.LastSuccessAt = healthEvent.CompletedAt;
                source.FailureCount = 0;
            }
            else
            {
                source.LastFailureAt = healthEvent.CompletedAt;
                source.FailureCount += 1;
            }

            dbContext.IngestionEvents.Add(new IngestionEventEntity
            {
                Id = Guid.NewGuid(),
                SourceName = healthEvent.SourceName,
                Kind = healthEvent.Kind,
                Status = healthEvent.Status,
                Url = healthEvent.Url,
                Message = healthEvent.Message,
                StartedAt = healthEvent.StartedAt,
                CompletedAt = healthEvent.CompletedAt,
                ItemsCount = healthEvent.ItemsCount
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not persist source telemetry for {SourceName}.", healthEvent.SourceName);
        }
    }

    public async Task SaveResearchItemsAsync(IReadOnlyList<NewsItem> items, DataSourceKind kind, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingMonitorDbContext>();
            var urls = items
                .Select(item => string.IsNullOrWhiteSpace(item.Url) ? $"{item.Source}:{item.Title}" : item.Url)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var existingUrls = await dbContext.ResearchItems
                .AsNoTracking()
                .Where(item => urls.Contains(item.Url))
                .Select(item => item.Url)
                .ToArrayAsync(cancellationToken);

            var existingSet = existingUrls.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var now = DateTimeOffset.UtcNow;

            foreach (var item in items)
            {
                var url = string.IsNullOrWhiteSpace(item.Url) ? $"{item.Source}:{item.Title}" : item.Url;
                if (existingSet.Contains(url))
                {
                    continue;
                }

                dbContext.ResearchItems.Add(new ResearchItemEntity
                {
                    Id = Guid.NewGuid(),
                    Source = item.Source,
                    Kind = kind,
                    Title = item.Title,
                    Url = url,
                    PublishedAt = item.PublishedAt,
                    Sentiment = item.Sentiment,
                    SymbolsJson = JsonSerializer.Serialize(item.Symbols, JsonOptions),
                    RawJson = JsonSerializer.Serialize(item, JsonOptions),
                    CreatedAt = now
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not persist research items.");
        }
    }
}
