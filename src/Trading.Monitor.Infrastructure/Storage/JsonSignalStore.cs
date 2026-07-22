using System.Runtime.CompilerServices;
using System.Text.Json;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Storage;

public sealed class JsonSignalStore(string path) : ISignalStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly string _path = Path.GetFullPath(path);

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<bool> HasRecentSimilarSignalAsync(TradingOpportunity opportunity, TimeSpan duplicateWindow, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(_path))
                return false;

            var cutoff = DateTimeOffset.UtcNow.Subtract(duplicateWindow);

            await foreach (var saved in ReadSignalsAsync(cancellationToken))
            {
                if (saved.ObservedAt < cutoff)
                    continue;

                if (string.Equals(saved.Symbol, opportunity.Symbol, StringComparison.OrdinalIgnoreCase) && saved.Side == opportunity.Side)
                    return true;
            }

            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveAsync(TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(opportunity, SerializerOptions);
            await File.AppendAllTextAsync(_path, json + Environment.NewLine, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async IAsyncEnumerable<TradingOpportunity> ReadSignalsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(_path);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(line))
                continue;

            TradingOpportunity? opportunity = null;

            try
            {
                opportunity = JsonSerializer.Deserialize<TradingOpportunity>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                // Ignore malformed historical lines and keep the monitor alive.
            }

            if (opportunity is not null)
                yield return opportunity;
        }
    }
}