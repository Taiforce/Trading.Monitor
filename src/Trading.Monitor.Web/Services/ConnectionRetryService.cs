using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Services;

public sealed class ConnectionRetryService(HttpClient httpClient, ISourceTelemetryRecorder telemetryRecorder, ILogger<ConnectionRetryService> logger)
{
    public async Task<ConnectionRetryResult> RetryAsync(ConnectionRetryRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sourceName = string.IsNullOrWhiteSpace(request.SourceName) ? "Fuente sin nombre" : request.SourceName.Trim();
        var kind = ResolveKind(request.Kind, sourceName);
        var url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim();
        DataSourceStatus status;
        string message;

        try
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                status = DataSourceStatus.Degraded;
                message = "La fuente no tiene una URL HTTP directa para probar; se mantiene mapeada, pero requiere integración específica.";
            }
            else
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                status = response.IsSuccessStatusCode ? DataSourceStatus.Healthy : DataSourceStatus.Failed;
                message = response.IsSuccessStatusCode
                    ? $"Respondió {(int)response.StatusCode} {response.ReasonPhrase}. La conexión puede seguir usándose como fuente disponible."
                    : $"Respondió {(int)response.StatusCode} {response.ReasonPhrase}. Se baja su confianza y el sistema sigue con otras fuentes.";
            }
        }
        catch (Exception exception)
        {
            status = DataSourceStatus.Failed;
            message = $"Error al reintentar: {exception.Message}";
            logger.LogWarning(exception, "Connection retry failed for {SourceName}.", sourceName);
        }

        var completedAt = DateTimeOffset.UtcNow;
        await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(sourceName, kind, status, url, message, startedAt, completedAt, status == DataSourceStatus.Healthy ? 1 : 0), cancellationToken);

        return new ConnectionRetryResult(sourceName, kind.ToString(), status.ToString(), StatusCss(status), message, completedAt);
    }

    private static DataSourceKind ResolveKind(string? kind, string sourceName)
    {
        if (Enum.TryParse<DataSourceKind>(kind, true, out var parsed))
            return parsed;

        var text = $"{kind} {sourceName}";
        if (ContainsAny(text, "market", "mercado", "binance", "coinbase", "kraken", "yahoo", "oanda"))
            return DataSourceKind.MarketData;

        if (ContainsAny(text, "ia", "ai", "openai", "kensho", "tickeron", "trendspider"))
            return DataSourceKind.AiAnalysis;

        if (ContainsAny(text, "sentimiento", "fear", "social"))
            return DataSourceKind.SocialSentiment;

        if (ContainsAny(text, "macro", "fred", "fed", "banxico", "forex"))
            return DataSourceKind.MacroReport;

        if (ContainsAny(text, "noticia", "news", "rss"))
            return DataSourceKind.News;

        return DataSourceKind.Research;
    }

    private static string StatusCss(DataSourceStatus status)
    {
        return status switch
        {
            DataSourceStatus.Healthy => "status-win",
            DataSourceStatus.Failed => "status-loss",
            _ => "status-muted"
        };
    }

    private static bool ContainsAny(string value, params string[] patterns)
    {
        return patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record ConnectionRetryRequest(string SourceName, string? Kind, string? Url, string? Scope);

public sealed record ConnectionRetryResult(string SourceName, string Kind, string StatusLabel, string CssClass, string Message, DateTimeOffset CheckedAt);
